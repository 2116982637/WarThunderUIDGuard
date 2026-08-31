"""Shared, dependency-free helpers for the War Thunder UID Guard server.

This module intentionally uses only the Python standard library.  RSA signing is
delegated to the operating system's OpenSSL executable because Python's standard
library does not implement RSA private-key operations.
"""

from __future__ import annotations

import base64
import contextlib
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import threading
import time
from typing import Any, Callable, Mapping

try:  # POSIX production path.
    import fcntl  # type: ignore[import-not-found]
except ImportError:  # pragma: no cover - exercised by the Windows test runner.
    fcntl = None  # type: ignore[assignment]

try:  # Allows the standard-library tests to run on Windows as well.
    import msvcrt  # type: ignore[import-not-found]
except ImportError:  # pragma: no cover - Linux production path.
    msvcrt = None  # type: ignore[assignment]


MAX_UPLOAD_BYTES = 1_048_576
MAX_PLAYERS = 50_000
MAX_DELETED_PLAYERS = 50_000
MAX_ALIASES = 50
MAX_NOTE_UTF16_UNITS = 500
MAX_ALIAS_UTF16_UNITS = 100
UID_PATTERN = re.compile(r"^\d{1,20}$")


class BlacklistValidationError(ValueError):
    """Raised when an uploaded document is not a valid schema-v2 blacklist."""


class LockTimeoutError(TimeoutError):
    """Raised when the shared data writer lock cannot be acquired in time."""


class SigningError(RuntimeError):
    """Raised when OpenSSL cannot produce a compatible signature."""


def sha256_hex(payload: bytes) -> str:
    """Return the uppercase SHA-256 representation used by the C# client."""

    return hashlib.sha256(payload).hexdigest().upper()


def load_raw_key(path: Path, expected_bytes: int = 32) -> bytearray:
    """Load an exact-size raw secret without following symbolic links.

    Group access is permitted so a root-owned key can be readable by the service
    group.  Any permission for ``other`` users is rejected on POSIX.
    """

    path = Path(path)
    if path.is_symlink():
        raise ValueError(f"Secret path must not be a symbolic link: {path}")
    flags = os.O_RDONLY
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    if hasattr(os, "O_CLOEXEC"):
        flags |= os.O_CLOEXEC
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    try:
        descriptor = os.open(path, flags)
    except OSError as exc:
        raise ValueError(f"Secret file could not be opened safely: {path}") from exc
    try:
        metadata = os.fstat(descriptor)
        if not stat.S_ISREG(metadata.st_mode):
            raise ValueError(f"Secret path is not a regular file: {path}")
        if os.name == "posix" and metadata.st_mode & 0o007:
            raise PermissionError(f"Secret file is accessible to other users: {path}")
        if metadata.st_size != expected_bytes:
            raise ValueError(f"Secret must contain exactly {expected_bytes} raw bytes.")
        key = os.read(descriptor, expected_bytes + 1)
    finally:
        os.close(descriptor)
    if len(key) != expected_bytes:
        raise ValueError(f"Secret must contain exactly {expected_bytes} raw bytes.")
    return bytearray(key)


def zero_bytearray(value: bytearray) -> None:
    for index in range(len(value)):
        value[index] = 0


def _case_insensitive_member(value: Mapping[str, Any], name: str) -> Any:
    if name in value:
        return value[name]
    wanted = name.casefold()
    for key, item in value.items():
        if isinstance(key, str) and key.casefold() == wanted:
            return item
    return None


def _powershell_array(value: Any) -> list[Any]:
    if value is None:
        return []
    if isinstance(value, list):
        return value
    return [value]


def _dotnet_string(value: Any) -> str:
    if value is None:
        return ""
    if value is True:
        return "True"
    if value is False:
        return "False"
    return str(value)


def _utf16_length(value: str) -> int:
    return len(value.encode("utf-16-le")) // 2


def _reject_json_constant(value: str) -> None:
    raise BlacklistValidationError(f"InvalidJsonConstant:{value}")


def _strict_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    """Reject duplicate keys, including case-only collisions.

    The C# client reads properties case-insensitively, so accepting both ``Uid``
    and ``uid`` would make the signed document ambiguous across runtimes.
    """

    result: dict[str, Any] = {}
    seen: set[str] = set()
    for key, value in pairs:
        folded = key.casefold()
        if folded in seen:
            raise BlacklistValidationError("DuplicateJsonProperty")
        seen.add(folded)
        result[key] = value
    return result


