import { useState, useEffect } from 'react'
import { useParams } from 'react-router-dom'
import { calculateMaskedValue } from '../utils/noise'
import { getProducers, getEpoch, submitMetric } from '../utils/api'

const PARTNER_CONFIG = {
  partnerA: { name: 'Partner A', icon: '🏢', color: 'partner-a' },
  partnerB: { name: 'Partner B', icon: '🏛️', color: 'partner-b' },
  partnerC: { name: 'Partner C', icon: '🏗️', color: 'partner-c' },
}

const COUNTRIES = ['USA', 'UK', 'Germany', 'France', 'Japan', 'Brazil', 'India']

function PartnerPage() {
  const { partnerId } = useParams()
  const partner = PARTNER_CONFIG[partnerId] || { name: partnerId, icon: '🏢', color: 'partner-a' }
  
  const [country, setCountry] = useState('USA')
  const [month, setMonth] = useState(() => {
    const now = new Date()
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  })
  const [actualValue, setActualValue] = useState('')
  const [calculation, setCalculation] = useState(null)
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [submitted, setSubmitted] = useState(false)
  const [error, setError] = useState(null)
  const [producers, setProducers] = useState([])
  const [epoch, setEpoch] = useState(null)

  // Fetch producers and epoch on mount
  useEffect(() => {
    async function fetchData() {
      try {
        const [producersData, epochData] = await Promise.all([
          getProducers(),
          getEpoch()
        ])
        setProducers(producersData)
        setEpoch(epochData)
      } catch (err) {
        setError('Failed to connect to API. Make sure the backend is running on port 5000.')
      }
    }
    fetchData()
  }, [])

  // Calculate noise when inputs change
  useEffect(() => {
    async function calculate() {
      if (!actualValue || !producers.length) {
        setCalculation(null)
        return
      }

      setLoading(true)
      setError(null)
      
      try {
        // Parse month string and create UTC date to avoid timezone issues
        const [year, monthNum] = month.split('-').map(Number)
        const monthDate = new Date(Date.UTC(year, monthNum - 1, 1))
        const producerIds = producers.map(p => p.producerId)
        
        const result = await calculateMaskedValue(
          partnerId,
          producerIds,
          country,
          monthDate,
          parseInt(actualValue)
        )
        
        setCalculation(result)
      } catch (err) {
        setError(err.message)
      } finally {
        setLoading(false)
      }
    }
    
    calculate()
  }, [actualValue, country, month, partnerId, producers])

  const handleSubmit = async () => {
    if (!calculation || !epoch) return
    
    setSubmitting(true)
    setError(null)
    
    try {
      // Parse month string and create UTC date to avoid timezone issues
      const [year, monthNum] = month.split('-').map(Number)
      const monthDate = new Date(Date.UTC(year, monthNum - 1, 1))
      
      await submitMetric({
        producerId: partnerId,
        country: country,
        month: monthDate.toISOString(),
        value: calculation.maskedValue,
        epochId: epoch.epochId,
        signature: 'demo-signature'
      })
      
      setSubmitted(true)
    } catch (err) {
      setError(err.message)
    } finally {
      setSubmitting(false)
    }
  }

  const handleReset = () => {
    setActualValue('')
    setCalculation(null)
    setSubmitted(false)
    setError(null)
  }

  const formatNumber = (num) => {
    return num?.toLocaleString() ?? '—'
  }

  return (
    <div className="animate-fade-in">
      <div className="page-header">
        <span className="partner-badge" style={{ fontSize: '1rem', marginBottom: '1rem', display: 'inline-flex' }}>
          {partner.icon} {partner.name}
        </span>
        <h1 className="page-title">Submit Metrics</h1>
        <p className="page-subtitle">Enter your actual value — it will be masked before submission</p>
      </div>

      {error && (
        <div className="info-box" style={{ background: 'rgba(248, 113, 113, 0.1)', borderColor: 'rgba(248, 113, 113, 0.3)', color: '#fca5a5' }}>
          <span className="info-box-icon">⚠️</span>
          {error}
        </div>
      )}

      <div className="grid-2">
        {/* Input Card */}
        <div className="card">
          <div className="card-header">
            <span className="card-icon">📝</span>
            <h2 className="card-title">Input Data</h2>
          </div>

          <div className="form-group">
            <label className="form-label">Country</label>
            <select 
              className="form-select" 
              value={country} 
              onChange={(e) => setCountry(e.target.value)}
              disabled={submitted}
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
              disabled={submitted}
            />
          </div>

          <div className="form-group">
            <label className="form-label">Actual Value (Monthly Active Users)</label>
            <input
              type="number"
              className="form-input"
              placeholder="e.g., 1000000"
              value={actualValue}
              onChange={(e) => setActualValue(e.target.value)}
              disabled={submitted}
            />
          </div>

          {epoch && (
            <div className="info-box">
              <span className="info-box-icon">ℹ️</span>
              Current Epoch: <strong>{epoch.epochId}</strong> with <strong>{epoch.producerCount}</strong> producers
              ({epoch.producerIds?.join(', ')})
            </div>
          )}
        </div>

        {/* Calculation Card */}
        <div className="card">
          <div className="card-header">
            <span className="card-icon">🔐</span>
            <h2 className="card-title">Noise Calculation</h2>
          </div>

          {!actualValue ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#71717a' }}>
              Enter an actual value to see the noise calculation
            </div>
          ) : loading ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#71717a' }}>
              Calculating noise...
            </div>
          ) : calculation ? (
            <>
              <div className="noise-breakdown">
                <div className="noise-item">
                  <span className="noise-label">Your Actual Value</span>
                  <span className="noise-value">{formatNumber(calculation.actualValue)}</span>
                </div>
                
                {Object.entries(calculation.noiseBreakdown).map(([otherId, data]) => (
                  <div key={otherId} className="noise-item">
                    <span className="noise-label">
                      Noise with <strong>{otherId}</strong>
                      <span style={{ fontSize: '0.75rem', color: '#71717a', marginLeft: '0.5rem' }}>
                        ({data.sign > 0 ? 'add' : 'subtract'})
                      </span>
                    </span>
                    <span className={`noise-value ${data.appliedNoise >= 0 ? 'positive' : 'negative'}`}>
                      {data.appliedNoise >= 0 ? '+' : ''}{formatNumber(data.appliedNoise)}
                    </span>
                  </div>
                ))}
                
                <div className="noise-item" style={{ borderTop: '2px solid rgba(255,255,255,0.1)', marginTop: '0.5rem', paddingTop: '1rem' }}>
                  <span className="noise-label"><strong>Total Noise</strong></span>
                  <span className={`noise-value ${calculation.totalNoise >= 0 ? 'positive' : 'negative'}`}>
                    {calculation.totalNoise >= 0 ? '+' : ''}{formatNumber(calculation.totalNoise)}
                  </span>
                </div>
              </div>

              <div className="summary-box" style={{ marginTop: '1rem' }}>
                <div className="summary-label">Masked Value to Submit</div>
                <div className="summary-value">{formatNumber(calculation.maskedValue)}</div>
              </div>

              <div style={{ marginTop: '1rem', textAlign: 'center' }}>
                {submitted ? (
                  <>
                    <div className="status-badge success" style={{ marginBottom: '1rem' }}>
                      ✅ Successfully Submitted
                    </div>
                    <button className="btn btn-secondary" onClick={handleReset}>
                      Submit Another
                    </button>
                  </>
                ) : (
                  <button 
                    className="btn btn-primary" 
                    onClick={handleSubmit}
                    disabled={submitting}
                  >
                    {submitting ? '⏳ Submitting...' : '🚀 Submit Masked Value'}
                  </button>
                )}
              </div>
            </>
          ) : null}
        </div>
      </div>

      {/* Explanation Card */}
      <div className="card">
        <div className="card-header">
          <span className="card-icon">💡</span>
          <h2 className="card-title">How Your Privacy is Protected</h2>
        </div>
        
        <div className="calc-step">
          <div className="step-number">1</div>
          <div className="step-content">
            <div className="step-title">Deterministic Noise Generation</div>
            <div className="step-description">
              For each other partner, we calculate a noise value using SHA-256 hash of both partner IDs + country + month.
              This means both partners calculate the <strong>exact same noise</strong> independently — no communication needed!
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">2</div>
          <div className="step-content">
            <div className="step-title">Alphabetical Sign Assignment</div>
            <div className="step-description">
              The partner that comes first alphabetically <strong>adds</strong> the noise, the other <strong>subtracts</strong> it.
              This ensures perfect cancellation when aggregating.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">3</div>
          <div className="step-content">
            <div className="step-title">Your Value is Hidden</div>
            <div className="step-description">
              The aggregator only sees your masked value. They cannot determine your actual value without 
              knowing the noise — which is computed from private partner relationships.
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default PartnerPage
