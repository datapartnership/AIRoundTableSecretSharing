/**
 * Diffie-Hellman Key Exchange using Web Crypto API (ECDH P-256)
 * This provides cryptographically secure key exchange that the aggregator cannot compute.
 */

// Store key pairs in memory (in production, use secure storage)
const keyPairs = new Map();
const sharedSecrets = new Map();

/**
 * Generate an ECDH P-256 key pair for a partner
 */
export async function generateKeyPair(partnerId) {
  const keyPair = await crypto.subtle.generateKey(
    {
      name: 'ECDH',
      namedCurve: 'P-256',
    },
    true, // extractable - needed to export public key
    ['deriveBits']
  );
  
  keyPairs.set(partnerId, keyPair);
  return keyPair;
}

/**
 * Export public key as base64 string (for sending to aggregator)
 */
export async function exportPublicKey(partnerId) {
  const keyPair = keyPairs.get(partnerId);
  if (!keyPair) {
    throw new Error(`No key pair found for ${partnerId}`);
  }
  
  const exported = await crypto.subtle.exportKey('spki', keyPair.publicKey);
  return btoa(String.fromCharCode(...new Uint8Array(exported)));
}

/**
 * Import a public key from base64 string (received from aggregator)
 */
export async function importPublicKey(publicKeyBase64) {
  const binaryString = atob(publicKeyBase64);
  const bytes = new Uint8Array(binaryString.length);
  for (let i = 0; i < binaryString.length; i++) {
    bytes[i] = binaryString.charCodeAt(i);
  }
  
  return crypto.subtle.importKey(
    'spki',
    bytes,
    {
      name: 'ECDH',
      namedCurve: 'P-256',
    },
    true,
    []
  );
}

/**
 * Compute shared secret with another partner using ECDH
 * This is the key insight: the aggregator CANNOT compute this!
 */
export async function computeSharedSecret(myPartnerId, otherPartnerId, otherPublicKeyBase64) {
  const myKeyPair = keyPairs.get(myPartnerId);
  if (!myKeyPair) {
    throw new Error(`No key pair found for ${myPartnerId}`);
  }
  
  const otherPublicKey = await importPublicKey(otherPublicKeyBase64);
  
  // Derive shared secret using ECDH
  const sharedBits = await crypto.subtle.deriveBits(
    {
      name: 'ECDH',
      public: otherPublicKey,
    },
    myKeyPair.privateKey,
    256 // 256 bits = 32 bytes
  );
  
  // Store the shared secret
  const secretKey = getSecretKey(myPartnerId, otherPartnerId);
  sharedSecrets.set(secretKey, new Uint8Array(sharedBits));
  
  return new Uint8Array(sharedBits);
}

/**
 * Get a consistent key for storing shared secrets (order-independent)
 */
function getSecretKey(id1, id2) {
  const sorted = [id1, id2].sort();
  return `${sorted[0]}|${sorted[1]}`;
}

/**
 * Retrieve a previously computed shared secret
 */
export function getSharedSecret(myPartnerId, otherPartnerId) {
  const secretKey = getSecretKey(myPartnerId, otherPartnerId);
  return sharedSecrets.get(secretKey);
}

/**
 * Check if we have a key pair for a partner
 */
export function hasKeyPair(partnerId) {
  return keyPairs.has(partnerId);
}

/**
 * Check if we have a shared secret with another partner
 */
export function hasSharedSecret(myPartnerId, otherPartnerId) {
  const secretKey = getSecretKey(myPartnerId, otherPartnerId);
  return sharedSecrets.has(secretKey);
}

/**
 * Clear all keys (for testing/reset)
 */
export function clearAllKeys() {
  keyPairs.clear();
  sharedSecrets.clear();
}

/**
 * Get key exchange status for a partner
 */
export function getKeyExchangeStatus(partnerId, allPartnerIds) {
  const hasOwnKey = hasKeyPair(partnerId);
  const otherPartners = allPartnerIds.filter(id => id !== partnerId);
  const completedExchanges = otherPartners.filter(otherId => 
    hasSharedSecret(partnerId, otherId)
  );
  
  return {
    hasOwnKeyPair: hasOwnKey,
    totalPartners: otherPartners.length,
    completedExchanges: completedExchanges.length,
    isComplete: hasOwnKey && completedExchanges.length === otherPartners.length,
    pendingPartners: otherPartners.filter(id => !hasSharedSecret(partnerId, id))
  };
}