def _parse_datetime_offset(value: Any) -> None:
    text = _dotnet_string(value).strip()
    if not text:
        raise BlacklistValidationError("InvalidDateTime")
    candidate = text[:-1] + "+00:00" if text.endswith(("Z", "z")) else text
    # System.Text.Json can emit seven fractional digits.  datetime accepts these
    # on current CPython, but trimming a validation-only copy keeps 3.11+ support.
    candidate = re.sub(
        r"(?<=\.)(\d{6})\d+(?=(?:[+-]\d{2}:?\d{2})?$)",
        r"\1",
        candidate,
    )
    try:
        dt.datetime.fromisoformat(candidate)
    except ValueError as exc:
        raise BlacklistValidationError("InvalidDateTime") from exc


def validate_blacklist_document(document: Any) -> Mapping[str, Any]:
    """Validate the same fields and limits enforced by the Windows service.

    The returned object is only for audit counts.  Callers must publish the
    original request bytes, never a re-serialized representation of this object.
    """

    if not isinstance(document, Mapping):
        raise BlacklistValidationError("InvalidSchema")
    schema = _case_insensitive_member(document, "SchemaVersion")
    if isinstance(schema, bool) or not isinstance(schema, int) or schema != 2:
        raise BlacklistValidationError("InvalidSchema")
    language = _case_insensitive_member(document, "Language")
    sync_enabled = _case_insensitive_member(document, "OneDriveSyncEnabled")
    if not isinstance(language, str) or not isinstance(sync_enabled, bool):
        raise BlacklistValidationError("InvalidSchema")

    players_value = _case_insensitive_member(document, "Players")
    deleted_value = _case_insensitive_member(document, "DeletedPlayers")
    if not isinstance(players_value, list) or not isinstance(deleted_value, list):
        raise BlacklistValidationError("InvalidArrays")
    players = players_value
    deleted = deleted_value
    if len(players) > MAX_PLAYERS or len(deleted) > MAX_DELETED_PLAYERS:
        raise BlacklistValidationError("TooManyRecords")

    seen_uids: set[str] = set()
    for player in players:
        if not isinstance(player, Mapping):
            raise BlacklistValidationError("InvalidPlayerUid")
        uid_value = _case_insensitive_member(player, "Uid")
        if not isinstance(uid_value, str):
            raise BlacklistValidationError("InvalidPlayerUid")
        uid = uid_value
        if UID_PATTERN.fullmatch(uid) is None or uid in seen_uids:
            raise BlacklistValidationError("InvalidPlayerUid")
        seen_uids.add(uid)

        note_value = _case_insensitive_member(player, "Note")
        if not isinstance(note_value, str):
            raise BlacklistValidationError("InvalidNote")
        note = note_value
        if note and _utf16_length(note) > MAX_NOTE_UTF16_UNITS:
            raise BlacklistValidationError("InvalidNote")

        aliases_value = _case_insensitive_member(player, "Aliases")
        if not isinstance(aliases_value, list):
            raise BlacklistValidationError("InvalidAlias")
        aliases = aliases_value
        if len(aliases) > MAX_ALIASES:
            raise BlacklistValidationError("TooManyAliases")
        for alias_value in aliases:
            if not isinstance(alias_value, str):
                raise BlacklistValidationError("InvalidAlias")
            alias = alias_value
            if not alias.strip() or _utf16_length(alias) > MAX_ALIAS_UTF16_UNITS:
                raise BlacklistValidationError("InvalidAlias")

        created = _case_insensitive_member(player, "CreatedAt")
        updated = _case_insensitive_member(player, "UpdatedAt")
        if not isinstance(created, str) or not isinstance(updated, str):
            raise BlacklistValidationError("InvalidDateTime")
        _parse_datetime_offset(created)
        _parse_datetime_offset(updated)

    for item in deleted:
        if not isinstance(item, Mapping):
            raise BlacklistValidationError("InvalidDeletedUid")
        uid_value = _case_insensitive_member(item, "Uid")
        if not isinstance(uid_value, str):
            raise BlacklistValidationError("InvalidDeletedUid")
        uid = uid_value
        if UID_PATTERN.fullmatch(uid) is None:
            raise BlacklistValidationError("InvalidDeletedUid")
        deleted_at = _case_insensitive_member(item, "DeletedAt")
        if not isinstance(deleted_at, str):
            raise BlacklistValidationError("InvalidDateTime")
        _parse_datetime_offset(deleted_at)

    return document


