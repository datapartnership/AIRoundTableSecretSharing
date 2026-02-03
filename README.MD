# Privacy-Preserving Secure Aggregation

A .NET 8 + React implementation of **cryptographically secure multi-party aggregation** using pairwise noise cancellation with **Elliptic Curve Diffie-Hellman (ECDH)** key exchange. Partners can contribute sensitive values to a shared aggregate without revealing individual values — not even to the aggregator.

## 🎯 Problem Statement

Imagine a scenario where the World Bank wants to collect monthly active user statistics from multiple messaging platforms (PartnerA, PartnerB, PartnerC) for each country. The partners want to contribute to the aggregate statistic but **cannot reveal their individual numbers** due to competitive sensitivity.

**Requirements:**
- ✅ The aggregator learns the **total** across all partners
- ✅ The aggregator **cannot** learn individual partner values (cryptographically enforced)
- ✅ Partners do **not communicate** directly with each other
- ✅ No pre-shared secrets required between partners

## ⚖️ Dual Metric Support

This implementation supports **two parallel masked metrics** submitted simultaneously:

| Metric | Description | Use Case |
|--------|-------------|----------|
| **MAU** | Monthly Active Users (raw count) | Basic user count |
| **Adjusted MAU** | MAU × partner-specific coefficient | Quality-adjusted users, engagement score, etc. |

Each partner multiplies their MAU by a **secret coefficient** before masking. The coefficient is **never revealed** to the aggregator.

### Derived Metrics

The aggregator can compute:

| Metric | Formula | Meaning |
|--------|---------|---------|
| **Total MAU** | Σ(MAU_i) | Sum of all partners' MAU |
| **Total Adjusted MAU** | Σ(MAU_i × coef_i) | Sum of adjusted values |
| **Weighted Ratio** | TotalAdjustedMAU / TotalMAU | Aggregate weighted coefficient |

The **Weighted Ratio** represents the MAU-weighted average of all partners' coefficients — useful for computing aggregate quality scores without revealing individual coefficients.

## 🔐 The Secure Pairwise Noise Cancellation Protocol

### Core Idea

Each pair of partners establishes a **shared secret** using Diffie-Hellman key exchange (facilitated by the aggregator, but the aggregator cannot compute the shared secret). From this secret, they derive **pairwise noise**: one partner adds it, the other subtracts it. When aggregated, the noise cancels perfectly, leaving only the true sum.

### Why Diffie-Hellman?

The critical insight is that the aggregator facilitates the key exchange but **cannot compute the shared secrets**:

```
┌─────────────────────────────────────────────────────────────────────────────┐
│  DIFFIE-HELLMAN KEY EXCHANGE (ECDH P-256)                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  PartnerA generates:  private_a (SECRET), public_a = g^private_a           │
│  PartnerB generates:  private_b (SECRET), public_b = g^private_b           │
│                                                                             │
│  They exchange public keys through the aggregator (safe!)                   │
│                                                                             │
│  PartnerA computes:   shared_AB = public_b^private_a = g^(a*b)             │
│  PartnerB computes:   shared_AB = public_a^private_b = g^(a*b)  ← SAME!    │
│                                                                             │
│  Aggregator sees:     public_a, public_b                                   │
│  Aggregator CANNOT compute: g^(a*b) (Computational Diffie-Hellman problem) │
│                                                                             │
│  Noise = HMAC-SHA256(shared_AB, country || month || metricType)            │
│  → Only partners can compute this, not the aggregator!                     │
│  → Different metricType generates different noise for MAU vs AdjustedMAU   │
└─────────────────────────────────────────────────────────────────────────────┘
```

### Protocol Steps

#### Step 1: Key Generation

Each partner generates an ECDH key pair:

```csharp
// DiffieHellmanKeyExchange.cs
using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
var publicKey = ecdh.PublicKey.ExportSubjectPublicKeyInfo();
// private key NEVER leaves the partner's system
```

#### Step 2: Key Exchange via Aggregator

