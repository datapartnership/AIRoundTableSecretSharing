# Secret Sharing Demo - Web UI

A React-based web interface to demonstrate the privacy-preserving secret sharing system.

## Features

- **Partner Pages** (PartnerA, PartnerB, PartnerC): Enter actual values and see how noise is calculated and applied before submission
- **Results Page**: Query aggregated results and see the noise cancellation in action
- **Home Page**: Interactive explanation of how the protocol works

## Prerequisites

- Node.js 18+ 
- The .NET API running (`AIRoundTableSecretSharingAPI`)

## Getting Started

### 1. Start the API

In one terminal, start the backend API:

```bash
cd AIRoundTableSecretSharingAPI
dotnet run
```

The API will run on `http://localhost:5149`

### 2. Start the Web UI

In another terminal:

```bash
cd web-ui
npm install   # Only needed first time
npm run dev
```

The web UI will run on `http://localhost:3000`

## Usage

1. Open `http://localhost:3000` in your browser
2. Navigate to a partner page (PartnerA, PartnerB, or PartnerC)
3. Enter a country, month, and actual value (e.g., 1,000,000 monthly active users)
4. See the noise calculation breakdown showing how your value is masked
5. Submit the masked value
6. Repeat for other partners
7. Go to the Results page to see the aggregated total

## How It Works

The UI demonstrates the pairwise noise cancellation protocol:

1. Each partner enters their **actual value**
2. The UI calculates **deterministic noise** with every other partner using SHA-256 hashing
3. Based on alphabetical ordering, one partner **adds** the noise, the other **subtracts** it
4. The **masked value** (actual + total noise) is submitted to the API
5. When all partners submit, the noise **cancels out** perfectly, revealing only the true aggregate

## Project Structure

```
web-ui/
├── src/
│   ├── pages/
│   │   ├── Home.jsx        # Landing page with explanation
│   │   ├── PartnerPage.jsx # Generic partner submission page
│   │   └── Results.jsx     # Aggregation results page
│   ├── utils/
│   │   ├── api.js          # API client functions
│   │   └── noise.js        # Deterministic noise generation (JS port)
│   ├── App.jsx             # Router setup
│   ├── main.jsx            # Entry point
│   └── index.css           # Styles
├── index.html
├── package.json
└── vite.config.js
```

## Note on Noise Calculation

The JavaScript noise generation is a port of the C# `DeterministicNoiseGenerator`. Due to differences in random number generation between platforms, the exact noise values may differ slightly from the C# implementation. In a production system, all clients should use the same implementation (either all C# or all JS).