def parse_and_validate_blacklist(body: bytes) -> Mapping[str, Any]:
    if len(body) > MAX_UPLOAD_BYTES:
        raise BlacklistValidationError("UploadTooLarge")
    try:
        text = body.decode("utf-8", errors="strict")
    except UnicodeDecodeError as exc:
        raise BlacklistValidationError("InvalidUtf8") from exc
    try:
        document = json.loads(
            text,
            object_pairs_hook=_strict_object,
            parse_constant=_reject_json_constant,
        )
    except (json.JSONDecodeError, RecursionError, BlacklistValidationError) as exc:
        raise BlacklistValidationError("InvalidJson") from exc
    return validate_blacklist_document(document)


class ExclusiveFileLock:
    """Cross-process exclusive lock; fcntl is used on the Linux server."""

    def __init__(self, path: Path, timeout_seconds: float) -> None:
        self._path = Path(path)
        self._timeout_seconds = timeout_seconds
        self._descriptor: int | None = None

    def __enter__(self) -> "ExclusiveFileLock":
        self._path.parent.mkdir(parents=True, exist_ok=True)
        flags = os.O_CREAT | os.O_RDWR
        if hasattr(os, "O_CLOEXEC"):
            flags |= os.O_CLOEXEC
        if hasattr(os, "O_NOFOLLOW"):
            flags |= os.O_NOFOLLOW
        descriptor = os.open(self._path, flags, 0o640)
        self._descriptor = descriptor
        metadata = os.fstat(descriptor)
        if not stat.S_ISREG(metadata.st_mode):
            os.close(descriptor)
            self._descriptor = None
            raise RuntimeError(f"Lock path is not a regular file: {self._path}")
        if os.name == "posix":
            os.fchmod(descriptor, 0o660)
        if msvcrt is not None and os.name == "nt":
            if os.fstat(descriptor).st_size == 0:
                os.write(descriptor, b"\0")
                os.fsync(descriptor)
            os.lseek(descriptor, 0, os.SEEK_SET)

        deadline = time.monotonic() + self._timeout_seconds
        while True:
            try:
                if fcntl is not None:
                    fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
                elif msvcrt is not None:
                    os.lseek(descriptor, 0, os.SEEK_SET)
                    msvcrt.locking(descriptor, msvcrt.LK_NBLCK, 1)
                else:  # pragma: no cover - supported platforms have one backend.
                    raise RuntimeError("No file-lock implementation is available.")
                return self
            except (BlockingIOError, OSError):
                if time.monotonic() >= deadline:
                    os.close(descriptor)
                    self._descriptor = None
                    raise LockTimeoutError(f"Timed out waiting for {self._path}")
                time.sleep(0.05)

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        descriptor = self._descriptor
        self._descriptor = None
        if descriptor is None:
            return
        try:
            if fcntl is not None:
                fcntl.flock(descriptor, fcntl.LOCK_UN)
            elif msvcrt is not None:
                os.lseek(descriptor, 0, os.SEEK_SET)
                msvcrt.locking(descriptor, msvcrt.LK_UNLCK, 1)
        finally:
            os.close(descriptor)


def _fsync_directory(directory: Path) -> None:
    if os.name != "posix":
        return
    flags = os.O_RDONLY
    if hasattr(os, "O_DIRECTORY"):
        flags |= os.O_DIRECTORY
    descriptor = os.open(directory, flags)
    try:
        os.fsync(descriptor)
    finally:
        os.close(descriptor)


def atomic_write_bytes(path: Path, payload: bytes, mode: int = 0o644) -> None:
    path = Path(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.tmp-", dir=path.parent
    )
    temporary_path = Path(temporary_name)
    try:
        if hasattr(os, "fchmod"):
            os.fchmod(descriptor, mode)
        with os.fdopen(descriptor, "wb", closefd=True) as output:
            descriptor = -1
            output.write(payload)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary_path, path)
        _fsync_directory(path.parent)
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        with contextlib.suppress(FileNotFoundError):
            temporary_path.unlink()


