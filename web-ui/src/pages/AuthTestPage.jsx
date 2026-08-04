import { useState } from 'react'
import { useMsal } from '@azure/msal-react'
import { acquireApiToken } from '../utils/api'

function decodeJwtPayload(token) {
  try {
    const base64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const padded = base64 + '='.repeat((4 - (base64.length % 4)) % 4)
    return JSON.parse(atob(padded))
  } catch {
    return null
  }
}

export default function AuthTestPage() {
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  const [results, setResults] = useState({})
  const [tokenClaims, setTokenClaims] = useState(null)

  async function runTest(key, fn) {
    setResults(prev => ({ ...prev, [key]: { running: true } }))
    try {
      const { status, body } = await fn()
      setResults(prev => ({ ...prev, [key]: { status, body } }))
    } catch (e) {
      setResults(prev => ({ ...prev, [key]: { status: 'error', body: e.message } }))
    }
  }

  async function testWithToken(url) {
    const token = await acquireApiToken(instance, account)
    const res = await fetch(url, { headers: { Authorization: `Bearer ${token}` } })
    const body = await res.json().catch(() => res.text())
    return { status: res.status, body }
  }

  async function testNoToken(url) {
    const res = await fetch(url)
    const body = await res.json().catch(() => res.text())
    return { status: res.status, body }
  }

  async function handleDecodeToken() {
    const token = await acquireApiToken(instance, account)
    setTokenClaims(decodeJwtPayload(token))
  }

  if (!account) {
    return (
      <div className="container" style={{ textAlign: 'center', paddingTop: '4rem' }}>
        <div className="card" style={{ display: 'inline-block', padding: '3rem 4rem' }}>
          <h2 style={{ marginBottom: '1rem' }}>Not signed in</h2>
          <p style={{ color: '#a1a1aa' }}>Use the Sign in button in the navbar to authenticate with Entra ID.</p>
        </div>
      </div>
    )
  }

  const tests = [
    {
      key: 'partner',
      label: 'Partner endpoint',
      desc: 'GET /api/registry/producers — requires Partner or Admin group',
      fn: () => testWithToken('/api/registry/producers'),
    },
    {
      key: 'admin',
      label: 'Admin endpoint',
      desc: 'GET /api/metrics/aggregate?country=US&month=2025-01 — requires Admin group',
      fn: () => testWithToken('/api/metrics/aggregate?country=US&month=2025-01'),
    },
    {
      key: 'notoken',
      label: 'No token (expect 401)',
      desc: 'GET /api/registry/producers — no Authorization header',
      fn: () => testNoToken('/api/registry/producers'),
    },
  ]

  return (
    <div className="container">
      <h1 style={{ marginBottom: '1.5rem' }}>Auth Test</h1>

      <div className="card" style={{ marginBottom: '1.5rem' }}>
        <h3 style={{ marginBottom: '1rem' }}>Signed-in user</h3>
        <p style={{ marginBottom: '0.4rem' }}><strong>Name:</strong> {account.name}</p>
        <p style={{ marginBottom: '0.4rem' }}><strong>Username:</strong> {account.username}</p>
        <button className="btn btn-primary" onClick={handleDecodeToken} style={{ marginTop: '0.75rem' }}>
          Decode API access token
        </button>
        {tokenClaims && (
          <pre className="code-block" style={{ marginTop: '1rem' }}>
            {JSON.stringify(tokenClaims, null, 2)}
          </pre>
        )}
      </div>

      {tests.map(({ key, label, desc, fn }) => {
        const result = results[key]
        return (
          <div key={key} className="card" style={{ marginBottom: '1rem' }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', gap: '1rem' }}>
              <div>
                <h4 style={{ margin: 0 }}>{label}</h4>
                <p style={{ margin: '0.3rem 0 0', fontSize: '0.85rem', color: '#a1a1aa' }}>{desc}</p>
              </div>
              <button
                className="btn btn-primary"
                style={{ whiteSpace: 'nowrap', flexShrink: 0 }}
                onClick={() => runTest(key, fn)}
                disabled={result?.running}
              >
                {result?.running ? 'Testing…' : 'Run'}
              </button>
            </div>
            {result && !result.running && (
              <div style={{ marginTop: '0.75rem' }}>
                <StatusBadge status={result.status} />
                <pre className="code-block" style={{ marginTop: '0.5rem' }}>
                  {typeof result.body === 'string'
                    ? result.body
                    : JSON.stringify(result.body, null, 2)}
                </pre>
              </div>
            )}
          </div>
        )
      })}
    </div>
  )
}

function StatusBadge({ status }) {
  const isOk = typeof status === 'number' && status >= 200 && status < 300
  const isAuthErr = status === 401 || status === 403
  const color = isOk ? '#22c55e' : isAuthErr ? '#f59e0b' : '#ef4444'
  return (
    <span style={{
      background: `${color}20`,
      color,
      border: `1px solid ${color}`,
      borderRadius: '4px',
      padding: '2px 10px',
      fontSize: '0.8rem',
      fontWeight: 600,
    }}>
      {typeof status === 'number' ? `HTTP ${status}` : status}
    </span>
  )
}
