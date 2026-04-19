from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone


@dataclass(slots=True)
class DeviceDescriptor:
    device_id: str
    display_name: str
    service_host: str
    service_port: int
    last_seen_utc: datetime = field(default_factory=lambda: datetime.now(timezone.utc))

    @property
    def base_url(self) -> str:
        return f"http://{self.service_host}:{self.service_port}"


@dataclass(slots=True)
class TransferOffer:
    session_id: str
    sender_device_id: str
    file_name: str
    file_size: int
    content_type: str
    sha256_hex: str
    created_utc: str
