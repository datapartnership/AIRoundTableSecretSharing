#!/usr/bin/env python3
"""
exchange.py - Perform ML-KEM-768 ciphertext exchange with all partners.

Run this script AFTER keygen.py and BEFORE submit.py.

  Phase 2  Encapsulation  - fetch partner public keys, encapsulate shared secrets,
                            post ciphertexts to the API.
  Phase 3  Decapsulation  - fetch incoming ciphertexts, derive shared secrets.

This script is safe to re-run: previously established secrets are reused,
so noise values stay consistent across retries.

Once ALL partners have run this script, run submit.py to submit your data.

Usage:
    python exchange.py

Configuration is read from a .env file in the same directory.
Copy .env.example to .env and fill in your credentials first.
"""

import base64
import json
import os
import sys

from dotenv import load_dotenv
from kyber_py.kyber import Kyber768
import requests

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
KEYS_DIR = os.path.join(SCRIPT_DIR, "keys")


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
    if not resp.ok:
        print(f"ERROR: Authentication failed (HTTP {resp.status_code}): {resp.text}")
        sys.exit(1)
    data = resp.json()
    if "accessToken" not in data:
        print(f"ERROR: Unexpected auth response: {data}")
        sys.exit(1)
    return data["accessToken"]


def secrets_file(producer_id):
    return os.path.join(KEYS_DIR, f"{producer_id}_secrets.json")


def sent_ciphertexts_file(producer_id):
    return os.path.join(KEYS_DIR, f"{producer_id}_sent_ciphertexts.json")


def load_secrets(producer_id):
    path = secrets_file(producer_id)
    if not os.path.exists(path):
        return {}
    with open(path) as f:
        raw = json.load(f)
    return {pid: base64.b64decode(s) for pid, s in raw.items()}


def save_secrets(producer_id, shared_secrets):
    path = secrets_file(producer_id)
    os.makedirs(KEYS_DIR, exist_ok=True)
    with open(path, "w") as f:
        json.dump({pid: base64.b64encode(s).decode() for pid, s in shared_secrets.items()}, f, indent=2)


def load_sent_ciphertexts(producer_id):
    path = sent_ciphertexts_file(producer_id)
    if not os.path.exists(path):
        return {}
    with open(path) as f:
        raw = json.load(f)
    return {pid: base64.b64decode(ct) for pid, ct in raw.items()}


def save_sent_ciphertexts(producer_id, sent_ciphertexts):
    path = sent_ciphertexts_file(producer_id)
    os.makedirs(KEYS_DIR, exist_ok=True)
    with open(path, "w") as f:
        json.dump({pid: base64.b64encode(ct).decode() for pid, ct in sent_ciphertexts.items()}, f, indent=2)


def main():
    config = load_config()
    api_base_url = config["API_BASE_URL"].rstrip("/")
    client_id = config["CLIENT_ID"]
    client_secret = config["CLIENT_SECRET"]
    producer_id = client_id

    _, secret_key = load_key(producer_id)

    print(f"Producer: {producer_id}")
    print("\nAuthenticating...")
    token = get_token(api_base_url, client_id, client_secret)
    headers = {"Authorization": f"Bearer {token}"}

    print("\nChecking key registration status...")
    status_resp = requests.get(f"{api_base_url}/api/keyexchange/status", headers=headers)
    status_resp.raise_for_status()
    status = status_resp.json()
    if not status["isComplete"]:
        missing = ", ".join(status["missingPartners"])
        print(
            f"ERROR: Not all partners have registered keys yet "
            f"({status['registeredCount']}/{status['expectedCount']}).\n"
            f"       Waiting for: {missing}\n"
            f"       Ask them to run keygen.py first, then retry."
        )
        sys.exit(1)
    print(f"  All {status['expectedCount']} partners have registered keys — proceeding")

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

    shared_secrets = load_secrets(producer_id)
    sent_ciphertexts = load_sent_ciphertexts(producer_id)

    for partner in partner_keys:
        partner_id = partner["producerId"]
        if producer_id > partner_id:
            if partner_id in shared_secrets and partner_id in sent_ciphertexts:
                # Reuse the persisted keypair — no re-encapsulation needed.
                ct_b64 = base64.b64encode(sent_ciphertexts[partner_id]).decode()
                print(f"  Reusing persisted secret for {partner_id}")
            else:
                # First time, or ciphertext bytes were never saved (old format) — encapsulate fresh.
                partner_public_key = base64.b64decode(partner["publicKeyBase64"])
                shared_secret, ciphertext = Kyber768.encaps(partner_public_key)
                shared_secrets[partner_id] = shared_secret
                sent_ciphertexts[partner_id] = ciphertext
                ct_b64 = base64.b64encode(ciphertext).decode()
                print(f"  Encapsulated for {partner_id}")
            # Always post to the server — it does an upsert, so this is safe even if
            # the ciphertext is already present (handles server-side resets cleanly).
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
        else:
            print(f"  Skipping {partner_id} — they will encapsulate for us")

    save_secrets(producer_id, shared_secrets)
    save_sent_ciphertexts(producer_id, sent_ciphertexts)

    # ------------------------------------------------------------------
    # Phase 3: Decapsulation
    # Receive ciphertexts from partners that encapsulated for us (larger IDs).
    # Always decapsulate — ensures we have the latest shared secret even if
    # the sender had to re-encapsulate after a server reset.
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
        new_secret = Kyber768.decaps(secret_key, ciphertext)
        old_secret = shared_secrets.get(sender_id)
        shared_secrets[sender_id] = new_secret
        if old_secret is None:
            print(f"  Derived shared secret from {sender_id}")
        elif old_secret == new_secret:
            print(f"  Verified shared secret from {sender_id} (unchanged)")
        else:
            print(f"  Updated shared secret from {sender_id} (ciphertext was re-posted)")

    save_secrets(producer_id, shared_secrets)

    # ------------------------------------------------------------------
    # Report exchange status — tell the user who still needs to run this script.
    # ------------------------------------------------------------------
    status_resp = requests.get(f"{api_base_url}/api/keyexchange/status", headers=headers)
    status_resp.raise_for_status()
    status = status_resp.json()

    if status["isCiphertextExchangeComplete"]:
        print("\nExchange complete. You can now run submit.py.")
    else:
        waiting = ", ".join(status.get("missingCiphertextSenders", []))
        print(
            f"\nExchange in progress — waiting for these partner(s) to run exchange.py:\n"
            f"  {waiting}\n"
            f"Once they confirm, re-run this script to pick up their ciphertext\n"
            f"and derive your shared secret, then run submit.py."
        )


if __name__ == "__main__":
    main()
