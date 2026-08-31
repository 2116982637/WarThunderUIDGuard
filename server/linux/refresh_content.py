#!/usr/bin/env python3
"""Refresh and sign WarThunderUIDGuard public content on Linux.

This module intentionally uses only the Python standard library. RSA signing is
delegated to the system OpenSSL executable because Python's standard library
does not provide private-key RSA operations.
"""

from __future__ import annotations

import argparse
import base64
import contextlib
import dataclasses
import datetime as dt
import hashlib
import hmac
import json
import logging
import os
from pathlib import Path
import re
import stat
import subprocess
import tempfile
import time
from typing import Any, BinaryIO, Callable, ContextManager, Iterable, Mapping, Protocol
import urllib.parse
import urllib.request

try:
    import fcntl
except ImportError:  # pragma: no cover - exercised only on non-POSIX hosts.
    fcntl = None  # type: ignore[assignment]


LOGGER = logging.getLogger("warthunder_uid_guard.refresh")

MIB = 1024 * 1024
BLACKLIST_LIMIT = 1 * MIB
RELEASE_METADATA_LIMIT = 1 * MIB
CHECKSUM_LIMIT = 16 * 1024
ARCHIVE_LIMIT = 300 * MIB
SIGNATURE_LIMIT = 16 * 1024
MAX_PLAYERS = 50_000
MAX_DELETED_PLAYERS = 50_000
MAX_ALIASES = 50
MAX_NOTE_UTF16_UNITS = 500
MAX_ALIAS_UTF16_UNITS = 100

REPOSITORY = "elainasamae/WarThunderUIDGuard"
GITHUB_LATEST_RELEASE_URL = (
    f"https://api.github.com/repos/{REPOSITORY}/releases/latest"
)
DEFAULT_BOOTSTRAP_URLS = (
    "https://gcore.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json",
    "https://fastly.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json",
    "https://cdn.jsdelivr.net/gh/elainasamae/WarThunderUIDGuard@main/data/blacklist.json",
    "https://raw.githubusercontent.com/elainasamae/WarThunderUIDGuard/main/data/blacklist.json",
)

TAG_PATTERN = re.compile(r"^v\d+\.\d+\.\d+$", re.ASCII)
SHA256_PATTERN = re.compile(r"^[0-9a-fA-F]{64}$", re.ASCII)
UID_PATTERN = re.compile(r"^\d{1,20}$", re.ASCII)


class RefreshError(RuntimeError):
    """A safe, user-facing refresh failure."""


class ContentTooLargeError(RefreshError):
    """A remote or local content item exceeded its configured limit."""


class LockTimeoutError(RefreshError):
    """The shared data lock could not be acquired in time."""


@dataclasses.dataclass(frozen=True)
class Settings:
    """Runtime paths and endpoints.

    The first three defaults are shared with the Linux administrator-upload
    service. Changing one side without changing the other breaks serialization.
    """

    www_root: Path = Path("/srv/wtuidguard/www")
    signing_key: Path = Path("/etc/wtuidguard/secrets/signing-private.pem")
    lock_file: Path = Path("/var/lib/wtuidguard/data.lock")
    github_api_url: str = GITHUB_LATEST_RELEASE_URL
    bootstrap_urls: tuple[str, ...] = DEFAULT_BOOTSTRAP_URLS
    openssl_path: Path = Path("/usr/bin/openssl")
    network_timeout_seconds: float = 30.0
    lock_timeout_seconds: float = 60.0
    user_agent: str = "WarThunderUIDGuardSync/3-linux"

    @property
    def updates_root(self) -> Path:
        return self.www_root / "updates"


@dataclasses.dataclass(frozen=True)
class ReleaseInfo:
    tag: str
    archive_name: str
    archive_url: str
    checksum_name: str
    checksum_url: str | None
    digest: str | None
    declared_size: int | None
    published_at: str


class Signer(Protocol):
    def sign(self, payload: bytes) -> bytes:
        """Return a raw RSA signature for payload."""


class Transport(Protocol):
    def read_bytes(
        self,
        url: str,
        maximum_bytes: int,
        *,
        accept: str | None = None,
        final_url_validator: Callable[[str], bool] | None = None,
    ) -> bytes:
        """Fetch a bounded response into memory."""

    def download_verified(
        self,
        url: str,
        destination: Path,
        maximum_bytes: int,
        expected_sha256: str,
        *,
        final_url_validator: Callable[[str], bool] | None = None,
    ) -> int:
        """Atomically publish a bounded download only after hash verification."""


