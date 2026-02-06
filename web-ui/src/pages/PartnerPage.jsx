import { useState, useEffect, useCallback } from 'react'
import { useParams } from 'react-router-dom'
import { calculateMaskedValue } from '../utils/noise'
import { 
  generateKeyPair, 
  exportPublicKey, 
  computeSharedSecret, 
  hasKeyPair,
  getKeyExchangeStatus as getLocalKeyStatus 
} from '../utils/crypto'
import { 
  getProducers, 
  getEpoch, 
  submitMetric,
  registerPublicKey,
  getAllPublicKeys
} from '../utils/api'

const PARTNER_CONFIG = {
  partnerA: { name: 'Partner A', icon: '🏢', color: 'partner-a' },
  partnerB: { name: 'Partner B', icon: '🏛️', color: 'partner-b' },
  partnerC: { name: 'Partner C', icon: '🏗️', color: 'partner-c' },
}

const COUNTRIES = ['USA', 'UK', 'Germany', 'France', 'Japan', 'Brazil', 'India']

// Default demo API keys matching appsettings.json
const DEFAULT_API_KEYS = {
  partnerA: 'pA-secret-key-2026-abc123',
  partnerB: 'pB-secret-key-2026-def456',
  partnerC: 'pC-secret-key-2026-ghi789',
}

