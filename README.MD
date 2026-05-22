# Privacy-Preserving Secure Aggregation

A .NET 10 + React implementation of **cryptographically secure multi-party aggregation** using pairwise noise cancellation with **ML-KEM-768** (FIPS 203, the NIST post-quantum Key Encapsulation Mechanism standard). Partners can contribute sensitive values to a shared aggregate without revealing individual values — not even to the aggregator.

## 🎯 Problem Statement

Imagine a scenario where the Development Data Partnership wants to collect monthly active user statistics from multiple messaging platforms (PartnerA, PartnerB, PartnerC) for each country. The partners want to contribute to the aggregate statistic but **cannot reveal their individual numbers** due to competitive sensitivity.

**Requirements:**
- ✅ The aggregator learns the **total** across all partners
- ✅ The aggregator **cannot** learn individual partner values (cryptographically enforced)
- ✅ Partners do **not communicate** directly with each other
- ✅ No pre-shared secrets required between partners
- ✅ Secure against quantum adversaries (post-quantum KEM)

## 🔐 The Secure Pairwise Noise Cancellation Protocol

### Core Idea

Each pair of partners establishes a **shared secret** using ML-KEM key encapsulation (facilitated by the aggregator, but the aggregator cannot recover the shared secret). From this secret, they derive **pairwise noise**: one partner adds it, the other subtracts it. When aggregated, the noise cancels perfectly, leaving only the true sum.

### Why ML-KEM?

ML-KEM (Module Lattice-based Key Encapsulation Mechanism, standardised as FIPS 203) is the NIST post-quantum replacement for ECDH. Unlike Diffie-Hellman, it is an asymmetric KEM: the **encapsulator** generates a fresh shared secret together with a ciphertext; the **decapsulator** recovers the same secret from the ciphertext using their private key. The aggregator relays the ciphertext but cannot recover the secret.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  ML-KEM-768 KEY ENCAPSULATION (FIPS 203)                                     │
├──────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  Convention: alphabetically LARGER partner encapsulates for the SMALLER one  │
│  (B→A, C→A, C→B for a three-partner setup)                                  │
│                                                                              │
│  PartnerB (larger) generates:                                                │
│    Encapsulate(pk_A) → (ciphertext_BA, secret_AB)                           │
│    Posts ciphertext_BA to the aggregator                                     │
│                                                                              │
│  PartnerA (smaller) retrieves ciphertext_BA and:                             │
│    Decapsulate(ciphertext_BA, sk_A) → secret_AB   ← SAME SECRET!            │
│                                                                              │
│  Aggregator stores: pk_A, pk_B, ciphertext_BA (all opaque blobs)            │
│  Aggregator CANNOT recover: secret_AB (KEM security, IND-CCA2)              │
│                                                                              │
│  Noise = HMAC-SHA256(secret_AB, country || month)                           │
│  → Only the two partners can compute this, not the aggregator!              │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Protocol Steps

#### Phase 1: Key Generation and Registration

Each partner generates an ML-KEM-768 key pair locally. Only the **encapsulation key** (public key, 1184 bytes) is posted to the aggregator. The **decapsulation key** (private key, 2400 bytes) never leaves the partner's system.

```csharp
// MlKemKeyExchange.cs
MLKem kem = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
byte[] encapsulationKey = kem.ExportEncapsulationKey(); // safe to share
// decapsulation key stays local, never transmitted
```

```
PartnerA → Aggregator: POST pk_A  (1184 bytes)
PartnerB → Aggregator: POST pk_B
PartnerC → Aggregator: POST pk_C
```

#### Phase 2: Encapsulation (Larger → Smaller)

Each partner fetches all other partners' encapsulation keys. For every partner that is **alphabetically smaller**, it encapsulates and posts the resulting ciphertext:

```csharp
// PartnerB encapsulates for PartnerA
using var partnerAKem = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, pk_A);
partnerAKem.Encapsulate(out byte[] ciphertext_BA, out byte[] secret_AB);
// POST ciphertext_BA to aggregator; keep secret_AB locally
```

```
PartnerB → Aggregator: POST ciphertext_BA  (1088 bytes, addressed to A)
PartnerC → Aggregator: POST ciphertext_CA  (addressed to A)
PartnerC → Aggregator: POST ciphertext_CB  (addressed to B)
PartnerA: no encapsulations (smallest)
```