def _fsync_directory(path: Path) -> None:
    """Best-effort directory fsync for durable rename/link publication."""

    flags = os.O_RDONLY
    if hasattr(os, "O_DIRECTORY"):
        flags |= os.O_DIRECTORY
    try:
        descriptor = os.open(path, flags)
    except OSError:
        return
    try:
        os.fsync(descriptor)
    except OSError:
        pass
    finally:
        os.close(descriptor)


def atomic_write(path: Path, payload: bytes, mode: int = 0o644) -> None:
    """Durably replace path with payload using a same-directory temporary."""

    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".tmp", dir=path.parent
    )
    temporary = Path(temporary_name)
    try:
        if hasattr(os, "fchmod"):
            os.fchmod(descriptor, mode)
        else:  # Allows the Linux-targeted logic to be unit-tested on Windows.
            os.chmod(temporary, mode)
        with os.fdopen(descriptor, "wb", closefd=True) as output:
            descriptor = -1
            output.write(payload)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
        _fsync_directory(path.parent)
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


def atomic_create_if_absent(
    path: Path, payload: bytes, mode: int = 0o644
) -> bool:
    """Atomically create path without ever replacing an existing directory entry.

    A hard link publishes a fully written same-filesystem temporary. Unlike
    os.replace(), link creation fails if another process has already created the
    authoritative blacklist.
    """

    path.parent.mkdir(parents=True, exist_ok=True)
    descriptor, temporary_name = tempfile.mkstemp(
        prefix=f".{path.name}.", suffix=".bootstrap", dir=path.parent
    )
    temporary = Path(temporary_name)
    try:
        if hasattr(os, "fchmod"):
            os.fchmod(descriptor, mode)
        else:  # Allows the Linux-targeted logic to be unit-tested on Windows.
            os.chmod(temporary, mode)
        with os.fdopen(descriptor, "wb", closefd=True) as output:
            descriptor = -1
            output.write(payload)
            output.flush()
            os.fsync(output.fileno())
        try:
            os.link(temporary, path)
        except FileExistsError:
            return False
        _fsync_directory(path.parent)
        return True
    finally:
        if descriptor >= 0:
            os.close(descriptor)
        try:
            temporary.unlink()
        except FileNotFoundError:
            pass


def read_regular_file(path: Path, maximum_bytes: int) -> bytes:
    """Read a bounded regular file without following a final symlink on Linux."""

    flags = os.O_RDONLY
    if hasattr(os, "O_CLOEXEC"):
        flags |= os.O_CLOEXEC
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    try:
        descriptor = os.open(path, flags)
    except OSError as error:
        raise RefreshError(f"Cannot safely open {path}: {error}") from error
    try:
        metadata = os.fstat(descriptor)
        if not stat.S_ISREG(metadata.st_mode):
            raise RefreshError(f"Expected a regular file: {path}")
        if metadata.st_size <= 0 or metadata.st_size > maximum_bytes:
            raise ContentTooLargeError(f"File size is invalid: {path}")

        chunks: list[bytes] = []
        total = 0
        while True:
            chunk = os.read(descriptor, min(64 * 1024, maximum_bytes - total + 1))
            if not chunk:
                break
            total += len(chunk)
            if total > maximum_bytes:
                raise ContentTooLargeError(f"File is too large: {path}")
            chunks.append(chunk)
        if total == 0:
            raise RefreshError(f"File is empty: {path}")
        return b"".join(chunks)
    finally:
        os.close(descriptor)


