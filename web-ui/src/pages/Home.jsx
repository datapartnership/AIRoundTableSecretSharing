import { Link } from 'react-router-dom'

function Home() {
  return (
    <div className="animate-fade-in">
      <div className="hero">
        <h1 className="hero-title">Privacy-Preserving Secure Aggregation</h1>
        <p className="hero-description">
          Demonstrate how multiple partners can submit sensitive data while keeping individual values private — 
          <strong> even from the aggregator</strong>. Using Elliptic Curve Diffie-Hellman (ECDH) key exchange, 
          partners establish shared secrets that enable cryptographically secure noise cancellation.
        </p>
        <Link to="/partner/partnerA" className="btn btn-primary">
          🚀 Start Demo
        </Link>
      </div>

      <div className="feature-grid">
        <div className="feature-card">
          <div className="feature-icon">🔐</div>
          <h3 className="feature-title">Cryptographically Secure</h3>
          <p className="feature-description">
            Uses ECDH P-256 key exchange to establish shared secrets. 
            The aggregator <strong>cannot compute</strong> the noise, even though it facilitates the key exchange.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">🔑</div>
          <h3 className="feature-title">Diffie-Hellman Magic</h3>
          <p className="feature-description">
            Partners exchange public keys through the aggregator, but derive shared secrets locally.
            The aggregator sees public keys but cannot derive the shared secrets.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">✨</div>
          <h3 className="feature-title">Perfect Cancellation</h3>
          <p className="feature-description">
            Noise generated from shared secrets (via HMAC-SHA256) cancels perfectly when aggregated.
            Only the true total is revealed — individual values remain hidden.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">🛡️</div>
          <h3 className="feature-title">Aggregator Cannot Cheat</h3>
          <p className="feature-description">
            Unlike basic hash-based noise, the aggregator cannot reverse-engineer individual values.
            Security is based on the Computational Diffie-Hellman (CDH) problem.
          </p>
        </div>
      </div>

      <div className="card" style={{ marginTop: '3rem' }}>
        <div className="card-header">
          <span className="card-icon">📋</span>
          <h2 className="card-title">How the Secure Protocol Works</h2>
        </div>
        
        <div className="calc-step">
          <div className="step-number">1</div>
          <div className="step-content">
            <div className="step-title">Generate ECDH Key Pair</div>
            <div className="step-description">
              Each partner generates an Elliptic Curve Diffie-Hellman key pair (P-256 curve).
              The private key stays in your browser — only the public key is shared.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">2</div>
          <div className="step-content">
            <div className="step-title">Exchange Public Keys</div>
            <div className="step-description">
              Partners register their public keys with the aggregator and retrieve others' public keys.
              The aggregator facilitates this exchange but <strong>cannot derive the shared secrets</strong>.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">3</div>
          <div className="step-content">
            <div className="step-title">Compute Shared Secrets</div>
            <div className="step-description">
              Each partner uses ECDH to compute shared secrets with every other partner.
              Both partners derive the <strong>exact same secret</strong> — this is Diffie-Hellman magic!
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">4</div>
          <div className="step-content">
            <div className="step-title">Generate HMAC-Based Noise</div>
            <div className="step-description">
              Noise is computed as <code>HMAC-SHA256(shared_secret, country + month + metricType)</code>.
              One partner adds, the other subtracts (determined by alphabetical order).
              <strong> Each metric type generates independent noise!</strong>
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">5</div>
          <div className="step-content">
            <div className="step-title">Submit & Aggregate</div>
            <div className="step-description">
              Partners submit masked values for both metrics (MAU and Adjusted MAU). When all submit, noise cancels perfectly: 
              <code>Σ(masked) = Σ(actual)</code> ✅ The aggregator learns the totals but NOT individual values or coefficients.
            </div>
          </div>
        </div>
      </div>

      {/* Dual Metrics Card */}
      <div className="card" style={{ marginTop: '2rem' }}>
        <div className="card-header">
          <span className="card-icon">⚖️</span>
          <h2 className="card-title">Dual Metric Support</h2>
        </div>
        
        <div className="info-box" style={{ marginBottom: '1rem' }}>
          <span className="info-box-icon">🎯</span>
          Partners can now submit <strong>two parallel masked metrics</strong>:
        </div>

        <div className="grid-2">
          <div style={{ padding: '1rem', background: 'rgba(16, 185, 129, 0.1)', borderRadius: '0.5rem', border: '1px solid rgba(16, 185, 129, 0.3)' }}>
            <div style={{ fontWeight: 600, marginBottom: '0.5rem', color: '#10b981' }}>📊 MAU (Monthly Active Users)</div>
            <div style={{ fontSize: '0.875rem', color: '#a1a1aa' }}>
              Raw count of monthly active users. Masked with noise generated from <code>metricType = "MAU"</code>.
            </div>
          </div>

          <div style={{ padding: '1rem', background: 'rgba(139, 92, 246, 0.1)', borderRadius: '0.5rem', border: '1px solid rgba(139, 92, 246, 0.3)' }}>
            <div style={{ fontWeight: 600, marginBottom: '0.5rem', color: '#8b5cf6' }}>⚖️ Adjusted MAU (MAU × Coefficient)</div>
            <div style={{ fontSize: '0.875rem', color: '#a1a1aa' }}>
              Partners multiply their MAU by a <strong>secret coefficient</strong> (e.g., quality factor). The coefficient is never revealed!
            </div>
          </div>
        </div>

        <div style={{ marginTop: '1rem', padding: '0.75rem', background: 'rgba(139, 92, 246, 0.1)', borderRadius: '0.5rem', fontSize: '0.875rem', color: '#c4b5fd' }}>
          🔒 The aggregator learns <strong>Σ(MAU)</strong> and <strong>Σ(Adjusted MAU)</strong> but cannot determine individual MAU values or any partner's coefficient!
        </div>
      </div>

      <div className="card" style={{ marginTop: '2rem' }}>
        <div className="card-header">
          <span className="card-icon">🔒</span>
          <h2 className="card-title">Security Guarantee</h2>
        </div>
        
        <div style={{ padding: '1rem', background: 'rgba(0,0,0,0.2)', borderRadius: '0.5rem', fontFamily: 'monospace', fontSize: '0.875rem', lineHeight: '1.6' }}>
          <div style={{ color: '#a1a1aa', marginBottom: '0.5rem' }}>// The aggregator sees:</div>
          <div>public_key_A = g<sup>private_a</sup></div>
          <div>public_key_B = g<sup>private_b</sup></div>
          <div style={{ color: '#a1a1aa', marginTop: '0.5rem', marginBottom: '0.5rem' }}>// Partners compute:</div>
          <div style={{ color: '#4ade80' }}>shared_secret = g<sup>(private_a × private_b)</sup></div>
          <div style={{ color: '#a1a1aa', marginTop: '0.5rem', marginBottom: '0.5rem' }}>// The aggregator CANNOT compute:</div>
          <div style={{ color: '#f87171' }}>g<sup>(a×b)</sup> from g<sup>a</sup> and g<sup>b</sup> (Computational Diffie-Hellman Problem)</div>
        </div>
      </div>

      <div className="card" style={{ marginTop: '2rem' }}>
        <div className="card-header">
          <span className="card-icon">🧮</span>
          <h2 className="card-title">Example Dual Metric Calculation</h2>
        </div>
        
        <table className="results-table">
          <thead>
            <tr>
              <th>Partner</th>
              <th>MAU</th>
              <th>Coefficient</th>
              <th>Adjusted MAU</th>
              <th>Masked MAU</th>
              <th>Masked Adjusted</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><span className="partner-badge partner-a">PartnerA</span></td>
              <td>1,000,000</td>
              <td style={{ color: '#8b5cf6' }}>1.5</td>
              <td>1,500,000</td>
              <td>1,150,000</td>
              <td>1,620,000</td>
            </tr>
            <tr>
              <td><span className="partner-badge partner-b">PartnerB</span></td>
              <td>500,000</td>
              <td style={{ color: '#8b5cf6' }}>2.0</td>
              <td>1,000,000</td>
              <td>420,000</td>
              <td>880,000</td>
            </tr>
            <tr>
              <td><span className="partner-badge partner-c">PartnerC</span></td>
              <td>200,000</td>
              <td style={{ color: '#8b5cf6' }}>0.8</td>
              <td>160,000</td>
              <td>130,000</td>
              <td>160,000</td>
            </tr>
            <tr style={{ fontWeight: 'bold', borderTop: '2px solid rgba(255,255,255,0.2)' }}>
              <td>Total</td>
              <td>1,700,000</td>
              <td style={{ color: '#71717a' }}>—</td>
              <td>2,660,000</td>
              <td>1,700,000 ✅</td>
              <td>2,660,000 ✅</td>
            </tr>
          </tbody>
        </table>
        <div style={{ marginTop: '1rem', padding: '0.75rem', background: 'rgba(74, 222, 128, 0.1)', borderRadius: '0.5rem', fontSize: '0.875rem', color: '#86efac' }}>
          <div>🔐 <strong>Aggregator learns:</strong></div>
          <ul style={{ margin: '0.5rem 0 0 1.5rem', lineHeight: '1.8' }}>
            <li>Total MAU = <strong>1,700,000</strong></li>
            <li>Total Adjusted MAU = <strong>2,660,000</strong></li>
            <li>Weighted Ratio = 2,660,000 / 1,700,000 = <strong style={{ color: '#fbbf24' }}>1.5647</strong></li>
          </ul>
          <div style={{ marginTop: '0.5rem' }}>
            The <strong>Weighted Ratio</strong> represents the aggregate weighted coefficient — useful for computing 
            quality scores without revealing individual coefficients!
          </div>
        </div>
      </div>
    </div>
  )
}

export default Home
