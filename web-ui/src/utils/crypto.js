import { MlKem768 } from 'mlkem'

export function bytesToBase64(bytes) {
  let bin = ''
  for (let i = 0; i < bytes.length; i++) bin += String.fromCharCode(bytes[i])
  return btoa(bin)
}

export function base64ToBytes(b64) {
  const bin = atob(b64)
  const bytes = new Uint8Array(bin.length)
  for (let i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i)
  return bytes
}

// ML-KEM-768: ek = 1184 bytes (public), dk = 2400 bytes (private)
export async function generateMlKemKeyPair() {
  const [ek, dk] = await new MlKem768().generateKeyPair()
  return { ekBase64: bytesToBase64(ek), dkBase64: bytesToBase64(dk) }
}

// Returns { ctBase64: string, sharedSecret: Uint8Array }
export async function encapsulate(partnerEkBase64) {
  const [ct, ss] = await new MlKem768().encap(base64ToBytes(partnerEkBase64))
  return { ctBase64: bytesToBase64(ct), sharedSecret: ss }
}

// Returns Uint8Array (32-byte shared secret)
export async function decapsulate(ctBase64, dkBase64) {
  return new MlKem768().decap(base64ToBytes(ctBase64), base64ToBytes(dkBase64))
}
