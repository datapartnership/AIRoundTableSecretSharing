# AI Round Table Secret Sharing — Web UI

React 18 + Vite partner/admin interface for the Privacy-Preserving Secure Aggregation system. Partners sign in with their organisational Azure AD account and the browser automatically runs the full four-phase ML-KEM-768 protocol — no manual coordination, no client secrets.

## Prerequisites

- Node.js 20+
- The .NET API running locally (`AIRoundTableSecretSharingAPI`, default port 5149)
- An Azure Entra ID account in the configured tenant that belongs to either the Partner or Admin security group

## Getting Started

```bash
npm install   # first time only
npm run dev
```

The UI runs on `http://localhost:3000`. All `/api/*` requests are proxied to `http://localhost:5149` by Vite.

## Pages

### Home
Protocol overview and sign-in prompt.

### Protocol Flow (`/protocol`)
Automated four-step wizard for registered partners:

| Step | What happens automatically |
|------|---------------------------|
| **1 — Key Generation** | Generates an ML-KEM-768 key pair in the browser; posts the encapsulation key (1184 bytes) to the API; waits for all epoch partners to register |
| **2 — Encapsulation** | For each partner with a smaller OID, encapsulates and posts a ciphertext (1088 bytes) to the API |
| **3 — Decapsulation** | Polls for incoming ciphertexts; decapsulates each using the local private key to derive shared secrets |
| **4 — Submit Data** | User enters actual MAU values per country / month; the UI applies HMAC-SHA256 noise and submits masked values |

Key material (key pair, shared secrets, epoch ID) is stored in `localStorage` keyed by the user's OID. On each page load the stored epoch ID is compared to the current epoch; stale state is cleared automatically when a new epoch is detected.

### Admin Panel (`/admin`)
Available to Admin group members only:

- View all registered partners
- Select a subset (≥ 2) to participate in a new epoch
- Clear all data

### Results (`/results`)
Query the aggregate for any country / month combination in the current epoch. Available once all epoch partners have submitted.

## Authentication

MSAL v5 (`@azure/msal-browser` + `@azure/msal-react`) with interactive redirect flow. The access token is injected into every API request automatically. The `groups` claim in the token determines Admin vs Partner access.

## Project Structure

```
web-ui/
├── src/
│   ├── pages/
│   │   ├── Home.jsx          # Landing page
│   │   ├── ProtocolFlow.jsx  # Automated 4-step protocol wizard
│   │   ├── AdminPanel.jsx    # Epoch management (admin only)
│   │   └── Results.jsx       # Aggregate results
│   ├── utils/
│   │   ├── api.js            # All API calls; Entra token injected via acquireApiToken
│   │   ├── crypto.js         # ML-KEM-768 via `mlkem` npm package
│   │   └── noise.js          # HMAC-SHA256 noise; matches C# & Python exactly
│   ├── App.jsx               # Routing; admin flag from access token groups claim
│   ├── main.jsx              # MSAL provider setup
│   └── index.css             # Global styles
├── index.html
├── package.json
└── vite.config.js            # Proxy /api → localhost:5149
```

## Noise Compatibility

The JavaScript noise calculation in `noise.js` is mathematically identical to `SecureNoiseGenerator.cs` (C#) and `submit.py` (Python):

```
noise = HMAC-SHA256(sharedSecret, "{country}|{month}")
      → first 8 bytes as LE signed int64
      → (seed % (2×maxNoise+1) + (2×maxNoise+1)) % (2×maxNoise+1) − maxNoise
```

All three runtimes produce the same value for any given shared secret + country + month tuple, which is required for the noise to cancel in the aggregate.
