/**
 * Secure noise generation using HMAC-SHA256 with Diffie-Hellman shared secrets
 * This mirrors the C# SecureNoiseGenerator implementation
 */

import { getSharedSecret } from './crypto';

/**
 * Compute HMAC-SHA256 using Web Crypto API
 */
async function hmacSha256(key, message) {
  // Import the shared secret as an HMAC key
  const cryptoKey = await crypto.subtle.importKey(
    'raw',
    key,
    { name: 'HMAC', hash: 'SHA-256' },
    false,
    ['sign']
  );
  
  const msgBuffer = new TextEncoder().encode(message);
  const signature = await crypto.subtle.sign('HMAC', cryptoKey, msgBuffer);
  return new Uint8Array(signature);
}

function bytesToLong(bytes) {
  // Convert first 8 bytes to a BigInt, then to Number (safe for our range)
  let value = BigInt(0);
  for (let i = 7; i >= 0; i--) {
    value = (value << BigInt(8)) | BigInt(bytes[i]);
  }
  return value;
}

/**
 * Generates SECURE noise between two producers using their shared secret.
 * The aggregator CANNOT compute this - it requires the shared secret from DH key exchange.
 */
export async function generateSecureNoise(myProducerId, otherProducerId, country, month, maxNoise = 100_000_000) {
  if (!myProducerId || !otherProducerId) {
    throw new Error('Producer IDs cannot be null or empty');
  }
  
  if (myProducerId === otherProducerId) {
    throw new Error('Cannot generate noise with self');
  }
  
  // Get the shared secret computed via Diffie-Hellman
  const sharedSecret = getSharedSecret(myProducerId, otherProducerId);
  if (!sharedSecret) {
    throw new Error(`No shared secret with ${otherProducerId}. Complete key exchange first.`);
  }
  
  // Create context string (same as C# implementation)
  const sortedIds = [myProducerId, otherProducerId].sort();
  const monthStr = `${month.getUTCFullYear()}-${String(month.getUTCMonth() + 1).padStart(2, '0')}`;
  const context = `${sortedIds[0]}|${sortedIds[1]}|${country}|${monthStr}`;
  
  // Compute HMAC-SHA256(sharedSecret, context)
  const hash = await hmacSha256(sharedSecret, context);
  
  // Convert to noise value (matching C# implementation)
  const rawValue = bytesToLong(hash);
  // Mask to positive value
  const positiveValue = rawValue & BigInt('0x7FFFFFFFFFFFFFFF');
  // Convert to range [-maxNoise, maxNoise]
  const maxNoiseBig = BigInt(maxNoise);
  const noise = Number((positiveValue % (BigInt(2) * maxNoiseBig)) - maxNoiseBig);
  
  // Sign: first partner alphabetically adds, second subtracts
  const sign = myProducerId.localeCompare(otherProducerId) < 0 ? 1 : -1;
  
  return noise * sign;
}

/**
 * Determine the sign for noise application based on alphabetical ordering.
 * This ensures that Producer A adds what Producer B subtracts.
 */
export function getNoiseSign(myProducerId, otherProducerId) {
  const comparison = myProducerId.localeCompare(otherProducerId);
  
  if (comparison < 0) return 1;   // I come first alphabetically, ADD
  if (comparison > 0) return -1;  // I come second alphabetically, SUBTRACT
  
  throw new Error('Cannot compare producer with itself');
}

/**
 * Calculate the total noise and masked value using SECURE noise
 * Requires completed Diffie-Hellman key exchange with all other partners
 */
export async function calculateMaskedValue(myProducerId, allProducerIds, country, month, actualValue) {
  const otherProducers = allProducerIds.filter(id => id !== myProducerId);
  const noiseBreakdown = {};
  let totalNoise = 0;
  
  // IMPORTANT: maxNoise must be the SAME for all partners to ensure cancellation!
  const maxNoise = 100_000_000;
  
  for (const otherProducerId of otherProducers) {
    // This uses HMAC with the shared secret - aggregator CANNOT compute this!
    const appliedNoise = await generateSecureNoise(myProducerId, otherProducerId, country, month, maxNoise);
    const sign = appliedNoise >= 0 ? 1 : -1;
    
    noiseBreakdown[otherProducerId] = {
      rawNoise: Math.abs(appliedNoise),
      sign: sign,
      appliedNoise: appliedNoise
    };
    
    totalNoise += appliedNoise;
  }
  
  return {
    actualValue,
    totalNoise,
    maskedValue: actualValue + totalNoise,
    noiseBreakdown
  };
}

