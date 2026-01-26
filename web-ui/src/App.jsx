import { Routes, Route, NavLink } from 'react-router-dom'
import Home from './pages/Home'
import PartnerPage from './pages/PartnerPage'
import Results from './pages/Results'

function App() {
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
          </div>
        </div>
      </nav>
      
      <main className="main-content">
        <Routes>
          <Route path="/" element={<Home />} />
          <Route path="/partner/:partnerId" element={<PartnerPage />} />
          <Route path="/results" element={<Results />} />
        </Routes>
      </main>
    </div>
  )
}

export default App
