import { InteractionRequiredAuthError } from '@azure/msal-browser'
import { apiTokenRequest } from '../authConfig'

const BASE = `${(import.meta.env.VITE_API_URL ?? '').replace(/\/$/, '')}/api`

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
export const getEpochs = (token) => get('/registry/epochs', token)
export const selfRegister = (token) => post('/registry/producers/me', {}, token)

// ── Key Exchange ──────────────────────────────────────────────────────────────
export const registerPublicKey = (epochId, deviceId, publicKeyBase64, token) =>
  post('/keyexchange/register', { epochId, deviceId, publicKeyBase64 }, token)

export const getPartnerKeys = (epochId, deviceId, token) =>
  get(`/keyexchange/keys?epochId=${encodeURIComponent(epochId)}&deviceId=${encodeURIComponent(deviceId)}`, token)

export const getKeyExchangeStatus = (epochId, deviceId, token) =>
  get(`/keyexchange/status?epochId=${encodeURIComponent(epochId)}&deviceId=${encodeURIComponent(deviceId)}`, token)

// ── Ciphertexts ───────────────────────────────────────────────────────────────
export const postCiphertext = (epochId, deviceId, recipientId, recipientDeviceId, ciphertextBase64, token) =>
  post('/ciphertext', { epochId, deviceId, recipientId, recipientDeviceId, ciphertextBase64 }, token)

export const getCiphertexts = (epochId, deviceId, token) =>
  get(`/ciphertext?epochId=${encodeURIComponent(epochId)}&deviceId=${encodeURIComponent(deviceId)}`, token)

export const getSentCiphertexts = (epochId, deviceId, token) =>
  get(`/ciphertext/sent?epochId=${encodeURIComponent(epochId)}&deviceId=${encodeURIComponent(deviceId)}`, token)

// ── Metrics ───────────────────────────────────────────────────────────────────
export const getMySubmissions = (epochId, token) =>
  get(`/metrics/mysubmissions?epochId=${encodeURIComponent(epochId)}`, token)

export const submitMetric = (submission, token) =>
  post('/metrics/submit', submission, token)

export const submitMetricsBatch = (submissions, token) =>
  post('/metrics/submit-batch', submissions, token)

export const getAggregate = (country, month, indicator, segment, token) =>
  get(`/metrics/aggregate?country=${encodeURIComponent(country)}&month=${encodeURIComponent(month)}&indicator=${encodeURIComponent(indicator)}&segment=${encodeURIComponent(segment)}`, token)

// ── Admin ─────────────────────────────────────────────────────────────────────
export const adminReset = (token) => post('/admin/reset', {}, token)

export const adminResetAndCreateEpoch = (body, token) =>
  post('/admin/producers/reset-and-create-epoch', body, token)

export const adminGetEpochs = (token) => get('/admin/epochs', token)

export const adminGetEpochDetail = (epochId, token) => get(`/admin/epochs/${epochId}`, token)