#### Phase 3: Decapsulation (Smaller Recovers Secret)

Each partner fetches ciphertexts addressed to it and decapsulates using its local private key:

```csharp
// PartnerA recovers both shared secrets
byte[] secret_AB = myKem.Decapsulate(ciphertext_BA);
byte[] secret_AC = myKem.Decapsulate(ciphertext_CA);
```

**The aggregator holds the ciphertexts but cannot decapsulate them** — only the holder of the decapsulation key can.

#### Phase 4: Generate Noise and Submit

Noise is derived using HMAC-SHA256 with the shared secret as the key. The sign depends on alphabetical order — the smaller partner adds, the larger subtracts:

```csharp
// SecureNoiseGenerator.cs
long noise = HMAC-SHA256(sharedSecret, country || month) → scaled to [-maxNoise, +maxNoise]
int  sign  = (myId < otherId) ? +1 : -1;     // smaller adds, larger subtracts

maskedMAU = actualMAU + Σ(noise_with_partner × sign)
```

The aggregator receives only `maskedMAU` — it cannot reproduce the noise without the shared secrets.

#### Aggregation

The aggregator sums all masked values. The noise cancels perfectly, leaving the true total:

```
Σ(maskedMAU) = Σ(actualMAU) + 0 = TotalMAU ✓
```

### Mathematical Proof (3 Partners)

Let:
- `V_A`, `V_B`, `V_C` = actual values
- `n_AB` = noise from secret between A and B (same value for both, derived via HMAC)
- `n_AC` = noise from secret between A and C
- `n_BC` = noise from secret between B and C

| Partner | Masked Value |
|---------|--------------|
| PartnerA (smallest) | `V_A + n_AB + n_AC` |
| PartnerB (middle)   | `V_B − n_AB + n_BC` |
| PartnerC (largest)  | `V_C − n_AC − n_BC` |

**Sum:**
```
(V_A + n_AB + n_AC) + (V_B − n_AB + n_BC) + (V_C − n_AC − n_BC)
= V_A + V_B + V_C + (n_AB − n_AB) + (n_AC − n_AC) + (n_BC − n_BC)
= V_A + V_B + V_C  ✓
```

### Numerical Example

| Partner | Actual MAU | Noise Applied | Masked MAU |
|---------|------------|---------------|------------|
| PartnerA | 1,000,000 | +150,000 (with B) +80,000 (with C) | 1,230,000 |
| PartnerB | 500,000 | −150,000 (with A) +45,000 (with C) | 395,000 |
| PartnerC | 200,000 | −80,000 (with A) −45,000 (with B) | 75,000 |
| **Total** | **1,700,000** | **0** | **1,700,000** ✓ |

The aggregator learns only the total (1,700,000) but **cannot determine** any individual partner's MAU value.

## 🏗️ Project Structure

```
AIRoundTableSecretSharing/
├── AIRoundTableSecretSharingCommon.sln    # Solution file
│
├── AIRoundTableSecretSharingAPI/          # ASP.NET Core Web API (Aggregator)
│   ├── Controllers/
│   │   ├── AuthController.cs              # OAuth 2.0 client credentials token endpoint
│   │   ├── MetricsController.cs           # Submit metrics, get aggregates
│   │   ├── RegistryController.cs          # Manage partners and epochs
│   │   ├── KeyExchangeController.cs       # ML-KEM public key registration
│   │   └── CiphertextController.cs        # ML-KEM ciphertext relay
│   ├── Services/
│   │   ├── InMemoryDatastore.cs           # In-memory storage for demo
│   │   ├── KeyStore.cs                    # Store partner encapsulation keys
│   │   └── CiphertextStore.cs             # Store ML-KEM ciphertexts
│   ├── Program.cs
│   └── appsettings.json
│
├── AIRoundTableSecretSharingCommon/       # Shared library
│   ├── Core/
│   │   ├── MlKemKeyExchange.cs            # ML-KEM-768 key gen, encapsulate, decapsulate
│   │   ├── SecureNoiseGenerator.cs        # HMAC-based noise from shared secrets
│   │   └── DeterministicNoiseGenerator.cs # Simple hash-based noise (basic demo only)
│   ├── Models/
│   │   ├── MetricSubmission.cs            # Submission with masked value
│   │   ├── AggregationResult.cs           # Aggregation result with Total
│   │   ├── SubmissionResult.cs            # Submission result
│   │   ├── ProducerInfo.cs                # Partner information
│   │   ├── ProducerEpoch.cs               # Partner set for a time period
│   │   └── PartnerPublicKey.cs            # Encapsulation key + ciphertext models
│   └── Producers/
│       ├── SecureProducerClient.cs        # Secure client with ML-KEM flow
│       └── ProducerClient.cs              # Basic client (deterministic demo only)
│
├── AIRoundTableSecretSharingSecureDemo/   # Console demo with full ML-KEM exchange
│   └── Program.cs                         # Interactive secure aggregation demo
│
└── web-ui/                                # React demonstration UI
    └── src/
        ├── pages/                         # Partner and results pages
        └── utils/                         # Noise calculation utilities
```

