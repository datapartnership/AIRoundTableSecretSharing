import { useState, useEffect, useCallback, useRef } from 'react'
import { useMsal } from '@azure/msal-react'
import * as api from '../utils/api'
import { generateMlKemKeyPair, encapsulate, decapsulate, bytesToBase64, base64ToBytes } from '../utils/crypto'
import { calculateMaskedValue } from '../utils/noise'

const COUNTRIES = ['US', 'GB', 'DE']
const MONTHS = ['2026-06', '2026-07', '2026-08'] // fallback only

// Derive 3 submission months from epoch start date
function epochMonths(epoch) {
  if (!epoch?.startDate) return MONTHS
  return Array.from({ length: 3 }, (_, i) => {
    const d = new Date(epoch.startDate)
    d.setUTCMonth(d.getUTCMonth() + i)
    return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
  })
}

const kpKey = (id) => `mlkem_kp_${id}`
const ssKey = (id) => `mlkem_ss_${id}`
const ctKey = (id) => `mlkem_ct_${id}`
const ctSeenKey = (id) => `mlkem_ctseen_${id}`
const epKey = (id) => `mlkem_epoch_${id}`

const STEPS = ['Key Generation', 'Encapsulation', 'Decapsulation', 'Submit Data']

function wipeLocalCrypto(id) {
  localStorage.removeItem(kpKey(id))
  localStorage.removeItem(ssKey(id))
  localStorage.removeItem(ctKey(id))
  localStorage.removeItem(ctSeenKey(id))
}

function persistSecrets(id, map) {
  localStorage.setItem(ssKey(id), JSON.stringify(
    Object.fromEntries([...map].map(([k, v]) => [k, bytesToBase64(v)]))
  ))
}

function persistSent(id, sent) {
  localStorage.setItem(ctKey(id), JSON.stringify([...sent]))
}

function persistSeen(id, map) {
  localStorage.setItem(ctSeenKey(id), JSON.stringify(Object.fromEntries(map)))
}

function loadLocalCrypto(id) {
  let keyPair = null
  const secrets = new Map()
  let sentTo = new Set()
  const ctSeen = new Map()

  const kp = localStorage.getItem(kpKey(id))
  if (kp) {
    try { keyPair = JSON.parse(kp) } catch { keyPair = null }
  }

  const ss = localStorage.getItem(ssKey(id))
  if (ss) {
    try {
      for (const [k, v] of Object.entries(JSON.parse(ss))) {
        secrets.set(k, base64ToBytes(v))
      }
    } catch { /* ignore corrupt cache */ }
  }

  const ct = localStorage.getItem(ctKey(id))
  if (ct) {
    try { sentTo = new Set(JSON.parse(ct)) } catch { sentTo = new Set() }
  }

  const seen = localStorage.getItem(ctSeenKey(id))
  if (seen) {
    try {
      for (const [k, v] of Object.entries(JSON.parse(seen))) ctSeen.set(k, v)
    } catch { /* ignore corrupt cache */ }
  }

  return { keyPair, secrets, sentTo, ctSeen }
}

