// Noise formula compatible with the C# SecureNoiseGenerator (HMAC-SHA256 based)

async function deriveNoise(sharedSecretBytes, country, month, indicator, segment) {
  const key = await crypto.subtle.importKey(
    'raw', sharedSecretBytes, { name: 'HMAC', hash: 'SHA-256' }, false, ['sign']
  )
  const hmac = new Uint8Array(
    await crypto.subtle.sign(
      'HMAC',
      key,
      new TextEncoder().encode(`${country}|${month}|${indicator}|${segment}`)
    )
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
// `actual` may be a number, string, or bigint. Returns bigint.
export async function calculateMaskedValue(actual, country, month, indicator, segment, myId, secretsMap) {
  let masked = typeof actual === 'bigint' ? actual : BigInt(actual)
  for (const [partnerId, ss] of secretsMap) {
    const noise = await deriveNoise(ss, country, month, indicator, segment)
    masked += BigInt(noise) * (myId < partnerId ? 1n : -1n)
  }
  return masked
}