## 🔧 Key Components

### MlKemKeyExchange.cs

Wraps `System.Security.Cryptography.MLKem` (built into .NET 9+):

```csharp
public class MlKemKeyExchange : IDisposable
{
    private readonly MLKem _kem;

    public MlKemKeyExchange()
    {
        _kem = MLKem.GenerateKey(MLKemAlgorithm.MLKem768);
    }

    // Share this with other partners via the aggregator (1184 bytes)
    public string GetPublicKeyBase64() => Convert.ToBase64String(_kem.ExportEncapsulationKey());

    // Called by the alphabetically LARGER partner
    public (byte[] ciphertext, byte[] sharedSecret) Encapsulate(byte[] partnerPublicKey)
    {
        using var partnerKem = MLKem.ImportEncapsulationKey(MLKemAlgorithm.MLKem768, partnerPublicKey);
        partnerKem.Encapsulate(out byte[] ciphertext, out byte[] sharedSecret);
        return (ciphertext, sharedSecret);  // POST ciphertext; keep sharedSecret
    }

    // Called by the alphabetically SMALLER partner after fetching ciphertext
    public byte[] Decapsulate(byte[] ciphertext) => _kem.Decapsulate(ciphertext);
}
```

### SecureNoiseGenerator.cs

Derives pairwise noise from a shared secret using HMAC-SHA256 — unchanged from ECDH version, since the noise generation only depends on having a shared secret:

```csharp
public static long GenerateNoise(byte[] sharedSecret, string country, DateTime month,
                                  long maxNoise = 100_000_000)
{
    var context = $"{country}|{month:yyyy-MM}";
    using var hmac = new HMACSHA256(sharedSecret);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
    var seed = BitConverter.ToInt64(hash, 0);
    return new Random((int)(seed & 0x7FFFFFFF)).NextInt64(-maxNoise, maxNoise + 1);
}

// Smaller partner (alphabetically) adds; larger subtracts → noise cancels in the sum
public static int GetNoiseSign(string myId, string otherId)
    => string.Compare(myId, otherId, StringComparison.Ordinal) < 0 ? 1 : -1;
```

### Aggregator Data Flow

```
Phase 1 — Key Registration
┌─────────────┐  POST pk_A (1184 B)  ┌──────────────┐
│  PartnerA   │ ────────────────────► │              │
└─────────────┘                       │  Key Store   │
┌─────────────┐  POST pk_B           │  (encaps.    │
│  PartnerB   │ ────────────────────► │   keys only) │
└─────────────┘                       └──────────────┘

Phase 2 — Ciphertext Relay
┌─────────────┐  POST ct_BA (1088 B) ┌──────────────┐
│  PartnerB   │ ────────────────────► │  Ciphertext  │
└─────────────┘  POST ct_CA          │  Store       │
┌─────────────┐ ─────────────────────►  (opaque     │
│  PartnerC   │  POST ct_CB          │   blobs)     │
└─────────────┘ ────────────────────► └──────────────┘

Phase 3 — Decapsulation (local, no API call to aggregator)
┌─────────────┐  GET ciphertexts     ┌──────────────┐
│  PartnerA   │ ◄──────────────────── │  Ciphertext  │
└─────────────┘  [ct_BA, ct_CA]      │  Store       │
                                      └──────────────┘
  PartnerA.Decapsulate(ct_BA) → secret_AB  (aggregator CANNOT do this)
  PartnerA.Decapsulate(ct_CA) → secret_AC

Phase 4 — Masked Submission
┌─────────────┐  POST maskedMAU_A    ┌──────────────┐
│  PartnerA   │ ────────────────────► │   Metrics    │
└─────────────┘                       │   Store      │
               Aggregator sums all → TotalMAU ✓
```

