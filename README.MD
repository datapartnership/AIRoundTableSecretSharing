# Privacy-Preserving Secure Aggregation

A .NET 10 + React implementation of **cryptographically secure multi-party aggregation** using pairwise noise cancellation with **ML-KEM-768** (FIPS 203, the NIST post-quantum Key Encapsulation Mechanism standard). Partners authenticate via Azure Entra ID and run the full four-phase protocol directly in the browser — no pre-shared secrets, no client credentials, no manual coordination.

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
var hmac   = new HMACSHA256(sharedSecret);
var hash   = hmac.ComputeHash(Encoding.UTF8.GetBytes($"{country}|{month:yyyy-MM}"));
var seed   = BitConverter.ToInt64(hash, 0);         // first 8 bytes as LE signed int64
var range  = 2 * maxNoise + 1;
var noise  = ((seed % range) + range) % range - maxNoise;  // Python-compatible modulo

int sign = (myId < otherId) ? +1 : -1;   // smaller partner adds, larger subtracts

maskedMAU = actualMAU + Σ(noise × sign)
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
├── AIRoundTableSecretSharingCommon.sln
│
├── AIRoundTableSecretSharingAPI/          # ASP.NET Core Web API (Aggregator)
│   ├── Controllers/
│   │   ├── AdminController.cs             # Reset, create epoch (admin group only)
│   │   ├── MetricsController.cs           # Submit masked metrics, get aggregates
│   │   ├── RegistryController.cs          # Partners & epoch registry; self-registration
│   │   ├── KeyExchangeController.cs       # ML-KEM encapsulation key registration
│   │   ├── CiphertextController.cs        # ML-KEM ciphertext relay
│   │   └── ClaimsPrincipalExtensions.cs   # OID extraction from Entra JWT
│   ├── Data/
│   │   └── AppDbContext.cs                # EF Core DbContext (Azure SQL)
│   ├── Migrations/                        # EF Core migrations
│   ├── Models/
│   │   └── ApiResponses.cs                # API response types
│   ├── Repositories/                      # EF Core repository implementations
│   ├── Services/
│   │   └── DbClientCredentialService.cs   # (retained for historical compatibility)
│   ├── Program.cs                         # DI setup, Entra auth, CORS, DB migration
│   └── appsettings.json                   # Azure AD config, connection string
│
├── AIRoundTableSecretSharingCommon/       # Shared library
│   ├── Core/
│   │   ├── MlKemKeyExchange.cs            # ML-KEM-768 key gen, encapsulate, decapsulate
│   │   ├── SecureNoiseGenerator.cs        # HMAC-SHA256 noise; Python-compatible
│   │   └── DeterministicNoiseGenerator.cs # Hash-based noise (legacy console demo)
│   ├── Models/                            # Shared DTOs (MetricSubmission, ProducerInfo, …)
│   └── Producers/
│       └── SecureProducerClient.cs        # .NET client with full ML-KEM protocol flow
│
├── AIRoundTableSecretSharingSecureDemo/   # Console demo (full ML-KEM exchange)
│
├── python-clients/                        # Reference Python implementation
│   ├── keygen.py                          # ML-KEM-768 key generation (kyber-py)
│   ├── exchange.py                        # Encapsulation / decapsulation
│   ├── submit.py                          # Masked submission
│   ├── generate_simulation.py             # Bulk test data generator
│   └── PROTOCOL.md                        # Python-side protocol notes
│
└── web-ui/                                # React + MSAL partner / admin UI
    └── src/
        ├── pages/
        │   ├── Home.jsx          # Landing page with protocol overview
        │   ├── ProtocolFlow.jsx  # Automated 4-step wizard (key gen → submit)
        │   ├── AdminPanel.jsx    # Create epochs, clear data (admin group only)
        │   └── Results.jsx       # Aggregated results by country / month
        └── utils/
            ├── api.js            # All API calls (Entra token injected automatically)
            ├── crypto.js         # ML-KEM-768 via `mlkem` npm package
            └── noise.js          # HMAC-SHA256 noise; matches C# & Python exactly
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

All endpoints require a valid Azure Entra ID Bearer token. The caller's producer ID is read from the `oid` claim in the token — no `producerId` in the request body.

| Endpoint | Method | Auth | Description |
|----------|--------|------|-------------|
| `/registry/producers` | GET | Partner | List all registered producers |
| `/registry/producers/me` | POST | Partner | Self-register (upsert) on first login |
| `/registry/epoch` | GET | Partner | Get current epoch |
| `/keyexchange/register` | POST | Partner | Register ML-KEM-768 encapsulation key |
| `/keyexchange/keys` | GET | Partner | All partners' encapsulation keys |
| `/keyexchange/status` | GET | Partner | Key registration status for current epoch |
| `/ciphertext` | POST | Partner | Store a KEM ciphertext (sender → recipient) |
| `/ciphertext` | GET | Partner | Fetch ciphertexts addressed to the caller |
| `/metrics/submit` | POST | Partner | Submit a masked metric value |
| `/metrics/aggregate` | GET | Partner | Get aggregate for a country / month |
| `/admin/producers/reset-and-create-epoch` | POST | Admin | Select partners and open a new epoch |
| `/admin/reset` | POST | Admin | Clear all data |

### Authentication — Azure Entra ID (Microsoft Identity Platform)

