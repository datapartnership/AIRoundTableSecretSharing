import { useState, useEffect } from 'react'
import { Routes, Route, NavLink, Navigate } from 'react-router-dom'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import Home from './pages/Home'
import ProtocolFlow from './pages/ProtocolFlow'
import AdminPanel from './pages/AdminPanel'
import Results from './pages/Results'
import { loginRequest, apiTokenRequest } from './authConfig'

// Azure AD group IDs from appsettings.json
const ADMIN_GROUP = '962dafa7-7ec1-43c3-bb67-235d64f9582f'

// Groups claim is in the access token, not the ID token — parse it client-side for UI gating only
function parseGroups(accessToken) {
  try {
    const payload = JSON.parse(atob(accessToken.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')))
    return payload.groups ?? []
  } catch {
    return []
  }
}

function App() {
  const isAuthenticated = useIsAuthenticated()
  const { instance, accounts } = useMsal()
  const account = accounts[0]
  const [isAdmin, setIsAdmin] = useState(false)

  useEffect(() => {
    if (!isAuthenticated || !account) { setIsAdmin(false); return }
    instance.acquireTokenSilent({ ...apiTokenRequest, account })
      .then(r => setIsAdmin(parseGroups(r.accessToken).includes(ADMIN_GROUP)))
      .catch(() => setIsAdmin(false))
  }, [isAuthenticated, account?.homeAccountId])

  const handleLogin = () => instance.loginRedirect(loginRequest)
  const handleLogout = () => instance.logoutRedirect()

  return (
    <div className="app">
      <nav className="navbar">
        <div className="navbar-content">
          <NavLink to="/" className="logo">
            <span className="logo-icon">🔐</span>
            AI Roundtable
          </NavLink>
          <div className="nav-links">
            <NavLink to="/" end className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              Home
            </NavLink>
            {isAuthenticated && (
              <NavLink to="/flow" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
                Protocol
              </NavLink>
            )}
            {isAdmin && (
              <NavLink to="/admin" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
                Admin
              </NavLink>
            )}
            {isAuthenticated && (
              <NavLink to="/results" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
                Results
              </NavLink>
            )}
          </div>
          <div className="nav-auth">
            {isAuthenticated ? (
              <>
                <span className="nav-user">{account?.name ?? account?.username}</span>
                <button className="btn btn-secondary nav-btn" onClick={handleLogout}>Sign out</button>
              </>
            ) : (
              <button className="btn btn-primary nav-btn" onClick={handleLogin}>Sign in</button>
            )}
          </div>
        </div>
      </nav>

      <main className="main-content">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/flow" element={isAuthenticated ? <ProtocolFlow /> : <Navigate to="/" replace />} />
          <Route path="/admin" element={isAuthenticated ? <AdminPanel /> : <Navigate to="/" replace />} />
          <Route path="/results" element={isAuthenticated ? <Results /> : <Navigate to="/" replace />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </div>
  )
}

export default App
