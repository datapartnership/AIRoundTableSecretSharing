// Noise formula compatible with the Python client (HMAC-SHA256 based)

// Returns the signed noise value for one pair, deterministic from shared secret + (country, month)
async function deriveNoise(sharedSecretBytes, country, month) {
  const key = await crypto.subtle.importKey(
    'raw', sharedSecretBytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']
  )
  const hmac = new Uint8Array(
    await crypto.subtle.sign('HMAC', key, new TextEncoder().encode(`${country}|${month}`))
  )
  // Signed little-endian int64 from first 8 bytes
  let seed = 0n
  for (let i = 0; i < 8; i++) seed |= BigInt(hmac[i]) << BigInt(i * 8)
  if (seed >= 2n ** 63n) seed -= 2n ** 64n
  // Python-style modulo — always non-negative before subtracting offset
  const mod = 200_000_001n
  const raw = seed % mod
  return Number(raw < 0n ? raw + mod : raw) - 100_000_000
}

// secretsMap: Map<partnerId, Uint8Array(32)>
// Sign convention: +1 if myId < partnerId (lexicographic), -1 otherwise
export async function calculateMaskedValue(actual, country, month, myId, secretsMap) {
  let masked = actual
  for (const [partnerId, ss] of secretsMap) {
    const noise = await deriveNoise(ss, country, month)
    masked += noise * (myId < partnerId ? 1 : -1)
  }
  return masked
}
