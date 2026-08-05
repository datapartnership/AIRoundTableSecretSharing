import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { apiTokenRequest } from '../authConfig'

const BASE = '/api'

export async function acquireApiToken(msalInstance, account) {
  try {
    const r = await msalInstance.acquireTokenSilent({ ...apiTokenRequest, account })
    return r.accessToken
  } catch (e) {
    if (e instanceof InteractionRequiredAuthError) {
      const r = await msalInstance.acquireTokenPopup({ ...apiTokenRequest, account })
      return r.accessToken
    }
    throw e
  }
}

function hdr(token, json = false) {
  const h = { Authorization: `Bearer ${token}` }
  if (json) h['Content-Type'] = 'application/json'
  return h
}

async function get(path, token) {
  const r = await fetch(`${BASE}${path}`, { headers: hdr(token) })
  if (!r.ok) {
    const body = await r.json().catch(() => ({}))
    throw Object.assign(new Error(body.error || body.message || r.statusText), { status: r.status })
  }
  return r.json()
}

async function post(path, body, token) {
  const r = await fetch(`${BASE}${path}`, {
    method: 'POST', headers: hdr(token, true), body: JSON.stringify(body),
  })
  if (!r.ok) {
    const data = await r.json().catch(() => ({}))
    throw Object.assign(new Error(data.error || data.message || r.statusText), { status: r.status })
  }
  return r.json()
}

// ── Registry ──────────────────────────────────────────────────────────────────
export const getProducers = (token) => get('/registry/producers', token)
export const getEpoch = (token) => get('/registry/epoch', token)
export const selfRegister = (token) => post('/registry/producers/me', {}, token)

// ── Key Exchange ──────────────────────────────────────────────────────────────
export const registerPublicKey = (producerId, publicKeyBase64, token) =>
  post('/keyexchange/register', { producerId, publicKeyBase64 }, token)

export const getPartnerKeys = (excludeProducerId, token) =>
  get(`/keyexchange/keys?excludeProducerId=${encodeURIComponent(excludeProducerId)}`, token)

export const getKeyExchangeStatus = (token) => get('/keyexchange/status', token)

// ── Ciphertexts ───────────────────────────────────────────────────────────────
export const postCiphertext = (senderId, recipientId, ciphertextBase64, token) =>
  post('/ciphertext', { senderId, recipientId, ciphertextBase64 }, token)

export const getCiphertexts = (recipientId, token) =>
  get(`/ciphertext?recipientId=${encodeURIComponent(recipientId)}`, token)

// ── Metrics ───────────────────────────────────────────────────────────────────
export const getMySubmissions = (token) => get('/metrics/mysubmissions', token)

export const submitMetric = (submission, token) =>
  post('/metrics/submit', submission, token)

export const getAggregate = (country, month, token) =>
  get(`/metrics/aggregate?country=${encodeURIComponent(country)}&month=${encodeURIComponent(month)}`, token)

// ── Admin ─────────────────────────────────────────────────────────────────────
export const adminReset = (token) => post('/admin/reset', {}, token)

export const adminResetAndCreateEpoch = (body, token) =>
  post('/admin/producers/reset-and-create-epoch', body, token)

export const adminGetAggregates = (token) => get('/admin/aggregates-latest-epoch', token)
