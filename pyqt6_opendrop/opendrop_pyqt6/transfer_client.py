from __future__ import annotations

from dataclasses import asdict

import requests

from .models import TransferOffer


class TransferClient:
    def create_session(self, base_url: str, offer: TransferOffer, sender_ephemeral_public_key_hex: str) -> dict:
        resp = requests.post(
            f"{base_url}/api/v1/sessions",
            json={"offer": asdict(offer), "sender_ephemeral_public_key_hex": sender_ephemeral_public_key_hex},
            timeout=30,
        )
        resp.raise_for_status()
        return resp.json()

    def get_resume_state(self, base_url: str, session_id: str) -> dict:
        resp = requests.get(f"{base_url}/api/v1/sessions/{session_id}/status", timeout=30)
        resp.raise_for_status()
        return resp.json()

    def upload_chunk(self, base_url: str, session_id: str, offset: int, index: int, encrypted_chunk: bytes, chunk_sha256_hex: str) -> None:
        resp = requests.put(
            f"{base_url}/api/v1/sessions/{session_id}/chunks",
            params={"offset": offset, "index": index, "sha256": chunk_sha256_hex},
            data=encrypted_chunk,
            timeout=60,
        )
        resp.raise_for_status()

    def finalize(self, base_url: str, session_id: str) -> None:
        resp = requests.post(f"{base_url}/api/v1/sessions/{session_id}/finalize", timeout=30)
        resp.raise_for_status()
