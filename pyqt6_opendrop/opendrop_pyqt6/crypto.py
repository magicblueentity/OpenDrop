from __future__ import annotations

import hashlib
from dataclasses import dataclass

from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import ec
from cryptography.hazmat.primitives.ciphers.aead import AESGCM
from cryptography.hazmat.primitives.kdf.hkdf import HKDF


@dataclass(slots=True)
class KexKeyPair:
    private_key: ec.EllipticCurvePrivateKey

    @property
    def public_bytes(self) -> bytes:
        return self.private_key.public_key().public_bytes(
            encoding=serialization.Encoding.X962,
            format=serialization.PublicFormat.UncompressedPoint,
        )


def create_keypair() -> KexKeyPair:
    return KexKeyPair(private_key=ec.generate_private_key(ec.SECP256R1()))


def compute_shared_secret(private_key: ec.EllipticCurvePrivateKey, peer_public_bytes: bytes) -> bytes:
    peer_key = ec.EllipticCurvePublicKey.from_encoded_point(ec.SECP256R1(), peer_public_bytes)
    return private_key.exchange(ec.ECDH(), peer_key)


def derive_key_and_base_nonce(shared_secret: bytes, salt: bytes, info: bytes = b"airdroplike/v1/session") -> tuple[bytes, bytes]:
    derived = HKDF(algorithm=hashes.SHA256(), length=44, salt=salt, info=info).derive(shared_secret)
    return derived[:32], derived[32:44]


def nonce_for_chunk(base_nonce: bytes, chunk_index: int) -> bytes:
    mask = chunk_index.to_bytes(12, "big", signed=False)
    return bytes(a ^ b for a, b in zip(base_nonce, mask, strict=True))


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def encrypt_chunk(key: bytes, nonce: bytes, plaintext: bytes, aad: bytes) -> bytes:
    return AESGCM(key).encrypt(nonce, plaintext, aad)


def decrypt_chunk(key: bytes, nonce: bytes, ciphertext: bytes, aad: bytes) -> bytes:
    return AESGCM(key).decrypt(nonce, ciphertext, aad)
