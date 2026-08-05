import { useState, useEffect, useCallback } from 'react'
import { useMsal } from '@azure/msal-react'
import * as api from '../utils/api'
import { generateMlKemKeyPair, encapsulate, decapsulate, bytesToBase64, base64ToBytes } from '../utils/crypto'
import { calculateMaskedValue } from '../utils/noise'

const COUNTRIES = ['US', 'GB', 'DE']
const MONTHS = ['2026-06', '2026-07', '2026-08']

const kpKey = (id) => `mlkem_kp_${id}`
const ssKey = (id) => `mlkem_ss_${id}`
const ctKey = (id) => `mlkem_ct_${id}`

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

  // ── Restore persisted state ─────────────────────────────────────────────────
  useEffect(() => {
    if (!myId) return
    const kp = localStorage.getItem(kpKey(myId))
    if (kp) setKeyPair(JSON.parse(kp))

    const ss = localStorage.getItem(ssKey(myId))
    if (ss) {
      const parsed = JSON.parse(ss)
      setSharedSecrets(new Map(Object.entries(parsed).map(([k, v]) => [k, base64ToBytes(v)])))
    }

    const ct = localStorage.getItem(ctKey(myId))
    if (ct) setSentTo(new Set(JSON.parse(ct)))
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
        const [s, pk] = await Promise.all([
          api.getKeyExchangeStatus(token),
          api.getPartnerKeys(myId, token),
        ])
        if (!alive) return
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

  // ── Step 1: generate & register key pair ────────────────────────────────────
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
        const [ep, my] = await Promise.all([
          api.getEpoch(token),
          api.getMySubmissions(token),
        ])
        setEpoch(ep)
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
    if (isNaN(raw)) {
      setCellErrors((p) => ({ ...p, [cellId]: 'Enter a numeric value' }))
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
    for (const country of COUNTRIES) {
      for (const month of MONTHS) {
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
      </div>

      <div className="info-box">
        <span className="info-box-icon">👤</span>
        Your producer ID (Azure AD OID): <code style={{ color: '#93c5fd' }}>{myId}</code>
      </div>

      <div style={{ marginBottom: '1.5rem' }}>
        {dot(!!keyPair)} Key pair {keyPair ? 'generated and stored locally' : 'not yet generated'}
        <br />
        {dot(status?.registeredPartners?.includes(myId))} Public key registered with API
      </div>

      {keyError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5' }}>
          ⚠️ {keyError}
        </div>
      )}

      <button className="btn btn-primary" onClick={generateAndRegister} disabled={keyBusy}
        style={{ marginBottom: '1.5rem' }}>
        {keyBusy ? '⏳ Working…' : keyPair ? '🔄 Re-register Public Key' : '⚡ Generate & Register Key Pair'}
      </button>

      {status && (
        <div style={{ marginBottom: '1.5rem' }}>
          <div style={{ fontWeight: 600, marginBottom: '0.75rem', color: '#d4d4d8' }}>
            Key Registration Status ({status.registeredCount}/{status.expectedCount})
          </div>
          {(status.registeredPartners ?? []).map((id) => (
            <div key={id} style={{ padding: '0.5rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', fontSize: '0.875rem' }}>
              {dot(true)} <code style={{ color: '#a78bfa' }}>{id}</code>
              {id === myId && <span style={{ color: '#6b7280', marginLeft: 8 }}>(you)</span>}
            </div>
          ))}
          {(status.missingPartners ?? []).map((id) => (
            <div key={id} style={{ padding: '0.5rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', fontSize: '0.875rem', color: '#71717a' }}>
              {dot(false)} <code>{id}</code> — waiting
            </div>
          ))}
        </div>
      )}

      <button className="btn btn-primary" onClick={() => setStep(2)}
        disabled={!status?.isComplete}
        title={!status?.isComplete ? 'Waiting for all partners to register' : undefined}>
        Continue to Encapsulation →
      </button>
    </div>
  )

  // ── Step 2 ────────────────────────────────────────────────────────────────────
  const renderStep2 = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">📤</span>
        <h2 className="card-title">Step 2 — Encapsulation (Send Ciphertexts)</h2>
      </div>

      <div className="info-box">
        Send an ML-KEM ciphertext to each partner whose ID is smaller than yours. 
        They will decapsulate it to derive your shared secret.
      </div>

      {smallerIdPartners.length === 0 ? (
        <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac' }}>
          ✨ You have the smallest producer ID — nothing to send. Proceed to Step 3.
        </div>
      ) : (
        <>
          {smallerIdPartners.map((pk) => (
            <div key={pk.producerId} style={{ display: 'flex', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', gap: '0.75rem' }}>
              {dot(sentTo.has(pk.producerId))}
              <code style={{ color: '#a78bfa', flex: 1 }}>{pk.producerId}</code>
              <span className={`status-badge ${sentTo.has(pk.producerId) ? 'success' : 'pending'}`}>
                {sentTo.has(pk.producerId) ? 'Sent ✓' : 'Pending'}
              </span>
            </div>
          ))}
          <div style={{ marginTop: '1rem' }}>
            {largerIdPartners.length > 0 && (
              <div style={{ color: '#71717a', fontSize: '0.875rem', marginBottom: '0.75rem' }}>
                Partners who will send to you: {largerIdPartners.map((p) => p.producerId).join(', ')}
              </div>
            )}
          </div>
        </>
      )}

      {encapError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginTop: '1rem' }}>
          ⚠️ {encapError}
        </div>
      )}

      <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem' }}>
        <button className="btn btn-secondary" onClick={() => setStep(1)}>← Back</button>
        {smallerIdPartners.length > 0 && (
          <button className="btn btn-primary" onClick={performEncapsulation} disabled={encapBusy || allEncapsDone}>
            {encapBusy ? '⏳ Encapsulating…' : allEncapsDone ? '✓ All ciphertexts sent' : '📤 Encapsulate & Send All'}
          </button>
        )}
        <button className="btn btn-primary" onClick={() => setStep(3)}
          disabled={smallerIdPartners.length > 0 && !allEncapsDone}>
          Continue to Decapsulation →
        </button>
      </div>
    </div>
  )

  // ── Step 3 ────────────────────────────────────────────────────────────────────
  const renderStep3 = () => (
    <div className="card animate-fade-in">
      <div className="card-header">
        <span className="card-icon">📥</span>
        <h2 className="card-title">Step 3 — Decapsulation (Receive Ciphertexts)</h2>
      </div>

      <div className="info-box">
        Partners with larger IDs will send ciphertexts to you. Decapsulate each one 
        using your private key to derive the shared secret.
      </div>

      {largerIdPartners.length === 0 ? (
        <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac' }}>
          ✨ You have the largest producer ID — nothing to receive. Proceed once all ciphertexts are sent.
        </div>
      ) : (
        largerIdPartners.map((pk) => (
          <div key={pk.producerId} style={{ display: 'flex', alignItems: 'center', padding: '0.75rem 0', borderBottom: '1px solid rgba(255,255,255,0.06)', gap: '0.75rem' }}>
            {dot(sharedSecrets.has(pk.producerId))}
            <code style={{ color: '#a78bfa', flex: 1 }}>{pk.producerId}</code>
            <span className={`status-badge ${sharedSecrets.has(pk.producerId) ? 'success' : 'pending'}`}>
              {sharedSecrets.has(pk.producerId) ? 'Decrypted ✓' : 'Waiting'}
            </span>
          </div>
        ))
      )}

      {status && (
        <div style={{ marginTop: '1.25rem', padding: '1rem', background: 'rgba(0,0,0,0.2)', borderRadius: 8, fontSize: '0.875rem' }}>
          Ciphertexts: {status.actualCiphertexts}/{status.expectedCiphertexts} &nbsp;·&nbsp;
          Exchange complete: {exchangeComplete ? '✅ Yes' : '⏳ No'}
        </div>
      )}

      {encapError && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginTop: '1rem' }}>
          ⚠️ {encapError}
        </div>
      )}

      <div style={{ display: 'flex', gap: '0.75rem', marginTop: '1.5rem' }}>
        <button className="btn btn-secondary" onClick={() => setStep(2)}>← Back</button>
        <button className="btn btn-primary" onClick={performDecapsulation} disabled={encapBusy || allDecapsDone}>
          {encapBusy ? '⏳ Decapsulating…' : allDecapsDone ? '✓ All secrets derived' : '🔓 Decapsulate Received Ciphertexts'}
        </button>
        <button className="btn btn-primary" onClick={() => setStep(4)} disabled={!exchangeComplete}>
          Continue to Submit Data →
        </button>
      </div>
    </div>
  )

  // ── Step 4 ────────────────────────────────────────────────────────────────────
  const renderStep4 = () => {
    const allDone = COUNTRIES.every((c) => MONTHS.every((m) => submittedCells.has(`${c}|${m}`)))
    return (
      <div className="card animate-fade-in">
        <div className="card-header">
          <span className="card-icon">📊</span>
          <h2 className="card-title">Step 4 — Submit Data</h2>
          {epoch && (
            <span style={{ marginLeft: 'auto', color: '#71717a', fontSize: '0.875rem' }}>
              Epoch {epoch.epochId}
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
                {MONTHS.map((m) => <th key={m}>{m}</th>)}
              </tr>
            </thead>
            <tbody>
              {COUNTRIES.map((country) => (
                <tr key={country}>
                  <td style={{ fontWeight: 600, color: '#d4d4d8' }}>{country}</td>
                  {MONTHS.map((month) => {
                    const id = `${country}|${month}`
                    const done = submittedCells.has(id)
                    const busy = busyCells.has(id)
                    const err = cellErrors[id]
                    return (
                      <td key={month}>
                        {done ? (
                          <span className="status-badge success">✓ Submitted</span>
                        ) : (
                          <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
                            <input
                              type="number"
                              className="form-input"
                              style={{ width: 110, padding: '0.4rem 0.6rem' }}
                              placeholder="MAU"
                              value={values[id] ?? ''}
                              onChange={(e) => setValues((p) => ({ ...p, [id]: e.target.value }))}
                              disabled={busy}
                            />
                            <button
                              className="btn btn-secondary"
                              style={{ padding: '0.4rem 0.75rem', fontSize: '0.8rem' }}
                              onClick={() => submitCell(country, month)}
                              disabled={busy}>
                              {busy ? '…' : '→'}
                            </button>
                          </div>
                        )}
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
            ✅ All 9 cells submitted! Ask your admin to check the aggregate results.
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