### Epochs

An **epoch** represents a fixed set of partners for a time period. All partners in an epoch must submit before the aggregate is computed. This ensures noise cancellation works correctly (all pairs are complete) and lets partners join or leave without disrupting ongoing submissions.

### API Endpoints

All `/api/` endpoints require a valid Bearer token obtained from `POST /auth/token`.

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/auth/token` | POST | Obtain a Bearer token (client credentials flow) |
| `/api/keyexchange/register` | POST | Register ML-KEM-768 encapsulation key (1184 bytes) |
| `/api/keyexchange/keys` | GET | Get all partners' encapsulation keys |
| `/api/keyexchange/keys/{id}` | GET | Get a specific partner's encapsulation key |
| `/api/keyexchange/status` | GET | Check key registration status |
| `/api/ciphertext` | POST | Store a KEM ciphertext (from encapsulating partner) |
| `/api/ciphertext` | GET | Fetch ciphertexts addressed to a given partner |
| `/api/registry/producers` | GET | List active partners |
| `/api/registry/epoch` | GET | Get current epoch info |
| `/api/metrics/submit` | POST | Submit a masked metric value |
| `/api/metrics/aggregate` | GET | Get aggregate for a country/month |

### Authentication — OAuth 2.0 Client Credentials

All `/api/` endpoints are protected by JWT Bearer authentication. Clients first obtain a short-lived token from `POST /auth/token` using their `client_id` and `client_secret`, then include it as a Bearer token in every subsequent request.

Client credentials are configured in `appsettings.json`:

```json
{
  "Jwt": {
    "Key": "AIRoundTable-JWT-Signing-Key-2026-MustBe32CharsOrMore!",
    "Issuer": "AIRoundTableSecretSharingAPI",
    "Audience": "AIRoundTableSecretSharingAPI",
    "ExpiryMinutes": 60
  },
  "ClientCredentials": {
    "partnerA": "pA-client-secret-2026-abc123",
    "partnerB": "pB-client-secret-2026-def456",
    "partnerC": "pC-client-secret-2026-ghi789"
  }
}
```

The C# client handles this automatically via `SecureProducerClient.CreateAsync(...)`.

## 🚀 Getting Started

### Prerequisites

- .NET 10 SDK

### Run the API

```bash
cd AIRoundTableSecretSharingAPI
dotnet run
```

The API runs on `http://localhost:5149`.

### Run the Secure Console Demo

The demo runs the full four-phase ML-KEM protocol:

```bash
# In a separate terminal (keep the API running)
cd AIRoundTableSecretSharingSecureDemo
dotnet run
```

The demo walks through:
1. **Auth** — Each partner obtains a Bearer token via client credentials
2. **Phase 1** — Each partner generates an ML-KEM-768 key pair and registers the encapsulation key
3. **Phase 2** — Alphabetically larger partners encapsulate for smaller ones, posting ciphertexts to the aggregator
4. **Phase 3** — Smaller partners fetch and decapsulate ciphertexts to recover shared secrets
5. **Phase 4** — Each partner masks their MAU with HMAC noise and submits; the aggregator sums to the true total

### Run the Web UI (Visual Demo)

```bash
cd web-ui
npm install
npm run dev
```

The UI runs on `http://localhost:3000`.

> **Note:** The web UI demonstrates the noise-cancellation concept visually using a simplified hash-based approach. For production use, integrate the `SecureProducerClient` with the full ML-KEM protocol.

## 🔒 Security Analysis

### Why This Implementation Is Secure

The security of this protocol rests on the **IND-CCA2 security of ML-KEM-768**. Given a ciphertext and the corresponding encapsulation key, recovering the shared secret is as hard as solving Module Learning With Errors (MLWE), which is believed to be intractable for both classical and quantum computers.

This means:
- The aggregator stores ciphertexts and encapsulation keys — it **cannot** recover any shared secret
- Without the shared secrets, the aggregator **cannot** compute the HMAC-based noise
- Therefore, the aggregator **cannot** reverse-engineer individual masked values

### Security Guarantees