Partners register their public keys with the aggregator:

```
PartnerA → Aggregator: "Here's my public key: pk_A"
PartnerB → Aggregator: "Here's my public key: pk_B"
PartnerC → Aggregator: "Here's my public key: pk_C"

Each partner retrieves others' public keys:
PartnerA ← Aggregator: "pk_B, pk_C"
PartnerB ← Aggregator: "pk_A, pk_C"
PartnerC ← Aggregator: "pk_A, pk_B"
```

**The aggregator only sees public keys — it cannot derive shared secrets.**

#### Step 3: Compute Shared Secrets

Each partner computes shared secrets with every other partner:

```csharp
// PartnerA's computation
byte[] sharedSecret_AB = myPrivateKey.DeriveKeyMaterial(partnerB_PublicKey);
byte[] sharedSecret_AC = myPrivateKey.DeriveKeyMaterial(partnerC_PublicKey);

// PartnerB independently computes the SAME sharedSecret_AB using:
byte[] sharedSecret_AB = myPrivateKey.DeriveKeyMaterial(partnerA_PublicKey);
```

#### Step 4: Generate Noise from Shared Secrets

Noise is computed using HMAC-SHA256 with the shared secret as the key. Each metric type generates **independent noise**:

```csharp
// SecureNoiseGenerator.cs
public enum MetricType { MAU, WeightedMAU }

public static long GenerateNoise(byte[] sharedSecret, string country, 
                                  DateTime month, MetricType metricType, long maxNoise)
{
    // Include metricType in context to generate different noise per metric
    var context = $"{country}|{month:yyyy-MM}|{metricType}";
    
    using var hmac = new HMACSHA256(sharedSecret);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
    
    long value = BitConverter.ToInt64(hash, 0) & 0x7FFFFFFFFFFFFFFF;
    return (long)(value % (ulong)(2 * maxNoise)) - maxNoise;
}
```

**The aggregator cannot compute this noise** — it would need the shared secret, which requires a private key it doesn't have.

#### Step 5: Submit Masked Values

Each partner calculates noise for **both metrics** and submits:

```
masked_MAU = actual_MAU + Σ(MAU_noise_with_each_partner)
masked_AdjustedMAU = actual_MAU × coefficient + Σ(adjusted_noise_with_each_partner)
```

#### Step 6: Aggregation

The aggregator sums all masked values. Noise cancels perfectly for **each metric independently**:

```
Σ(masked_MAU) = Σ(actual_MAU) + 0 = TotalMAU ✓
Σ(masked_AdjustedMAU) = Σ(actual_AdjustedMAU) + 0 = TotalAdjustedMAU ✓

WeightedRatio = TotalAdjustedMAU / TotalMAU
```

### Mathematical Proof (3 Partners)

Let's denote:
- `V_A`, `V_B`, `V_C` = actual values for PartnerA, PartnerB, PartnerC
- `n_AB` = noise derived from shared secret between A and B (via HMAC)
- `n_AC` = noise derived from shared secret between A and C
- `n_BC` = noise derived from shared secret between B and C

Each partner submits:

| Partner | Masked Value |
|---------|--------------|
| PartnerA | `V_A + n_AB + n_AC` |
| PartnerB | `V_B - n_AB + n_BC` |
| PartnerC | `V_C - n_AC - n_BC` |

**Sum:**
```
(V_A + n_AB + n_AC) + (V_B - n_AB + n_BC) + (V_C - n_AC - n_BC)
= V_A + V_B + V_C + (n_AB - n_AB) + (n_AC - n_AC) + (n_BC - n_BC)
= V_A + V_B + V_C + 0 + 0 + 0
= V_A + V_B + V_C  ✓
```

### Numerical Example

#### Single Metric (MAU)

| Partner | Actual MAU | Noise Applied | Masked MAU |
|---------|------------|---------------|------------|
| PartnerA | 1,000,000 | +150,000 (with B) +80,000 (with C) | 1,230,000 |
| PartnerB | 500,000 | -150,000 (with A) +45,000 (with C) | 395,000 |
| PartnerC | 200,000 | -80,000 (with A) -45,000 (with B) | 75,000 |
| **Total** | **1,700,000** | **0** | **1,700,000** ✓ |

