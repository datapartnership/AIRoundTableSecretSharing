const API_BASE = '/api';

export async function getProducers(effectiveDate = null) {
  const params = effectiveDate ? `?effectiveDate=${effectiveDate.toISOString().split('T')[0]}` : '';
  const response = await fetch(`${API_BASE}/registry/producers${params}`);
  if (!response.ok) throw new Error('Failed to fetch producers');
  return response.json();
}

export async function getEpoch(date = null) {
  const params = date ? `?date=${date.toISOString().split('T')[0]}` : '';
  const response = await fetch(`${API_BASE}/registry/epoch${params}`);
  if (!response.ok) throw new Error('Failed to fetch epoch');
  return response.json();
}

export async function submitMetric(submission) {
  const response = await fetch(`${API_BASE}/metrics/submit`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
    body: JSON.stringify(submission),
  });
  
  const data = await response.json();
  
  if (!response.ok) {
    throw new Error(data.error || 'Failed to submit metric');
  }
  
  return data;
}

export async function getAggregate(country, month) {
  // month is already a Date object - use UTC methods to avoid timezone issues
  const year = month.getUTCFullYear();
  const monthNum = month.getUTCMonth() + 1;
  const monthStr = `${year}-${String(monthNum).padStart(2, '0')}-01`;
  const response = await fetch(`${API_BASE}/metrics/aggregate?country=${encodeURIComponent(country)}&month=${monthStr}`);
  if (!response.ok) throw new Error('Failed to fetch aggregate');
  return response.json();
}

// ==================== Key Exchange API ====================

/**
 * Register a partner's public key with the aggregator
 */
export async function registerPublicKey(partnerId, publicKeyBase64) {
  const response = await fetch(`${API_BASE}/keyexchange/register`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
    },
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

/**
 * Get all registered public keys from the aggregator
 */
export async function getAllPublicKeys() {
  const response = await fetch(`${API_BASE}/keyexchange/keys`);
  if (!response.ok) throw new Error('Failed to fetch public keys');
  const data = await response.json();
  // API returns { partnerKeys: [...], totalPartners: n }
  return data.partnerKeys || [];
}

/**
 * Get a specific partner's public key
 */
export async function getPublicKey(partnerId) {
  const response = await fetch(`${API_BASE}/keyexchange/keys/${encodeURIComponent(partnerId)}`);
  if (!response.ok) {
    if (response.status === 404) return null;
    throw new Error('Failed to fetch public key');
  }
  return response.json();
}

/**
 * Get key exchange status from the aggregator
 */
export async function getKeyExchangeStatus() {
  const response = await fetch(`${API_BASE}/keyexchange/status`);
  if (!response.ok) throw new Error('Failed to fetch key exchange status');
  return response.json();
}

