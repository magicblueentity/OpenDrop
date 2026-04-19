from __future__ import annotations

import os
import socket
import sys
from pathlib import Path

from PyQt6.QtCore import Qt
from PyQt6.QtWidgets import (
    QApplication,
    QFileDialog,
    QHBoxLayout,
    QLabel,
    QListWidget,
    QListWidgetItem,
    QMainWindow,
    QMessageBox,
    QPushButton,
    QVBoxLayout,
    QWidget,
)

from .discovery import UdpDeviceDiscovery
from .models import DeviceDescriptor
from .sender import SecureFileSender
from .transfer_client import TransferClient
from .transfer_server import TransferServer


class MainWindow(QMainWindow):
    def __init__(self) -> None:
        super().__init__()
        self.setWindowTitle("OpenDrop PyQt6")
        self.resize(720, 480)

        self.machine_name = socket.gethostname()
        incoming = Path(os.getenv("LOCALAPPDATA", str(Path.home()))) / "OpenDrop" / "Incoming"

        self.server = TransferServer(host="0.0.0.0", port=58080, storage_dir=incoming)
        self.server.start()

        self.discovery = UdpDeviceDiscovery(display_name=self.machine_name, service_port=58080)
        self.discovery.on_upsert = self.on_upsert
        self.discovery.on_expire = self.on_expire
        self.discovery.start()

        self.sender = SecureFileSender(TransferClient())
        self.devices: dict[str, DeviceDescriptor] = {}

        root = QWidget()
        self.setCentralWidget(root)
        layout = QVBoxLayout(root)
        self.status = QLabel("Discovering nearby devices…")
        self.status.setWordWrap(True)
        self.status.setAlignment(Qt.AlignmentFlag.AlignLeft)
        layout.addWidget(self.status)

        self.device_list = QListWidget()
        layout.addWidget(self.device_list)

        btns = QHBoxLayout()
        self.send_btn = QPushButton("Datei senden")
        self.send_btn.clicked.connect(self.send_file)
        btns.addWidget(self.send_btn)
        layout.addLayout(btns)

    def on_upsert(self, device: DeviceDescriptor) -> None:
        self.devices[device.device_id] = device
        self._refresh_devices()

    def on_expire(self, device_id: str) -> None:
        self.devices.pop(device_id, None)
        self._refresh_devices()

    def _refresh_devices(self) -> None:
        self.device_list.clear()
        for dev in self.devices.values():
            item = QListWidgetItem(f"{dev.display_name} ({dev.service_host}:{dev.service_port})")
            item.setData(Qt.ItemDataRole.UserRole, dev.device_id)
            self.device_list.addItem(item)

    def send_file(self) -> None:
        item = self.device_list.currentItem()
        if not item:
            QMessageBox.warning(self, "OpenDrop", "Bitte zuerst ein Zielgerät auswählen.")
            return
        device_id = item.data(Qt.ItemDataRole.UserRole)
        target = self.devices[device_id]

        path, _ = QFileDialog.getOpenFileName(self, "Datei wählen")
        if not path:
            return

        try:
            self.status.setText(f"Sende {Path(path).name} an {target.display_name}…")
            self.sender.send_file(target.base_url, sender_device_id=self.machine_name, file_path=Path(path))
            self.status.setText(f"Gesendet: {Path(path).name}")
        except Exception as exc:
            self.status.setText(f"Fehler: {exc}")
            QMessageBox.critical(self, "OpenDrop", str(exc))

    def closeEvent(self, event):  # noqa: N802
        self.discovery.stop()
        self.server.stop()
        super().closeEvent(event)


def main() -> int:
    app = QApplication(sys.argv)
    mw = MainWindow()
    mw.show()
    return app.exec()


if __name__ == "__main__":
    raise SystemExit(main())
