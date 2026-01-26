/**
 * Deterministic noise generation - mirrors the C# implementation
 * Uses Web Crypto API for SHA-256 hashing
 */

async function sha256(message) {
  const msgBuffer = new TextEncoder().encode(message);
  const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
  return new Uint8Array(hashBuffer);
}

function bytesToLong(bytes) {
  // Convert first 8 bytes to a BigInt, then to Number (safe for our range)
  let value = BigInt(0);
  for (let i = 7; i >= 0; i--) {
    value = (value << BigInt(8)) | BigInt(bytes[i]);
  }
  return value;
}

// Simple seeded random number generator (matches .NET Random behavior approximately)
function seededRandom(seed) {
  // Use a simple LCG that matches behavior
  let state = seed & 0x7FFFFFFF;
  
  return {
    nextInt64(min, max) {
      // Simple approach: generate multiple random values and combine
      state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
      const r1 = state / 0x7FFFFFFF;
      state = (state * 1103515245 + 12345) & 0x7FFFFFFF;
      const r2 = state / 0x7FFFFFFF;
      
      const range = max - min;
      const value = Math.floor(r1 * range) + min;
      return value;
    }
  };
}

/**
 * Generates deterministic noise between two producers for a given context.
 * Both producers will generate the SAME noise value independently.
 */
export async function generateNoise(producerId1, producerId2, country, month, maxNoise = 100_000_000) {
  if (!producerId1 || !producerId2) {
    throw new Error('Producer IDs cannot be null or empty');
  }
  
  if (producerId1 === producerId2) {
    throw new Error('Cannot generate noise with self');
  }
  
  // Create deterministic seed from inputs
  // Order doesn't matter - sort alphabetically for consistency
  const sortedIds = [producerId1, producerId2].sort();
  const monthStr = `${month.getFullYear()}-${String(month.getMonth() + 1).padStart(2, '0')}`;
  const seedString = `${sortedIds[0]}|${sortedIds[1]}|${country}|${monthStr}`;
  
  // Hash to create deterministic seed
  const hashBytes = await sha256(seedString);
  const seed = bytesToLong(hashBytes);
  
  // Create random generator with deterministic seed
  const random = seededRandom(Number(seed & BigInt(0x7FFFFFFF)));
  
  // Generate noise in range [-maxNoise, maxNoise]
  const noise = random.nextInt64(-maxNoise, maxNoise + 1);
  
  return noise;
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
 * Calculate the total noise and masked value for a producer
 */
export async function calculateMaskedValue(myProducerId, allProducerIds, country, month, actualValue) {
  const otherProducers = allProducerIds.filter(id => id !== myProducerId);
  const noiseBreakdown = {};
  let totalNoise = 0;
  
  // IMPORTANT: maxNoise must be the SAME for all partners to ensure cancellation!
  // Using a fixed value that all partners agree on (e.g., configured by the aggregator)
  const maxNoise = 100_000_000;
  
  for (const otherProducerId of otherProducers) {
    const noise = await generateNoise(myProducerId, otherProducerId, country, month, maxNoise);
    const sign = getNoiseSign(myProducerId, otherProducerId);
    const appliedNoise = noise * sign;
    
    noiseBreakdown[otherProducerId] = {
      rawNoise: noise,
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