| Attack Vector | Protection |
|---------------|------------|
| Unauthenticated access | OAuth 2.0 client credentials; all `/api/` endpoints require a valid JWT Bearer token |
| Token theft | JWTs are short-lived (60 min) and signed with HMAC-SHA256 |
| Aggregator tries to recover noise | Would need shared secrets; ML-KEM is IND-CCA2 secure |
| Aggregator stores ciphertexts indefinitely | Still cannot decapsulate without decapsulation keys |
| Eavesdropper intercepts traffic | Sees only encapsulation keys, ciphertexts, and masked values |
| Quantum adversary | ML-KEM security is based on MLWE, not factoring or discrete log |
| N−1 partners collude | Can compute Nth partner's value (inherent to pairwise noise) |

### Limitations

- **All-or-nothing**: Aggregate is only available when all partners submit
- **Collusion risk**: If N−1 partners share their decapsulation keys, they can recover all shared secrets and deduce the Nth partner's value — inherent to pairwise noise schemes
- **No submission integrity**: The aggregator cannot verify that submitted values are honest
- **No dropout tolerance**: If a partner disappears after posting a ciphertext but before submitting, the noise for that pair will not cancel

### Cryptographic Choices

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Key Encapsulation | ML-KEM-768 (FIPS 203) | NIST post-quantum standard; IND-CCA2; 128-bit post-quantum security |
| Runtime | `System.Security.Cryptography.MLKem` (.NET 9+) | Built-in, no external dependency |
| Noise Derivation | HMAC-SHA256 | Secure PRF; deterministic; context-bound per (country, month) |
| Key Storage | Encapsulation keys only | Decapsulation keys never leave partners; ciphertexts are opaque to aggregator |
| Authentication | OAuth 2.0 client credentials + JWT Bearer | Standard machine-to-machine auth; short-lived tokens; no long-lived secrets on the wire |

## 📚 Related Concepts

- **Secure Multi-Party Computation (MPC)**: Broader field this protocol belongs to
- **Differential Privacy**: Alternative approach using statistical noise
- **Secret Sharing (Shamir)**: Related but different technique for splitting secrets
- **Homomorphic Encryption**: Alternative allowing computation directly on encrypted data
- **Federated Learning**: Uses similar secure aggregation protocols

## 🧪 Testing with curl

```bash
# Obtain a Bearer token (client credentials flow)
TOKEN=$(curl -s -X POST http://localhost:5149/auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=partnerB&client_secret=pB-client-secret-2026-def456" \
  | jq -r .access_token)

# Phase 1 — Register encapsulation key
curl -X POST http://localhost:5149/api/keyexchange/register \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"producerId": "partnerB", "publicKeyBase64": "<BASE64_1184_BYTES>"}'

# Get all registered encapsulation keys
curl -H "Authorization: Bearer $TOKEN" \
  http://localhost:5149/api/keyexchange/keys

# Phase 2 — Post ciphertext (B → A)
curl -X POST http://localhost:5149/api/ciphertext \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"senderId": "partnerB", "recipientId": "partnerA", "ciphertextBase64": "<BASE64_1088_BYTES>"}'

# Phase 3 — Fetch ciphertexts addressed to A (use partnerA token)
TOKEN_A=$(curl -s -X POST http://localhost:5149/auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=client_credentials&client_id=partnerA&client_secret=pA-client-secret-2026-abc123" \
  | jq -r .access_token)

curl -H "Authorization: Bearer $TOKEN_A" \
  "http://localhost:5149/api/ciphertext?recipientId=partnerA"

# Phase 4 — Submit masked metric
curl -X POST http://localhost:5149/api/metrics/submit \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d '{
    "producerId": "partnerA",
    "country": "USA",
    "month": "2026-05-01",
    "value": 1230000,
    "epochId": 1,
    "signature": "demo"
  }'

# Get the aggregate (requires all partners to have submitted)
curl -H "Authorization: Bearer $TOKEN_A" \
  "http://localhost:5149/api/metrics/aggregate?country=USA&month=2026-05-01"
```

## 📄 License

MIT License — feel free to use for educational purposes.

## 🙏 Acknowledgments

This implementation demonstrates concepts from privacy-preserving data aggregation research. The pairwise noise cancellation technique is a form of secure aggregation similar to protocols used in federated learning systems. Using ML-KEM-768 instead of ECDH makes the protocol resistant to quantum adversaries while preserving the same noise-cancellation guarantees.
