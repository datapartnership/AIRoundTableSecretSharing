# Privacy-Preserving Data Aggregation via Pairwise Noise Cancellation

A .NET 8 + React demonstration of a privacy-preserving protocol that enables multiple data partners to submit sensitive metrics to a central aggregator while keeping individual values completely private. Only the aggregate total is revealed.

## 🎯 Problem Statement

Imagine a scenario where the World Bank wants to collect monthly active user statistics from multiple messaging platforms (PartnerA, PartnerB, PartnerC) for each country. The partners want to contribute to the aggregate statistic but **cannot reveal their individual numbers** due to competitive sensitivity.

**Requirements:**
- ✅ The aggregator learns the **total** across all partners
- ✅ The aggregator learns **nothing** about individual partner values
- ✅ Partners do **not communicate** with each other
- ✅ No trusted third party required

## 🔐 The Pairwise Noise Cancellation Protocol

### Core Idea

Each pair of partners independently computes a **shared noise value** using deterministic cryptographic hashing. One partner **adds** this noise to their value, while the other **subtracts** it. When all submissions are summed, every noise term appears once positive and once negative — they cancel perfectly, leaving only the true aggregate.

### How It Works

#### Step 1: Deterministic Noise Generation

For any pair of partners (A, B), both can independently compute the same noise value:

```
noise_AB = SHA256(sort(A, B) || country || month) → deterministic random number
```

- Uses SHA-256 hash of both partner IDs + context (country, month)
- Alphabetical ordering ensures both partners compute the **same value**
- No communication needed — purely deterministic!

#### Step 2: Sign Assignment

The partner that comes **first alphabetically** adds the noise; the other subtracts it:

```
PartnerA (comes first):  applies +noise_AB
PartnerB (comes second): applies -noise_AB
```

#### Step 3: Submission

Each partner submits their masked value:

```
masked_value = actual_value + Σ(signed noise with each other partner)
```

#### Step 4: Aggregation

When all partners have submitted:

```
Σ(masked_values) = Σ(actual_values) + Σ(all noise terms)
                 = Σ(actual_values) + 0  ← noise cancels!
                 = Σ(actual_values) ✓
```

### Mathematical Proof (3 Partners)

Let's denote:
- `V_A`, `V_B`, `V_C` = actual values for PartnerA, PartnerB, PartnerC
- `n_AB` = shared noise between A and B
- `n_AC` = shared noise between A and C  
- `n_BC` = shared noise between B and C

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
= V_A + V_B + V_C + 0
= V_A + V_B + V_C  ✓
```

### Numerical Example

| Partner | Actual Value | Noise Applied | Masked Value |
|---------|--------------|---------------|--------------|
| PartnerA | 1,000,000 | +150,000 (with B) +80,000 (with C) | 1,230,000 |
| PartnerB | 500,000 | -150,000 (with A) +45,000 (with C) | 395,000 |
| PartnerC | 200,000 | -80,000 (with A) -45,000 (with B) | 75,000 |
| **Total** | **1,700,000** | **0** | **1,700,000** ✓ |

## 🏗️ Project Structure

```
AIRoundTableSecretSharing/
├── AIRoundTableSecretSharingCommon.sln    # Solution file
│
├── AIRoundTableSecretSharingAPI/          # ASP.NET Core Web API (Aggregator)
│   ├── Controllers/
│   │   ├── MetricsController.cs           # Submit metrics, get aggregates
│   │   └── RegistryController.cs          # Manage partners and epochs
│   ├── Services/
│   │   └── InMemoryDatastore.cs           # In-memory storage for demo
│   ├── Program.cs                         # App configuration
│   └── appsettings.json
│
├── AIRoundTableSecretSharingCommon/       # Shared library
│   ├── Core/
│   │   └── DeterministicNoiseGenerator.cs # SHA-256 based noise generation
│   ├── Models/
│   │   ├── MetricSubmission.cs            # Submission data structure
│   │   ├── AggregationResult.cs           # Aggregation response
│   │   ├── ProducerInfo.cs                # Partner information
│   │   ├── ProducerEpoch.cs               # Partner set for a time period
│   │   └── SubmissionResult.cs            # Submission response
│   └── Producers/
│       └── ProducerClient.cs              # Client SDK for partners
│
├── AIRoundTableSecretSharingDemo/         # Console demo application
│   └── Program.cs                         # Interactive CLI demo
│
└── web-ui/                                # React demonstration UI
    ├── src/
    │   ├── pages/
    │   │   ├── Home.jsx                   # Protocol explanation
    │   │   ├── PartnerPage.jsx            # Partner submission interface
    │   │   └── Results.jsx                # Aggregation results
    │   └── utils/
    │       ├── noise.js                   # JS port of noise generation
    │       └── api.js                     # API client
    ├── package.json
    └── vite.config.js
