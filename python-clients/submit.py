#!/usr/bin/env python3
"""
submit.py - Submit a masked metric using the ML-KEM-768 secure noise protocol.

Mirrors the 4-phase protocol of SecureProducerClient.cs:
  Phase 1  (prerequisite) - keygen.py must have already run to produce a keypair.
  Phase 2  Encapsulation  - fetch partner public keys, encapsulate shared secrets,
                            post ciphertexts to the API.
  Phase 3  Decapsulation  - fetch incoming ciphertexts, derive shared secrets.
  Phase 4  Submit         - apply HMAC noise to the value, post masked metric.

Noise formula (self-consistent among Python producers):
  h    = HMAC-SHA256(key=shared_secret, msg="{country}|{YYYY-MM}")
  seed = little-endian signed int64 from first 8 bytes of h
  noise = (seed % (2*MAX_NOISE + 1)) - MAX_NOISE

NOTE: this formula is NOT compatible with the C# SecureNoiseGenerator, which uses
.NET System.Random internally. Mixed Python+C# deployments require updating the
C# formula as well.

Usage:
    python submit.py --country US --month 2025-01 --value 1000000

Configuration is read from config.json in the same directory.
"""

import argparse
import base64
import datetime
import hashlib
import hmac as hmac_module
import json
import os
import struct
import sys

import oqs
import requests

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
CONFIG_FILE = os.path.join(SCRIPT_DIR, "config.json")
KEYS_DIR = os.path.join(SCRIPT_DIR, "keys")
MAX_NOISE = 100_000_000


def load_config():
    if not os.path.exists(CONFIG_FILE):
        print(
            "ERROR: config.json not found.\n"
            "       Copy config.json.example to config.json and fill in your credentials."
        )
        sys.exit(1)
    with open(CONFIG_FILE) as f:
        return json.load(f)


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


def main():
    parser = argparse.ArgumentParser(
        description="Submit a masked metric via the ML-KEM-768 secure noise protocol."
    )
    parser.add_argument("--country", required=True, help="Country code, e.g. US")
    parser.add_argument(
        "--month", required=True, help="Reporting month in YYYY-MM format, e.g. 2025-01"
    )
    parser.add_argument("--value", required=True, type=int, help="Actual (private) metric value")
    args = parser.parse_args()

    try:
        month_dt = datetime.datetime.strptime(args.month, "%Y-%m")
    except ValueError:
        print("ERROR: --month must be in YYYY-MM format (e.g. 2025-01)")
        sys.exit(1)

    month_str = month_dt.strftime("%Y-%m")
    month_iso = month_dt.strftime("%Y-%m-01T00:00:00Z")

    config = load_config()
    api_base_url = config["api_base_url"].rstrip("/")
    producer_id = config["producer_id"]
    client_id = config["client_id"]
    client_secret = config["client_secret"]

    print(f"Producer:     {producer_id}")
    print(f"Country:      {args.country}")
    print(f"Month:        {month_str}")
    print(f"Actual value: {args.value:,}")

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
            with oqs.KeyEncapsulation("Kyber768") as kem:
                ciphertext, shared_secret = kem.encap_secret(partner_public_key)
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

    with oqs.KeyEncapsulation("Kyber768", secret_key) as kem:
        for entry in incoming:
            sender_id = entry["senderId"]
            ciphertext = base64.b64decode(entry["ciphertextBase64"])
            shared_secret = kem.decap_secret(ciphertext)
            shared_secrets[sender_id] = shared_secret
            print(f"  Decapsulated shared secret from {sender_id}")

    if not shared_secrets:
        print("  No shared secrets established — value will be submitted without noise masking")

    # ------------------------------------------------------------------
    # Phase 4: Compute masked value and submit
    # ------------------------------------------------------------------
    print("\n--- Phase 4: Noise Calculation & Submission ---")
    masked_value = args.value
    noise_breakdown: dict[str, int] = {}

    for partner_id, shared_secret in sorted(shared_secrets.items()):
        noise = compute_noise(shared_secret, args.country, month_str)
        sign = noise_sign(producer_id, partner_id)
        applied = noise * sign
        noise_breakdown[partner_id] = applied
        masked_value += applied
        print(f"  {partner_id}: raw_noise={noise:+,}  sign={sign:+d}  applied={applied:+,}")

    total_noise = masked_value - args.value
    print(f"\n  Original value : {args.value:,}")
    print(f"  Total noise    : {total_noise:+,}")
    print(f"  Masked value   : {masked_value:,}")

    print("\nFetching current epoch...")
    epoch_resp = requests.get(f"{api_base_url}/api/registry/epoch", headers=headers)
    epoch_resp.raise_for_status()
    epoch_id = epoch_resp.json()["epochId"]
    print(f"  Epoch ID: {epoch_id}")

    print("\nSubmitting masked metric...")
    submission = {
        "ProducerId": producer_id,
        "Country": args.country,
        "Month": month_iso,
        "Value": masked_value,
        "EpochId": epoch_id,
        "Signature": "mlkem-demo",
        "SubmittedAt": datetime.datetime.utcnow().strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    submit_resp = requests.post(
        f"{api_base_url}/api/metrics/submit",
        json=submission,
        headers=headers,
    )
    submit_resp.raise_for_status()
    print(f"API response: {submit_resp.json().get('message', submit_resp.text)}")


if __name__ == "__main__":
    main()
