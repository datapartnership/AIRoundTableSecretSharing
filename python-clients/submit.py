#!/usr/bin/env python3
"""
submit.py - Submit masked metrics using previously established shared secrets.

Prerequisites (run in order):
  1. keygen.py   - generate and register your ML-KEM-768 keypair
  2. exchange.py - perform ciphertext exchange with all partners
  3. submit.py   - this script

For each row in submissions.csv, applies HMAC noise derived from the shared
secrets established by exchange.py and posts the masked value to the API.
Rows with an empty value are skipped.

Noise formula (compatible with C# SecureNoiseGenerator):
  h    = HMAC-SHA256(key=shared_secret, msg="{country}|{YYYY-MM}")
  seed = little-endian signed int64 from first 8 bytes of h
  noise = (seed % (2*MAX_NOISE + 1)) - MAX_NOISE

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


def get_token(api_base_url, client_id, client_secret):
    resp = requests.post(
        f"{api_base_url}/auth/token",
        data={
            "grant_type": "client_credentials",
            "client_id": client_id,
            "client_secret": client_secret,
        },
    )
    if not resp.ok:
        print(f"ERROR: Authentication failed (HTTP {resp.status_code}): {resp.text}")
        sys.exit(1)
    data = resp.json()
    if "accessToken" not in data:
        print(f"ERROR: Unexpected auth response: {data}")
        sys.exit(1)
    return data["accessToken"]


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


def secrets_file(producer_id):
    return os.path.join(KEYS_DIR, f"{producer_id}_secrets.json")


def load_secrets(producer_id):
    path = secrets_file(producer_id)
    if not os.path.exists(path):
        return {}
    with open(path) as f:
        raw = json.load(f)
    return {pid: base64.b64decode(s) for pid, s in raw.items()}


def fetch_my_submissions(api_base_url, headers):
    """Returns (epoch_id, set of (country, 'YYYY-MM') already submitted)."""
    resp = requests.get(f"{api_base_url}/api/metrics/mysubmissions", headers=headers)
    resp.raise_for_status()
    data = resp.json()
    submitted = {
        (entry["country"], entry["month"])  # already YYYY-MM string
        for entry in data.get("submissions", [])
    }
    return data["epochId"], submitted


def main():
    config = load_config()
    api_base_url = config["API_BASE_URL"].rstrip("/")
    client_id = config["CLIENT_ID"]
    client_secret = config["CLIENT_SECRET"]
    producer_id = client_id

    submissions = load_submissions()
    print(f"Producer: {producer_id}")
    print(f"Loaded {len(submissions)} row(s) from submissions.csv")

    shared_secrets = load_secrets(producer_id)
    if not shared_secrets:
        print(
            f"ERROR: No shared secrets found for '{producer_id}'.\n"
            "       Run exchange.py first to complete the ciphertext exchange."
        )
        sys.exit(1)

    print("\nAuthenticating...")
    token = get_token(api_base_url, client_id, client_secret)
    headers = {"Authorization": f"Bearer {token}"}

    # Belt-and-suspenders: confirm the server also sees a complete exchange.
    status_resp = requests.get(f"{api_base_url}/api/keyexchange/status", headers=headers)
    status_resp.raise_for_status()
    status = status_resp.json()
    if not status["isCiphertextExchangeComplete"]:
        missing = ", ".join(status.get("missingCiphertextSenders", []))
        print(
            f"ERROR: Ciphertext exchange incomplete — "
            f"{status['actualCiphertexts']}/{status['expectedCiphertexts']} ciphertexts posted.\n"
            f"       Waiting for: {missing}\n"
            f"       Ask them to run exchange.py, then retry."
        )
        sys.exit(1)

    # ------------------------------------------------------------------
    # Phase 4: Fetch epoch + already-submitted rows, then submit each CSV row
    # ------------------------------------------------------------------
    print("\n--- Noise Calculation & Submission ---")
    print("\nChecking existing submissions...")
    epoch_id, already_submitted = fetch_my_submissions(api_base_url, headers)
    print(f"  Epoch ID: {epoch_id}")
    if already_submitted:
        print(f"  Already submitted {len(already_submitted)} row(s) this epoch")

    success_count = 0
    skipped_count = 0
    failure_count = 0

    for country, month_dt, actual_value in submissions:
        month_str = month_dt.strftime("%Y-%m")

        if (country, month_str) in already_submitted:
            print(f"  [{country} {month_str}] already submitted this epoch — skipping")
            skipped_count += 1
            continue

        masked_value = actual_value
        for partner_id, shared_secret in sorted(shared_secrets.items()):
            noise = compute_noise(shared_secret, country, month_str)
            sign = noise_sign(producer_id, partner_id)
            masked_value += noise * sign

        submission = {
            "ProducerId": producer_id,
            "Country": country,
            "Month": month_str,
            "Value": masked_value,
            "EpochId": epoch_id,
            "Signature": "mlkem-demo",
            "SubmittedAt": datetime.datetime.now(datetime.UTC).strftime("%Y-%m-%dT%H:%M:%SZ"),
        }
        try:
            resp = requests.post(
                f"{api_base_url}/api/metrics/submit",
                json=submission,
                headers=headers,
            )
            if resp.status_code == 409:
                # Race condition safeguard — shouldn't normally reach here
                print(f"  [{country} {month_str}] already submitted this epoch — skipping")
                skipped_count += 1
                continue
            resp.raise_for_status()
            msg = resp.json().get("message", resp.text)
            print(f"  [{country} {month_str}] Submitted → {msg}")
            success_count += 1
        except requests.HTTPError as e:
            print(f"  [{country} {month_str}] ERROR: {e} — {resp.text}")
            failure_count += 1

    print(f"\nDone. {success_count} submitted, {skipped_count} already submitted (skipped), {failure_count} failed.")


if __name__ == "__main__":
    main()