export default function ProtocolFlow() {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  // Azure AD OID matches the JWT sub claim the API uses for metrics
  const myId = account?.localAccountId ?? ''

  const [step, setStep] = useState(1)
  const [hydrated, setHydrated] = useState(false)

  // Key pair state
  const [keyPair, setKeyPair] = useState(null)
  const [keyBusy, setKeyBusy] = useState(false)
  const [keyError, setKeyError] = useState(null)

  // Polling
  const [status, setStatus] = useState(null)
  const [partnerKeys, setPartnerKeys] = useState([])

  // Ciphertext exchange
  const [sentTo, setSentTo] = useState(new Set())       // partners I encapsulated for
  const [sharedSecrets, setSharedSecrets] = useState(new Map())
  const [ctSeen, setCtSeen] = useState(new Map())       // senderId → ciphertext blob we already decapped
  const [encapBusy, setEncapBusy] = useState(false)
  const [encapError, setEncapError] = useState(null)

  // Data grid
  const [epoch, setEpoch] = useState(null)
  const [submittedCells, setSubmittedCells] = useState(new Set())
  const [values, setValues] = useState({})
  const [busyCells, setBusyCells] = useState(new Set())
  const [cellErrors, setCellErrors] = useState({})

  const keyPairRef = useRef(null)
  const secretsRef = useRef(new Map())
  const sentToRef = useRef(new Set())
  const ctSeenRef = useRef(new Map())
  keyPairRef.current = keyPair
  secretsRef.current = sharedSecrets
  sentToRef.current = sentTo
  ctSeenRef.current = ctSeen

  const applyClearedCrypto = () => {
    setKeyPair(null)
    setSharedSecrets(new Map())
    setSentTo(new Set())
    setCtSeen(new Map())
    setStep(1)
    setEncapError(null)
    setKeyError(null)
  }

  const applyLocalCrypto = (id) => {
    const local = loadLocalCrypto(id)
    setKeyPair(local.keyPair)
    setSharedSecrets(local.secrets)
    setSentTo(local.sentTo)
    setCtSeen(local.ctSeen)
  }

  // ── Restore persisted state before any auto-run (epoch change wipes cache) ──
  useEffect(() => {
    if (!myId) return
    let cancelled = false
    setHydrated(false)

    ;(async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const ep = await api.getEpoch(token)
        if (cancelled) return
        setEpoch(ep)
        const storedEpochId = localStorage.getItem(epKey(myId))
        if (storedEpochId !== String(ep.epochId)) {
          wipeLocalCrypto(myId)
          localStorage.setItem(epKey(myId), String(ep.epochId))
          applyClearedCrypto()
        } else {
          applyLocalCrypto(myId)
        }
      } catch {
        if (cancelled) return
        applyLocalCrypto(myId)
      } finally {
        if (!cancelled) setHydrated(true)
      }
    })()

    return () => { cancelled = true }
  }, [myId, instance, account])

  // ── Self-register as producer on first visit ─────────────────────────────────
  useEffect(() => {
    if (!myId) return
    api.acquireApiToken(instance, account)
      .then(token => api.selfRegister(token))
      .catch(() => {}) // non-fatal — partner can still proceed
  }, [myId])

  // ── Poll status & partner keys on steps 1–3 (after local restore) ───────────
  useEffect(() => {
    if (!hydrated || step > 3 || !myId) return
    let alive = true

    const poll = async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const [s, pk, ep, sent] = await Promise.all([
          api.getKeyExchangeStatus(token),
          api.getPartnerKeys(myId, token),
          api.getEpoch(token),
          api.getSentCiphertexts(token).catch(() => ({ ciphertexts: [] })),
        ])
        if (!alive) return

        const storedEpochId = localStorage.getItem(epKey(myId))
        if (storedEpochId !== String(ep.epochId)) {
          wipeLocalCrypto(myId)
          localStorage.setItem(epKey(myId), String(ep.epochId))
          setEpoch(ep)
          applyClearedCrypto()
          return
        }

        setEpoch(ep)
        setStatus(s)
        setPartnerKeys(pk.partnerKeys ?? [])

        const serverSent = (sent.ciphertexts ?? []).map((c) => c.recipientId)
        if (serverSent.length) {
          setSentTo((prev) => {
            const next = new Set(prev)
            for (const id of serverSent) next.add(id)
            persistSent(myId, next)
            return next
          })
        }

        const serverKey = s.myPublicKeyBase64
        const localKp = keyPairRef.current
        if (serverKey && localKp && serverKey !== localKp.ekBase64) {
          setKeyError('This browser’s key does not match the key registered on the server. Recreate the epoch, then reset local state.')
        } else if (serverKey && !localKp) {
          setKeyError('The server has a public key for you, but this browser has no matching private key. Recreate the epoch, then generate a new key.')
        }
      } catch {
        // silent poll failure
      }
    }

    poll()
    const id = setInterval(poll, 5000)
    return () => { alive = false; clearInterval(id) }
  }, [hydrated, step, myId, instance, account?.homeAccountId])

  // ── Manual state reset (escape hatch for stuck states) ─────────────────────
  const resetLocalState = () => {
    wipeLocalCrypto(myId)
    localStorage.removeItem(epKey(myId))
    applyClearedCrypto()
    setHydrated(true)
  }

  // ── Step 1: generate & register key pair ─────────────────────────────────────
  const generateAndRegister = useCallback(async () => {
    setKeyBusy(true)
    setKeyError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const existing = keyPairRef.current
      const kp = existing ?? await generateMlKemKeyPair()
      await api.registerPublicKey(myId, kp.ekBase64, token)
      if (!existing) {
        localStorage.setItem(kpKey(myId), JSON.stringify(kp))
        setKeyPair(kp)
      }
    } catch (e) {
      setKeyError(e.message)
    } finally {
      setKeyBusy(false)
    }
  }, [instance, account, myId])

  // ── Step 2: encapsulate for all smaller-ID partners (never overwrite) ───────
  const performEncapsulation = useCallback(async () => {
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const targets = partnerKeys.filter((pk) => pk.producerId < myId)
      const newSecrets = new Map(secretsRef.current)
      const newSent = new Set(sentToRef.current)

      for (const pk of targets) {
        if (newSecrets.has(pk.producerId) && newSent.has(pk.producerId)) continue

        if (newSent.has(pk.producerId) && !newSecrets.has(pk.producerId)) {
          throw new Error(
            `A ciphertext for ${pk.producerId} is already on the server, but this browser lost the shared secret. Recreate the epoch so both sides start over.`
          )
        }

        const { ctBase64, sharedSecret } = await encapsulate(pk.publicKeyBase64)
        await api.postCiphertext(myId, pk.producerId, ctBase64, token)
        newSecrets.set(pk.producerId, sharedSecret)
        newSent.add(pk.producerId)
        persistSecrets(myId, newSecrets)
        persistSent(myId, newSent)
      }

      secretsRef.current = newSecrets
      sentToRef.current = newSent
      setSharedSecrets(newSecrets)
      setSentTo(newSent)
    } catch (e) {
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId, partnerKeys])

  // ── Step 3: decapsulate received ciphertexts (re-run if blob changed) ───────
  const performDecapsulation = useCallback(async () => {
    const kp = keyPairRef.current
    if (!kp) return
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const { ciphertexts } = await api.getCiphertexts(token)
      const newSecrets = new Map(secretsRef.current)
      const seen = new Map(ctSeenRef.current)

      for (const ct of ciphertexts ?? []) {
        if (seen.get(ct.senderId) === ct.ciphertextBase64 && newSecrets.has(ct.senderId)) continue
        const ss = await decapsulate(ct.ciphertextBase64, kp.dkBase64)
        newSecrets.set(ct.senderId, ss)
        seen.set(ct.senderId, ct.ciphertextBase64)
      }

      persistSecrets(myId, newSecrets)
      persistSeen(myId, seen)
      secretsRef.current = newSecrets
      ctSeenRef.current = seen
      setSharedSecrets(newSecrets)
      setCtSeen(seen)
    } catch (e) {
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId])

  // ── Step 4: load epoch + existing submissions ────────────────────────────────
  useEffect(() => {
    if (step !== 4 || !myId) return
    ;(async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const my = await api.getMySubmissions(token)
        const done = new Set((my.submissions ?? []).map((s) => `${s.country}|${s.month}`))
        setSubmittedCells(done)
      } catch (e) {
        setCellErrors({ _load: e.message })
      }
    })()
  }, [step, myId, instance, account?.homeAccountId])

  // ── Submit a single cell ─────────────────────────────────────────────────────
  const submitCell = async (country, month) => {
    const cellId = `${country}|${month}`
    const raw = parseFloat(values[cellId])
    if (isNaN(raw) || raw < 0) {
      setCellErrors((p) => ({ ...p, [cellId]: 'Enter a non-negative number' }))
      return
    }
    setBusyCells((p) => new Set(p).add(cellId))
    setCellErrors((p) => { const n = { ...p }; delete n[cellId]; return n })
    try {
      const token = await api.acquireApiToken(instance, account)
      const masked = await calculateMaskedValue(raw, country, month, myId, sharedSecrets)
      await api.submitMetric(
        { country, month, value: Math.round(masked), epochId: epoch.epochId, signature: 'web-ui' },
        token
      )
      setSubmittedCells((p) => new Set(p).add(cellId))
    } catch (e) {
      if (e.status === 409) {
        setSubmittedCells((p) => new Set(p).add(cellId))
      } else {
        setCellErrors((p) => ({ ...p, [cellId]: e.message }))
      }
    } finally {
      setBusyCells((p) => { const n = new Set(p); n.delete(cellId); return n })
    }
  }

  const submitAll = () => {
    const months = epochMonths(epoch)
    for (const country of COUNTRIES) {
      for (const month of months) {
        const id = `${country}|${month}`
        if (!submittedCells.has(id)) submitCell(country, month)
      }
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────────────
  const epochPartnerIds = epoch?.producerIds ?? []
  const expectedSmallerIds = epochPartnerIds.filter((id) => id < myId)
  const expectedLargerIds = epochPartnerIds.filter((id) => id > myId)
  const smallerIdPartners = partnerKeys.filter((pk) => pk.producerId < myId)
  const largerIdPartners = partnerKeys.filter((pk) => pk.producerId > myId)
  const epochKeysReady = (status?.isComplete === true)
    && epochPartnerIds.length >= 2
    && (status?.expectedCount ?? 0) >= 2
    && partnerKeys.length >= epochPartnerIds.filter((id) => id !== myId).length
  const allEncapsDone = expectedSmallerIds.length === 0
    || expectedSmallerIds.every((id) => sentTo.has(id) && sharedSecrets.has(id))
  const allDecapsDone = expectedLargerIds.length === 0
    || expectedLargerIds.every((id) => sharedSecrets.has(id))
  const exchangeComplete = status?.isCiphertextExchangeComplete ?? false
  const keysMatch = !status?.myPublicKeyBase64
    || (keyPair && status.myPublicKeyBase64 === keyPair.ekBase64)

  // ── Auto-progression ─────────────────────────────────────────────────────────

  // Step 1: auto-generate only after restore, and only if the server has no key yet
  useEffect(() => {
    if (!hydrated || step !== 1 || keyPair || keyBusy || !epoch || !myId) return
    if (status?.myPublicKeyBase64) return
    generateAndRegister()
  }, [hydrated, step, !!keyPair, keyBusy, !!epoch, myId, status?.myPublicKeyBase64])

  // Step 1 → 2: advance once all epoch partners registered AND we hold the matching private key
  useEffect(() => {
    if (!hydrated || step !== 1 || !keyPair || !keysMatch) return
    if (!status?.isComplete || !status?.registeredPartners?.includes(myId)) return
    setStep(2)
  }, [hydrated, step, !!keyPair, keysMatch, status?.isComplete, status?.registeredCount])

  // Step 2: auto-encapsulate once the full epoch key set is present
  useEffect(() => {
    if (!hydrated || step !== 2 || encapBusy || encapError || !epochKeysReady) return
    if (expectedSmallerIds.length === 0 || allEncapsDone) return
    performEncapsulation()
  }, [hydrated, step, epochKeysReady, encapBusy, encapError, allEncapsDone])

  // Step 2 → 3: wait for every smaller epoch partner, not a partial poll snapshot
  useEffect(() => {
    if (!hydrated || step !== 2 || !epochKeysReady) return
    if (expectedSmallerIds.length === 0 || allEncapsDone) setStep(3)
  }, [hydrated, step, epochKeysReady, allEncapsDone, expectedSmallerIds.length])

  // Step 3: auto-decapsulate whenever new ciphertexts arrive
  useEffect(() => {
    if (!hydrated || step !== 3 || encapBusy || encapError || !keyPair || allDecapsDone) return
    if (expectedLargerIds.length === 0) return
    performDecapsulation()
  }, [hydrated, step, status?.actualCiphertexts, !!keyPair, encapError, allDecapsDone])

  // Step 3 → 4: advance when ciphertext exchange is complete and all secrets derived
  useEffect(() => {
    if (!hydrated || step !== 3) return
    if (epochPartnerIds.length < 2) return
    if (expectedLargerIds.length === 0 && exchangeComplete) { setStep(4); return }
    if (exchangeComplete && allDecapsDone) setStep(4)
  }, [hydrated, step, exchangeComplete, allDecapsDone, expectedLargerIds.length, epochPartnerIds.length])

  // ── Render helpers ────────────────────────────────────────────────────────────
  const dot = (ok) => (
    <span style={{ color: ok ? '#4ade80' : '#fbbf24', marginRight: 8 }}>{ok ? '✓' : '○'}</span>
  )

  const renderStepIndicator = () => (
    <div style={{ display: 'flex', gap: '0.5rem', marginBottom: '2rem', justifyContent: 'center' }}>
      {STEPS.map((label, i) => {
        const n = i + 1
        const active = n === step
        const done = n < step
        return (
          <div key={n} style={{ display: 'flex', alignItems: 'center', gap: '0.5rem' }}>
            <div style={{
              width: 32, height: 32, borderRadius: '50%', display: 'flex',
              alignItems: 'center', justifyContent: 'center', fontWeight: 700, fontSize: '0.875rem',
              background: done ? '#4ade80' : active ? 'linear-gradient(135deg,#3b82f6,#8b5cf6)' : 'rgba(255,255,255,0.1)',
              color: done ? '#14532d' : '#fff',
            }}>
              {done ? '✓' : n}
            </div>
            <span style={{ fontSize: '0.875rem', color: active ? '#fff' : '#71717a', whiteSpace: 'nowrap' }}>
              {label}
            </span>
            {i < STEPS.length - 1 && <div style={{ width: 24, height: 1, background: 'rgba(255,255,255,0.15)' }} />}
          </div>
        )
      })}
    </div>
  )

  // ── Step 1 ────────────────────────────────────────────────────────────────────
  const renderStep1 = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">🔑</span>
        <h2 className="card-title">Step 1 — Key Generation & Registration</h2>
        {keyBusy && <span style={{ marginLeft: 'auto', color: '#60a5fa', fontSize: '0.85rem' }}>⏳ Auto-running…</span>}
      </div>

      <div style={{ marginBottom: '1.5rem' }}>
        {dot(!!keyPair)} Key pair {keyPair ? 'generated and stored locally' : keyBusy ? 'generating…' : 'pending'}
        <br />
        {dot(status?.registeredPartners?.includes(myId))} Public key {status?.registeredPartners?.includes(myId) ? 'registered' : 'pending registration'}
      </div>

      {status && (
        <div style={{ marginBottom: '1.5rem' }}>
          <div style={{ fontWeight: 600, marginBottom: '0.75rem', color: '#d4d4d8' }}>
            Waiting for all partners to register ({status.registeredCount}/{status.expectedCount})
          </div>
          {(status.registeredPartners ?? []).map((id) => (
            <div key={id} style={{ padding: '0.5rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', fontSize: '0.875rem' }}>
              {dot(true)} <code style={{ color: '#a78bfa' }}>{id === myId ? `${id} (you)` : id}</code>
            </div>
          ))}
          {(status.missingPartners ?? []).map((id) => (
            <div key={id} style={{ padding: '0.5rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', fontSize: '0.875rem', color: '#71717a' }}>
              {dot(false)} <code>{id}</code> — waiting
            </div>
          ))}
        </div>
      )}

      {keyError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginBottom: '1rem' }}>
          ⚠️ {keyError}
        </div>
      )}

      <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap' }}>
        <button className="btn btn-secondary" onClick={generateAndRegister} disabled={keyBusy} style={{ fontSize: '0.85rem' }}>
          {keyBusy ? '⏳ Working…' : keyPair ? '🔄 Re-register Key' : '⚡ Generate Key'}
        </button>
        <button className="btn btn-secondary" onClick={resetLocalState} style={{ fontSize: '0.8rem', color: '#f87171' }}>
          🗑 Reset Local State
        </button>
      </div>
    </div>
  )

  // ── Step 2 ────────────────────────────────────────────────────────────────────
  const renderStep2 = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">📤</span>
        <h2 className="card-title">Step 2 — Encapsulation</h2>
        {encapBusy && <span style={{ marginLeft: 'auto', color: '#60a5fa', fontSize: '0.85rem' }}>⏳ Auto-running…</span>}
      </div>

      {smallerIdPartners.length === 0 ? (
        <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac' }}>
          {epochKeysReady
            ? '✨ You have the smallest producer ID — nothing to send. Advancing to Step 3…'
            : '⏳ Waiting for every epoch partner to register a public key…'}
        </div>
      ) : (
        smallerIdPartners.map((pk) => (
          <div key={pk.producerId} style={{ display: 'flex', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', gap: '0.75rem' }}>
            {dot(sentTo.has(pk.producerId))}
            <code style={{ color: '#a78bfa', flex: 1 }}>{pk.producerId}</code>
            <span className={`status-badge ${sentTo.has(pk.producerId) ? 'success' : 'pending'}`}>
              {sentTo.has(pk.producerId) ? 'Sent ✓' : encapBusy ? 'Sending…' : 'Pending'}
            </span>
          </div>
        ))
      )}

      {encapError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginTop: '1rem' }}>
          ⚠️ {encapError}
          <button className="btn btn-secondary" onClick={performEncapsulation} disabled={encapBusy}
            style={{ marginLeft: '1rem', padding: '0.25rem 0.6rem', fontSize: '0.8rem' }}>Retry</button>
        </div>
      )}
    </div>
  )

  // ── Step 3 ────────────────────────────────────────────────────────────────────
  const renderStep3 = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">📥</span>
        <h2 className="card-title">Step 3 — Decapsulation</h2>
        {encapBusy && <span style={{ marginLeft: 'auto', color: '#60a5fa', fontSize: '0.85rem' }}>⏳ Auto-running…</span>}
        {!encapBusy && !exchangeComplete && <span style={{ marginLeft: 'auto', color: '#71717a', fontSize: '0.85rem' }}>🔄 Polling for ciphertexts…</span>}
      </div>

      {largerIdPartners.length === 0 ? (
        <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac' }}>
          ✨ You have the largest producer ID — nothing to receive.
        </div>
      ) : (
        largerIdPartners.map((pk) => (
          <div key={pk.producerId} style={{ display: 'flex', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', gap: '0.75rem' }}>
            {dot(sharedSecrets.has(pk.producerId))}
            <code style={{ color: '#a78bfa', flex: 1 }}>{pk.producerId}</code>
            <span className={`status-badge ${sharedSecrets.has(pk.producerId) ? 'success' : 'pending'}`}>
              {sharedSecrets.has(pk.producerId) ? 'Secret derived ✓' : encapBusy ? 'Decapsulating…' : 'Waiting for ciphertext'}
            </span>
          </div>
        ))
      )}

      {status && (
        <div style={{ marginTop: '1.25rem', padding: '1rem', background: 'rgba(0,0,0,0.2)', borderRadius: 8, fontSize: '0.875rem' }}>
          Ciphertexts received: {status.actualCiphertexts}/{status.expectedCiphertexts} &nbsp;·&nbsp;
          Exchange complete: {exchangeComplete ? '✅' : '⏳ waiting'}
        </div>
      )}

      {encapError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginTop: '1rem' }}>
          ⚠️ {encapError}
          <button className="btn btn-secondary" onClick={performDecapsulation} disabled={encapBusy}
            style={{ marginLeft: '1rem', padding: '0.25rem 0.6rem', fontSize: '0.8rem' }}>Retry</button>
        </div>
      )}
    </div>
  )

  const renderStep4 = () => {
    const months = epochMonths(epoch)
    const allDone = COUNTRIES.every((c) => months.every((m) => submittedCells.has(`${c}|${m}`)))
    return (
      <div className="card animate-fade-in">
        <div className="card-header">
          <span className="card-icon">📊</span>
          <h2 className="card-title">Step 4 — Submit Data</h2>
          {epoch && (
            <span style={{ marginLeft: 'auto', color: '#71717a', fontSize: '0.875rem' }}>
              Epoch {epoch.epochId} · {months[0]} – {months[2]}
            </span>
          )}
        </div>

        <div className="info-box">
          Enter your actual MAU values. The noise derived from your shared secrets is applied automatically
          before submission — the aggregator only sees masked values.
        </div>

        {cellErrors._load && (
          <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5' }}>
            ⚠️ {cellErrors._load}
          </div>
        )}

        <div style={{ overflowX: 'auto', marginBottom: '1.5rem' }}>
          <table className="results-table">
            <thead>
              <tr>
                <th>Country</th>
                {months.map((m) => <th key={m}>{m}</th>)}
              </tr>
            </thead>
            <tbody>
              {COUNTRIES.map((country) => (
                <tr key={country}>
                  <td style={{ fontWeight: 600, color: '#d4d4d8' }}>{country}</td>
                  {months.map((month) => {
                    const id = `${country}|${month}`
                    const done = submittedCells.has(id)
                    const busy = busyCells.has(id)
                    const err = cellErrors[id]
                    return (
                      <td key={month}>
                        {done ? (
                          <span className="status-badge success">✓ Submitted</span>
                        ) : (
                          <input
                            type="number"
                            className="form-input"
                            style={{ width: 110, padding: '0.4rem 0.6rem' }}
                            placeholder="MAU"
                            min="0"
                            value={values[id] ?? ''}
                            onChange={(e) => setValues((p) => ({ ...p, [id]: e.target.value }))}
                            disabled={busy}
                          />
                        )}
                        {busy && <div style={{ color: '#71717a', fontSize: '0.75rem', marginTop: 4 }}>submitting…</div>}
                        {err && <div style={{ color: '#f87171', fontSize: '0.75rem', marginTop: 4 }}>{err}</div>}
                      </td>
                    )
                  })}
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {allDone ? (
          <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac' }}>
            ✅ All {COUNTRIES.length * months.length} cells submitted! Ask your admin to check the aggregate results.
          </div>
        ) : (
          <div style={{ display: 'flex', gap: '0.75rem' }}>
            <button className="btn btn-secondary" onClick={() => setStep(3)}>← Back</button>
            <button className="btn btn-primary" onClick={submitAll}>
              ⚡ Submit All
            </button>
          </div>
        )}
      </div>
    )
  }

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <h1 className="page-title">Protocol Flow</h1>
        <p className="page-subtitle">ML-KEM-768 pairwise key exchange and privacy-preserving metric submission</p>
      </div>

      {renderStepIndicator()}

      {step === 1 && renderStep1()}
      {step === 2 && renderStep2()}
      {step === 3 && renderStep3()}
      {step === 4 && renderStep4()}
    </div>
  )
}
