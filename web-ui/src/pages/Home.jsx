import { Link } from 'react-router-dom'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { loginRequest } from '../authConfig'

export default function Home() {
  const isAuthenticated = useIsAuthenticated()
  const { instance } = useMsal()

  return (
    <div className="animate-fade-in">
      <div className="hero">
        <h1 className="hero-title">Privacy-Preserving Secure Aggregation</h1>
        <p className="hero-description">
          Multiple partners contribute private metrics and only the aggregate sum is revealed —
          <strong> not even the aggregator can see individual values</strong>. Powered by ML-KEM-768
          (post-quantum key encapsulation) and HMAC-SHA256 noise cancellation.
        </p>
        <div className="line" />
        {isAuthenticated ? (
          <Link to="/flow" className="btn btn-primary">🚀 Start Protocol</Link>
        ) : (
          <button className="btn btn-primary" onClick={() => instance.loginRedirect(loginRequest)}>
            🔐 Sign In to Start
          </button>
        )}
      </div>

      <div className="feature-grid">
        <div className="feature-card">
          <div className="feature-icon">🔐</div>
          <h3 className="feature-title">Post-Quantum Secure</h3>
          <p className="feature-description">
            Uses ML-KEM-768 (NIST PQC standard, formerly Kyber768) for key encapsulation.
            Resistant to quantum computer attacks.
          </p>
        </div>
        <div className="feature-card">
          <div className="feature-icon">🔑</div>
          <h3 className="feature-title">Pairwise Shared Secrets</h3>
          <p className="feature-description">
            Every pair of partners derives a unique 32-byte shared secret via ML-KEM.
            The aggregator stores only public keys and ciphertexts — never the secrets.
          </p>
        </div>
        <div className="feature-card">
          <div className="feature-icon">✨</div>
          <h3 className="feature-title">Perfect Noise Cancellation</h3>
          <p className="feature-description">
            Noise is derived via HMAC-SHA256 on the shared secret and (country, month).
            One partner adds it; the other subtracts it — they cancel exactly in the sum.
          </p>
        </div>
        <div className="feature-card">
          <div className="feature-icon">🛡️</div>
          <h3 className="feature-title">Azure AD Authentication</h3>
          <p className="feature-description">
            Partners authenticate via Microsoft Entra ID. Your Azure AD OID is your producer ID —
            no static API keys, no manual partner selection.
          </p>
        </div>
      </div>

      <div className="card" style={{ marginTop: '3rem' }}>
        <div className="card-header">
          <span className="card-icon">📋</span>
          <h2 className="card-title">Protocol Steps</h2>
        </div>
        {[
          ['Generate ML-KEM-768 key pair', 'Your public key (1184 bytes) is registered with the API. The private key (2400 bytes) stays in your browser.'],
          ['Encapsulate for smaller-ID partners', 'For each partner whose ID is lexicographically smaller than yours, encapsulate their public key → a ciphertext (1088 bytes) + shared secret. Post the ciphertext to the API.'],
          ['Decapsulate received ciphertexts', 'Partners with larger IDs send you ciphertexts. Decapsulate each one with your private key to derive the same shared secret.'],
          ['Submit masked metrics', 'For each (country, month) cell, apply deterministic noise from shared secrets (HMAC-SHA256) before submitting. Noise cancels in the aggregate.'],
        ].map(([title, desc], i) => (
          <div key={i} className="calc-step">
            <div className="step-number">{i + 1}</div>
            <div className="step-content">
              <div className="step-title">{title}</div>
              <div className="step-description">{desc}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}
