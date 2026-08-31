#!/usr/bin/env python3
"""Loopback-only administrator upload service for War Thunder UID Guard."""

from __future__ import annotations

import argparse
import base64
import binascii
from collections import defaultdict, deque
from dataclasses import dataclass
import hmac
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
import os
from pathlib import Path
import re
import signal
import threading
import time
from typing import Callable, Mapping
from urllib.parse import urlsplit

from wtuidguard_common import (
    AuditLog,
    BlacklistValidationError,
    ExclusiveFileLock,
    LockTimeoutError,
    MAX_UPLOAD_BYTES,
    OpenSslSigner,
    backup_blacklist,
    load_raw_key,
    parse_and_validate_blacklist,
    publish_signed_blacklist,
    repair_blacklist_signature,
    sha256_hex,
    zero_bytearray,
)


UPLOAD_PATH = "/admin/upload"
AUTHORIZATION_PREFIX = "WT-HMAC "
NONCE_PATTERN = re.compile(r"^[0-9A-F]{32}$")
HASH_PATTERN = re.compile(r"^[0-9A-F]{64}$")
MAXIMUM_ATTEMPTS = 10
ATTEMPT_WINDOW_SECONDS = 600
CLOCK_SKEW_SECONDS = 120
NONCE_LIFETIME_SECONDS = 300


@dataclass(frozen=True)
class ServerConfig:
    data_path: Path = Path("/srv/wtuidguard/www/blacklist.json")
    signature_path: Path = Path("/srv/wtuidguard/www/blacklist.sig")
    backup_directory: Path = Path("/var/lib/wtuidguard/upload-backups")
    lock_path: Path = Path("/var/lib/wtuidguard/data.lock")
    audit_log_path: Path = Path("/var/log/wtuidguard/admin-upload.log")
    hmac_key_path: Path = Path(
        "/etc/wtuidguard/secrets/admin-upload-hmac.key"
    )
    signing_key_path: Path = Path(
        "/etc/wtuidguard/secrets/signing-private.pem"
    )
    openssl_path: Path = Path("/usr/bin/openssl")
    bind_address: str = "127.0.0.1"
    bind_port: int = 8090


@dataclass(frozen=True)
class UploadResponse:
    status: int
    text: str


def create_authorization(
    body: bytes,
    base_hash: str,
    timestamp_text: str,
    nonce: str,
    key: bytes | bytearray,
) -> str:
    body_hash = sha256_hex(body)
    canonical = (
        f"POST\n{UPLOAD_PATH}\n{timestamp_text}\n{nonce}\n"
        f"{base_hash}\n{body_hash}\n"
    ).encode("utf-8")
    return base64.b64encode(hmac.digest(key, canonical, "sha256")).decode("ascii")


