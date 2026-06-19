#!/usr/bin/env python3
"""
keygen.py - Generate an ML-KEM-768 keypair and register the public key with the API.

If a keypair already exists for this producer it is NOT regenerated; the existing
public key is (re-)posted to the API so the script is safe to run multiple times.

Usage:
    python keygen.py

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


def main():
    config = load_config()
    api_base_url = config["API_BASE_URL"].rstrip("/")
    client_id = config["CLIENT_ID"]
    client_secret = config["CLIENT_SECRET"]
    producer_id = client_id

    os.makedirs(KEYS_DIR, exist_ok=True)
    key_file = os.path.join(KEYS_DIR, f"{producer_id}.json")

    if os.path.exists(key_file):
        print(f"Key already exists at {key_file} — skipping generation.")
        with open(key_file) as f:
            key_data = json.load(f)
        public_key_b64 = key_data["public_key_b64"]
    else:
        print("Generating new ML-KEM-768 keypair...")
        public_key, secret_key = Kyber768.keygen()  # pk: 1184 bytes, sk: 2400 bytes

        public_key_b64 = base64.b64encode(public_key).decode()
        secret_key_b64 = base64.b64encode(secret_key).decode()

        key_data = {
            "producer_id": producer_id,
            "public_key_b64": public_key_b64,
            "secret_key_b64": secret_key_b64,
        }
        with open(key_file, "w") as f:
            json.dump(key_data, f, indent=2)
        print(f"Keypair saved to {key_file}")

    print("Authenticating...")
    token = get_token(api_base_url, client_id, client_secret)

    print(f"Registering public key for producer '{producer_id}'...")
    resp = requests.post(
        f"{api_base_url}/api/keyexchange/register",
        json={"ProducerId": producer_id, "PublicKeyBase64": public_key_b64},
        headers={"Authorization": f"Bearer {token}"},
    )
    resp.raise_for_status()
    print(f"API response: {resp.json().get('message', resp.text)}")


if __name__ == "__main__":
    main()
