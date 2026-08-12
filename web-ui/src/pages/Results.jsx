import { useState, useEffect } from 'react'
import { useMsal } from '@azure/msal-react'
import * as api from '../utils/api'

const COUNTRIES = ['US', 'GB', 'DE']

function epochMonths(epoch) {
  if (!epoch?.startDate) return []
  return Array.from({ length: 3 }, (_, i) => {
    const d = new Date(epoch.startDate)
    d.setUTCMonth(d.getUTCMonth() + i)
    return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}`
  })
}

export default function Results() {
  const { instance, accounts } = useMsal()
  const account = accounts[0]

  const [epoch, setEpoch] = useState(null)
  const [country, setCountry] = useState(COUNTRIES[0])
  const [month, setMonth] = useState('')
  const [result, setResult] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)

  useEffect(() => {
    api.acquireApiToken(instance, account)
      .then(token => api.getEpoch(token))
      .then(ep => {
        setEpoch(ep)
        const months = epochMonths(ep)
        if (months.length) setMonth(months[0])
      })
      .catch(() => {})
  }, [])

  const months = epochMonths(epoch)

  const fetchResult = async () => {
    setLoading(true)
    setError(null)
    setResult(null)
    try {
      const token = await api.acquireApiToken(instance, account)
      const data = await api.getAggregate(country, month, token)
      setResult(data)
    } catch (e) {
      setError(e.message)
    } finally {
      setLoading(false)
    }
  }

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <h1 className="page-title">📊 Aggregation Results</h1>
        <p className="page-subtitle">View the aggregate total once all partners have submitted</p>
      </div>

      <div className="card">
        <div className="card-header">
          <span className="card-icon">🔍</span>
          <h2 className="card-title">Query</h2>
        </div>
        <div className="grid-2">
          <div className="form-group">
            <label className="form-label">Country</label>
            <select className="form-select" value={country} onChange={(e) => setCountry(e.target.value)}>
              {COUNTRIES.map((c) => <option key={c}>{c}</option>)}
            </select>
          </div>
          <div className="form-group">
            <label className="form-label">Month</label>
            <select className="form-select" value={month} onChange={(e) => setMonth(e.target.value)}>
              {months.map((m) => <option key={m}>{m}</option>)}
            </select>
          </div>
        </div>
        <div style={{ textAlign: 'center' }}>
          <button className="btn btn-primary" onClick={fetchResult} disabled={loading}>
            {loading ? '⏳ Loading…' : '🔍 Fetch Results'}
          </button>
        </div>
      </div>

      {error && (
        <div className="info-box" style={{ background: 'rgba(248,113,113,0.1)', borderColor: 'rgba(248,113,113,0.3)', color: '#fca5a5' }}>
          ⚠️ {error}
        </div>
      )}

      {result && (
        <div className="card animate-fade-in">
          <div className="card-header">
            <span className="card-icon">📈</span>
            <h2 className="card-title">{result.country} — {result.month}</h2>
            <div style={{ marginLeft: 'auto' }}>
              <span className={`status-badge ${result.status === 'complete' ? 'success' : 'pending'}`}>
                {result.status === 'complete' ? '✅ Complete' : '⏳ Incomplete'}
              </span>
            </div>
          </div>

          {result.status === 'complete' ? (
            <>
              <div className="summary-box" style={{ marginBottom: '1.5rem' }}>
                <div className="summary-label">Aggregated MAU Total</div>
                <div className="summary-value" style={{ color: '#4ade80' }}>
                  {result.total?.toLocaleString()}
                </div>
              </div>
              <div className="info-box" style={{ background: 'rgba(74,222,128,0.1)', borderColor: 'rgba(74,222,128,0.3)', color: '#86efac', marginBottom: 0 }}>
                ✨ All {result.submissionCount} partners submitted. Noise cancelled perfectly — only the true total is revealed.
              </div>
            </>
          ) : (
            <div>
              <div className="grid-2" style={{ marginBottom: '1.5rem' }}>
                <div className="summary-box">
                  <div className="summary-label">Submissions Received</div>
                  <div className="summary-value">{result.submissionCount} / {result.expectedSubmissions}</div>
                </div>
                <div className="summary-box" style={{ background: 'rgba(251,191,36,0.1)', borderColor: 'rgba(251,191,36,0.3)' }}>
                  <div className="summary-label">Aggregate Total</div>
                  <div className="summary-value" style={{ color: '#fbbf24' }}>Pending…</div>
                </div>
              </div>
              {result.missingProducers?.length > 0 && (
                <div className="info-box" style={{ background: 'rgba(251,191,36,0.1)', borderColor: 'rgba(251,191,36,0.3)', color: '#fde047', marginBottom: 0 }}>
                  Waiting for: {result.missingProducers.join(', ')}
                </div>
              )}
            </div>
          )}
        </div>
      )}
    </div>
  )
}
