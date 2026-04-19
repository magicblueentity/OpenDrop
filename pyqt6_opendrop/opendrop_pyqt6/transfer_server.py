from __future__ import annotations

import hashlib
import os
import threading
import uuid
from dataclasses import dataclass
from pathlib import Path

from flask import Flask, jsonify, request
from werkzeug.serving import make_server

from .crypto import compute_shared_secret, create_keypair, decrypt_chunk, derive_key_and_base_nonce, nonce_for_chunk, sha256_hex
from .security import sanitize_file_name


@dataclass(slots=True)
class TransferSession:
    session_id: str
    file_name: str
    file_size: int
    sha256_hex_expected: str
    temp_path: Path
    key: bytes
    base_nonce: bytes
    bytes_received: int = 0
    finalized: bool = False


class TransferServer:
    def __init__(self, host: str, port: int, storage_dir: Path, max_file_size: int = 4 * 1024 * 1024 * 1024) -> None:
        self.host = host
        self.port = port
        self.storage_dir = storage_dir
        self.max_file_size = max_file_size
        self.sessions: dict[str, TransferSession] = {}
        self.app = Flask("opendrop-transfer")
        self._server = None
        self._thread: threading.Thread | None = None
        self._register_routes()

    def _register_routes(self) -> None:
        @self.app.post("/api/v1/sessions")
        def create_session():
            req = request.get_json(force=True)
            offer = req["offer"]
            if offer["file_size"] <= 0 or offer["file_size"] > self.max_file_size:
                return jsonify({"error": "invalid size"}), 400

            recv_keys = create_keypair()
            salt = os.urandom(16)
            shared = compute_shared_secret(recv_keys.private_key, bytes.fromhex(req["sender_ephemeral_public_key_hex"]))
            key, nonce = derive_key_and_base_nonce(shared, salt)

            session_id = uuid.uuid4().hex
            safe_name = sanitize_file_name(offer["file_name"])
            temp_path = self.storage_dir / f"{session_id}.{safe_name}.part"
            self.sessions[session_id] = TransferSession(
                session_id=session_id,
                file_name=safe_name,
                file_size=int(offer["file_size"]),
                sha256_hex_expected=offer["sha256_hex"],
                temp_path=temp_path,
                key=key,
                base_nonce=nonce,
            )
            return jsonify(
                {
                    "session_id": session_id,
                    "receiver_ephemeral_public_key_hex": recv_keys.public_bytes.hex(),
                    "salt_hex": salt.hex(),
                }
            )

        @self.app.get("/api/v1/sessions/<session_id>/status")
        def status(session_id: str):
            s = self.sessions.get(session_id)
            if not s:
                return jsonify({"error": "not found"}), 404
            return jsonify({"session_id": s.session_id, "bytes_received": s.bytes_received, "finalized": s.finalized})

        @self.app.put("/api/v1/sessions/<session_id>/chunks")
        def chunk(session_id: str):
            s = self.sessions.get(session_id)
            if not s:
                return jsonify({"error": "not found"}), 404
            offset = int(request.args.get("offset", "-1"))
            index = int(request.args.get("index", "-1"))
            declared_sha = request.args.get("sha256", "")
            if offset != s.bytes_received:
                return jsonify({"error": "offset mismatch"}), 409
            enc = request.get_data()
            if sha256_hex(enc) != declared_sha:
                return jsonify({"error": "chunk hash mismatch"}), 400
            aad = f"{session_id}:{offset}:{index}".encode("utf-8")
            plain = decrypt_chunk(s.key, nonce_for_chunk(s.base_nonce, index), enc, aad)
            if s.bytes_received + len(plain) > s.file_size:
                return jsonify({"error": "too much data"}), 400
            s.temp_path.parent.mkdir(parents=True, exist_ok=True)
            with s.temp_path.open("ab") as f:
                f.write(plain)
            s.bytes_received += len(plain)
            return ("", 204)

        @self.app.post("/api/v1/sessions/<session_id>/finalize")
        def finalize(session_id: str):
            s = self.sessions.get(session_id)
            if not s:
                return jsonify({"error": "not found"}), 404
            if s.bytes_received != s.file_size:
                return jsonify({"error": "incomplete"}), 409
            digest = hashlib.sha256(s.temp_path.read_bytes()).hexdigest()
            if digest != s.sha256_hex_expected:
                return jsonify({"error": "file hash mismatch"}), 400
            final_path = self.storage_dir / s.file_name
            i = 1
            while final_path.exists():
                final_path = self.storage_dir / f"{Path(s.file_name).stem} ({i}){Path(s.file_name).suffix}"
                i += 1
            s.temp_path.replace(final_path)
            s.finalized = True
            return jsonify({"path": str(final_path)})

    def start(self) -> None:
        self.storage_dir.mkdir(parents=True, exist_ok=True)
        self._server = make_server(self.host, self.port, self.app)
        self._thread = threading.Thread(target=self._server.serve_forever, daemon=True)
        self._thread.start()

    def stop(self) -> None:
        if self._server:
            self._server.shutdown()
