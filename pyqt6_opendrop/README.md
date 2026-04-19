# OpenDrop PyQt6 (kompletter Python-Neuaufbau)

Diese Implementierung ist eine vollständige Python/PyQt6-Neufassung der bestehenden OpenDrop-Idee:

- **UI:** PyQt6 Desktop-App
- **Discovery:** UDP Broadcast auf Port `35353`
- **Transfer:** HTTP API (`/api/v1/sessions`, `/chunks`, `/finalize`)
- **Krypto:** ECDH P-256 + HKDF-SHA256 + AES-256-GCM (Chunk-basiert)
- **Resume:** Upload ab `bytes_received`

## Start

```bash
cd pyqt6_opendrop
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -e .
opendrop-pyqt6
```

## Hinweis

Die C#-Implementierung bleibt im Repository unverändert erhalten; dieser Ordner enthält den vollständigen Python-Neuaufbau.
