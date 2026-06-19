#!/usr/bin/env python3
"""
submit.py - Submit a masked metric using the ML-KEM-768 secure noise protocol.

Mirrors the 4-phase protocol of SecureProducerClient.cs:
  Phase 1  (prerequisite) - keygen.py must have already run to produce a keypair.
  Phase 2  Encapsulation  - fetch partner public keys, encapsulate shared secrets,
                            post ciphertexts to the API.
  Phase 3  Decapsulation  - fetch incoming ciphertexts, derive shared secrets.
  Phase 4  Submit         - for each row in submissions.csv, apply HMAC noise
                            and post the masked metric.

Fill in submissions.csv (country, month, value) before running.
Rows with an empty value are skipped.

Noise formula (self-consistent among Python producers):
  h    = HMAC-SHA256(key=shared_secret, msg="{country}|{YYYY-MM}")
  seed = little-endian signed int64 from first 8 bytes of h
  noise = (seed % (2*MAX_NOISE + 1)) - MAX_NOISE

NOTE: this formula is NOT compatible with the C# SecureNoiseGenerator, which uses
.NET System.Random internally. Mixed Python+C# deployments require updating the
C# formula as well.

Usage:
    python submit.py

Configuration is read from a .env file in the same directory.
"""

import base64
import csv
import datetime
import hashlib
import hmac as hmac_module
import json
import os
import struct
import sys

from dotenv import load_dotenv
from kyber_py.kyber import Kyber768
import requests

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
KEYS_DIR = os.path.join(SCRIPT_DIR, "keys")
MAX_NOISE = 100_000_000


def load_config():
    env_file = os.path.join(SCRIPT_DIR, ".env")
    if not os.path.exists(env_file):
        print(
            "ERROR: .env not found.\n"
            "       Copy .env.example to .env and fill in your credentials."
        )
        sys.exit(1)
    load_dotenv(env_file)
    required = ["API_BASE_URL", "CLIENT_ID", "CLIENT_SECRET"]
    config = {key: os.getenv(key) for key in required}
    missing = [k for k, v in config.items() if not v]
    if missing:
        print(f"ERROR: Missing required .env variables: {', '.join(missing)}")
        sys.exit(1)
    return config


def load_key(producer_id):
    key_file = os.path.join(KEYS_DIR, f"{producer_id}.json")
    if not os.path.exists(key_file):
        print(
            f"ERROR: Key file not found at {key_file}.\n"
            "       Run keygen.py first to generate and register your keypair."
        )
        sys.exit(1)
    with open(key_file) as f:
        data = json.load(f)
    return (
        base64.b64decode(data["public_key_b64"]),
        base64.b64decode(data["secret_key_b64"]),
    )


def get_token(api_base_url, client_id, client_secret):
    resp = requests.post(
        f"{api_base_url}/auth/token",
        data={
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
        },
    )
    resp.raise_for_status()
    return resp.json()["access_token"]


def compute_noise(shared_secret, country, month_str):
    """
    Derives a deterministic noise value in [-MAX_NOISE, MAX_NOISE] from the
    shared secret and the submission context (country + month).
    """
    msg = f"{country}|{month_str}".encode()
    h = hmac_module.new(shared_secret, msg, hashlib.sha256).digest()
    seed = struct.unpack("<q", h[:8])[0]  # signed little-endian int64
    noise_range = 2 * MAX_NOISE + 1
    return (seed % noise_range) - MAX_NOISE


def noise_sign(my_id, other_id):
    """
    Returns +1 if my_id is alphabetically smaller (ADD), -1 if larger (SUBTRACT).
    Ensures that for any pair A < B: A adds what B subtracts, so net noise = 0.
    """
    return 1 if my_id < other_id else -1


def load_submissions():
    csv_file = os.path.join(SCRIPT_DIR, "submissions.csv")
    if not os.path.exists(csv_file):
        print("ERROR: submissions.csv not found in the script directory.")
        sys.exit(1)
    rows = []
    with open(csv_file, newline="") as f:
        for i, row in enumerate(csv.DictReader(f), start=2):  # start=2: row 1 is header
            country = row.get("country", "").strip()
            month_raw = row.get("month", "").strip()
            value_raw = row.get("value", "").strip()
            if not value_raw:
                continue  # skip unfilled rows
            if not country or not month_raw:
                print(f"WARNING: skipping row {i} — missing country or month")
                continue
            try:
                month_dt = datetime.datetime.strptime(month_raw, "%Y-%m")
            except ValueError:
                print(f"WARNING: skipping row {i} — invalid month '{month_raw}' (expected YYYY-MM)")
                continue
            try:
                value = int(value_raw)
            except ValueError:
                print(f"WARNING: skipping row {i} — invalid value '{value_raw}'")
                continue
            rows.append((country, month_dt, value))
    if not rows:
        print("ERROR: No valid rows found in submissions.csv. Fill in the value column and retry.")
        sys.exit(1)
    return rows