All endpoints are protected by `AddMicrosoftIdentityWebApi`. Clients obtain a Bearer token from Entra ID using MSAL (interactive browser flow in the web UI, or device-code / client-credentials for CLI tools) and include it as `Authorization: Bearer <token>`.

Authorization policies are enforced via group membership embedded in the access token:

- **Partner** policy — caller must be a member of the `PartnerGroupId` group
- **Admin** policy — caller must be a member of the `AdminGroupId` group

```json
// appsettings.json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<tenant-id>",
    "ClientId": "<api-app-client-id>",
    "AdminGroupId": "<admin-aad-group-object-id>",
    "PartnerGroupId": "<partner-aad-group-object-id>"
  }
}
```

The caller's Azure AD Object ID (`oid` claim) is used as the `producerId` everywhere — no per-client secrets or pre-seeded partner lists are required.

## 🚀 Getting Started

### Prerequisites

- .NET 10 SDK
- Node.js 20+
- Access to an Azure SQL database (update `ConnectionStrings:DefaultConnection` in `appsettings.json`)
- An Azure Entra ID tenant with:
  - An API app registration (set as `AzureAd:ClientId` in `appsettings.json`)
  - A Web UI app registration (set in `web-ui/src/main.jsx`)
  - An admin security group and a partner security group

### Run the API

```bash
cd AIRoundTableSecretSharingAPI
dotnet run
```

The API runs on `http://localhost:5149`. On first start it applies EF Core migrations automatically.

### Run the Web UI

```bash
cd web-ui
npm install   # first time only
npm run dev
```

The UI runs on `http://localhost:3000`. Partners sign in with their organisational Azure AD account; the browser UI runs the full four-phase ML-KEM protocol automatically:

1. **Step 1** — Generate an ML-KEM-768 key pair locally and register the encapsulation key
2. **Step 2** — Automatically encapsulate for every partner with a smaller OID and post the ciphertext
3. **Step 3** — Poll for incoming ciphertexts and automatically decapsulate each one to derive shared secrets
4. **Step 4** — Enter actual MAU values per country / month and submit masked values

An **Admin** account can open the Admin Panel to select which registered partners participate in a new epoch, or clear all data to start over.

### Run the Console Demo

```bash
# Keep the API running in another terminal
cd AIRoundTableSecretSharingSecureDemo
dotnet run
```

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
| Unauthenticated access | Azure Entra ID JWT Bearer; all endpoints require a valid token |
| Privilege escalation | Group-membership policies (Admin / Partner) enforced by the API |
| Aggregator tries to recover noise | Needs shared secrets; ML-KEM is IND-CCA2 secure |
| Aggregator stores ciphertexts | Still cannot decapsulate without the partner's private decapsulation key |
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
| Authentication | Azure Entra ID (MSAL / Microsoft Identity Platform) JWT Bearer | Standard enterprise SSO; no per-client secrets; OID claim identifies partner |

## 📚 Related Concepts

- **Secure Multi-Party Computation (MPC)**: Broader field this protocol belongs to
- **Differential Privacy**: Alternative approach using statistical noise
- **Secret Sharing (Shamir)**: Related but different technique for splitting secrets
- **Homomorphic Encryption**: Alternative allowing computation directly on encrypted data
- **Federated Learning**: Uses similar secure aggregation protocols

## 🧪 Testing with curl

Obtain a token using MSAL device-code flow or from the browser's developer tools, then:

```bash
TOKEN="<paste-entra-bearer-token>"
BASE="http://localhost:5149"

# Self-register as a partner (upsert on first login)
curl -X POST "$BASE/registry/producers/me" \
  -H "Authorization: Bearer $TOKEN"

# Get current epoch
curl -H "Authorization: Bearer $TOKEN" "$BASE/registry/epoch"

# Register ML-KEM-768 encapsulation key (1184 bytes, base64)
curl -X POST "$BASE/keyexchange/register" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"publicKeyBase64": "<BASE64_1184_BYTES>"}'

# Check key registration status
curl -H "Authorization: Bearer $TOKEN" "$BASE/keyexchange/status"

# Post ciphertext (encapsulating partner → recipient OID)
curl -X POST "$BASE/ciphertext" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"recipientId": "<partner-oid>", "ciphertextBase64": "<BASE64_1088_BYTES>"}'

# Fetch ciphertexts addressed to the caller
curl -H "Authorization: Bearer $TOKEN" "$BASE/ciphertext"

# Submit a masked metric (producerId and signature are set from the JWT — omit them)
curl -X POST "$BASE/metrics/submit" \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer $TOKEN" \
  -d '{"country": "US", "month": "2026-06-01", "value": 1230000, "epochId": <epochId>}'

# Get the aggregate (available once all partners have submitted)
curl -H "Authorization: Bearer $TOKEN" \
  "$BASE/metrics/aggregate?country=US&month=2026-06-01"
```

## 📄 License

MIT License — feel free to use for educational purposes.

## 🙏 Acknowledgments

This implementation demonstrates concepts from privacy-preserving data aggregation research. The pairwise noise cancellation technique is a form of secure aggregation similar to protocols used in federated learning systems. Using ML-KEM-768 instead of ECDH makes the protocol resistant to quantum adversaries while preserving the same noise-cancellation guarantees.