#### Dual Metrics (MAU + Adjusted MAU)

| Partner | MAU | Coefficient | Adjusted MAU | Masked MAU | Masked Adjusted |
|---------|-----|-------------|--------------|------------|-----------------|
| PartnerA | 1,000,000 | 1.5 | 1,500,000 | 1,230,000 | 1,620,000 |
| PartnerB | 500,000 | 2.0 | 1,000,000 | 395,000 | 880,000 |
| PartnerC | 200,000 | 0.8 | 160,000 | 75,000 | 160,000 |
| **Total** | **1,700,000** | — | **2,660,000** | **1,700,000** ✓ | **2,660,000** ✓ |

**Aggregator computes:**
- Total MAU: 1,700,000
- Total Adjusted MAU: 2,660,000  
- **Weighted Ratio**: 2,660,000 / 1,700,000 = **1.5647**

The aggregator learns the aggregate weighted coefficient (1.5647) but **cannot determine** individual MAU values or that PartnerB used coefficient 2.0!

## 🏗️ Project Structure

```
AIRoundTableSecretSharing/
├── AIRoundTableSecretSharingCommon.sln    # Solution file
│
├── AIRoundTableSecretSharingAPI/          # ASP.NET Core Web API (Aggregator)
│   ├── Controllers/
│   │   ├── MetricsController.cs           # Submit metrics, get aggregates
│   │   ├── RegistryController.cs          # Manage partners and epochs
│   │   └── KeyExchangeController.cs       # Diffie-Hellman key exchange
│   ├── Services/
│   │   ├── InMemoryDatastore.cs           # In-memory storage for demo
│   │   └── KeyStore.cs                    # Store partner public keys (NEVER private keys)
│   ├── Program.cs
│   └── appsettings.json
│
├── AIRoundTableSecretSharingCommon/       # Shared library
│   ├── Core/
│   │   ├── DiffieHellmanKeyExchange.cs    # ECDH P-256 key pair generation
│   │   ├── SecureNoiseGenerator.cs        # HMAC-based noise from shared secrets (with MetricType)
│   │   └── DeterministicNoiseGenerator.cs # Simple hash-based noise (demo only)
│   ├── Models/
│   │   ├── MetricSubmission.cs            # Submission with Value + WeightedValue
│   │   ├── AggregationResult.cs           # Total, WeightedTotal, WeightedRatio
│   │   ├── SubmissionResult.cs            # Dual metric submission result
│   │   ├── ProducerInfo.cs                # Partner information
│   │   ├── ProducerEpoch.cs               # Partner set for a time period
│   │   └── PartnerPublicKey.cs            # DH public key model
│   └── Producers/
│       ├── SecureProducerClient.cs        # Secure client with dual metric support
│       └── ProducerClient.cs              # Basic client (demo only)
│
├── AIRoundTableSecretSharingSecureDemo/   # Console demo with full DH exchange
│   └── Program.cs                         # Interactive secure aggregation demo
│
└── web-ui/                                # React demonstration UI
    └── src/
        ├── pages/                         # Partner and results pages
        └── utils/                         # Noise calculation utilities
```

## 🔧 Key Components

### DiffieHellmanKeyExchange.cs

Handles ECDH key pair generation and shared secret derivation:

```csharp
public class DiffieHellmanKeyExchange : IDisposable
{
    private readonly ECDiffieHellman _ecdh;
    
    public DiffieHellmanKeyExchange()
    {
        _ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
    }
    
    public string GetPublicKeyBase64()
    {
        return Convert.ToBase64String(_ecdh.PublicKey.ExportSubjectPublicKeyInfo());
    }
    
    public byte[] ComputeSharedSecret(string otherPublicKeyBase64)
    {
        var publicKeyBytes = Convert.FromBase64String(otherPublicKeyBase64);
        using var otherEcdh = ECDiffieHellman.Create();
        otherEcdh.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        return _ecdh.DeriveKeyMaterial(otherEcdh.PublicKey);
    }
}
```

