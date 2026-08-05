import { useState, useEffect, useCallback } from 'react'
import { useMsal } from '@azure/msal-react'
import * as api from '../utils/api'

export default function AdminPanel() {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  const myId = account?.localAccountId ?? ''

  // ── Registered partners ─────────────────────────────────────────────────────
  const [registered, setRegistered] = useState([])      // { producerId, displayName }
  const [regLoading, setRegLoading] = useState(false)
  const [regError, setRegError] = useState(null)

  // selected OIDs + their clientSecret values
  const [selected, setSelected] = useState(new Set())
  const [secrets, setSecrets] = useState({})            // producerId → clientSecret

  // ── Epoch creation ──────────────────────────────────────────────────────────
  const [startMonth, setStartMonth] = useState(() => {
    const d = new Date()
    return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}`
  })
  const [epochBusy, setEpochBusy] = useState(false)
  const [epochResult, setEpochResult] = useState(null)
  const [epochError, setEpochError] = useState(null)
  const [confirmReset, setConfirmReset] = useState(false)

  // ── Clear all ───────────────────────────────────────────────────────────────
  const [clearBusy, setClearBusy] = useState(false)
  const [clearConfirm, setClearConfirm] = useState(false)

  // ── Aggregates ──────────────────────────────────────────────────────────────
  const [aggregates, setAggregates] = useState(null)
  const [aggLoading, setAggLoading] = useState(false)
  const [aggError, setAggError] = useState(null)

  const loadRegistered = useCallback(async () => {
    setRegLoading(true)
    setRegError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const data = await api.getProducers(token)
      setRegistered(data)
    } catch (e) {
      setRegError(e.message)
    } finally {
      setRegLoading(false)
    }
  }, [instance, account])

  const loadAggregates = useCallback(async () => {
    setAggLoading(true)
    setAggError(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const data = await api.adminGetAggregates(token)
      setAggregates(data)
    } catch (e) {
      setAggError(e.message)
    } finally {
      setAggLoading(false)
    }
  }, [instance, account])

  useEffect(() => {
    loadRegistered()
    loadAggregates()
  }, [])

  const toggleSelect = (id) => setSelected((prev) => {
    const next = new Set(prev)
    next.has(id) ? next.delete(id) : next.add(id)
    return next
  })

  const toggleAll = () => {
    if (selected.size === registered.length) {
      setSelected(new Set())
    } else {
      setSelected(new Set(registered.map((p) => p.producerId)))
    }
  }

  const handleCreateEpoch = async () => {
    setEpochBusy(true)
    setEpochResult(null)
    setEpochError(null)
    try {
      const producers = [...selected].map((id) => {
        const p = registered.find((r) => r.producerId === id)
        return { producerId: id, displayName: p?.displayName ?? id, clientSecret: secrets[id] ?? '' }
      })
      const token = await api.acquireApiToken(instance, account)
      const data = await api.adminResetAndCreateEpoch({ startMonth, producers }, token)
      setEpochResult(data)
      setSelected(new Set())
      setSecrets({})
      loadRegistered()
      loadAggregates()
    } catch (e) {
      setEpochError(e.message)
    } finally {
      setEpochBusy(false)
      setConfirmReset(false)
    }
  }

  const handleClearAll = async () => {
    setClearBusy(true)
    try {
      const token = await api.acquireApiToken(instance, account)
      await api.adminReset(token)
      setRegistered([])
      setSelected(new Set())
      setSecrets({})
      setAggregates(null)
    } catch (e) {
      setRegError(e.message)
    } finally {
      setClearBusy(false)
      setClearConfirm(false)
    }
  }

  const allSelected = registered.length > 0 && selected.size === registered.length
  const selectedList = registered.filter((p) => selected.has(p.producerId))
  const canCreateEpoch = selected.size >= 2 && selectedList.every((p) => (secrets[p.producerId] ?? '').trim())

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <h1 className="page-title">⚙️ Admin Panel</h1>
        <p className="page-subtitle">Manage epochs, producers, and view aggregate results</p>
      </div>

      {/* Registered Partners */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">👥</span>
          <h2 className="card-title">Registered Partners</h2>
          <button className="btn btn-secondary" style={{ marginLeft: 'auto', padding: '0.35rem 0.75rem', fontSize: '0.8rem' }}
            onClick={loadRegistered} disabled={regLoading}>
            {regLoading ? '⏳' : '↻ Refresh'}
          </button>
        </div>

        <div className="info-box" style={{ marginBottom: '1rem' }}>
          <span className="info-box-icon">ℹ️</span>
          Partners appear here automatically when they log in and visit the Protocol page.
        </div>

        {regError && (
          <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginBottom: '0.75rem' }}>
            ⚠️ {regError}
          </div>
        )}

        {registered.length === 0 && !regLoading ? (
          <div style={{ color: '#71717a', textAlign: 'center', padding: '1.5rem 0' }}>
            No partners registered yet. Partners must log in and visit the Protocol page first.
          </div>
        ) : (
          <table className="results-table" style={{ marginBottom: '1rem' }}>
            <thead>
              <tr>
                <th style={{ width: 40 }}>
                  <input type="checkbox" checked={allSelected} onChange={toggleAll} />
                </th>
                <th>Display Name</th>
                <th>OID (Producer ID)</th>
                <th>Joined</th>
              </tr>
            </thead>
            <tbody>
              {registered.map((p) => (
                <tr key={p.producerId} style={{ cursor: 'pointer' }} onClick={() => toggleSelect(p.producerId)}>
                  <td onClick={(e) => e.stopPropagation()}>
                    <input type="checkbox" checked={selected.has(p.producerId)}
                      onChange={() => toggleSelect(p.producerId)} />
                  </td>
                  <td style={{ fontWeight: 600 }}>{p.displayName}</td>
                  <td style={{ fontFamily: 'monospace', fontSize: '0.8rem', color: '#a1a1aa' }}>{p.producerId}</td>
                  <td style={{ color: '#71717a', fontSize: '0.85rem' }}>
                    {p.joinedDate ? new Date(p.joinedDate).toLocaleDateString() : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}

        {/* Clear All Data */}
        <div style={{ borderTop: '1px solid rgba(255,255,255,0.06)', paddingTop: '1rem', marginTop: '0.5rem' }}>
          {!clearConfirm ? (
            <button className="btn btn-secondary" style={{ fontSize: '0.8rem', color: '#f87171' }}
              onClick={() => setClearConfirm(true)}>
              🗑 Clear All Data
            </button>
          ) : (
            <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
              <span style={{ color: '#fde047', fontSize: '0.875rem' }}>Wipes all producers, keys, ciphertexts and submissions.</span>
              <button className="btn btn-primary" onClick={handleClearAll} disabled={clearBusy}>
                {clearBusy ? '⏳' : '⚠️ Confirm Clear'}
              </button>
              <button className="btn btn-secondary" onClick={() => setClearConfirm(false)}>Cancel</button>
            </div>
          )}
        </div>
      </div>

      {/* Create Epoch */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">🔄</span>
          <h2 className="card-title">Create Epoch</h2>
        </div>
        <div className="info-box" style={{ background: 'rgba(251,191,36,0.1)', borderColor: 'rgba(251,191,36,0.3)', color: '#fde047', marginBottom: '1rem' }}>
          ⚠️ This wipes ALL existing protocol data (keys, ciphertexts, submissions) and starts a fresh epoch with the selected partners.
        </div>

        <div className="form-group">
          <label className="form-label">Start Month</label>
          <input type="month" className="form-input" style={{ maxWidth: 200 }}
            value={startMonth} onChange={(e) => setStartMonth(e.target.value)} />
        </div>

        {selected.size === 0 ? (
          <div style={{ color: '#71717a', padding: '0.75rem 0' }}>
            Select at least 2 partners above to continue.
          </div>
        ) : (
          <div style={{ marginBottom: '1rem' }}>
            <div style={{ color: '#d4d4d8', fontWeight: 600, marginBottom: '0.5rem' }}>
              Client Secrets for {selected.size} selected partner{selected.size !== 1 ? 's' : ''}
            </div>
            {selectedList.map((p) => (
              <div key={p.producerId} style={{ display: 'grid', gridTemplateColumns: '1fr 2fr', gap: '0.5rem', marginBottom: '0.5rem', alignItems: 'center' }}>
                <span style={{ color: '#a1a1aa', fontSize: '0.875rem', overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                  {p.displayName}
                </span>
                <input className="form-input" type="password" placeholder="Client secret"
                  value={secrets[p.producerId] ?? ''}
                  onChange={(e) => setSecrets((s) => ({ ...s, [p.producerId]: e.target.value }))} />
              </div>
            ))}
          </div>
        )}

        {epochError && (
          <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5', marginBottom: '0.75rem' }}>
            ⚠️ {epochError}
          </div>
        )}

        {epochResult && (
          <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac', marginBottom: '0.75rem' }}>
            ✅ Epoch {epochResult.epoch?.epochId} created with {epochResult.producers?.length} producers.
          </div>
        )}

        {!confirmReset ? (
          <button className="btn btn-primary" onClick={() => setConfirmReset(true)} disabled={!canCreateEpoch}>
            🔄 Reset & Create Epoch
          </button>
        ) : (
          <div style={{ display: 'flex', gap: '0.75rem', alignItems: 'center' }}>
            <span style={{ color: '#fde047' }}>Are you sure? All protocol data will be deleted.</span>
            <button className="btn btn-primary" onClick={handleCreateEpoch} disabled={epochBusy}>
              {epochBusy ? '⏳ Working…' : '⚠️ Confirm'}
            </button>
            <button className="btn btn-secondary" onClick={() => setConfirmReset(false)}>Cancel</button>
          </div>
        )}
      </div>

      {/* Aggregates */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">📊</span>
          <h2 className="card-title">Latest Epoch Aggregates</h2>
          <button className="btn btn-secondary" style={{ marginLeft: 'auto', padding: '0.35rem 0.75rem', fontSize: '0.8rem' }}
            onClick={loadAggregates} disabled={aggLoading}>
            {aggLoading ? '⏳' : '↻ Refresh'}
          </button>
        </div>

        {aggError && (
          <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5' }}>
            ⚠️ {aggError}
          </div>
        )}

        {aggregates && (
          <>
            <div style={{ color: '#71717a', fontSize: '0.875rem', marginBottom: '1rem' }}>
              Epoch {aggregates.epochId} · {aggregates.partnerCount} partners: {aggregates.partners?.join(', ')}
            </div>

            {aggregates.aggregates?.length > 0 ? (
              <table className="results-table">
                <thead>
                  <tr>
                    <th>Country</th>
                    <th>Month</th>
                    <th>Status</th>
                    <th>Total</th>
                    <th>Submissions</th>
                  </tr>
                </thead>
                <tbody>
                  {aggregates.aggregates.map((r) => (
                    <tr key={`${r.country}-${r.month}`}>
                      <td style={{ fontWeight: 600 }}>{r.country}</td>
                      <td>{r.month}</td>
                      <td>
                        <span className={`status-badge ${r.status === 'complete' ? 'success' : 'pending'}`}>
                          {r.status}
                        </span>
                      </td>
                      <td style={{ color: r.status === 'complete' ? '#4ade80' : '#71717a' }}>
                        {r.total != null ? r.total.toLocaleString() : '—'}
                      </td>
                      <td>{r.submissionCount}/{r.expectedSubmissions}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            ) : (
              <div style={{ color: '#71717a', textAlign: 'center', padding: '1rem' }}>No submissions yet.</div>
            )}
          </>
        )}
      </div>
    </div>
  )
}