class AdminUploadApplication:
    """Protocol implementation separated from HTTP framing for deterministic tests."""

    def __init__(
        self,
        config: ServerConfig,
        authentication_key: bytearray,
        signer: Callable[[bytes], bytes],
        *,
        clock: Callable[[], float] = time.time,
        audit: Callable[[str], None] | None = None,
    ) -> None:
        if len(authentication_key) != 32:
            raise ValueError("The administrator authentication key must be 32 bytes.")
        self.config = config
        self._authentication_key = authentication_key
        self._signer = signer
        self._clock = clock
        self._audit = audit or AuditLog(config.audit_log_path).write
        self._attempts: dict[str, deque[int]] = defaultdict(deque)
        self._nonces: dict[str, int] = {}
        self._state_lock = threading.Lock()

    def close(self) -> None:
        zero_bytearray(self._authentication_key)

    def _write_audit(self, message: str) -> None:
        try:
            self._audit(message)
        except OSError:
            # An audit-disk failure must not disclose credentials or crash the HTTP
            # worker.  systemd still captures unexpected process-level failures.
            pass

    def _rate_limit_allows(self, address: str, now: int) -> bool:
        cutoff = now - ATTEMPT_WINDOW_SECONDS
        with self._state_lock:
            recent = self._attempts[address]
            while recent and recent[0] < cutoff:
                recent.popleft()
            if len(recent) >= MAXIMUM_ATTEMPTS:
                return False
            recent.append(now)
            return True

    def _nonce_seen(self, nonce: str, now: int) -> bool:
        with self._state_lock:
            expired = [value for value, expiry in self._nonces.items() if expiry < now]
            for value in expired:
                del self._nonces[value]
            return nonce in self._nonces

    def _reserve_nonce(self, nonce: str, now: int) -> bool:
        with self._state_lock:
            expired = [value for value, expiry in self._nonces.items() if expiry < now]
            for value in expired:
                del self._nonces[value]
            if nonce in self._nonces:
                return False
            self._nonces[nonce] = now + NONCE_LIFETIME_SECONDS
            return True

    def handle_request(
        self,
        method: str,
        request_target: str,
        headers: Mapping[str, str],
        body: bytes,
        address: str,
    ) -> UploadResponse:
        path = urlsplit(request_target).path
        if method != "POST" or path != UPLOAD_PATH:
            return UploadResponse(404, "Not found.")

        now = int(self._clock())
        if not self._rate_limit_allows(address, now):
            self._write_audit(f"rate-limited address={address}")
            return UploadResponse(429, "Too many requests.")

        normalized_headers = {key.lower(): value for key, value in headers.items()}
        timestamp_text = normalized_headers.get("x-wt-timestamp", "")
        nonce = normalized_headers.get("x-wt-nonce", "")
        base_hash = normalized_headers.get("x-wt-base-sha256", "").upper()
        authorization = normalized_headers.get("authorization", "")

        if (
            not timestamp_text
            or not authorization.startswith(AUTHORIZATION_PREFIX)
            or NONCE_PATTERN.fullmatch(nonce) is None
            or HASH_PATTERN.fullmatch(base_hash) is None
        ):
            self._write_audit(f"upload-rejected address={address} reason=unauthorized")
            return UploadResponse(401, "Unauthorized.")

        try:
            timestamp = int(timestamp_text, 10)
        except ValueError:
            self._write_audit(f"upload-rejected address={address} reason=unauthorized")
            return UploadResponse(401, "Unauthorized.")
        if abs(now - timestamp) > CLOCK_SKEW_SECONDS:
            self._write_audit(f"upload-rejected address={address} reason=unauthorized")
            return UploadResponse(401, "Unauthorized.")
        if self._nonce_seen(nonce, now):
            self._write_audit(f"upload-rejected address={address} reason=replay")
            return UploadResponse(401, "Unauthorized.")

        if len(body) > MAX_UPLOAD_BYTES:
            self._write_audit(f"upload-rejected address={address} reason=too-large")
            return UploadResponse(413, "Upload too large.")

        expected_text = create_authorization(
            body, base_hash, timestamp_text, nonce, self._authentication_key
        )
        supplied_text = authorization[len(AUTHORIZATION_PREFIX) :]
        try:
            # Convert.FromBase64String accepts whitespace; preserve that behavior.
            supplied_mac = base64.b64decode(
                "".join(supplied_text.split()), validate=True
            )
            expected_mac = base64.b64decode(expected_text, validate=True)
        except (binascii.Error, ValueError):
            self._write_audit(f"upload-rejected address={address} reason=unauthorized")
            return UploadResponse(401, "Unauthorized.")
        if not hmac.compare_digest(expected_mac, supplied_mac):
            self._write_audit(f"upload-rejected address={address} reason=unauthorized")
            return UploadResponse(401, "Unauthorized.")
        if not self._reserve_nonce(nonce, now):
            self._write_audit(f"upload-rejected address={address} reason=replay")
            return UploadResponse(401, "Unauthorized.")

        try:
            with ExclusiveFileLock(self.config.lock_path, timeout_seconds=30.0):
                current = self.config.data_path.read_bytes()
                if sha256_hex(current) != base_hash:
                    self._write_audit(
                        f"upload-rejected address={address} reason=conflict"
                    )
                    return UploadResponse(409, "Server data changed.")

                document = parse_and_validate_blacklist(body)
                backup_blacklist(
                    self.config.data_path, self.config.backup_directory, keep=30
                )
                publish_signed_blacklist(
                    self.config.data_path,
                    self.config.signature_path,
                    body,
                    self._signer,
                )
        except BlacklistValidationError as exc:
            if str(exc) == "UploadTooLarge":
                self._write_audit(
                    f"upload-rejected address={address} reason=too-large"
                )
                return UploadResponse(413, "Upload too large.")
            self._write_audit(
                f"upload-rejected address={address} reason=invalid "
                f"type={type(exc).__name__} message={str(exc)}"
            )
            return UploadResponse(400, "Invalid blacklist.")
        except (LockTimeoutError, OSError, RuntimeError, ValueError) as exc:
            self._write_audit(
                f"upload-rejected address={address} reason=invalid "
                f"type={type(exc).__name__} message={str(exc)}"
            )
            return UploadResponse(400, "Invalid blacklist.")

        players = document.get("Players") or document.get("players") or []
        deleted = document.get("DeletedPlayers") or document.get("deletedPlayers") or []
        self._write_audit(
            f"upload-ok address={address} players={len(players)} "
            f"deleted={len(deleted)} sha256={sha256_hex(body)}"
        )
        return UploadResponse(200, "OK")


class UploadHttpServer(ThreadingHTTPServer):
    daemon_threads = True
    allow_reuse_address = True

    def __init__(self, address: tuple[str, int], application: AdminUploadApplication):
        self.application = application
        super().__init__(address, UploadRequestHandler)


class UploadRequestHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "WTUIDGuardUpload/1"
    sys_version = ""

    def do_POST(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._handle()

    def do_GET(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._handle()

    def do_HEAD(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._handle()

    def do_PUT(self) -> None:  # noqa: N802 - BaseHTTPRequestHandler API
        self._handle()

    def log_message(self, format_string: str, *args: object) -> None:
        # Nginx has the access log.  Avoid accidentally logging request headers.
        return

    def _send_plain(self, response: UploadResponse) -> None:
        payload = response.text.encode("ascii")
        self.send_response(response.status)
        self.send_header("Content-Type", "text/plain; charset=us-ascii")
        self.send_header("Content-Length", str(len(payload)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("X-Content-Type-Options", "nosniff")
        self.send_header("Connection", "close")
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(payload)
        self.close_connection = True

    def _client_address(self) -> str:
        # The service only listens on loopback; Nginx overwrites this header.
        forwarded = self.headers.get("X-Forwarded-For", "")
        if forwarded:
            return forwarded.split(",")[-1].strip()[:128]
        return self.client_address[0]

    def _handle(self) -> None:
        application = self.server.application  # type: ignore[attr-defined]
        if self.command != "POST" or urlsplit(self.path).path != UPLOAD_PATH:
            self._send_plain(UploadResponse(404, "Not found."))
            return

        if self.headers.get("Transfer-Encoding"):
            self._send_plain(UploadResponse(400, "Invalid blacklist."))
            return
        content_length_text = self.headers.get("Content-Length")
        try:
            content_length = int(content_length_text or "", 10)
        except ValueError:
            self._send_plain(UploadResponse(400, "Invalid blacklist."))
            return
        if content_length < 0:
            self._send_plain(UploadResponse(400, "Invalid blacklist."))
            return
        if content_length > MAX_UPLOAD_BYTES:
            self._send_plain(UploadResponse(413, "Upload too large."))
            return

        self.connection.settimeout(30.0)
        body = self.rfile.read(content_length)
        if len(body) != content_length:
            self._send_plain(UploadResponse(400, "Invalid blacklist."))
            return
        headers = {key: value for key, value in self.headers.items()}
        response = application.handle_request(
            self.command, self.path, headers, body, self._client_address()
        )
        self._send_plain(response)


def _parse_arguments() -> argparse.Namespace:
    defaults = ServerConfig()
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--data", type=Path, default=defaults.data_path)
    parser.add_argument("--signature", type=Path, default=defaults.signature_path)
    parser.add_argument("--backups", type=Path, default=defaults.backup_directory)
    parser.add_argument("--lock", type=Path, default=defaults.lock_path)
    parser.add_argument("--audit-log", type=Path, default=defaults.audit_log_path)
    parser.add_argument("--hmac-key", type=Path, default=defaults.hmac_key_path)
    parser.add_argument("--signing-key", type=Path, default=defaults.signing_key_path)
    parser.add_argument("--openssl", type=Path, default=defaults.openssl_path)
    parser.add_argument("--bind", default=defaults.bind_address)
    parser.add_argument("--port", type=int, default=defaults.bind_port)
    return parser.parse_args()


def main() -> int:
    arguments = _parse_arguments()
    config = ServerConfig(
        data_path=arguments.data,
        signature_path=arguments.signature,
        backup_directory=arguments.backups,
        lock_path=arguments.lock,
        audit_log_path=arguments.audit_log,
        hmac_key_path=arguments.hmac_key,
        signing_key_path=arguments.signing_key,
        openssl_path=arguments.openssl,
        bind_address=arguments.bind,
        bind_port=arguments.port,
    )
    if config.bind_address not in {"127.0.0.1", "::1", "localhost"}:
        raise SystemExit("The administrator service must bind to loopback only.")

    authentication_key = load_raw_key(config.hmac_key_path, expected_bytes=32)
    signer = OpenSslSigner(config.signing_key_path, config.openssl_path)
    audit = AuditLog(config.audit_log_path)
    repair_blacklist_signature(
        config.data_path,
        config.signature_path,
        config.lock_path,
        signer.sign,
    )
    audit.write("signature-verified-and-repaired-at-startup")
    application = AdminUploadApplication(
        config, authentication_key, signer.sign, audit=audit.write
    )
    server = UploadHttpServer((config.bind_address, config.bind_port), application)

    def request_shutdown(signum: int, frame: object) -> None:
        # shutdown() must run outside serve_forever's signal-handling thread.
        threading.Thread(target=server.shutdown, daemon=True).start()

    signal.signal(signal.SIGTERM, request_shutdown)
    signal.signal(signal.SIGINT, request_shutdown)
    audit.write("service-started")
    try:
        server.serve_forever(poll_interval=0.25)
    finally:
        server.server_close()
        application.close()
        audit.write("service-stopped")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
