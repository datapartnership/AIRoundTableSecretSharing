import { useState, useEffect, useCallback, useRef } from 'react'
import { useMsal } from '@azure/msal-react'
import * as api from '../utils/api'
import { generateMlKemKeyPair, encapsulate, decapsulate, bytesToBase64, base64ToBytes } from '../utils/crypto'
import { calculateMaskedValue } from '../utils/noise'
import { parseAndValidateCsv, isCsvFile, epochMonths, CELL_COUNT, ERROR_DISPLAY_CAP, formatInt } from '../utils/csvUpload'

const deviceKey = 'mlkem_device_id'
const getDeviceId = () => {
  let id = localStorage.getItem(deviceKey)
  if (!id) {
    id = globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(36).slice(2)}`
    localStorage.setItem(deviceKey, id)
  }
  return id
}
const scoped = (id, epochId, deviceId) => `${id}_${epochId}_${deviceId}`
const kpKey = (id, epochId, deviceId) => `mlkem_kp_${scoped(id, epochId, deviceId)}`
const ssKey = (id, epochId, deviceId) => `mlkem_ss_${scoped(id, epochId, deviceId)}`
const ctKey = (id, epochId, deviceId) => `mlkem_ct_${scoped(id, epochId, deviceId)}`
const ctSeenKey = (id, epochId, deviceId) => `mlkem_ctseen_${scoped(id, epochId, deviceId)}`

const POLL_MS = 2000
const TAG = '[protocol]'

function log(event, data) {
  if (data === undefined) console.log(TAG, event)
  else console.log(TAG, event, data)
}

function warn(event, data) {
  if (data === undefined) console.warn(TAG, event)
  else console.warn(TAG, event, data)
}

function fail(event, err, data) {
  if (data === undefined) console.error(TAG, event, err)
  else console.error(TAG, event, err, data)
}

function wipeLocalCrypto(id, epochId, deviceId) {
  localStorage.removeItem(kpKey(id, epochId, deviceId))
  localStorage.removeItem(ssKey(id, epochId, deviceId))
  localStorage.removeItem(ctKey(id, epochId, deviceId))
  localStorage.removeItem(ctSeenKey(id, epochId, deviceId))
}

function persistSecrets(id, epochId, deviceId, map) {
  localStorage.setItem(ssKey(id, epochId, deviceId), JSON.stringify(
    Object.fromEntries([...map].map(([k, v]) => [k, bytesToBase64(v)]))
  ))
}

function persistSent(id, epochId, deviceId, sent) {
  localStorage.setItem(ctKey(id, epochId, deviceId), JSON.stringify([...sent]))
}

function persistSeen(id, epochId, deviceId, map) {
  localStorage.setItem(ctSeenKey(id, epochId, deviceId), JSON.stringify(Object.fromEntries(map)))
}

function loadLocalCrypto(id, epochId, deviceId) {
  let keyPair = null
  const secrets = new Map()
  let sentTo = new Set()
  const ctSeen = new Map()

  const kp = localStorage.getItem(kpKey(id, epochId, deviceId))
  if (kp) {
    try { keyPair = JSON.parse(kp) } catch { keyPair = null }
  }

  const ss = localStorage.getItem(ssKey(id, epochId, deviceId))
  if (ss) {
    try {
      for (const [k, v] of Object.entries(JSON.parse(ss))) {
        secrets.set(k, base64ToBytes(v))
      }
    } catch { /* ignore corrupt cache */ }
  }

  const ct = localStorage.getItem(ctKey(id, epochId, deviceId))
  if (ct) {
    try { sentTo = new Set(JSON.parse(ct)) } catch { sentTo = new Set() }
  }

  const seen = localStorage.getItem(ctSeenKey(id, epochId, deviceId))
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
  const deviceId = getDeviceId()

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

  // CSV upload
  const [epoch, setEpoch] = useState(null)
  const [submittedCells, setSubmittedCells] = useState(new Set())
  const [csvFileName, setCsvFileName] = useState('')
  const [csvResult, setCsvResult] = useState(null)
  const [submitBusy, setSubmitBusy] = useState(false)
  const [submitError, setSubmitError] = useState(null)
  const [loadError, setLoadError] = useState(null)

  const keyPairRef = useRef(null)
  const secretsRef = useRef(new Map())
  const sentToRef = useRef(new Set())
  const ctSeenRef = useRef(new Map())
  keyPairRef.current = keyPair
  secretsRef.current = sharedSecrets
  sentToRef.current = sentTo
  ctSeenRef.current = ctSeen

  const applyClearedCrypto = () => {
    warn('local crypto wiped')
    setKeyPair(null)
    setSharedSecrets(new Map())
    setSentTo(new Set())
    setCtSeen(new Map())
    setStep(1)
    setEncapError(null)
    setKeyError(null)
  }

  const applyLocalCrypto = (id, epochId, currentDeviceId = deviceId) => {
    const local = loadLocalCrypto(id, epochId, currentDeviceId)
    log('restored local crypto', {
      hasKeyPair: !!local.keyPair,
      secretPartners: [...local.secrets.keys()],
      sentTo: [...local.sentTo],
      seenFrom: [...local.ctSeen.keys()],
    })
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
        log('hydrate', {
          myId,
          epochId: ep.epochId,
          producers: ep.producerIds,
          storedEpochId,
          isClosed: !!ep.isClosed,
        })
        applyLocalCrypto(myId, ep.epochId, deviceId)
      } catch (e) {
        fail('hydrate failed — using local cache', e)
        if (cancelled) return
        if (epoch?.epochId) applyLocalCrypto(myId, epoch.epochId, deviceId)
        else applyClearedCrypto()
      } finally {
        if (!cancelled) {
          setHydrated(true)
          log('hydrated')
        }
      }
    })()

    return () => { cancelled = true }
  }, [myId, instance, account, deviceId])

  // ── Self-register as producer on first visit ─────────────────────────────────
  useEffect(() => {
    if (!myId) return
    api.acquireApiToken(instance, account)
      .then(token => api.selfRegister(token))
      .then((r) => log('self-register ok', r))
      .catch((e) => fail('self-register failed', e))
  }, [myId])

  // ── Poll epoch always; key exchange only while setup is in progress ─────────
  useEffect(() => {
    if (!hydrated || !myId) return
    let alive = true

    const poll = async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const ep = await api.getEpoch(token)
        if (!alive) return

        setEpoch(ep)

        if (ep.isClosed || ep.isEligible === false) {
          log('poll: epoch closed — waiting for a new epoch', { epochId: ep.epochId })
          return
        }

        if (step > 3) {
          log('poll', { step, epochId: ep.epochId, isClosed: false })
          return
        }

        const [s, pk, sent] = await Promise.all([
          api.getKeyExchangeStatus(ep.epochId, deviceId, token),
          api.getPartnerKeys(ep.epochId, deviceId, token),
          api.getSentCiphertexts(ep.epochId, deviceId, token).catch(() => ({ ciphertexts: [] })),
        ])
        if (!alive) return

        setStatus(s)
        setPartnerKeys(pk.partnerKeys ?? [])

        const serverSent = (sent.ciphertexts ?? []).map((c) => c.recipientId)
        if (serverSent.length) {
          setSentTo((prev) => {
            const next = new Set(prev)
            for (const id of serverSent) next.add(id)
            persistSent(myId, ep.epochId, deviceId, next)
            return next
          })
        }

        log('poll', {
          step,
          epochId: ep.epochId,
          isClosed: !!ep.isClosed,
          producers: ep.producerIds,
          registered: `${s.registeredCount}/${s.expectedCount}`,
          missing: s.missingPartners,
          partnerKeys: (pk.partnerKeys ?? []).map((p) => p.producerId),
          ciphertexts: `${s.actualCiphertexts}/${s.expectedCiphertexts}`,
          exchangeComplete: s.isCiphertextExchangeComplete,
          serverSentTo: serverSent,
          localSentTo: [...sentToRef.current],
          secretPartners: [...secretsRef.current.keys()],
          hasLocalKey: !!keyPairRef.current,
          hasServerKey: !!s.myPublicKeyBase64,
        })

        const serverKey = s.myPublicKeyBase64
        const localKp = keyPairRef.current
        if (serverKey && localKp && serverKey !== localKp.ekBase64) {
          warn('key mismatch: local public key ≠ server public key')
          setKeyError('This browser’s key does not match the key registered on the server. Recreate the epoch, then reset local state.')
        } else if (serverKey && !localKp) {
          warn('key mismatch: server has a key, this browser has none')
          setKeyError('The server has a public key for you, but this browser has no matching private key. Recreate the epoch, then generate a new key.')
        }
      } catch (e) {
        fail('poll failed', e)
      }
    }

    poll()
    const id = setInterval(poll, POLL_MS)
    return () => { alive = false; clearInterval(id) }
  }, [hydrated, step, myId, instance, account?.homeAccountId, deviceId])

  // ── Manual state reset (escape hatch for stuck states) ─────────────────────
  const resetLocalState = () => {
    warn('manual reset local state', { myId })
    wipeLocalCrypto(myId, epoch?.epochId, deviceId)
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
      log(existing ? 're-registering existing key' : 'generated new key pair', { myId })
      await api.registerPublicKey(epoch.epochId, deviceId, kp.ekBase64, token)
      log('public key registered')
      if (!existing) {
        localStorage.setItem(kpKey(myId, epoch.epochId, deviceId), JSON.stringify(kp))
        setKeyPair(kp)
      }
    } catch (e) {
      fail('key generate/register failed', e)
      setKeyError(e.message)
    } finally {
      setKeyBusy(false)
    }
  }, [instance, account, myId, epoch, deviceId])

  // ── Step 2: encapsulate for all smaller-ID partners (never overwrite) ───────
  const performEncapsulation = useCallback(async () => {
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const targets = partnerKeys.filter((pk) => pk.producerId < myId)
      log('encapsulation start', { targets: targets.map((p) => p.producerId) })
      const newSecrets = new Map(secretsRef.current)
      const newSent = new Set(sentToRef.current)

      for (const pk of targets) {
        if (newSecrets.has(pk.producerId) && newSent.has(pk.producerId)) {
          log('encapsulation skip (already done)', { recipient: pk.producerId })
          continue
        }

        if (newSent.has(pk.producerId) && !newSecrets.has(pk.producerId)) {
          throw new Error(
            `A ciphertext for ${pk.producerId} is already on the server, but this browser lost the shared secret. Recreate the epoch so both sides start over.`
          )
        }

        const { ctBase64, sharedSecret } = await encapsulate(pk.publicKeyBase64)
        await api.postCiphertext(epoch.epochId, deviceId, pk.producerId, pk.deviceId, ctBase64, token)
        log('encapsulated + posted ciphertext', { recipient: pk.producerId, ctBytes: ctBase64?.length })
        newSecrets.set(pk.producerId, sharedSecret)
        newSent.add(pk.producerId)
        persistSecrets(myId, epoch.epochId, deviceId, newSecrets)
        persistSent(myId, epoch.epochId, deviceId, newSent)
      }

      secretsRef.current = newSecrets
      sentToRef.current = newSent
      setSharedSecrets(newSecrets)
      setSentTo(newSent)
      log('encapsulation done', { sentTo: [...newSent], secretPartners: [...newSecrets.keys()] })
    } catch (e) {
      fail('encapsulation failed', e)
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId, epoch, deviceId, partnerKeys])

  // ── Step 3: decapsulate received ciphertexts (re-run if blob changed) ───────
  const performDecapsulation = useCallback(async () => {
    const kp = keyPairRef.current
    if (!kp) {
      warn('decapsulation skipped — no local key pair')
      return
    }
    setEncapBusy(true)
    setEncapError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const { ciphertexts } = await api.getCiphertexts(epoch.epochId, deviceId, token)
      log('decapsulation start', { receivedFrom: (ciphertexts ?? []).map((c) => c.senderId) })
      const newSecrets = new Map(secretsRef.current)
      const seen = new Map(ctSeenRef.current)

      for (const ct of ciphertexts ?? []) {
        if (seen.get(ct.senderId) === ct.ciphertextBase64 && newSecrets.has(ct.senderId)) {
          log('decapsulation skip (already done)', { sender: ct.senderId })
          continue
        }
        const ss = await decapsulate(ct.ciphertextBase64, kp.dkBase64)
        log('decapsulated ciphertext', { sender: ct.senderId })
        newSecrets.set(ct.senderId, ss)
        seen.set(ct.senderId, ct.ciphertextBase64)
      }

      persistSecrets(myId, epoch.epochId, deviceId, newSecrets)
      persistSeen(myId, epoch.epochId, deviceId, seen)
      secretsRef.current = newSecrets
      ctSeenRef.current = seen
      setSharedSecrets(newSecrets)
      setCtSeen(seen)
      log('decapsulation done', { secretPartners: [...newSecrets.keys()] })
    } catch (e) {
      fail('decapsulation failed', e)
      setEncapError(e.message)
    } finally {
      setEncapBusy(false)
    }
  }, [instance, account, myId, epoch, deviceId])

  // ── Step 4: load epoch + existing submissions ────────────────────────────────
  useEffect(() => {
    if (step !== 4 || !myId) return
    ;(async () => {
      try {
        const token = await api.acquireApiToken(instance, account)
        const my = await api.getMySubmissions(epoch.epochId, token)
        const done = new Set((my.submissions ?? []).map((s) => `${s.country}|${s.month}|${s.indicator}|${s.segment}`))
        log('loaded submissions', { cells: done.size })
        setSubmittedCells(done)
      } catch (e) {
        fail('load submissions failed', e)
        setLoadError(e.message)
      }
    })()
  }, [step, myId, instance, account?.homeAccountId])

  // ── Submit CSV ────────────────────────────────────────────────────────────────
  const onCsvPicked = async (event) => {
    const file = event.target.files?.[0]
    event.target.value = ''
    setSubmitError(null)
    setCsvResult(null)
    setCsvFileName('')
    if (!file) return
    if (!isCsvFile(file)) {
      setCsvResult({ ok: false, errors: ['File must be a .csv'], rows: [], monthMap: {}, preview: [] })
      setCsvFileName(file.name)
      return
    }
    setCsvFileName(file.name)
    try {
      const text = await file.text()
      const months = epochMonths(epoch)
      const result = parseAndValidateCsv(text, months)
      log('csv parsed', { ok: result.ok, rows: result.rows.length, errors: result.errors.length, monthMap: result.monthMap })
      setCsvResult(result)
    } catch (e) {
      fail('csv parse failed', e)
      setCsvResult({ ok: false, errors: [e.message || 'Failed to read CSV'], rows: [], monthMap: {}, preview: [] })
    }
  }

  const submitCsv = async () => {
    if (!csvResult?.ok || submitBusy) return
    setSubmitBusy(true)
    setSubmitError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const payload = []
      for (const row of csvResult.rows) {
        const masked = await calculateMaskedValue(
          row.value, row.country, row.month, row.indicator, row.segment, myId, sharedSecrets
        )
        payload.push({
          country: row.country,
          month: row.month,
          indicator: row.indicator,
          segment: row.segment,
          value: masked.toString(),
          epochId: epoch.epochId,
          signature: 'web-ui',
        })
      }
      log('submit batch', { rows: payload.length, epochId: epoch.epochId })
      await api.submitMetricsBatch(payload, token)
      const done = new Set(payload.map((s) => `${s.country}|${s.month}|${s.indicator}|${s.segment}`))
      setSubmittedCells((p) => new Set([...p, ...done]))
      const ep = await api.getEpoch(token)
      setEpoch(ep)
      if (ep.isClosed) log('epoch closed after submit', { epochId: ep.epochId })
      log('submit batch ok', { cells: done.size })
    } catch (e) {
      fail('submit batch failed', e)
      setSubmitError(e.message)
    } finally {
      setSubmitBusy(false)
    }
  }

  // ── Helpers ──────────────────────────────────────────────────────────────────
  const epochPartnerIds = epoch?.producerIds ?? []
  const expectedSmallerIds = epochPartnerIds.filter((id) => id < myId)
  const expectedLargerIds = epochPartnerIds.filter((id) => id > myId)
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
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 1 || keyPair || keyBusy || !epoch || !myId) return
    if (status?.myPublicKeyBase64) return
    log('auto: generate and register key')
    generateAndRegister()
  }, [hydrated, step, !!keyPair, keyBusy, !!epoch, epoch?.isClosed, myId, status?.myPublicKeyBase64])

  // Step 1 → 2: advance once all epoch partners registered AND we hold the matching private key
  useEffect(() => {
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 1 || !keyPair || !keysMatch) return
    if (!status?.isComplete || !status?.registeredPartners?.includes(myId)) return
    log('step 1 → 2 (all keys registered)')
    setStep(2)
  }, [hydrated, step, !!keyPair, keysMatch, status?.isComplete, status?.registeredCount, epoch?.isClosed])

  // Step 2: auto-encapsulate once the full epoch key set is present
  useEffect(() => {
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 2 || encapBusy || encapError || !epochKeysReady) return
    if (expectedSmallerIds.length === 0 || allEncapsDone) return
    log('auto: encapsulate', { expectedSmallerIds })
    performEncapsulation()
  }, [hydrated, step, epochKeysReady, encapBusy, encapError, allEncapsDone, epoch?.isClosed])

  // Step 2 → 3: wait for every smaller epoch partner, not a partial poll snapshot
  useEffect(() => {
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 2 || !epochKeysReady) return
    if (expectedSmallerIds.length === 0 || allEncapsDone) {
      log('step 2 → 3 (encapsulation complete)', { expectedSmallerIds, allEncapsDone })
      setStep(3)
    }
  }, [hydrated, step, epochKeysReady, allEncapsDone, expectedSmallerIds.length, epoch?.isClosed])

  // Step 3: auto-decapsulate whenever new ciphertexts arrive
  useEffect(() => {
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 3 || encapBusy || encapError || !keyPair || allDecapsDone) return
    if (expectedLargerIds.length === 0) return
    log('auto: decapsulate', { expectedLargerIds, actualCiphertexts: status?.actualCiphertexts })
    performDecapsulation()
  }, [hydrated, step, status?.actualCiphertexts, !!keyPair, encapError, allDecapsDone, epoch?.isClosed])

  // Step 3 → 4: advance when ciphertext exchange is complete and all secrets derived
  useEffect(() => {
    if (!hydrated || epoch?.isClosed || epoch?.isEligible === false || step !== 3) return
    if (epochPartnerIds.length < 2) return
    if (expectedLargerIds.length === 0 && exchangeComplete) {
      log('step 3 → 4 (nothing to receive, exchange complete)')
      setStep(4)
      return
    }
    if (exchangeComplete && allDecapsDone) {
      log('step 3 → 4 (decapsulation complete)')
      setStep(4)
    }
  }, [hydrated, step, exchangeComplete, allDecapsDone, expectedLargerIds.length, epochPartnerIds.length, epoch?.isClosed])

  const producerCount = epoch?.producerIds?.length ?? status?.expectedCount ?? 0

  const setupLabel = () => {
    if (keyBusy) return 'Generating key…'
    if (!status?.registeredPartners?.includes(myId)) return 'Registering…'
    if (!status?.isComplete) return 'Waiting for all producers…'
    return 'Preparing secure session…'
  }

  const renderStatusBar = () => (
    <div className="card" style={{ marginBottom: '1.5rem' }}>
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, minmax(0, 1fr))', gap: '1.25rem' }}>
        <div>
          <div className="form-label" style={{ marginBottom: 4 }}>Epoch</div>
          <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', flexWrap: 'wrap' }}>
            <span style={{ fontSize: '1.35rem', fontWeight: 700 }}>{epoch?.epochId ?? '—'}</span>
            {epoch?.isClosed && <span className="status-badge pending">Closed</span>}
          </div>
        </div>
        <div>
          <div className="form-label" style={{ marginBottom: 4 }}>Producers</div>
          <div style={{ fontSize: '1.35rem', fontWeight: 700 }}>{producerCount}</div>
        </div>
        <div>
          <div className="form-label" style={{ marginBottom: 4 }}>Your OID</div>
          <code className="text-accent" style={{ fontSize: '0.85rem', wordBreak: 'break-all' }}>{myId || '—'}</code>
        </div>
      </div>
    </div>
  )

  const renderSetup = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">⏳</span>
        <h2 className="card-title">{setupLabel()}</h2>
      </div>

      {(keyError || encapError) && (
        <div className="info-box error" style={{ marginBottom: '1rem' }}>
          ⚠️ {keyError || encapError}
          {encapError && (
            <button
              className="btn btn-secondary"
              onClick={step === 2 ? performEncapsulation : performDecapsulation}
              disabled={encapBusy}
              style={{ marginLeft: '1rem', padding: '0.25rem 0.6rem', fontSize: '0.8rem' }}
            >
              Retry
            </button>
          )}
        </div>
      )}

      <div style={{ display: 'flex', gap: '0.75rem', flexWrap: 'wrap' }}>
        <button className="btn btn-secondary" onClick={generateAndRegister} disabled={keyBusy} style={{ fontSize: '0.85rem' }}>
          {keyBusy ? '⏳ Working…' : keyPair ? '🔄 Re-register Key' : '⚡ Generate Key'}
        </button>
        <button className="btn btn-secondary text-danger" onClick={resetLocalState} style={{ fontSize: '0.8rem' }}>
          🗑 Reset Local State
        </button>
      </div>
    </div>
  )

  const renderSubmit = () => {
    const months = epochMonths(epoch) ?? []
    const allDone = submittedCells.size >= CELL_COUNT
    const errors = csvResult?.errors ?? []
    const shownErrors = errors.slice(0, ERROR_DISPLAY_CAP)
    const monthEntries = Object.entries(csvResult?.monthMap ?? {})
    return (
      <div className="card animate-fade-in">
        <div className="card-header">
          <span className="card-icon">📊</span>
          <h2 className="card-title">Submit Data</h2>
          {months.length > 0 && (
            <span className="text-muted" style={{ marginLeft: 'auto', fontSize: '0.875rem' }}>
              {months[0]} – {months[months.length - 1]}
            </span>
          )}
        </div>

        <div className="info-box">
          Upload a CSV of unmasked values. Noise from your shared secrets is applied in the browser before
          submission — the aggregator only sees masked values. Values must be positive integers (no zeros or negatives).
        </div>

        {loadError && (
          <div className="info-box error">
            ⚠️ {loadError}
          </div>
        )}

        {allDone ? (
          <div className="info-box ok">
            ✅ All {CELL_COUNT} cells submitted. Waiting for the remaining producers…
          </div>
        ) : (
          <>
            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap', marginBottom: '1rem' }}>
              <a className="btn btn-secondary" href="/sample.csv" download="sample.csv" style={{ fontSize: '0.85rem' }}>
                Download sample.csv
              </a>
              <label className="btn btn-secondary" style={{ fontSize: '0.85rem', marginBottom: 0, cursor: 'pointer' }}>
                Choose CSV
                <input
                  type="file"
                  accept=".csv,text/csv"
                  onChange={onCsvPicked}
                  disabled={submitBusy}
                  style={{ display: 'none' }}
                />
              </label>
              {csvFileName && <span className="text-muted" style={{ fontSize: '0.85rem' }}>{csvFileName}</span>}
            </div>

            {errors.length > 0 && (
              <div className="info-box error" style={{ maxHeight: 240, overflowY: 'auto' }}>
                <div style={{ fontWeight: 600, marginBottom: 6 }}>CSV validation failed</div>
                <ul style={{ margin: 0, paddingLeft: '1.2rem' }}>
                  {shownErrors.map((err, i) => <li key={i}>{err}</li>)}
                </ul>
                {errors.length > ERROR_DISPLAY_CAP && (
                  <div style={{ marginTop: 6 }}>and {errors.length - ERROR_DISPLAY_CAP} more</div>
                )}
              </div>
            )}

            {csvResult?.ok && (
              <>
                <div className="info-box ok">
                  Valid file: {CELL_COUNT} cells. Months remapped
                  {monthEntries.length > 0 && (
                    <>: {monthEntries.map(([from, to]) => `${from} → ${to}`).join(', ')}</>
                  )}
                </div>
                <div style={{ overflowX: 'auto', marginBottom: '1rem' }}>
                  <table className="results-table">
                    <thead>
                      <tr>
                        <th>Country</th>
                        <th>CSV month</th>
                        <th>Epoch month</th>
                        <th>Indicator</th>
                        <th>Segment</th>
                        <th>Value</th>
                      </tr>
                    </thead>
                    <tbody>
                      {(csvResult.preview ?? []).map((r, i) => (
                        <tr key={i}>
                          <td>{r.country}</td>
                          <td>{r.csvMonth}</td>
                          <td>{r.month}</td>
                          <td>{r.indicator}</td>
                          <td>{r.segment}</td>
                          <td>{formatInt(r.value)}</td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
                <div className="text-muted" style={{ fontSize: '0.8rem', marginBottom: '1rem' }}>
                  Showing first {csvResult.preview.length} of {CELL_COUNT} rows
                </div>
              </>
            )}

            {submitError && (
              <div className="info-box error">
                ⚠️ {submitError}
              </div>
            )}

            <button
              className="btn btn-primary"
              onClick={submitCsv}
              disabled={!csvResult?.ok || submitBusy}
            >
              {submitBusy ? 'Submitting…' : 'Submit CSV'}
            </button>
          </>
        )}
      </div>
    )
  }

  const renderWaiting = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">✅</span>
        <h2 className="card-title">Waiting for a new epoch</h2>
      </div>
      <div className="info-box">
        Epoch {epoch?.epochId} is closed — every producer has submitted. This page will continue when an admin creates a new epoch.
      </div>
    </div>
  )

  const renderNotEligible = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">🔒</span>
        <h2 className="card-title">Not eligible for this epoch</h2>
      </div>
      <div className="info-box">
        Your account is not listed as a participant in epoch {epoch?.epochId}. You can view its status, but you cannot exchange keys or submit metrics.
      </div>
    </div>
  )

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <h1 className="page-title">Protocol</h1>
        <p className="page-subtitle">Submit privacy-preserving metrics for the current epoch</p>
      </div>

      {renderStatusBar()}
      {epoch?.isEligible === false
        ? renderNotEligible()
        : epoch?.isClosed
          ? renderWaiting()
          : step < 4 ? renderSetup() : renderSubmit()}
    </div>
  )
}