@contextlib.contextmanager
def exclusive_flock(path: Path, timeout_seconds: float) -> Iterable[None]:
    """Acquire the upload/refresh shared advisory lock with a bounded wait."""

    if fcntl is None:
        raise RefreshError("fcntl.flock is required; this program runs on Linux.")
    if timeout_seconds < 0:
        raise ValueError("Lock timeout must not be negative.")

    path.parent.mkdir(parents=True, exist_ok=True)
    flags = os.O_RDWR | os.O_CREAT
    if hasattr(os, "O_CLOEXEC"):
        flags |= os.O_CLOEXEC
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    descriptor = os.open(path, flags, 0o660)
    try:
        if not stat.S_ISREG(os.fstat(descriptor).st_mode):
            raise RefreshError(f"Shared lock is not a regular file: {path}")
        os.fchmod(descriptor, 0o660)
        deadline = time.monotonic() + timeout_seconds
        while True:
            try:
                fcntl.flock(descriptor, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except BlockingIOError as error:
                remaining = deadline - time.monotonic()
                if remaining <= 0:
                    raise LockTimeoutError(
                        f"Timed out waiting for the shared data lock: {path}"
                    ) from error
                time.sleep(min(0.1, remaining))
        try:
            yield
        finally:
            fcntl.flock(descriptor, fcntl.LOCK_UN)
    finally:
        os.close(descriptor)


class OpenSslSigner:
    """RSA PKCS#1 v1.5 with SHA-256 via a fixed OpenSSL argument vector."""

    def __init__(
        self,
        openssl_path: Path,
        private_key_path: Path,
        *,
        runner: Callable[..., subprocess.CompletedProcess[bytes]] = subprocess.run,
    ) -> None:
        self._openssl_path = openssl_path
        self._private_key_path = private_key_path
        self._runner = runner

    def sign(self, payload: bytes) -> bytes:
        if not self._private_key_path.is_file():
            raise RefreshError(
                f"RSA signing key is missing: {self._private_key_path}"
            )
        try:
            result = self._runner(
                [
                    os.fspath(self._openssl_path),
                    "dgst",
                    "-sha256",
                    "-sign",
                    os.fspath(self._private_key_path),
                ],
                input=payload,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
                timeout=30,
            )
        except (OSError, subprocess.SubprocessError) as error:
            raise RefreshError(f"OpenSSL signing failed: {error}") from error
        if result.returncode != 0:
            detail = result.stderr.decode("utf-8", errors="replace").strip()
            raise RefreshError(
                "OpenSSL signing failed"
                + (f": {detail}" if detail else ".")
            )
        if not result.stdout or len(result.stdout) > SIGNATURE_LIMIT:
            raise RefreshError("OpenSSL returned an invalid RSA signature size.")
        return result.stdout


class HttpTransport:
    """Bounded urllib transport with atomic, hash-first archive publication."""

    def __init__(
        self,
        timeout_seconds: float,
        user_agent: str,
        *,
        opener: Callable[..., ContextManager[BinaryIO]] = urllib.request.urlopen,
    ) -> None:
        self._timeout_seconds = timeout_seconds
        self._user_agent = user_agent
        self._opener = opener

    def _request(self, url: str, accept: str | None) -> urllib.request.Request:
        headers = {"User-Agent": self._user_agent}
        if accept is not None:
            headers["Accept"] = accept
        return urllib.request.Request(url, headers=headers, method="GET")

    @staticmethod
    def _check_content_length(response: BinaryIO, maximum_bytes: int) -> None:
        headers = getattr(response, "headers", None)
        value = headers.get("Content-Length") if headers is not None else None
        if value is None:
            return
        try:
            declared = int(value)
        except (TypeError, ValueError) as error:
            raise RefreshError("The server returned an invalid Content-Length.") from error
        if declared <= 0 or declared > maximum_bytes:
            raise ContentTooLargeError("The remote content size is invalid.")

    @staticmethod
    def _validate_final_url(
        response: BinaryIO,
        original_url: str,
        validator: Callable[[str], bool] | None,
    ) -> None:
        if validator is None:
            return
        geturl = getattr(response, "geturl", None)
        final_url = geturl() if callable(geturl) else original_url
        if not validator(str(final_url)):
            raise RefreshError("The remote server redirected to an unapproved URL.")

    def read_bytes(
        self,
        url: str,
        maximum_bytes: int,
        *,
        accept: str | None = None,
        final_url_validator: Callable[[str], bool] | None = None,
    ) -> bytes:
        request = self._request(url, accept)
        with self._opener(request, timeout=self._timeout_seconds) as response:
            self._validate_final_url(response, url, final_url_validator)
            self._check_content_length(response, maximum_bytes)
            output = bytearray()
            while True:
                chunk = response.read(min(64 * 1024, maximum_bytes - len(output) + 1))
                if not chunk:
                    break
                output.extend(chunk)
                if len(output) > maximum_bytes:
                    raise ContentTooLargeError("The remote content is too large.")
            if not output:
                raise RefreshError("The remote content is empty.")
            return bytes(output)

    def download_verified(
        self,
        url: str,
        destination: Path,
        maximum_bytes: int,
        expected_sha256: str,
        *,
        final_url_validator: Callable[[str], bool] | None = None,
    ) -> int:
        if not SHA256_PATTERN.fullmatch(expected_sha256):
            raise ValueError("Expected SHA-256 is malformed.")
        destination.parent.mkdir(parents=True, exist_ok=True)
        descriptor, temporary_name = tempfile.mkstemp(
            prefix=f".{destination.name}.", suffix=".download", dir=destination.parent
        )
        temporary = Path(temporary_name)
        try:
            if hasattr(os, "fchmod"):
                os.fchmod(descriptor, 0o644)
            else:  # Allows the Linux-targeted logic to be unit-tested on Windows.
                os.chmod(temporary, 0o644)
            request = self._request(url, "application/octet-stream")
            digest = hashlib.sha256()
            total = 0
            with os.fdopen(descriptor, "wb", closefd=True) as output:
                descriptor = -1
                with self._opener(request, timeout=self._timeout_seconds) as response:
                    self._validate_final_url(
                        response, url, final_url_validator
                    )
                    self._check_content_length(response, maximum_bytes)
                    while True:
                        chunk = response.read(
                            min(1024 * 1024, maximum_bytes - total + 1)
                        )
                        if not chunk:
                            break
                        total += len(chunk)
                        if total > maximum_bytes:
                            raise ContentTooLargeError(
                                "The release archive is too large."
                            )
                        digest.update(chunk)
                        output.write(chunk)
                if total == 0:
                    raise RefreshError("The release archive is empty.")
                output.flush()
                os.fsync(output.fileno())

            actual_sha256 = digest.hexdigest().upper()
            if not hmac.compare_digest(
                actual_sha256, expected_sha256.upper()
            ):
                raise RefreshError(
                    "The downloaded release archive checksum does not match."
                )
            os.replace(temporary, destination)
            _fsync_directory(destination.parent)
            return total
        finally:
            if descriptor >= 0:
                os.close(descriptor)
            try:
                temporary.unlink()
            except FileNotFoundError:
                pass


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
    return value if isinstance(value, list) else [value]


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
    raise RefreshError(f"JSON contains an invalid constant: {value}")


def _strict_json_object(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
    result: dict[str, Any] = {}
    seen: set[str] = set()
    for key, value in pairs:
        folded = key.casefold()
        if folded in seen:
            raise RefreshError("JSON contains duplicate properties.")
        seen.add(folded)
        result[key] = value
    return result


def _validate_datetime_offset(value: Any) -> None:
    text = _dotnet_string(value).strip()
    if not text:
        raise RefreshError("Blacklist contains an invalid date/time.")
    candidate = text[:-1] + "+00:00" if text.endswith(("Z", "z")) else text
    candidate = re.sub(
        r"(?<=\.)(\d{6})\d+(?=(?:[+-]\d{2}:?\d{2})?$)",
        r"\1",
        candidate,
    )
    try:
        dt.datetime.fromisoformat(candidate)
    except ValueError as error:
        raise RefreshError("Blacklist contains an invalid date/time.") from error


def validate_blacklist(payload: bytes) -> None:
    """Apply the upload service's complete schema-v2 validation to raw bytes."""

    if not payload or len(payload) > BLACKLIST_LIMIT:
        raise ContentTooLargeError("Blacklist size is invalid.")
    try:
        document = json.loads(
            payload.decode("utf-8-sig"),
            object_pairs_hook=_strict_json_object,
            parse_constant=_reject_json_constant,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, RecursionError, RefreshError) as error:
        raise RefreshError("Blacklist JSON is invalid.") from error
    if not isinstance(document, Mapping):
        raise RefreshError("Blacklist JSON schema is invalid.")
    schema = _case_insensitive_member(document, "SchemaVersion")
    if isinstance(schema, bool) or not isinstance(schema, int) or schema != 2:
        raise RefreshError("Blacklist JSON schema is invalid.")
    language = _case_insensitive_member(document, "Language")
    sync_enabled = _case_insensitive_member(document, "OneDriveSyncEnabled")
    if not isinstance(language, str) or not isinstance(sync_enabled, bool):
        raise RefreshError("Blacklist JSON schema is invalid.")

    players_value = _case_insensitive_member(document, "Players")
    deleted_value = _case_insensitive_member(document, "DeletedPlayers")
    if not isinstance(players_value, list) or not isinstance(deleted_value, list):
        raise RefreshError("Blacklist arrays are invalid.")
    players = players_value
    deleted = deleted_value
    if len(players) > MAX_PLAYERS or len(deleted) > MAX_DELETED_PLAYERS:
        raise RefreshError("Blacklist has too many records.")

    seen_uids: set[str] = set()
    for player in players:
        if not isinstance(player, Mapping):
            raise RefreshError("Blacklist contains an invalid player UID.")
        uid_value = _case_insensitive_member(player, "Uid")
        if not isinstance(uid_value, str):
            raise RefreshError("Blacklist contains an invalid player UID.")
        uid = uid_value
        if UID_PATTERN.fullmatch(uid) is None or uid in seen_uids:
            raise RefreshError("Blacklist contains an invalid player UID.")
        seen_uids.add(uid)

        note_value = _case_insensitive_member(player, "Note")
        if not isinstance(note_value, str):
            raise RefreshError("Blacklist contains an invalid note.")
        note = note_value
        if note and _utf16_length(note) > MAX_NOTE_UTF16_UNITS:
            raise RefreshError("Blacklist contains an invalid note.")
        aliases_value = _case_insensitive_member(player, "Aliases")
        if not isinstance(aliases_value, list):
            raise RefreshError("Blacklist contains invalid aliases.")
        aliases = aliases_value
        if len(aliases) > MAX_ALIASES:
            raise RefreshError("Blacklist contains too many aliases.")
        for alias_value in aliases:
            if not isinstance(alias_value, str):
                raise RefreshError("Blacklist contains an invalid alias.")
            alias = alias_value
            if not alias.strip() or _utf16_length(alias) > MAX_ALIAS_UTF16_UNITS:
                raise RefreshError("Blacklist contains an invalid alias.")
        created = _case_insensitive_member(player, "CreatedAt")
        updated = _case_insensitive_member(player, "UpdatedAt")
        if not isinstance(created, str) or not isinstance(updated, str):
            raise RefreshError("Blacklist contains an invalid date/time.")
        _validate_datetime_offset(created)
        _validate_datetime_offset(updated)

    for item in deleted:
        if not isinstance(item, Mapping):
            raise RefreshError("Blacklist contains an invalid deleted UID.")
        uid_value = _case_insensitive_member(item, "Uid")
        if not isinstance(uid_value, str):
            raise RefreshError("Blacklist contains an invalid deleted UID.")
        uid = uid_value
        if UID_PATTERN.fullmatch(uid) is None:
            raise RefreshError("Blacklist contains an invalid deleted UID.")
        deleted_at = _case_insensitive_member(item, "DeletedAt")
        if not isinstance(deleted_at, str):
            raise RefreshError("Blacklist contains an invalid date/time.")
        _validate_datetime_offset(deleted_at)


def parse_checksum(payload: bytes, expected_file_name: str) -> str:
    try:
        text = payload.decode("utf-8-sig")
    except UnicodeDecodeError as error:
        raise RefreshError("GitHub release checksum is not UTF-8.") from error
    pattern = re.compile(
        rf"^\s*([0-9a-fA-F]{{64}})\s+\*?{re.escape(expected_file_name)}\s*$",
        re.MULTILINE | re.ASCII,
    )
    matches = [match.group(1).upper() for match in pattern.finditer(text)]
    if not matches or any(value != matches[0] for value in matches[1:]):
        raise RefreshError("GitHub release checksum is invalid.")
    return matches[0]


def semantic_version(tag: str) -> tuple[int, int, int]:
    if TAG_PATTERN.fullmatch(tag) is None:
        raise RefreshError("Release tag is not a stable semantic version.")
    major, minor, patch = tag[1:].split(".")
    return int(major), int(minor), int(patch)


def read_current_release_state(updates_root: Path) -> tuple[str, str] | None:
    metadata_path = updates_root / "latest.json"
    if not metadata_path.exists():
        return None
    payload = read_regular_file(metadata_path, RELEASE_METADATA_LIMIT)
    try:
        document = json.loads(
            payload.decode("utf-8"),
            object_pairs_hook=_strict_json_object,
            parse_constant=_reject_json_constant,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, RefreshError) as error:
        raise RefreshError("Existing release metadata is invalid.") from error
    if not isinstance(document, Mapping):
        raise RefreshError("Existing release metadata is invalid.")
    tag = document.get("tag")
    digest = document.get("sha256")
    if (
        not isinstance(tag, str)
        or TAG_PATTERN.fullmatch(tag) is None
        or not isinstance(digest, str)
        or SHA256_PATTERN.fullmatch(digest) is None
    ):
        raise RefreshError("Existing release metadata is invalid.")
    return tag, digest.upper()


def _url_has_no_credentials(parsed: urllib.parse.SplitResult) -> bool:
    return parsed.username is None and parsed.password is None


def is_exact_github_api_url(url: str) -> bool:
    parsed = urllib.parse.urlsplit(url)
    return (
        parsed.scheme == "https"
        and parsed.hostname == "api.github.com"
        and parsed.port in (None, 443)
        and _url_has_no_credentials(parsed)
        and parsed.path == f"/repos/{REPOSITORY}/releases/latest"
        and not parsed.query
        and not parsed.fragment
    )


def is_release_asset_url(url: str, tag: str, file_name: str) -> bool:
    parsed = urllib.parse.urlsplit(url)
    return (
        parsed.scheme == "https"
        and parsed.hostname == "github.com"
        and parsed.port in (None, 443)
        and _url_has_no_credentials(parsed)
        and parsed.path == f"/{REPOSITORY}/releases/download/{tag}/{file_name}"
        and not parsed.query
        and not parsed.fragment
    )


def is_allowed_release_redirect(
    url: str, tag: str, file_name: str
) -> bool:
    if is_release_asset_url(url, tag, file_name):
        return True
    parsed = urllib.parse.urlsplit(url)
    return (
        parsed.scheme == "https"
        and parsed.hostname
        in {
            "release-assets.githubusercontent.com",
            "objects.githubusercontent.com",
            "github-releases.githubusercontent.com",
        }
        and parsed.port in (None, 443)
        and _url_has_no_credentials(parsed)
        and not parsed.fragment
    )


def parse_release(payload: bytes) -> ReleaseInfo:
    try:
        document = json.loads(
            payload.decode("utf-8-sig"),
            object_pairs_hook=_strict_json_object,
            parse_constant=_reject_json_constant,
        )
    except (UnicodeDecodeError, json.JSONDecodeError, RefreshError) as error:
        raise RefreshError("GitHub release metadata is invalid JSON.") from error
    if not isinstance(document, dict):
        raise RefreshError("GitHub release metadata is not an object.")
    if document.get("draft") is not False or document.get("prerelease") is not False:
        raise RefreshError("GitHub latest release is not stable.")

    tag = document.get("tag_name")
    if not isinstance(tag, str) or TAG_PATTERN.fullmatch(tag) is None:
        raise RefreshError("GitHub latest release tag is not a stable semantic version.")
    published_at = document.get("published_at")
    if not isinstance(published_at, str) or not published_at.strip():
        raise RefreshError("GitHub latest release has no publication time.")

    archive_name = f"WarThunderUIDGuard-{tag}-win-x64.zip"
    checksum_name = archive_name + ".sha256.txt"
    assets = document.get("assets")
    if not isinstance(assets, list):
        raise RefreshError("GitHub latest release has no assets array.")
    archive_assets = [
        asset
        for asset in assets
        if isinstance(asset, dict) and asset.get("name") == archive_name
    ]
    checksum_assets = [
        asset
        for asset in assets
        if isinstance(asset, dict) and asset.get("name") == checksum_name
    ]
    if len(archive_assets) != 1 or len(checksum_assets) > 1:
        raise RefreshError("GitHub release assets are missing or ambiguous.")

    archive_asset = archive_assets[0]
    archive_url = archive_asset.get("browser_download_url")
    if not isinstance(archive_url, str) or not is_release_asset_url(
        archive_url, tag, archive_name
    ):
        raise RefreshError("GitHub release archive URL is not approved.")

    checksum_url: str | None = None
    if checksum_assets:
        value = checksum_assets[0].get("browser_download_url")
        if not isinstance(value, str) or not is_release_asset_url(
            value, tag, checksum_name
        ):
            raise RefreshError("GitHub release checksum URL is not approved.")
        checksum_url = value

    raw_digest = archive_asset.get("digest")
    digest: str | None = None
    if isinstance(raw_digest, str):
        match = re.fullmatch(r"sha256:([0-9a-fA-F]{64})", raw_digest, re.ASCII)
        if match is not None:
            digest = match.group(1).upper()
    if digest is None and checksum_url is None:
        raise RefreshError("GitHub release checksum is unavailable.")

    raw_size = archive_asset.get("size")
    declared_size: int | None = None
    if raw_size is not None:
        if isinstance(raw_size, bool) or not isinstance(raw_size, int):
            raise RefreshError("GitHub release archive size is invalid.")
        if raw_size <= 0 or raw_size > ARCHIVE_LIMIT:
            raise ContentTooLargeError("GitHub release archive size is invalid.")
        declared_size = raw_size

    return ReleaseInfo(
        tag=tag,
        archive_name=archive_name,
        archive_url=archive_url,
        checksum_name=checksum_name,
        checksum_url=checksum_url,
        digest=digest,
        declared_size=declared_size,
        published_at=published_at,
    )


def existing_file_digest(path: Path, maximum_bytes: int) -> tuple[str, int] | None:
    """Return a safe regular file's SHA-256 and length, or None if unusable."""

    flags = os.O_RDONLY
    if hasattr(os, "O_CLOEXEC"):
        flags |= os.O_CLOEXEC
    if hasattr(os, "O_NOFOLLOW"):
        flags |= os.O_NOFOLLOW
    if hasattr(os, "O_BINARY"):
        flags |= os.O_BINARY
    try:
        descriptor = os.open(path, flags)
    except OSError:
        return None
    try:
        metadata = os.fstat(descriptor)
        if (
            not stat.S_ISREG(metadata.st_mode)
            or metadata.st_size <= 0
            or metadata.st_size > maximum_bytes
        ):
            return None
        digest = hashlib.sha256()
        total = 0
        while True:
            chunk = os.read(descriptor, 1024 * 1024)
            if not chunk:
                break
            total += len(chunk)
            if total > maximum_bytes:
                return None
            digest.update(chunk)
        return digest.hexdigest().upper(), total
    finally:
        os.close(descriptor)


class LockFactory(Protocol):
    def __call__(self, path: Path, timeout_seconds: float) -> ContextManager[None]:
        """Return a context manager for the shared data lock."""


class ContentRefresher:
    def __init__(
        self,
        settings: Settings,
        transport: Transport,
        signer: Signer,
        *,
        lock_factory: LockFactory = exclusive_flock,
    ) -> None:
        self._settings = settings
        self._transport = transport
        self._signer = signer
        self._lock_factory = lock_factory

    def _publish_signature(self, payload: bytes, destination: Path) -> None:
        raw_signature = self._signer.sign(payload)
        if not raw_signature or len(raw_signature) > SIGNATURE_LIMIT:
            raise RefreshError("RSA signer returned an invalid signature size.")
        atomic_write(destination, base64.b64encode(raw_signature), 0o644)

    def _download_bootstrap(self) -> bytes:
        failures: list[str] = []
        for url in self._settings.bootstrap_urls:
            try:
                payload = self._transport.read_bytes(url, BLACKLIST_LIMIT)
                validate_blacklist(payload)
                return payload
            except Exception as error:  # Try every fixed public bootstrap source.
                failures.append(f"{url}: {error}")
        raise RefreshError(
            "All public blacklist bootstrap sources failed: " + "; ".join(failures)
        )

    def refresh_blacklist(self) -> None:
        """Initialize only if absent, then re-sign exact authoritative bytes."""

        self._settings.www_root.mkdir(parents=True, exist_ok=True)
        os.chmod(self._settings.www_root, 0o755)
        blacklist_path = self._settings.www_root / "blacklist.json"
        signature_path = self._settings.www_root / "blacklist.sig"
        with self._lock_factory(
            self._settings.lock_file, self._settings.lock_timeout_seconds
        ):
            if not blacklist_path.exists():
                bootstrap = self._download_bootstrap()
                created = atomic_create_if_absent(blacklist_path, bootstrap, 0o644)
                if created:
                    LOGGER.info("Initialized missing blacklist from a public mirror.")
            payload = read_regular_file(blacklist_path, BLACKLIST_LIMIT)
            validate_blacklist(payload)
            os.chmod(blacklist_path, 0o644)
            self._publish_signature(payload, signature_path)
            LOGGER.info("Re-signed authoritative blacklist bytes.")

    def _resolve_checksum(self, release: ReleaseInfo) -> tuple[str, bytes]:
        if release.digest is not None:
            text = f"{release.digest}  {release.archive_name}".encode("ascii")
            return release.digest, text
        if release.checksum_url is None:
            raise RefreshError("GitHub release checksum is unavailable.")
        payload = self._transport.read_bytes(
            release.checksum_url,
            CHECKSUM_LIMIT,
            accept="text/plain",
            final_url_validator=lambda url: is_allowed_release_redirect(
                url, release.tag, release.checksum_name
            ),
        )
        return parse_checksum(payload, release.archive_name), payload

    def sync_release(self) -> None:
        """Mirror the latest stable release, then publish signed exact metadata."""

        if not is_exact_github_api_url(self._settings.github_api_url):
            raise RefreshError("The configured GitHub release API URL is not approved.")
        self._settings.updates_root.mkdir(parents=True, exist_ok=True)
        os.chmod(self._settings.updates_root, 0o755)
        release_payload = self._transport.read_bytes(
            self._settings.github_api_url,
            RELEASE_METADATA_LIMIT,
            accept="application/vnd.github+json",
            final_url_validator=is_exact_github_api_url,
        )
        release = parse_release(release_payload)
        expected_sha256, checksum_payload = self._resolve_checksum(release)

        current_release = read_current_release_state(self._settings.updates_root)
        if current_release is not None:
            current_tag, current_sha256 = current_release
            if semantic_version(release.tag) < semantic_version(current_tag):
                raise RefreshError("Refusing to publish a release downgrade.")
            if release.tag == current_tag and not hmac.compare_digest(
                expected_sha256, current_sha256
            ):
                raise RefreshError(
                    "The same release tag now has a different archive hash."
                )

        archive_path = self._settings.updates_root / release.archive_name
        existing = existing_file_digest(archive_path, ARCHIVE_LIMIT)
        if existing is not None and hmac.compare_digest(
            existing[0], expected_sha256
        ):
            archive_size = existing[1]
            os.chmod(archive_path, 0o644)
        else:
            archive_size = self._transport.download_verified(
                release.archive_url,
                archive_path,
                ARCHIVE_LIMIT,
                expected_sha256,
                final_url_validator=lambda url: is_allowed_release_redirect(
                    url, release.tag, release.archive_name
                ),
            )
        if archive_size <= 0 or archive_size > ARCHIVE_LIMIT:
            raise ContentTooLargeError("Mirrored release archive size is invalid.")
        if release.declared_size is not None and archive_size != release.declared_size:
            raise RefreshError("Mirrored release archive size differs from GitHub metadata.")

        atomic_write(
            self._settings.updates_root / release.checksum_name,
            checksum_payload,
            0o644,
        )
        metadata = {
            "schemaVersion": 1,
            "tag": release.tag,
            "archive": release.archive_name,
            "sha256": expected_sha256,
            "size": archive_size,
            "publishedAt": release.published_at,
        }
        metadata_payload = json.dumps(
            metadata,
            ensure_ascii=False,
            indent=2,
            separators=(",", ": "),
        ).encode("utf-8")
        raw_signature = self._signer.sign(metadata_payload)
        if not raw_signature or len(raw_signature) > SIGNATURE_LIMIT:
            raise RefreshError("RSA signer returned an invalid signature size.")

        # Publishing JSON before its signature can cause only a brief fail-closed
        # verification error; it can never make unsigned metadata acceptable.
        atomic_write(self._settings.updates_root / "latest.json", metadata_payload)
        atomic_write(
            self._settings.updates_root / "latest.sig",
            base64.b64encode(raw_signature),
        )
        LOGGER.info("Mirrored and signed stable release %s.", release.tag)

    def run(self) -> None:
        self.refresh_blacklist()
        self.sync_release()


def build_argument_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Refresh signed WarThunderUIDGuard blacklist and releases."
    )
    parser.add_argument("--www-root", type=Path, default=Settings.www_root)
    parser.add_argument("--signing-key", type=Path, default=Settings.signing_key)
    parser.add_argument("--lock-file", type=Path, default=Settings.lock_file)
    parser.add_argument("--openssl", type=Path, default=Settings.openssl_path)
    parser.add_argument(
        "--network-timeout", type=float, default=Settings.network_timeout_seconds
    )
    parser.add_argument(
        "--lock-timeout", type=float, default=Settings.lock_timeout_seconds
    )
    parser.add_argument(
        "--bootstrap-url",
        action="append",
        dest="bootstrap_urls",
        help="Override public blacklist bootstrap URLs (repeatable).",
    )
    return parser


def main(arguments: list[str] | None = None) -> int:
    parser = build_argument_parser()
    namespace = parser.parse_args(arguments)
    if namespace.network_timeout <= 0 or namespace.lock_timeout < 0:
        parser.error("Timeouts must be positive (lock timeout may be zero).")
    settings = Settings(
        www_root=namespace.www_root,
        signing_key=namespace.signing_key,
        lock_file=namespace.lock_file,
        bootstrap_urls=tuple(namespace.bootstrap_urls or DEFAULT_BOOTSTRAP_URLS),
        openssl_path=namespace.openssl,
        network_timeout_seconds=namespace.network_timeout,
        lock_timeout_seconds=namespace.lock_timeout,
    )
    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s %(levelname)s %(name)s: %(message)s",
    )
    transport = HttpTransport(
        settings.network_timeout_seconds, settings.user_agent
    )
    signer = OpenSslSigner(settings.openssl_path, settings.signing_key)
    refresher = ContentRefresher(settings, transport, signer)
    try:
        refresher.run()
    except Exception as error:
        LOGGER.error("Refresh failed: %s", error)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