function PartnerPage() {
  const { partnerId } = useParams()
  const partner = PARTNER_CONFIG[partnerId] || { name: partnerId, icon: '🏢', color: 'partner-a' }
  
  const [country, setCountry] = useState('USA')
  const [month, setMonth] = useState(() => {
    const now = new Date()
    return `${now.getFullYear()}-${String(now.getMonth() + 1).padStart(2, '0')}`
  })
  const [actualValue, setActualValue] = useState('')
  const [apiKey, setApiKey] = useState(() => {
    return localStorage.getItem(`apiKey_${partnerId}`) || DEFAULT_API_KEYS[partnerId] || ''
  })
  const [calculation, setCalculation] = useState(null)
  const [loading, setLoading] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [submitted, setSubmitted] = useState(false)
  const [error, setError] = useState(null)
  const [producers, setProducers] = useState([])
  const [epoch, setEpoch] = useState(null)
  
  // Key exchange state
  const [keyExchangeStatus, setKeyExchangeStatus] = useState({
    hasOwnKeyPair: false,
    isComplete: false,
    completedExchanges: 0,
    totalPartners: 0,
    pendingPartners: []
  })
  const [keyExchangeLoading, setKeyExchangeLoading] = useState(false)

  // Persist API key changes
  useEffect(() => {
    if (apiKey) {
      localStorage.setItem(`apiKey_${partnerId}`, apiKey)
    }
  }, [apiKey, partnerId])

  // Fetch producers and epoch on mount
  useEffect(() => {
    async function fetchData() {
      try {
        const [producersData, epochData] = await Promise.all([
          getProducers(null, apiKey),
          getEpoch(null, apiKey)
        ])
        setProducers(producersData)
        setEpoch(epochData)
      } catch (err) {
        setError('Failed to connect to API. Make sure the backend is running on port 5149.')
      }
    }
    if (apiKey) fetchData()
  }, [apiKey])

  // Update key exchange status when producers change
  const updateKeyExchangeStatus = useCallback(() => {
    if (producers.length > 0) {
      const producerIds = producers.map(p => p.producerId)
      const status = getLocalKeyStatus(partnerId, producerIds)
      setKeyExchangeStatus(status)
    }
  }, [partnerId, producers])

  useEffect(() => {
    updateKeyExchangeStatus()
  }, [updateKeyExchangeStatus])

  // Perform key exchange
  const performKeyExchange = async () => {
    setKeyExchangeLoading(true)
    setError(null)
    
    try {
      // Step 1: Generate our key pair if we don't have one
      if (!hasKeyPair(partnerId)) {
        await generateKeyPair(partnerId)
      }
      
      // Step 2: Export and register our public key
      const myPublicKey = await exportPublicKey(partnerId)
      await registerPublicKey(partnerId, myPublicKey, apiKey)
      
      // Step 3: Get all other partners' public keys
      const allKeys = await getAllPublicKeys(apiKey)
      
      // Step 4: Compute shared secrets with each partner
      for (const keyInfo of allKeys) {
        if (keyInfo.producerId !== partnerId && keyInfo.publicKeyBase64) {
          try {
            await computeSharedSecret(partnerId, keyInfo.producerId, keyInfo.publicKeyBase64)
          } catch (e) {
            console.warn(`Could not compute shared secret with ${keyInfo.producerId}:`, e)
          }
        }
      }
      
      // Update status
      updateKeyExchangeStatus()
      
    } catch (err) {
      setError(`Key exchange failed: ${err.message}`)
    } finally {
      setKeyExchangeLoading(false)
    }
  }

  // Calculate noise when inputs change (only if key exchange is complete)
  useEffect(() => {
    async function calculate() {
      if (!actualValue || !producers.length || !keyExchangeStatus.isComplete) {
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
  }, [actualValue, country, month, partnerId, producers, keyExchangeStatus.isComplete])

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
      }, apiKey)
      
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

      {/* API Key Input */}
      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <div className="card-header">
          <span className="card-icon">🔑</span>
          <h2 className="card-title">API Authentication</h2>
        </div>
        <div className="form-group" style={{ marginBottom: 0 }}>
          <label className="form-label">API Key</label>
          <input
            type="password"
            className="form-input"
            placeholder="Enter your API key"
            value={apiKey}
            onChange={(e) => setApiKey(e.target.value)}
            disabled={submitted}
          />
          <div style={{ fontSize: '0.75rem', color: '#71717a', marginTop: '0.25rem' }}>
            Each partner has a unique API key. It is stored locally in your browser.
          </div>
        </div>
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

          {/* Key Exchange Status */}
          <div className="card" style={{ marginTop: '1rem', padding: '1rem', background: 'rgba(0,0,0,0.2)' }}>
            <div style={{ display: 'flex', alignItems: 'center', gap: '0.5rem', marginBottom: '0.75rem' }}>
              <span>🔑</span>
              <strong>Diffie-Hellman Key Exchange</strong>
            </div>
            
            {keyExchangeStatus.isComplete ? (
              <div className="status-badge success" style={{ marginBottom: '0.5rem' }}>
                ✅ Key exchange complete with {keyExchangeStatus.completedExchanges} partner(s)
              </div>
            ) : (
              <>
                <div style={{ marginBottom: '0.5rem', fontSize: '0.875rem', color: '#a1a1aa' }}>
                  {keyExchangeStatus.hasOwnKeyPair 
                    ? `Key pair generated. Exchanged with ${keyExchangeStatus.completedExchanges}/${keyExchangeStatus.totalPartners} partners.`
                    : 'Generate your ECDH key pair and exchange with other partners.'}
                </div>
                {keyExchangeStatus.pendingPartners.length > 0 && (
                  <div style={{ marginBottom: '0.5rem', fontSize: '0.75rem', color: '#71717a' }}>
                    Pending: {keyExchangeStatus.pendingPartners.join(', ')}
                  </div>
                )}
                <button 
                  className="btn btn-secondary" 
                  onClick={performKeyExchange}
                  disabled={keyExchangeLoading || submitted}
                  style={{ marginTop: '0.5rem' }}
                >
                  {keyExchangeLoading ? '⏳ Exchanging Keys...' : '🔐 Perform Key Exchange'}
                </button>
              </>
            )}
          </div>
        </div>

        {/* Calculation Card */}
        <div className="card">
          <div className="card-header">
            <span className="card-icon">🔐</span>
            <h2 className="card-title">Secure Noise Calculation</h2>
          </div>

          {!keyExchangeStatus.isComplete ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#71717a' }}>
              <div style={{ fontSize: '2rem', marginBottom: '0.5rem' }}>🔑</div>
              <div>Complete key exchange first to enable secure noise calculation</div>
              <div style={{ fontSize: '0.875rem', marginTop: '0.5rem', color: '#52525b' }}>
                The Diffie-Hellman key exchange establishes shared secrets that the aggregator cannot compute.
              </div>
            </div>
          ) : !actualValue ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#71717a' }}>
              Enter an actual value to see the secure noise calculation
            </div>
          ) : loading ? (
            <div style={{ textAlign: 'center', padding: '2rem', color: '#71717a' }}>
              Calculating secure noise...
            </div>
          ) : calculation ? (
            <>
              {/* MAU Calculation */}
              <div style={{ marginBottom: '1.5rem' }}>
                <h3 style={{ fontSize: '1rem', fontWeight: 600, marginBottom: '0.75rem', color: '#10b981' }}>
                  📊 MAU (Monthly Active Users)
                </h3>
                <div className="noise-breakdown">
                  <div className="noise-item">
                    <span className="noise-label">Your Actual MAU</span>
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
                  <div className="summary-label">Masked MAU to Submit</div>
                  <div className="summary-value">{formatNumber(calculation.maskedValue)}</div>
                </div>
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
          <h2 className="card-title">How Your Privacy is Protected (Secure DH Implementation)</h2>
        </div>
        
        <div className="calc-step">
          <div className="step-number">1</div>
          <div className="step-content">
            <div className="step-title">ECDH Key Pair Generation</div>
            <div className="step-description">
              Each partner generates an <strong>Elliptic Curve Diffie-Hellman (P-256)</strong> key pair.
              The private key <strong>never leaves your browser</strong> — only the public key is shared.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">2</div>
          <div className="step-content">
            <div className="step-title">Key Exchange via Aggregator</div>
            <div className="step-description">
              Partners exchange public keys through the aggregator. The aggregator sees the public keys but
              <strong> cannot compute the shared secrets</strong> — this is the magic of Diffie-Hellman!
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">3</div>
          <div className="step-content">
            <div className="step-title">Shared Secret Derivation</div>
            <div className="step-description">
              Each partner computes shared secrets with every other partner using <strong>ECDH key agreement</strong>.
              Both partners derive the <strong>exact same secret</strong> independently — the aggregator cannot!
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">4</div>
          <div className="step-content">
            <div className="step-title">HMAC-Based Noise Generation</div>
            <div className="step-description">
              Noise is computed as <code>HMAC-SHA256(shared_secret, context)</code>. Since the aggregator
              doesn't have the shared secrets, it <strong>cannot compute the noise</strong> and therefore
              cannot reverse engineer your actual value!
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">5</div>
          <div className="step-content">
            <div className="step-title">Perfect Noise Cancellation</div>
            <div className="step-description">
              One partner adds the noise, the other subtracts it. When aggregated, all noise cancels
              perfectly — revealing only the true total, while individual values remain <strong>cryptographically protected</strong>.
            </div>
          </div>
        </div>
      </div>
    </div>
  )
}

export default PartnerPage