def main():
    config = load_config()
    api_base_url = config["API_BASE_URL"].rstrip("/")
    client_id = config["CLIENT_ID"]
    client_secret = config["CLIENT_SECRET"]
    producer_id = client_id

    submissions = load_submissions()
    print(f"Producer: {producer_id}")
    print(f"Loaded {len(submissions)} row(s) from submissions.csv")

    _, secret_key = load_key(producer_id)

    print("\nAuthenticating...")
    token = get_token(api_base_url, client_id, client_secret)
    headers = {"Authorization": f"Bearer {token}"}

    shared_secrets: dict[str, bytes] = {}

    # ------------------------------------------------------------------
    # Phase 2: Encapsulation
    # The alphabetically LARGER producer encapsulates for the smaller one.
    # ------------------------------------------------------------------
    print("\n--- Phase 2: Encapsulation ---")
    resp = requests.get(
        f"{api_base_url}/api/keyexchange/keys",
        params={"excludeProducerId": producer_id},
        headers=headers,
    )
    resp.raise_for_status()
    partner_keys = resp.json().get("partnerKeys", [])

    for partner in partner_keys:
        partner_id = partner["producerId"]
        if producer_id > partner_id:
            partner_public_key = base64.b64decode(partner["publicKeyBase64"])
            shared_secret, ciphertext = Kyber768.encaps(partner_public_key)  # returns (key, ct)
            shared_secrets[partner_id] = shared_secret
            ct_b64 = base64.b64encode(ciphertext).decode()
            store_resp = requests.post(
                f"{api_base_url}/api/ciphertext",
                json={
                    "SenderId": producer_id,
                    "RecipientId": partner_id,
                    "CiphertextBase64": ct_b64,
                },
                headers=headers,
            )
            store_resp.raise_for_status()
            print(f"  Encapsulated for {partner_id} and stored ciphertext")
        else:
            print(f"  Skipping {partner_id} — they will encapsulate for us")

    # ------------------------------------------------------------------
    # Phase 3: Decapsulation
    # Receive ciphertexts from partners that encapsulated for us (larger IDs).
    # ------------------------------------------------------------------
    print("\n--- Phase 3: Decapsulation ---")
    resp = requests.get(
        f"{api_base_url}/api/ciphertext",
        params={"recipientId": producer_id},
        headers=headers,
    )
    resp.raise_for_status()
    incoming = resp.json().get("ciphertexts", [])

    for entry in incoming:
        sender_id = entry["senderId"]
        ciphertext = base64.b64decode(entry["ciphertextBase64"])
        shared_secret = Kyber768.decaps(secret_key, ciphertext)
        shared_secrets[sender_id] = shared_secret
        print(f"  Decapsulated shared secret from {sender_id}")

    if not shared_secrets:
        print("  No shared secrets established — values will be submitted without noise masking")

    # ------------------------------------------------------------------
    # Phase 4: Fetch epoch once, then submit each row from the CSV
    # ------------------------------------------------------------------
    print("\n--- Phase 4: Noise Calculation & Submission ---")
    print("\nFetching current epoch...")
    epoch_resp = requests.get(f"{api_base_url}/api/registry/epoch", headers=headers)
    epoch_resp.raise_for_status()
    epoch_id = epoch_resp.json()["epochId"]
    print(f"  Epoch ID: {epoch_id}")

    success_count = 0
    failure_count = 0

    for country, month_dt, actual_value in submissions:
        month_str = month_dt.strftime("%Y-%m")
        month_iso = month_dt.strftime("%Y-%m-01T00:00:00Z")

        masked_value = actual_value
        for partner_id, shared_secret in sorted(shared_secrets.items()):
            noise = compute_noise(shared_secret, country, month_str)
            sign = noise_sign(producer_id, partner_id)
            masked_value += noise * sign

        submission = {
            "ProducerId": producer_id,
            "Country": country,
            "Month": month_iso,
            "Value": masked_value,
            "EpochId": epoch_id,
            "Signature": "mlkem-demo",
            "SubmittedAt": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        try:
            resp = requests.post(
                f"{api_base_url}/api/metrics/submit",
                json=submission,
                headers=headers,
            )
            resp.raise_for_status()
            msg = resp.json().get("message", resp.text)
            print(f"  [{country} {month_str}] masked={masked_value:,}  → {msg}")
            success_count += 1
        except requests.HTTPError as e:
            print(f"  [{country} {month_str}] ERROR: {e}")
            failure_count += 1

    print(f"\nDone. {success_count} submitted, {failure_count} failed.")


if __name__ == "__main__":
    main()
