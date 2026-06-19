# AI Round Table — Python Client: Quick Start

These scripts let you participate in a secure metric submission round using ML-KEM-768
post-quantum key exchange and HMAC-based noise masking.

## Prerequisites

- Python 3.10+
- API credentials (`CLIENT_ID` and `CLIENT_SECRET`) from the round coordinator

## Setup

```bash
# Create and activate a virtual environment
python -m venv .venv
source .venv/bin/activate        # Windows: .venv\Scripts\activate

# Install dependencies
pip install -r requirements.txt

# Configure credentials
cp .env.example .env
# Edit .env and fill in API_BASE_URL, CLIENT_ID, CLIENT_SECRET
```

## Step-by-step Instructions

### Step 1 — Generate and register your keypair  *(run once)*

```bash
python keygen.py
```

This generates your ML-KEM-768 keypair, saves it locally under `keys/`, and registers
your public key with the API. Safe to re-run — it will not overwrite an existing keypair.

**Coordinate with partners:** everyone must complete this step before anyone moves to Step 2.
Check readiness: if `exchange.py` shows "Not all partners have registered keys yet", come back
here first.

---

### Step 2 — Exchange ciphertexts with partners  *(run once per partner group)*

```bash
python exchange.py
```

Establishes a shared secret with each partner via ML-KEM-768 key encapsulation and saves
them to `keys/<your-id>_secrets.json`.

**You may need to re-run this script** after a partner completes their run, to pick up their
ciphertext and derive your shared secret. The script will tell you who you are still waiting for:

```
Exchange in progress — waiting for these partner(s) to run exchange.py:
  partnerB
Once they confirm, re-run this script to pick up their ciphertext
and derive your shared secret, then run submit.py.
```

When all partners are done you will see:

```
Exchange complete. You can now run submit.py.
```

---

### Step 3 — Submit your data  *(run each submission round)*

1. Fill in `submissions.csv` with your values:

   ```csv
   country,month,value
   US,2025-01,1758
   GB,2025-01,1515
   ```

2. Run:

   ```bash
   python submit.py
   ```

Each row is noise-masked using the shared secrets from Step 2 before being sent.
The masked values cancel out across all partners so the aggregated result is exact.

---

## Subsequent Months

For a new submission round with the same partner group:

- **Update** `submissions.csv` with the new month's data
- **Re-run** `python submit.py` — no other steps needed

`exchange.py` only needs to be re-run if a new partner joins the group.

---

## File Reference

| File | Purpose |
|------|---------|
| `.env` | Your credentials (not committed to version control) |
| `.env.example` | Credential template |
| `submissions.csv` | Your data to submit (country, month, value) |
| `keys/<id>.json` | Your ML-KEM-768 keypair — **keep this secret** |
| `keys/<id>_secrets.json` | Derived shared secrets — **keep this secret** |
