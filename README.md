# OpenDrop (Windows)
Made by Samuel J. Tirwa. An open, secure Windows alternative to AirDrop.

AirDrop-like local file transfer for Windows with:
- Automatic nearby device discovery (UDP broadcast; replaceable with mDNS later)
- End-to-end encrypted file payloads (ECDH P-256 + HKDF-SHA256 + AES-256-GCM)
- Chunked transfer with resume support (byte offset)
- WinUI 3 UI (Windows App SDK) with drag-and-drop send

## Important: Apple AirDrop Compatibility
This repository does **not** implement Apple’s proprietary/private AirDrop stack (AWDL, Apple-issued certificates, private APIs). Building a Windows client that “speaks real AirDrop” would require proprietary components and protocol details that are not publicly documented and may violate platform security boundaries and/or legal restrictions.

This project targets an **open** and **secure** AirDrop-like UX that can be extended to other platforms with compatible clients.


## Python/PyQt6 Rewrite
A complete Python + PyQt6 rewrite is now available under `pyqt6_opendrop/` with equivalent discovery, encrypted chunk transfer, and desktop UI flow.

## Project Structure
- `OpenDrop.Core/`: domain models + interfaces (discovery/crypto/transfer)
- `OpenDrop.Infrastructure/`: discovery + crypto + transfer server/client implementation
- `OpenDrop.App.csproj`: WinUI 3 application (UI, tray/toast placeholders)
- `OpenDrop.Tests/`: unit tests (path safety, crypto round-trip)

## Build
Prereqs: Windows 11, .NET SDK 8.x, Visual Studio 2022 with WinUI 3 tooling recommended.

```powershell
dotnet restore
dotnet test
dotnet build
```

## Run
Open the solution `OpenDrop.sln` in Visual Studio and run `OpenDrop.App`.
Run the app on two Windows machines on the same LAN: devices should appear in the grid. Select a device and drag files onto the drop area to send.

Incoming files are saved to:
`%LocalAppData%\\OpenDrop\\Incoming`

## Security Notes (Current Skeleton)
- Payload encryption: each transfer session derives an AEAD key + base nonce from an ECDH shared secret via HKDF-SHA256; each chunk uses a unique per-chunk nonce and binds AAD to `(sessionId, offset, chunkIndex)`.
- Integrity: each encrypted chunk includes SHA-256 validation; the final file hash is verified before finalizing.
- Path safety: filenames are sanitized (`OpenDrop.Core/Security/PathSafety.cs`) to prevent directory traversal.
- Abuse controls: simple per-remote IP token-bucket rate limiting in the server.

### Known Gaps
- Transport is currently plain HTTP; confidentiality relies on the E2E payload encryption, but metadata is still visible. A production build should enable TLS (certificate pinning / pairing flow) and/or mutually authenticated transport.
- Pairing / trust UI is not implemented yet.
- File type validation is extension-based only (should add content sniffing + policy).
- Resume currently supports “resume from contiguous bytes received”; it does not support sparse ranges.

## Roadmap
- TLS + pairing flow (certificate pinning or PAKE-based pairing)
- Real Windows 11 integration: tray icon menu, toasts, “Send with …” context menu
- Concurrent transfer manager + progress UI per transfer
