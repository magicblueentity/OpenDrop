from pathlib import Path

from opendrop_pyqt6.security import safe_join, sanitize_file_name


def test_sanitize_file_name_basic() -> None:
    assert sanitize_file_name("../evil.txt") == "evil.txt"
    assert sanitize_file_name("") == "file"


def test_safe_join_stays_inside(tmp_path: Path) -> None:
    p = safe_join(tmp_path, "../a.txt")
    assert p.parent == tmp_path.resolve()
