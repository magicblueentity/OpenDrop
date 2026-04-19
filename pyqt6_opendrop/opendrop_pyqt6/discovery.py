from __future__ import annotations

import json
import socket
import threading
import time
import uuid
from collections.abc import Callable
from datetime import datetime, timezone

from .models import DeviceDescriptor


class UdpDeviceDiscovery:
    PORT = 35353

    def __init__(self, display_name: str, service_port: int) -> None:
        self.device_id = str(uuid.uuid4())
        self.display_name = display_name
        self.service_port = service_port
        self._devices: dict[str, DeviceDescriptor] = {}
        self._lock = threading.Lock()
        self._stop = threading.Event()
        self._threads: list[threading.Thread] = []
        self.on_upsert: Callable[[DeviceDescriptor], None] | None = None
        self.on_expire: Callable[[str], None] | None = None

    def start(self) -> None:
        self._stop.clear()
        self._threads = [
            threading.Thread(target=self._announce_loop, daemon=True),
            threading.Thread(target=self._receive_loop, daemon=True),
            threading.Thread(target=self._expire_loop, daemon=True),
        ]
        for t in self._threads:
            t.start()

    def stop(self) -> None:
        self._stop.set()

    def _announce_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
        payload = {
            "id": self.device_id,
            "name": self.display_name,
            "port": self.service_port,
        }
        while not self._stop.is_set():
            payload["ts"] = int(time.time() * 1000)
            data = json.dumps(payload).encode("utf-8")
            sock.sendto(data, ("255.255.255.255", self.PORT))
            time.sleep(2)

    def _receive_loop(self) -> None:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        sock.bind(("0.0.0.0", self.PORT))
        sock.settimeout(1)
        while not self._stop.is_set():
            try:
                data, addr = sock.recvfrom(64 * 1024)
            except socket.timeout:
                continue
            try:
                msg = json.loads(data.decode("utf-8"))
                if msg.get("id") == self.device_id:
                    continue
                dd = DeviceDescriptor(
                    device_id=msg["id"],
                    display_name=msg.get("name") or msg["id"],
                    service_host=addr[0],
                    service_port=int(msg["port"]),
                    last_seen_utc=datetime.now(timezone.utc),
                )
            except Exception:
                continue
            with self._lock:
                self._devices[dd.device_id] = dd
            if self.on_upsert:
                self.on_upsert(dd)

    def _expire_loop(self) -> None:
        while not self._stop.is_set():
            now = datetime.now(timezone.utc)
            expired: list[str] = []
            with self._lock:
                for device_id, dd in list(self._devices.items()):
                    if (now - dd.last_seen_utc).total_seconds() > 8:
                        expired.append(device_id)
                        self._devices.pop(device_id, None)
            for device_id in expired:
                if self.on_expire:
                    self.on_expire(device_id)
            time.sleep(2)
