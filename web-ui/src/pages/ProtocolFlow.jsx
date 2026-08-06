import { useState, useEffect, useCallback } from 'react'
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
const epKey = (id) => `mlkem_epoch_${id}`

const STEPS = ['Key Generation', 'Encapsulation', 'Decapsulation', 'Submit Data']

export default function ProtocolFlow() {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  // Azure AD OID matches the JWT sub claim the API uses for metrics
  const myId = account?.localAccountId ?? ''

  const [step, setStep] = useState(1)

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
  const [encapBusy, setEncapBusy] = useState(false)
  const [encapError, setEncapError] = useState(null)

  // Data grid
  const [epoch, setEpoch] = useState(null)
  const [submittedCells, setSubmittedCells] = useState(new Set())
  const [values, setValues] = useState({})
  const [busyCells, setBusyCells] = useState(new Set())
  const [cellErrors, setCellErrors] = useState({})

  // ── Restore persisted state — cleared automatically on epoch change ──────────
  useEffect(() => {
    if (!myId) return

    // Load current epoch and wipe stale state if it changed
    api.acquireApiToken(instance, account).then(token => api.getEpoch(token)).then(ep => {
      setEpoch(ep)
      const storedEpochId = localStorage.getItem(epKey(myId))
      if (storedEpochId !== String(ep.epochId)) {
        localStorage.removeItem(kpKey(myId))
        localStorage.removeItem(ssKey(myId))
        localStorage.removeItem(ctKey(myId))
        localStorage.setItem(epKey(myId), String(ep.epochId))
        setKeyPair(null)
        setSharedSecrets(new Map())
        setSentTo(new Set())
        setStep(1)
        return
      }
      const kp = localStorage.getItem(kpKey(myId))
      if (kp) setKeyPair(JSON.parse(kp))

      const ss = localStorage.getItem(ssKey(myId))
      if (ss) {
        const parsed = JSON.parse(ss)
        setSharedSecrets(new Map(Object.entries(parsed).map(([k, v]) => [k, base64ToBytes(v)])))
      }

      const ct = localStorage.getItem(ctKey(myId))
      if (ct) setSentTo(new Set(JSON.parse(ct)))
    }).catch(() => {
      // Epoch not yet created — still restore any local state
      const kp = localStorage.getItem(kpKey(myId))
      if (kp) setKeyPair(JSON.parse(kp))

      const ss = localStorage.getItem(ssKey(myId))
      if (ss) {
        const parsed = JSON.parse(ss)
        setSharedSecrets(new Map(Object.entries(parsed).map(([k, v]) => [k, base64ToBytes(v)])))
      }

      const ct = localStorage.getItem(ctKey(myId))
      if (ct) setSentTo(new Set(JSON.parse(ct)))
    })
  }, [myId])

  // ── Self-register as producer on first visit ─────────────────────────────────
  useEffect(() => {
    if (!myId) return
    api.acquireApiToken(instance, account)
      .then(token => api.selfRegister(token))
      .catch(() => {}) // non-fatal — partner can still proceed
  }, [myId])

  // ── Poll status & partner keys on steps 1–3 ─────────────────────────────────
  useEffect(() => {
    if (step > 3 || !myId) return
    let alive = true

    const poll = async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const [s, pk, ep] = await Promise.all([
          api.getKeyExchangeStatus(token),
          api.getPartnerKeys(myId, token),
          api.getEpoch(token),
        ])
        if (!alive) return

        // Detect epoch change and wipe stale state immediately
        const storedEpochId = localStorage.getItem(epKey(myId))
        if (storedEpochId !== String(ep.epochId)) {
          localStorage.removeItem(kpKey(myId))
          localStorage.removeItem(ssKey(myId))
          localStorage.removeItem(ctKey(myId))
          localStorage.setItem(epKey(myId), String(ep.epochId))
          setEpoch(ep)
          setKeyPair(null)
          setSharedSecrets(new Map())
          setSentTo(new Set())
          setStep(1)
          return
        }

        setStatus(s)
        setPartnerKeys(pk.partnerKeys ?? [])
      } catch {
        // silent poll failure
      }
    }

    poll()
    const id = setInterval(poll, 5000)
    return () => { alive = false; clearInterval(id) }
  }, [step, myId, instance, account?.homeAccountId])

  // ── Manual state reset (escape hatch for stuck states) ─────────────────────
  const resetLocalState = () => {
    localStorage.removeItem(kpKey(myId))
    localStorage.removeItem(ssKey(myId))
    localStorage.removeItem(ctKey(myId))
    localStorage.removeItem(epKey(myId))
    setKeyPair(null)
    setSharedSecrets(new Map())
    setSentTo(new Set())
    setStep(1)
  }

  // ── Step 1: generate & register key pair ─────────────────────────────────────
  const generateAndRegister = useCallback(async () => {
    setKeyBusy(true)
    setKeyError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      let kp = keyPair
      if (!kp) {
        kp = await generateMlKemKeyPair()
        localStorage.setItem(kpKey(myId), JSON.stringify(kp))
        setKeyPair(kp)
      }
      await api.registerPublicKey(myId, kp.ekBase64, token)
    } catch (e) {
      setKeyError(e.message)
    } finally {
      setKeyBusy(false)
    }
  }, [instance, account, myId, keyPair])

  // ── Step 2: encapsulate for all smaller-ID partners ─────────────────────────
  const performEncapsulation = useCallback(async () => {
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const allKeys = partnerKeys
      const targets = allKeys.filter((pk) => pk.producerId < myId)

      const newSecrets = new Map(sharedSecrets)
      const newSent = new Set(sentTo)

      for (const pk of targets) {
        if (newSent.has(pk.producerId)) continue
        const { ctBase64, sharedSecret } = await encapsulate(pk.publicKeyBase64)
        await api.postCiphertext(myId, pk.producerId, ctBase64, token)
        newSecrets.set(pk.producerId, sharedSecret)
        newSent.add(pk.producerId)
        // Persist after each successful send
        localStorage.setItem(ssKey(myId), JSON.stringify(
          Object.fromEntries([...newSecrets].map(([k, v]) => [k, bytesToBase64(v)]))
        ))
        localStorage.setItem(ctKey(myId), JSON.stringify([...newSent]))
      }

      setSharedSecrets(newSecrets)
      setSentTo(newSent)
    } catch (e) {
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId, partnerKeys, sharedSecrets, sentTo])

  // ── Step 3: decapsulate all received ciphertexts ────────────────────────────
  const performDecapsulation = useCallback(async () => {
    if (!keyPair) return
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const { ciphertexts } = await api.getCiphertexts(myId, token)
      const newSecrets = new Map(sharedSecrets)

      for (const ct of ciphertexts ?? []) {
        if (newSecrets.has(ct.senderId)) continue
        const ss = await decapsulate(ct.ciphertextBase64, keyPair.dkBase64)
        newSecrets.set(ct.senderId, ss)
      }

      localStorage.setItem(ssKey(myId), JSON.stringify(
        Object.fromEntries([...newSecrets].map(([k, v]) => [k, bytesToBase64(v)]))
      ))
      setSharedSecrets(newSecrets)
    } catch (e) {
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId, keyPair, sharedSecrets])

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
  const smallerIdPartners = partnerKeys.filter((pk) => pk.producerId < myId)
  const largerIdPartners = partnerKeys.filter((pk) => pk.producerId > myId)
  const allEncapsDone = smallerIdPartners.every((pk) => sentTo.has(pk.producerId))
  const allDecapsDone = largerIdPartners.every((pk) => sharedSecrets.has(pk.producerId))
  const exchangeComplete = status?.isCiphertextExchangeComplete ?? false

  // ── Auto-progression ─────────────────────────────────────────────────────────

  // Step 1: auto-generate key when epoch is loaded and no key pair exists
  useEffect(() => {
    if (step !== 1 || keyPair || keyBusy || !epoch || !myId) return
    generateAndRegister()
  }, [step, !!keyPair, keyBusy, !!epoch, myId])

  // Step 1 → 2: advance once all partners have registered keys
  useEffect(() => {
    if (step !== 1 || !status?.isComplete || !status?.registeredPartners?.includes(myId)) return
    setStep(2)
  }, [step, status?.isComplete, status?.registeredCount])

  // Step 2: auto-encapsulate as soon as partner keys are available
  useEffect(() => {
    if (step !== 2 || encapBusy || !partnerKeys.length) return
    if (smallerIdPartners.length === 0 || allEncapsDone) return
    performEncapsulation()
  }, [step, partnerKeys.length, encapBusy])

  // Step 2 → 3: advance when all encapsulations done (or nothing to send)
  useEffect(() => {
    if (step !== 2) return
    if (partnerKeys.length === 0) return // wait for keys to load
    if (smallerIdPartners.length === 0 || allEncapsDone) setStep(3)
  }, [step, allEncapsDone, partnerKeys.length])

  // Step 3: auto-decapsulate whenever new ciphertexts arrive
  useEffect(() => {
    if (step !== 3 || encapBusy || !keyPair || allDecapsDone) return
    if (largerIdPartners.length === 0) return
    performDecapsulation()
  }, [step, status?.actualCiphertexts, !!keyPair])

  // Step 3 → 4: advance when ciphertext exchange is complete and all secrets derived
  useEffect(() => {
    if (step !== 3) return
    if (largerIdPartners.length === 0 && exchangeComplete) { setStep(4); return }
    if (exchangeComplete && allDecapsDone) setStep(4)
  }, [step, exchangeComplete, allDecapsDone])

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
          ✨ You have the smallest producer ID — nothing to send. Advancing to Step 3…
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