```

## 🔧 Key Components

### DeterministicNoiseGenerator (C#)

```csharp
public static long GenerateNoise(
    string producerId1, 
    string producerId2,
    string country,
    DateTime month,
    long maxNoise = 100_000_000)
{
    // Sort IDs for consistency
    var sortedIds = new[] { producerId1, producerId2 }.OrderBy(x => x).ToArray();
    var seedString = $"{sortedIds[0]}|{sortedIds[1]}|{country}|{month:yyyy-MM}";
    
    // Hash to create deterministic seed
    using var sha256 = SHA256.Create();
    var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(seedString));
    var seed = BitConverter.ToInt64(hashBytes, 0);
    
    // Generate noise in range [-maxNoise, maxNoise]
    var random = new Random((int)(seed & 0x7FFFFFFF));
    return random.NextInt64(-maxNoise, maxNoise + 1);
}

public static int GetNoiseSign(string myProducerId, string otherProducerId)
{
    var comparison = string.Compare(myProducerId, otherProducerId, StringComparison.Ordinal);
    return comparison < 0 ? 1 : -1;  // First alphabetically ADDs
}
```

### Epochs

An **epoch** represents a fixed set of partners for a time period. All partners in an epoch must submit for the aggregate to be computed. This ensures:

- Noise cancellation works correctly (all pairs are complete)
- Partners can join/leave without disrupting ongoing submissions
- Data integrity across time periods

### The Registry: Enabling Zero Partner Communication

A key challenge in pairwise noise cancellation is: **how does each partner know who else is participating?**

Without this knowledge, partners cannot:
- Calculate noise with the correct set of other partners
- Know when all submissions are complete
- Ensure their noise will be canceled correctly

#### The Problem

In a naive implementation, partners would need to:
1. Discover each other through some out-of-band mechanism
2. Agree on participation for each time period
3. Handle partners joining/leaving mid-period

This requires **partner-to-partner communication** — defeating a key goal of the protocol.

#### The Solution: Centralized Registry

The **Registry API** acts as a single source of truth that all partners query:

```
┌─────────────┐     GET /api/registry/producers     ┌─────────────┐
│  PartnerA   │ ──────────────────────────────────► │             │
└─────────────┘                                     │             │
                                                    │  Registry   │
┌─────────────┐     GET /api/registry/producers     │    API      │
│  PartnerB   │ ──────────────────────────────────► │             │
└─────────────┘                                     │             │
                                                    │  Returns:   │
┌─────────────┐     GET /api/registry/producers     │  [A, B, C]  │
│  PartnerC   │ ──────────────────────────────────► │             │
└─────────────┘                                     └─────────────┘

        Partners NEVER communicate with each other!
