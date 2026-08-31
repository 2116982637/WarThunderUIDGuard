from __future__ import annotations

import base64
import hashlib
import hmac
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


MODULE_DIRECTORY = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE_DIRECTORY))

from admin_upload_server import (  # noqa: E402
    AdminUploadApplication,
    ServerConfig,
    create_authorization,
)
from wtuidguard_common import (  # noqa: E402
    BlacklistValidationError,
    MAX_UPLOAD_BYTES,
    OpenSslSigner,
    load_raw_key,
    parse_and_validate_blacklist,
    repair_blacklist_signature,
    sha256_hex,
)


FIXED_NOW = 1_700_000_000
AUTHENTICATION_KEY = bytes(range(32))


def blacklist_bytes(alias: str = "Alpha", uid: str = "123456") -> bytes:
    document = {
        "SchemaVersion": 2,
        "Language": "zh-CN",
        "OneDriveSyncEnabled": False,
        "Players": [
            {
                "Uid": uid,
                "Note": "",
                "Aliases": [alias],
                "CreatedAt": "2026-08-29T14:20:55+08:00",
                "UpdatedAt": "2026-08-29T14:30:58.9118707+08:00",
            }
        ],
        "DeletedPlayers": [],
    }
    return json.dumps(document, ensure_ascii=False, indent=2).encode("utf-8")


