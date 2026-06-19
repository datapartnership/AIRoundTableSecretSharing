# AI Round Table — Protocol & Script Reference

This document explains the secure metric submission protocol, the role of each script,
and how the noise masking scheme works.

---

## Overview

Multiple data producers want to submit a metric to a shared API without revealing their
individual values — only the aggregate across all producers should be recoverable.

This is achieved by having each pair of producers establish a shared secret via
**ML-KEM-768** (a post-quantum key encapsulation mechanism), then using that secret to
derive opposing noise values that cancel out in the aggregate:

```
Producer A submits:  value_A + noise(A,B)
Producer B submits:  value_B - noise(A,B)
─────────────────────────────────────────
Aggregate:           value_A + value_B          ← noise cancels exactly
```

With three producers A, B, C there are three pairs and three noise terms, all cancelling:

```
A submits:  value_A + noise(A,B) + noise(A,C)
B submits:  value_B - noise(A,B) + noise(B,C)
C submits:  value_C - noise(A,C) - noise(B,C)
─────────────────────────────────────────────
Aggregate:  value_A + value_B + value_C
```

---

## Scripts

### `keygen.py` — Phase 1: Key generation and registration

**Runs once per producer.**

1. Generates an ML-KEM-768 keypair (public key: 1184 bytes, secret key: 2400 bytes).
2. Saves both keys to `keys/<producer_id>.json` (never regenerated if already present).
3. Posts the public key to `POST /api/keyexchange/register`.

The public key is safe to share — it is uploaded to the API and distributed to partners.
The secret key never leaves the local machine.

---

### `exchange.py` — Phases 2 & 3: Ciphertext exchange

**Runs once per partner group** (may need re-running while waiting for partners).

#### Phase 2: Encapsulation

The alphabetically *larger* producer in each pair encapsulates for the smaller one:

- Fetches all partner public keys from `GET /api/keyexchange/keys`
- For each partner with a lexicographically smaller ID:
  - Calls `Kyber768.encaps(partner_public_key)` → `(shared_secret, ciphertext)`
  - Posts the ciphertext to `POST /api/ciphertext`
  - Saves `shared_secret` locally in `keys/<id>_secrets.json`
- Skips partners with larger IDs — they will encapsulate for us

#### Phase 3: Decapsulation

The alphabetically *smaller* producer decapsulates incoming ciphertexts:

- Fetches all ciphertexts addressed to this producer from `GET /api/ciphertext`
- For each incoming ciphertext from a partner not yet in `_secrets.json`:
  - Calls `Kyber768.decaps(secret_key, ciphertext)` → `shared_secret`
  - Saves `shared_secret` to `keys/<id>_secrets.json`

Both producers end up with the same `shared_secret` for each pair — this is the
mathematical guarantee of key encapsulation: encaps and decaps produce identical output.

#### Re-run behaviour

The script is designed to be re-run safely. Already-established secrets are loaded from
`_secrets.json` and reused — re-encapsulating would generate a *different* random secret,
which would break noise cancellation with the partner.

If the ciphertext exchange is still incomplete (some partners haven't run yet), the script
prints who is missing and exits. Re-run after they confirm.

---

### `submit.py` — Phase 4: Noise application and submission

**Runs each submission round** (new data → update `submissions.csv` → re-run).

For each row in `submissions.csv`:

1. **Loads** shared secrets from `keys/<id>_secrets.json` (errors if missing — run `exchange.py` first).
2. **Confirms** the server sees a complete ciphertext exchange (pre-flight check).
3. **Computes** a noise value for each partner secret and applies it with the correct sign.
4. **Submits** the masked value to `POST /api/metrics/submit`.
5. **Skips** rows already submitted this epoch (idempotent — safe to re-run).

---

## Noise formula

```python
msg   = f"{country}|{YYYY-MM}".encode()
h     = HMAC-SHA256(key=shared_secret, msg=msg)
seed  = struct.unpack("<q", h[:8])[0]      # signed little-endian int64
noise = (seed % (2 * MAX_NOISE + 1)) - MAX_NOISE
```

`MAX_NOISE = 100_000_000`

The noise is **deterministic**: the same shared secret and the same `(country, month)` always
produce the same value. This means:
- Re-running `submit.py` produces the same masked values.
- Each month gets a different noise value automatically (month is part of the message).
- No coordination is needed between producers on the noise values themselves.

#### Sign convention

For each pair `(A, B)` where `A < B` lexicographically:

| Producer | Sign applied |
|----------|-------------|
| A (smaller) | `+noise` |
| B (larger)  | `−noise` |

This ensures noise cancels exactly in the aggregate regardless of the number of producers.

---

## Security properties

| Property | Mechanism |
|----------|-----------|
| Post-quantum key exchange | ML-KEM-768 (NIST PQC standard) |
| Shared secret size | 32 bytes |
| Noise range | ±100,000,000 per pair |
| Noise derivation | HMAC-SHA256 (keyed by shared secret) |
| Individual value privacy | Noise masks each submission; only the sum is recoverable |
| Replay / duplicate guard | Server rejects duplicate `(producer, country, month, epoch)` tuples |

> **Note:** The Python noise formula uses HMAC-SHA256 and is **not** compatible with the
> C# `SecureNoiseGenerator`, which uses .NET `System.Random` seeded from the shared secret.
> All producers in a round must use the same implementation.

---

## Data flow diagram

```
keygen.py                    exchange.py                  submit.py
─────────                    ───────────                  ─────────
Generate keypair             Encapsulate (larger partner)
    │                            │
    ▼                            ▼
POST /keyexchange/register   POST /ciphertext
                                 │
                             Decapsulate (smaller partner)
                             ←── GET /ciphertext
                                 │
                             Save _secrets.json ──────────► Load _secrets.json
                                                                │
                                                            Compute masked value
                                                                │
                                                            POST /metrics/submit
```