### SecureNoiseGenerator.cs

Generates noise from shared secrets using HMAC-SHA256. Supports multiple metric types:

```csharp
public enum MetricType { MAU, WeightedMAU }

public static long GenerateNoise(byte[] sharedSecret, string country, DateTime month,
                                  MetricType metricType, long maxNoise)
{
    // Include metricType to generate independent noise for each metric
    var context = $"{country}|{month:yyyy-MM}|{metricType}";
    
    using var hmac = new HMACSHA256(sharedSecret);
    var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(context));
    
    long value = BitConverter.ToInt64(hash, 0) & 0x7FFFFFFFFFFFFFFF;
    long noise = (long)(value % (ulong)(2 * maxNoise)) - maxNoise;
    
    return noise;
}

public static int GetNoiseSign(string myId, string otherId)
{
    // First partner alphabetically adds noise, second subtracts
    return String.CompareOrdinal(myId, otherId) < 0 ? 1 : -1;
}
```

### Epochs

An **epoch** represents a fixed set of partners for a time period. All partners in an epoch must submit for the aggregate to be computed. This ensures:

- Noise cancellation works correctly (all pairs are complete)
- Partners can join/leave without disrupting ongoing submissions
- Data integrity across time periods

### The Registry & Key Exchange

The **Registry API** manages partner membership, while the **Key Exchange API** facilitates Diffie-Hellman:

```
┌─────────────┐                                      ┌─────────────┐
│  PartnerA   │ ─── POST public_key_A ─────────────► │             │
└─────────────┘ ◄── GET public_keys_B,C ──────────── │  Aggregator │
                                                     │             │
┌─────────────┐                                      │  Key Store  │
│  PartnerB   │ ─── POST public_key_B ─────────────► │  (public    │
└─────────────┘ ◄── GET public_keys_A,C ──────────── │   keys only)│
                                                     │             │
┌─────────────┐                                      │             │
│  PartnerC   │ ─── POST public_key_C ─────────────► │             │
└─────────────┘ ◄── GET public_keys_A,B ──────────── └─────────────┘

     Aggregator facilitates exchange but CANNOT compute shared secrets
```

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/keyexchange/register` | POST | Register a partner's DH public key |
| `/api/keyexchange/keys` | GET | Get all partners' public keys |
| `/api/keyexchange/keys/{id}` | GET | Get specific partner's public key |
| `/api/keyexchange/status` | GET | Get key exchange status |
| `/api/registry/producers` | GET | List active partners |
| `/api/registry/epoch` | GET | Get current epoch info |
| `/api/metrics/submit` | POST | Submit a masked metric |
| `/api/metrics/aggregate` | GET | Get aggregate for country/month |

## 🚀 Getting Started

### Prerequisites

- .NET 8 SDK
- Node.js 18+ (for web UI)

### Run the API

```bash
cd AIRoundTableSecretSharingAPI
dotnet run
```

The API runs on `http://localhost:5149`

### Run the Console Demo

The secure demo uses **Diffie-Hellman key exchange** — the aggregator CANNOT determine individual values:

```bash
# In a new terminal (keep the API running)
cd AIRoundTableSecretSharingSecureDemo
dotnet run
```

This demonstrates:
1. Each partner generates an ECDH P-256 key pair (private key never leaves the partner)
2. Partners register public keys with the aggregator
3. Partners compute shared secrets with each other (aggregator **cannot** compute these!)
4. Noise is generated from shared secrets using HMAC-SHA256
5. Aggregator receives masked values but **cannot** reverse engineer individual values

### Run the Web UI (Visual Demo)

```bash
cd web-ui
npm install
npm run dev
```

The UI runs on `http://localhost:3000`

