import { Routes, Route, NavLink } from 'react-router-dom'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import Home from './pages/Home'
import PartnerPage from './pages/PartnerPage'
import Results from './pages/Results'
import AuthTestPage from './pages/AuthTestPage'
import { loginRequest } from './authConfig'

function App() {
  const isAuthenticated = useIsAuthenticated()
  const { instance, accounts } = useMsal()

  const handleLogin = () => instance.loginRedirect(loginRequest)
  const handleLogout = () => instance.logoutRedirect()

  return (
    <div className="app">
      <nav className="navbar">
        <div className="navbar-content">
          <NavLink to="/" className="logo">
            <span className="logo-icon">🔐</span>
            Secret Sharing Demo
          </NavLink>
          <div className="nav-links">
            <NavLink to="/" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`} end>
              Home
            </NavLink>
            <NavLink to="/partner/partnerA" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              PartnerA
            </NavLink>
            <NavLink to="/partner/partnerB" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              PartnerB
            </NavLink>
            <NavLink to="/partner/partnerC" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              PartnerC
            </NavLink>
            <NavLink to="/results" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              Results
            </NavLink>
            <NavLink to="/auth-test" className={({ isActive }) => `nav-link ${isActive ? 'active' : ''}`}>
              Auth Test
            </NavLink>
          </div>
          <div className="nav-auth">
            {isAuthenticated ? (
              <>
                <span className="nav-user">{accounts[0]?.name}</span>
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
          <Route path="/partner/:partnerId" element={<PartnerPage />} />
          <Route path="/results" element={<Results />} />
          <Route path="/auth-test" element={<AuthTestPage />} />
        </Routes>
      </main>
    </div>
  )
}

export default App
