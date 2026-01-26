import { useState, useEffect } from 'react'
import { getAggregate, getEpoch } from '../utils/api'

const COUNTRIES = ['USA', 'UK', 'Germany', 'France', 'Japan', 'Brazil', 'India']

function Results() {
  const [country, setCountry] = useState('USA')
  const [month, setMonth] = useState(() => {
    const now = new Date()
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  })
  const [result, setResult] = useState(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState(null)
  const [epoch, setEpoch] = useState(null)

  // Fetch epoch on mount
  useEffect(() => {
    async function fetchEpoch() {
      try {
        const epochData = await getEpoch()
        setEpoch(epochData)
      } catch (err) {
        // Silently handle - will show error when fetching results
      }
    }
    fetchEpoch()
  }, [])

  const fetchResults = async () => {
    setLoading(true)
    setError(null)
    setResult(null)
    
    try {
      // Parse month string (e.g., "2026-01") and create date with explicit UTC to avoid timezone issues
      const [year, monthNum] = month.split('-').map(Number)
      const monthDate = new Date(Date.UTC(year, monthNum - 1, 1))
      const data = await getAggregate(country, monthDate)
      setResult(data)
    } catch (err) {
      setError('Failed to fetch results. Make sure the backend is running on port 5149.')
    } finally {
      setLoading(false)
    }
  }

  const formatNumber = (num) => {
    return num?.toLocaleString() ?? '—'
  }

  const formatDate = (dateStr) => {
    const date = new Date(dateStr)
    return date.toLocaleDateString('en-US', { year: 'numeric', month: 'long' })
  }

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <h1 className="page-title">📊 Aggregation Results</h1>
        <p className="page-subtitle">View the aggregated total when all partners have submitted</p>
      </div>

      {/* Query Card */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">🔍</span>
          <h2 className="card-title">Query Parameters</h2>
        </div>

        <div className="grid-2">
          <div className="form-group">
            <label className="form-label">Country</label>
            <select 
              className="form-select" 
              value={country} 
              onChange={(e) => setCountry(e.target.value)}
            >
              {COUNTRIES.map(c => (
                <option key={c} value={c}>{c}</option>
              ))}
            </select>
          </div>

          <div className="form-group">
            <label className="form-label">Month</label>
            <input
              type="month"
              className="form-input"
              value={month}
              onChange={(e) => setMonth(e.target.value)}
            />
          </div>
        </div>

        <div style={{ textAlign: 'center', marginTop: '1rem' }}>
          <button 
            className="btn btn-primary" 
            onClick={fetchResults}
            disabled={loading}
          >
            {loading ? '⏳ Loading...' : '🔍 Fetch Results'}
          </button>
        </div>
      </div>

      {error && (
        <div className="info-box" style={{ background: 'rgba(248, 113, 113, 0.1)', borderColor: 'rgba(248, 113, 113, 0.3)', color: '#fca5a5' }}>
          <span className="info-box-icon">⚠️</span>
          {error}
        </div>
      )}

      {/* Results Card */}
      {result && (
        <div className="card animate-fade-in">
          <div className="card-header">
            <span className="card-icon">📈</span>
            <h2 className="card-title">Results for {result.country} — {formatDate(result.month)}</h2>
            <div style={{ marginLeft: 'auto' }}>
              <span className={`status-badge ${result.status === 'complete' ? 'success' : 'pending'}`}>
                {result.status === 'complete' ? '✅ Complete' : '⏳ Incomplete'}
              </span>
            </div>
          </div>

          {result.status === 'complete' ? (
            <>
              <div className="summary-box" style={{ marginBottom: '1.5rem' }}>
                <div className="summary-label">Aggregated Total (Noise Canceled!)</div>
                <div className="summary-value" style={{ color: '#4ade80' }}>
                  {formatNumber(result.total)}
                </div>
              </div>

              <div className="info-box" style={{ background: 'rgba(74, 222, 128, 0.1)', borderColor: 'rgba(74, 222, 128, 0.3)', color: '#86efac' }}>
                <span className="info-box-icon">✨</span>
                All {result.submissionCount} partners have submitted. The noise has perfectly canceled out, 
                revealing only the true aggregate total — individual values remain private!
              </div>
            </>
          ) : (
            <>
              <div className="grid-2" style={{ marginBottom: '1.5rem' }}>
                <div className="summary-box">
                  <div className="summary-label">Submissions Received</div>
                  <div className="summary-value">
                    {result.submissionCount} / {result.expectedSubmissions}
                  </div>
                </div>
                
                <div className="summary-box" style={{ background: 'rgba(251, 191, 36, 0.1)', borderColor: 'rgba(251, 191, 36, 0.3)' }}>
                  <div className="summary-label">Aggregate Total</div>
                  <div className="summary-value" style={{ color: '#fbbf24' }}>
                    Pending...
                  </div>
                </div>
              </div>

              <div className="info-box" style={{ background: 'rgba(251, 191, 36, 0.1)', borderColor: 'rgba(251, 191, 36, 0.3)', color: '#fde047' }}>
                <span className="info-box-icon">⏳</span>
                Waiting for all partners to submit. The aggregate cannot be computed until all submissions are received 
                — this ensures the noise cancels out correctly.
              </div>

              {result.missingProducers && result.missingProducers.length > 0 && (
                <div className="card" style={{ marginTop: '1rem', background: 'rgba(0,0,0,0.2)' }}>
                  <h3 style={{ marginBottom: '1rem', fontSize: '1rem' }}>Missing Submissions:</h3>
                  <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                    {result.missingProducers.map(producer => (
                      <span key={producer} className="status-badge pending">
                        {producer}
                      </span>
                    ))}
                  </div>
                </div>
              )}
            </>
          )}
        </div>
      )}

      {/* Explanation Card */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">🔐</span>
          <h2 className="card-title">Why Noise Cancels Out</h2>
        </div>

        <div className="info-box">
          <span className="info-box-icon">💡</span>
          Each pair of partners shares a noise value. One partner <strong>adds</strong> it, the other <strong>subtracts</strong> it.
          When you sum all masked values, every noise term appears once as positive and once as negative — they cancel to zero!
        </div>

        <div style={{ marginTop: '1rem' }}>
          <h4 style={{ marginBottom: '0.5rem' }}>Mathematical Proof:</h4>
          <div style={{ background: 'rgba(0,0,0,0.3)', padding: '1rem', borderRadius: '8px', fontFamily: 'monospace', fontSize: '0.875rem' }}>
            <div>Let n<sub>AB</sub> = noise between A and B</div>
            <div style={{ marginTop: '0.5rem' }}>
              <div>WhatsApp submits: V<sub>W</sub> + n<sub>WS</sub> + n<sub>WT</sub></div>
              <div>Signal submits:&nbsp;&nbsp;&nbsp; V<sub>S</sub> - n<sub>WS</sub> + n<sub>ST</sub></div>
              <div>Telegram submits: V<sub>T</sub> - n<sub>WT</sub> - n<sub>ST</sub></div>
            </div>
            <div style={{ marginTop: '0.5rem', borderTop: '1px solid rgba(255,255,255,0.2)', paddingTop: '0.5rem' }}>
              <strong>Sum = V<sub>W</sub> + V<sub>S</sub> + V<sub>T</sub></strong> (all noise terms cancel!)
            </div>
          </div>
        </div>
      </div>

      {/* Current Epoch Info */}
      {epoch && (
        <div className="card">
          <div className="card-header">
            <span className="card-icon">📋</span>
            <h2 className="card-title">Current Epoch Information</h2>
          </div>
          
          <table className="results-table">
            <tbody>
              <tr>
                <td style={{ color: '#a1a1aa' }}>Epoch ID</td>
                <td>{epoch.epochId}</td>
              </tr>
              <tr>
                <td style={{ color: '#a1a1aa' }}>Start Date</td>
                <td>{new Date(epoch.startDate).toLocaleDateString()}</td>
              </tr>
              <tr>
                <td style={{ color: '#a1a1aa' }}>Producer Count</td>
                <td>{epoch.producerCount}</td>
              </tr>
              <tr>
                <td style={{ color: '#a1a1aa' }}>Active Producers</td>
                <td>
                  <div style={{ display: 'flex', gap: '0.5rem', flexWrap: 'wrap' }}>
                    {epoch.producerIds?.map(id => (
                      <span key={id} className="partner-badge partner-a">{id}</span>
                    ))}
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </div>
  )
}

export default Results
