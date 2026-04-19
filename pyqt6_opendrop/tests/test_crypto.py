from opendrop_pyqt6.crypto import nonce_for_chunk


def test_nonce_for_chunk_changes() -> None:
    base = bytes(range(12))
    assert nonce_for_chunk(base, 0) == base
    assert nonce_for_chunk(base, 1) != base