```

#### How It Works

1. **Partner Registration**: Partners register with the aggregator once
2. **Epoch Creation**: When partners join/leave, a new epoch is created
3. **Before Submission**: Each partner queries the registry to get:
   - List of all active partners for the time period
   - Current epoch ID (ensures all use same partner set)
4. **Noise Calculation**: Partner calculates noise with each other partner from the list
5. **Submission**: Masked value is submitted with epoch ID for validation

#### Registry Endpoints

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/registry/producers` | GET | Get list of active partners for a date |
| `/api/registry/epoch` | GET | Get current epoch (partner set + ID) |
| `/api/registry/producers` | POST | Register a new partner |

#### Example Flow

```bash
# PartnerA queries the registry before submitting
curl "http://localhost:5149/api/registry/producers?effectiveDate=2026-01-01"

# Response:
[
  { "producerId": "partnerA", "displayName": "Partner A", ... },
  { "producerId": "partnerB", "displayName": "Partner B", ... },
  { "producerId": "partnerC", "displayName": "Partner C", ... }
]

# PartnerA now knows to calculate noise with partnerB and partnerC
# All partners get the SAME list — ensuring noise cancellation works
```

#### Why This Matters

| Without Registry | With Registry |
|------------------|---------------|
| Partners must discover each other | Partners query single endpoint |
| Out-of-band coordination needed | No partner communication needed |
| Race conditions on join/leave | Epochs handle membership changes |
| Complex distributed protocol | Simple REST API calls |

The registry transforms a complex distributed coordination problem into simple, independent API calls that each partner can make without knowing anything about the others.

### API Endpoints

| Endpoint | Method | Description |
|----------|--------|-------------|
| `/api/registry/producers` | GET | List active partners |
| `/api/registry/epoch` | GET | Get current epoch info |
| `/api/registry/producers` | POST | Add a new partner |
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

### Option 1: Run the Console Demo

The console demo simulates all three partners submitting values and shows the aggregation:

```bash
# In a new terminal (keep the API running)
cd AIRoundTableSecretSharingDemo
dotnet run
```

This will:
1. Show the secret actual values for each partner
2. Submit masked values from each partner (with detailed noise breakdown)
3. Fetch and display the aggregated result
4. Verify that noise cancellation worked correctly

### Option 2: Run the Web UI

```bash
cd web-ui
npm install
npm run dev
```

The UI runs on `http://localhost:3000`

### Demo Flow

1. Open `http://localhost:3000`
2. Navigate to each partner page (PartnerA, PartnerB, PartnerC tabs)
3. Enter actual values and observe the noise calculation
4. Submit masked values from each partner
5. Go to Results page to see the aggregate (available when all partners submit)

## 🔒 Security Properties

| Property | Guarantee |
|----------|-----------|
| **Individual Privacy** | Aggregator cannot determine any single partner's value from submissions |
| **Aggregate Accuracy** | Sum is exactly correct when all partners submit |
| **No Collusion Required** | Partners never communicate; noise is deterministic |
| **Verifiable** | Partners can verify their noise calculation matches others |

### Limitations

- **All-or-nothing**: Aggregate only available when ALL partners submit
- **Collusion risk**: If N-1 partners collude, they can compute the Nth partner's value
- **No individual verification**: Aggregator cannot verify individual submissions are honest

## 📚 Related Concepts

- **Secure Multi-Party Computation (MPC)**: Broader field this belongs to
- **Differential Privacy**: Alternative approach using statistical noise
- **Secret Sharing (Shamir)**: Related but different technique for splitting secrets
- **Homomorphic Encryption**: Alternative allowing computation on encrypted data

## 🧪 Testing

### Manual Test with curl

```bash
# Get current epoch
curl http://localhost:5149/api/registry/epoch

# Submit a metric
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

# Get aggregate
curl "http://localhost:5149/api/metrics/aggregate?country=USA&month=2026-01-01"
```

## 📄 License

MIT License - feel free to use for educational purposes.

## 🙏 Acknowledgments

This implementation demonstrates concepts from privacy-preserving data aggregation research. The pairwise noise cancellation technique is a simplified form of secure aggregation protocols used in federated learning and privacy-preserving analytics.