def backup_blacklist(data_path: Path, backup_directory: Path, keep: int = 30) -> Path:
    data_path = Path(data_path)
    backup_directory = Path(backup_directory)
    backup_directory.mkdir(parents=True, exist_ok=True)
    now = dt.datetime.now(dt.timezone.utc)
    timestamp = now.strftime("%Y%m%d-%H%M%S") + f"{now.microsecond // 1000:03d}"
    backup_path = backup_directory / f"blacklist-{timestamp}.json"
    with data_path.open("rb") as source:
        atomic_write_bytes(backup_path, source.read(), mode=0o640)

    backups = sorted(
        (item for item in backup_directory.glob("blacklist-*.json") if item.is_file()),
        key=lambda item: (item.stat().st_mtime_ns, item.name),
        reverse=True,
    )
    for old_backup in backups[keep:]:
        old_backup.unlink()
    return backup_path


class OpenSslSigner:
    """RSA PKCS#1 v1.5 / SHA-256 signer compatible with .NET SignData."""

    def __init__(
        self,
        private_key_path: Path,
        openssl_path: Path | str = "/usr/bin/openssl",
        timeout_seconds: float = 15.0,
    ) -> None:
        self._private_key_path = Path(private_key_path)
        self._openssl_path = str(openssl_path)
        self._timeout_seconds = timeout_seconds

    def sign(self, payload: bytes) -> bytes:
        if self._private_key_path.is_symlink() or not self._private_key_path.is_file():
            raise SigningError("The signing key is unavailable.")
        try:
            result = subprocess.run(
                [
                    self._openssl_path,
                    "dgst",
                    "-sha256",
                    "-sign",
                    str(self._private_key_path),
                ],
                input=payload,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
                timeout=self._timeout_seconds,
                shell=False,
                close_fds=True,
            )
        except (OSError, subprocess.SubprocessError) as exc:
            raise SigningError("OpenSSL signing failed.") from exc
        if result.returncode != 0 or not result.stdout:
            raise SigningError("OpenSSL signing failed.")
        return result.stdout


def publish_signed_blacklist(
    data_path: Path,
    signature_path: Path,
    body: bytes,
    signer: Callable[[bytes], bytes],
) -> None:
    raw_signature = signer(body)
    if not raw_signature:
        raise SigningError("The signer returned an empty signature.")
    signature_text = base64.b64encode(raw_signature)
    # This order matches the current Windows service.  Both files are individually
    # atomic, and the signature is fully prepared before either file is replaced.
    atomic_write_bytes(data_path, body, mode=0o644)
    atomic_write_bytes(signature_path, signature_text, mode=0o644)


def repair_blacklist_signature(
    data_path: Path,
    signature_path: Path,
    lock_path: Path,
    signer: Callable[[bytes], bytes],
) -> None:
    """Validate and re-sign authoritative bytes during service startup.

    This repairs a fail-closed JSON/signature mismatch if the host previously
    stopped between the two individually atomic file replacements.
    """

    with ExclusiveFileLock(lock_path, timeout_seconds=30.0):
        body = Path(data_path).read_bytes()
        parse_and_validate_blacklist(body)
        raw_signature = signer(body)
        if not raw_signature:
            raise SigningError("The signer returned an empty signature.")
        atomic_write_bytes(
            signature_path, base64.b64encode(raw_signature), mode=0o644
        )


class AuditLog:
    """Small ASCII audit log that never records request bodies or credentials."""

    def __init__(self, path: Path) -> None:
        self._path = Path(path)
        self._thread_lock = threading.Lock()

    def write(self, message: str) -> None:
        safe_message = message.replace("\r", " ").replace("\n", " ")
        timestamp = dt.datetime.now(dt.timezone.utc).isoformat(timespec="microseconds")
        line = f"{timestamp} {safe_message}\n".encode("ascii", errors="replace")
        with self._thread_lock:
            self._path.parent.mkdir(parents=True, exist_ok=True)
            descriptor = os.open(
                self._path, os.O_CREAT | os.O_APPEND | os.O_WRONLY, 0o640
            )
            try:
                os.write(descriptor, line)
            finally:
                os.close(descriptor)
