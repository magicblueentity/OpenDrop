from __future__ import annotations

import hashlib
import mimetypes
import uuid
from datetime import datetime, timezone
from pathlib import Path

from .crypto import compute_shared_secret, create_keypair, derive_key_and_base_nonce, encrypt_chunk, nonce_for_chunk, sha256_hex
from .models import TransferOffer
from .transfer_client import TransferClient


class SecureFileSender:
    def __init__(self, client: TransferClient, chunk_size: int = 256 * 1024) -> None:
        self.client = client
        self.chunk_size = chunk_size

    def send_file(self, base_url: str, sender_device_id: str, file_path: Path) -> None:
        data = file_path.read_bytes()
        offer = TransferOffer(
            session_id="",
            sender_device_id=sender_device_id,
            file_name=file_path.name,
            file_size=len(data),
            content_type=mimetypes.guess_type(file_path.name)[0] or "application/octet-stream",
            sha256_hex=hashlib.sha256(data).hexdigest(),
            created_utc=datetime.now(timezone.utc).isoformat(),
        )
        sender_kp = create_keypair()
        session_resp = self.client.create_session(base_url, offer, sender_kp.public_bytes.hex())

        session_id = session_resp["session_id"]
        receiver_pub = bytes.fromhex(session_resp["receiver_ephemeral_public_key_hex"])
        salt = bytes.fromhex(session_resp["salt_hex"])

        shared = compute_shared_secret(sender_kp.private_key, receiver_pub)
        key, base_nonce = derive_key_and_base_nonce(shared, salt)

        state = self.client.get_resume_state(base_url, session_id)
        offset = int(state["bytes_received"])
        chunk_index = offset // self.chunk_size

        while offset < len(data):
            chunk = data[offset : offset + self.chunk_size]
            aad = f"{session_id}:{offset}:{chunk_index}".encode("utf-8")
            enc = encrypt_chunk(key, nonce_for_chunk(base_nonce, chunk_index), chunk, aad)
            self.client.upload_chunk(base_url, session_id, offset, chunk_index, enc, sha256_hex(enc))
            offset += len(chunk)
            chunk_index += 1

        self.client.finalize(base_url, session_id)