**Note**: The web UI demonstrates the noise cancellation concept visually but uses a simplified hash-based approach for noise generation. For production use, integrate the `SecureProducerClient` with proper Diffie-Hellman key exchange.

### Demo Flow

1. Start the API server
2. Run the secure console demo
3. Watch as partners:
   - Generate ECDH key pairs
   - Exchange public keys via aggregator
   - Compute shared secrets locally
   - Submit masked values
4. See the aggregate computed — individual values remain hidden!

## 🔒 Security Analysis

### Why This Implementation Is Secure

The security of this protocol rests on the **Computational Diffie-Hellman (CDH) problem**:

Given `g^a` and `g^b`, computing `g^(a*b)` is computationally infeasible without knowing `a` or `b`.

This means:
- The aggregator sees `public_a` and `public_b` (both are `g^something`)
- The aggregator **cannot** compute the shared secret `g^(a*b)`
- Without the shared secret, the aggregator **cannot** compute the HMAC-based noise
- Therefore, the aggregator **cannot** reverse engineer individual values

### Security Guarantees

| Attack Vector | Protection |
|---------------|------------|
| Aggregator tries to compute noise | Needs shared secret; CDH problem is computationally hard |
| Aggregator stores submissions for later | Still can't compute shared secrets without private keys |
| Eavesdropper intercepts traffic | Only sees public keys and masked values; cannot derive secrets |
| Man-in-the-middle on key exchange | Can be prevented with PKI/signatures (not in demo) |
| N-1 partners collude | Can compute Nth partner's value (inherent limitation of pairwise noise) |

### Limitations

- **All-or-nothing**: Aggregate only available when ALL partners submit
- **Collusion risk**: If N-1 partners share their private keys, they can compute all shared secrets and derive the Nth partner's value
- **No individual verification**: Aggregator cannot verify individual submissions are honest
- **No dropout tolerance**: If a partner disappears after key exchange, noise won't cancel

### Cryptographic Choices

| Component | Choice | Rationale |
|-----------|--------|-----------|
| Key Exchange | ECDH with P-256 (NIST curve) | Industry standard, good performance, 128-bit security |
| Noise Derivation | HMAC-SHA256 | Secure PRF, deterministic, well-studied |
| Key Storage | Never store private keys | Only public keys stored by aggregator |

## 📚 Related Concepts

- **Secure Multi-Party Computation (MPC)**: Broader field this belongs to
- **Differential Privacy**: Alternative approach using statistical noise
- **Secret Sharing (Shamir)**: Related but different technique for splitting secrets
- **Homomorphic Encryption**: Alternative allowing computation on encrypted data
- **Federated Learning**: Uses similar secure aggregation protocols

## 🧪 Testing

### Test Key Exchange with curl

```bash
# Register a partner's public key
curl -X POST http://localhost:5149/api/keyexchange/register \
  -H "Content-Type: application/json" \
  -d '{"partnerId": "testPartner", "publicKey": "BASE64_ENCODED_PUBLIC_KEY"}'

# Get all registered public keys
curl http://localhost:5149/api/keyexchange/keys

# Check key exchange status
curl http://localhost:5149/api/keyexchange/status
```

### Test Metrics Submission with curl

```bash
# Get current epoch
curl http://localhost:5149/api/registry/epoch

# Submit a metric (masked value)
curl -X POST http://localhost:5149/api/metrics/submit \
  -H "Content-Type: application/json" \
  -d '{
    "producerId": "partnerA",
    "country": "USA",
    "month": "2026-01-01",
    "value": 1230000,
    "epochId": 1,
    "signature": "demo"
  }'

# Get aggregate (requires all partners to submit)
curl "http://localhost:5149/api/metrics/aggregate?country=USA&month=2026-01-01"
```

## 📄 License

MIT License - feel free to use for educational purposes.

## 🙏 Acknowledgments

This implementation demonstrates concepts from privacy-preserving data aggregation research. The pairwise noise cancellation technique with Diffie-Hellman key exchange is a form of secure aggregation similar to protocols used in federated learning systems like those deployed by Google and Apple.
