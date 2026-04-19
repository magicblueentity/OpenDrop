from __future__ import annotations

import os
from pathlib import Path


def sanitize_file_name(file_name: str) -> str:
    if not file_name:
        return "file"
    candidate = os.path.basename(file_name).strip().replace("\x00", "")
    for ch in '<>:"/\\|?*':
        candidate = candidate.replace(ch, "_")
    if candidate in {"", ".", ".."}:
        return "file"
    return candidate


def safe_join(base_dir: Path, file_name: str) -> Path:
    base = base_dir.resolve()
    candidate = (base / sanitize_file_name(file_name)).resolve()
    if not str(candidate).startswith(str(base)):
        raise ValueError("Unsafe path")
    return candidate
