import { Link } from 'react-router-dom'

function Home() {
  return (
    <div className="animate-fade-in">
      <div className="hero">
        <h1 className="hero-title">Privacy-Preserving Data Aggregation</h1>
        <p className="hero-description">
          Demonstrate how multiple partners (PartnerA, PartnerB, PartnerC) can submit sensitive data while keeping individual values private. 
          Only the aggregate total is revealed — individual submissions remain hidden through deterministic noise cancellation.
        </p>
        <Link to="/partner/partnerA" className="btn btn-primary">
          🚀 Start Demo
        </Link>
      </div>

      <div className="feature-grid">
        <div className="feature-card">
          <div className="feature-icon">🔒</div>
          <h3 className="feature-title">Privacy by Design</h3>
          <p className="feature-description">
            Each partner adds deterministic noise to their value before submission. 
            Individual values cannot be determined from submissions.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">🔗</div>
          <h3 className="feature-title">No Communication Needed</h3>
          <p className="feature-description">
            Partners independently calculate the same pairwise noise using cryptographic hashing. 
            No coordination or communication between partners required.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">✨</div>
          <h3 className="feature-title">Perfect Cancellation</h3>
          <p className="feature-description">
            When all partners submit, the noise perfectly cancels out. 
            The aggregator sees only the true total — nothing more.
          </p>
        </div>

        <div className="feature-card">
          <div className="feature-icon">🌍</div>
          <h3 className="feature-title">Real-World Use Case</h3>
          <p className="feature-description">
            Ideal for scenarios like the World Bank collecting user statistics from messaging platforms 
            without exposing competitive data.
          </p>
        </div>
      </div>

      <div className="card" style={{ marginTop: '3rem' }}>
        <div className="card-header">
          <span className="card-icon">📋</span>
          <h2 className="card-title">How It Works</h2>
        </div>
        
        <div className="calc-step">
          <div className="step-number">1</div>
          <div className="step-content">
            <div className="step-title">Partner Enters Actual Value</div>
            <div className="step-description">
              Each partner (PartnerA, PartnerB, PartnerC) enters their actual monthly active users for a country.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">2</div>
          <div className="step-content">
            <div className="step-title">Calculate Pairwise Noise</div>
            <div className="step-description">
              For each other partner, deterministic noise is calculated using SHA-256 hash of both partner IDs + country + month. 
              One partner adds the noise, the other subtracts it (determined by alphabetical order).
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">3</div>
          <div className="step-content">
            <div className="step-title">Submit Masked Value</div>
            <div className="step-description">
              The partner submits: <code>actual_value + Σ(pairwise_noise)</code> to the aggregator. 
              The masked value hides the true value.
            </div>
          </div>
        </div>

        <div className="calc-step">
          <div className="step-number">4</div>
          <div className="step-content">
            <div className="step-title">Aggregate When Complete</div>
            <div className="step-description">
              When all partners submit, the aggregator sums all masked values. 
              All noise terms cancel out: <code>Σ(masked) = Σ(actual)</code> ✅
            </div>
          </div>
        </div>
      </div>

      <div className="card" style={{ marginTop: '2rem' }}>
        <div className="card-header">
          <span className="card-icon">🧮</span>
          <h2 className="card-title">Example Calculation</h2>
        </div>
        
        <table className="results-table">
          <thead>
            <tr>
              <th>Partner</th>
              <th>Actual Value</th>
              <th>Noise Applied</th>
              <th>Masked Value</th>
            </tr>
          </thead>
          <tbody>
            <tr>
              <td><span className="partner-badge partner-a">PartnerA</span></td>
              <td>1,000,000</td>
              <td style={{ color: '#4ade80' }}>+150,000</td>
              <td>1,150,000</td>
            </tr>
            <tr>
              <td><span className="partner-badge partner-b">PartnerB</span></td>
              <td>500,000</td>
              <td style={{ color: '#f87171' }}>-80,000</td>
              <td>420,000</td>
            </tr>
            <tr>
              <td><span className="partner-badge partner-c">PartnerC</span></td>
              <td>200,000</td>
              <td style={{ color: '#f87171' }}>-70,000</td>
              <td>130,000</td>
            </tr>
            <tr style={{ fontWeight: 'bold', borderTop: '2px solid rgba(255,255,255,0.2)' }}>
              <td>Total</td>
              <td>1,700,000</td>
              <td style={{ color: '#a1a1aa' }}>0 (canceled!)</td>
              <td>1,700,000 ✅</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  )
}

export default Home