class TemporaryApplication:
    def __init__(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        self.config = ServerConfig(
            data_path=root / "www" / "blacklist.json",
            signature_path=root / "www" / "blacklist.sig",
            backup_directory=root / "backups",
            lock_path=root / "data.lock",
            audit_log_path=root / "admin-upload.log",
            hmac_key_path=root / "unused-hmac.key",
            signing_key_path=root / "unused-signing.key",
            openssl_path=Path("openssl"),
        )
        self.config.data_path.parent.mkdir(parents=True)
        self.current = blacklist_bytes("Old")
        self.config.data_path.write_bytes(self.current)
        self.audit_messages: list[str] = []
        self.application = AdminUploadApplication(
            self.config,
            bytearray(AUTHENTICATION_KEY),
            lambda body: hashlib.sha384(body).digest(),
            clock=lambda: FIXED_NOW,
            audit=self.audit_messages.append,
        )

    def close(self) -> None:
        self.application.close()
        self.temporary.cleanup()

    def headers(
        self,
        body: bytes,
        *,
        nonce: str = "B" * 32,
        timestamp: str = str(FIXED_NOW),
        base_hash: str | None = None,
    ) -> dict[str, str]:
        selected_hash = base_hash or sha256_hex(self.current)
        authorization = create_authorization(
            body,
            selected_hash,
            timestamp,
            nonce,
            AUTHENTICATION_KEY,
        )
        return {
            "X-WT-Timestamp": timestamp,
            "X-WT-Nonce": nonce,
            "X-WT-Base-SHA256": selected_hash,
            "Authorization": "WT-HMAC " + authorization,
        }


class ProtocolTests(unittest.TestCase):
    def test_csharp_hmac_vector(self) -> None:
        body = b'{"SchemaVersion":2}'
        with tempfile.TemporaryDirectory() as temporary_name:
            raw_path = Path(temporary_name) / "admin-upload-hmac.key"
            raw_path.write_bytes(AUTHENTICATION_KEY)
            raw_path.chmod(0o640)
            loaded_key = load_raw_key(raw_path)
            actual = create_authorization(
                body,
                "A" * 64,
                "1700000000",
                "B" * 32,
                loaded_key,
            )
            self.assertEqual(
                actual, "f/Y4xupOy1f3/HKYLhUoGK1fMiBiqVw9Dy7b4HYSon0="
            )

            base64_path = Path(temporary_name) / "old-base64-format.key"
            base64_path.write_bytes(base64.b64encode(AUTHENTICATION_KEY))
            base64_path.chmod(0o640)
            with self.assertRaises(ValueError):
                load_raw_key(base64_path)

    def test_success_preserves_exact_bytes_signs_and_backs_up(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        uploaded = blacklist_bytes("New")
        response = fixture.application.handle_request(
            "POST",
            "/admin/upload",
            fixture.headers(uploaded),
            uploaded,
            "203.0.113.10",
        )
        self.assertEqual((response.status, response.text), (200, "OK"))
        self.assertEqual(fixture.config.data_path.read_bytes(), uploaded)
        expected_signature = base64.b64encode(hashlib.sha384(uploaded).digest())
        self.assertEqual(fixture.config.signature_path.read_bytes(), expected_signature)
        backups = list(fixture.config.backup_directory.glob("blacklist-*.json"))
        self.assertEqual(len(backups), 1)
        self.assertEqual(backups[0].read_bytes(), fixture.current)

    def test_startup_repairs_signature_without_rewriting_blacklist(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        before = fixture.config.data_path.read_bytes()
        repair_blacklist_signature(
            fixture.config.data_path,
            fixture.config.signature_path,
            fixture.config.lock_path,
            lambda body: hashlib.sha384(body).digest(),
        )
        self.assertEqual(before, fixture.config.data_path.read_bytes())
        self.assertEqual(
            base64.b64encode(hashlib.sha384(before).digest()),
            fixture.config.signature_path.read_bytes(),
        )

    def test_conflict_does_not_replace_server_data(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        uploaded = blacklist_bytes("New")
        headers = fixture.headers(uploaded, base_hash="A" * 64)
        response = fixture.application.handle_request(
            "POST", "/admin/upload", headers, uploaded, "203.0.113.11"
        )
        self.assertEqual(response.status, 409)
        self.assertEqual(fixture.config.data_path.read_bytes(), fixture.current)
        self.assertFalse(fixture.config.signature_path.exists())

    def test_nonce_replay_is_rejected_before_conflict(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        uploaded = blacklist_bytes("New")
        headers = fixture.headers(uploaded)
        first = fixture.application.handle_request(
            "POST", "/admin/upload", headers, uploaded, "203.0.113.12"
        )
        second = fixture.application.handle_request(
            "POST", "/admin/upload", headers, uploaded, "203.0.113.12"
        )
        self.assertEqual(first.status, 200)
        self.assertEqual(second.status, 401)
        self.assertIn("reason=replay", fixture.audit_messages[-1])

    def test_stale_timestamp_and_bad_mac_are_rejected(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        uploaded = blacklist_bytes("New")
        stale_headers = fixture.headers(
            uploaded, nonce="C" * 32, timestamp=str(FIXED_NOW - 121)
        )
        stale = fixture.application.handle_request(
            "POST", "/admin/upload", stale_headers, uploaded, "203.0.113.13"
        )
        bad_headers = fixture.headers(uploaded, nonce="D" * 32)
        bad_headers["Authorization"] = "WT-HMAC " + base64.b64encode(b"x" * 32).decode()
        bad = fixture.application.handle_request(
            "POST", "/admin/upload", bad_headers, uploaded, "203.0.113.13"
        )
        self.assertEqual(stale.status, 401)
        self.assertEqual(bad.status, 401)

    def test_rate_limit_blocks_eleventh_attempt(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        uploaded = blacklist_bytes("New")
        statuses = []
        for index in range(11):
            nonce = f"{index:032X}"
            headers = fixture.headers(uploaded, nonce=nonce)
            headers["Authorization"] = "WT-HMAC invalid"
            response = fixture.application.handle_request(
                "POST", "/admin/upload", headers, uploaded, "203.0.113.14"
            )
            statuses.append(response.status)
        self.assertEqual(statuses[:10], [401] * 10)
        self.assertEqual(statuses[10], 429)

    def test_invalid_document_and_oversized_body_are_rejected(self) -> None:
        fixture = TemporaryApplication()
        self.addCleanup(fixture.close)
        duplicate = json.loads(blacklist_bytes().decode())
        duplicate["Players"].append(dict(duplicate["Players"][0]))
        invalid_body = json.dumps(duplicate).encode()
        invalid = fixture.application.handle_request(
            "POST",
            "/admin/upload",
            fixture.headers(invalid_body, nonce="E" * 32),
            invalid_body,
            "203.0.113.15",
        )
        oversized_body = b"{" + b" " * MAX_UPLOAD_BYTES
        oversized = fixture.application.handle_request(
            "POST",
            "/admin/upload",
            fixture.headers(oversized_body, nonce="F" * 32),
            oversized_body,
            "203.0.113.16",
        )
        self.assertEqual(invalid.status, 400)
        self.assertEqual(oversized.status, 413)
        self.assertEqual(fixture.config.data_path.read_bytes(), fixture.current)

    def test_validator_accepts_utf8_and_seven_fractional_digits(self) -> None:
        parsed = parse_and_validate_blacklist(blacklist_bytes("北条麻妃"))
        self.assertEqual(parsed["Players"][0]["Aliases"], ["北条麻妃"])

    def test_validator_rejects_ambiguous_json_and_client_type_confusion(self) -> None:
        valid = json.loads(blacklist_bytes().decode("utf-8"))
        wrong_uid_type = json.loads(json.dumps(valid))
        wrong_uid_type["Players"][0]["Uid"] = 123456
        wrong_alias_type = json.loads(json.dumps(valid))
        wrong_alias_type["Players"][0]["Aliases"] = [123]
        wrong_sync_type = json.loads(json.dumps(valid))
        wrong_sync_type["OneDriveSyncEnabled"] = "false"

        rejected = [
            b'{"SchemaVersion":2,"schemaVersion":2}',
            b'{"SchemaVersion":2,"Language":"zh-CN","OneDriveSyncEnabled":false,"Players":[],"DeletedPlayers":[],"extra":NaN}',
            json.dumps(wrong_uid_type).encode("utf-8"),
            json.dumps(wrong_alias_type).encode("utf-8"),
            json.dumps(wrong_sync_type).encode("utf-8"),
        ]
        for payload in rejected:
            with self.subTest(payload=payload):
                with self.assertRaises(BlacklistValidationError):
                    parse_and_validate_blacklist(payload)


def find_openssl() -> str | None:
    discovered = shutil.which("openssl")
    if discovered:
        return discovered
    for candidate in (
        Path(r"C:\Program Files\Git\usr\bin\openssl.exe"),
        Path("/usr/bin/openssl"),
    ):
        if candidate.is_file():
            return str(candidate)
    return None


class OpenSslCompatibilityTests(unittest.TestCase):
    @unittest.skipUnless(find_openssl(), "OpenSSL is unavailable")
    def test_signer_produces_pkcs1_v15_sha256_signature(self) -> None:
        openssl = find_openssl()
        assert openssl is not None
        with tempfile.TemporaryDirectory() as temporary_name:
            temporary = Path(temporary_name)
            private_key = temporary / "private.pem"
            public_key = temporary / "public.pem"
            payload = temporary / "payload.bin"
            signature = temporary / "signature.bin"
            subprocess.run(
                [openssl, "genpkey", "-algorithm", "RSA", "-pkeyopt", "rsa_keygen_bits:2048", "-out", str(private_key)],
                check=True,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            subprocess.run(
                [openssl, "pkey", "-in", str(private_key), "-pubout", "-out", str(public_key)],
                check=True,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            body = b"cross-platform-signature-test\n"
            raw_signature = OpenSslSigner(private_key, openssl).sign(body)
            payload.write_bytes(body)
            signature.write_bytes(raw_signature)
            verified = subprocess.run(
                [openssl, "dgst", "-sha256", "-verify", str(public_key), "-signature", str(signature), str(payload)],
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
            )
            self.assertEqual(verified.returncode, 0, verified.stderr.decode(errors="replace"))


if __name__ == "__main__":
    unittest.main(verbosity=2)
