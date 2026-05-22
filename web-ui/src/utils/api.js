const API_BASE = '/api';

/**
 * Obtain a Bearer token via OAuth 2.0 client credentials flow.
 * Returns the access_token string.
 */
export async function getToken(clientId, clientSecret) {
  const body = new URLSearchParams({
    grant_type: 'client_credentials',
    client_id: clientId,
    client_secret: clientSecret,
  });
  const response = await fetch('/auth/token', {
    method: 'POST',
    headers: { 'Content-Type': 'application/x-www-form-urlencoded' },
    body: body.toString(),
  });
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || 'Authentication failed');
  }
  const data = await response.json();
  return data.access_token;
}

function buildHeaders(token, contentType = null) {
  const headers = {};
  if (token) headers['Authorization'] = `Bearer ${token}`;
  if (contentType) headers['Content-Type'] = contentType;
  return headers;
}

export async function getProducers(effectiveDate = null, token = null) {
  const params = effectiveDate ? `?effectiveDate=${effectiveDate.toISOString().split('T')[0]}` : '';
  const response = await fetch(`${API_BASE}/registry/producers${params}`, {
    headers: buildHeaders(token),
  });
  if (!response.ok) throw new Error('Failed to fetch producers');
  return response.json();
}

export async function getEpoch(date = null, token = null) {
  const params = date ? `?date=${date.toISOString().split('T')[0]}` : '';
  const response = await fetch(`${API_BASE}/registry/epoch${params}`, {
    headers: buildHeaders(token),
  });
  if (!response.ok) throw new Error('Failed to fetch epoch');
  return response.json();
}

export async function submitMetric(submission, token = null) {
  const response = await fetch(`${API_BASE}/metrics/submit`, {
    method: 'POST',
    headers: buildHeaders(token, 'application/json'),
    body: JSON.stringify(submission),
  });

  const data = await response.json();

  if (!response.ok) {
    throw new Error(data.error || 'Failed to submit metric');
  }

  return data;
}

export async function getAggregate(country, month, token = null) {
  const year = month.getUTCFullYear();
  const monthNum = month.getUTCMonth() + 1;
  const monthStr = `${year}-${String(monthNum).padStart(2, '0')}-01`;
  const response = await fetch(
    `${API_BASE}/metrics/aggregate?country=${encodeURIComponent(country)}&month=${monthStr}`,
    { headers: buildHeaders(token) }
  );
  if (!response.ok) throw new Error('Failed to fetch aggregate');
  return response.json();
}

// ==================== Key Exchange API ====================

export async function registerPublicKey(partnerId, publicKeyBase64, token = null) {
  const response = await fetch(`${API_BASE}/keyexchange/register`, {
    method: 'POST',
    headers: buildHeaders(token, 'application/json'),
    body: JSON.stringify({
      producerId: partnerId,
      publicKeyBase64: publicKeyBase64
    }),
  });

  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || data.message || 'Failed to register public key');
  }

  return response.json();
}

export async function getAllPublicKeys(token = null) {
  const response = await fetch(`${API_BASE}/keyexchange/keys`, {
    headers: buildHeaders(token),
  });
  if (!response.ok) throw new Error('Failed to fetch public keys');
  const data = await response.json();
  return data.partnerKeys || [];
}

export async function getPublicKey(partnerId, token = null) {
  const response = await fetch(`${API_BASE}/keyexchange/keys/${encodeURIComponent(partnerId)}`, {
    headers: buildHeaders(token),
  });
  if (!response.ok) {
    if (response.status === 404) return null;
    throw new Error('Failed to fetch public key');
  }
  return response.json();
}

export async function getKeyExchangeStatus(token = null) {
  const response = await fetch(`${API_BASE}/keyexchange/status`, {
    headers: buildHeaders(token),
  });
  if (!response.ok) throw new Error('Failed to fetch key exchange status');
  return response.json();
}
