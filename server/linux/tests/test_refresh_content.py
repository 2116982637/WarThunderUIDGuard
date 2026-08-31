from __future__ import annotations

import base64
import contextlib
import hashlib
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


MODULE_ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(MODULE_ROOT))
import refresh_content as refresh  # noqa: E402


class RecordingSigner:
    def __init__(self) -> None:
        self.payloads: list[bytes] = []

    def sign(self, payload: bytes) -> bytes:
        self.payloads.append(payload)
        return hashlib.sha256(b"test-signature:" + payload).digest()


class FakeTransport:
    def __init__(self, responses: dict[str, bytes]) -> None:
        self.responses = responses
        self.read_calls: list[str] = []
        self.download_calls: list[str] = []

    def read_bytes(
        self,
        url: str,
        maximum_bytes: int,
        *,
        accept: str | None = None,
        final_url_validator=None,
    ) -> bytes:
        del accept
        self.read_calls.append(url)
        if final_url_validator is not None and not final_url_validator(url):
            raise refresh.RefreshError("unapproved test URL")
        payload = self.responses[url]
        if not payload or len(payload) > maximum_bytes:
            raise refresh.ContentTooLargeError("fake response size")
        return payload

    def download_verified(
        self,
        url: str,
        destination: Path,
        maximum_bytes: int,
        expected_sha256: str,
        *,
        final_url_validator=None,
    ) -> int:
        self.download_calls.append(url)
        if final_url_validator is not None and not final_url_validator(url):
            raise refresh.RefreshError("unapproved test URL")
        payload = self.responses[url]
        if not payload or len(payload) > maximum_bytes:
            raise refresh.ContentTooLargeError("fake response size")
        if hashlib.sha256(payload).hexdigest().upper() != expected_sha256.upper():
            raise refresh.RefreshError("fake checksum mismatch")
        refresh.atomic_write(destination, payload)
        return len(payload)


def unlocked(_path: Path, _timeout: float):
    return contextlib.nullcontext()


class FakeResponse:
    def __init__(self, payload: bytes, url: str, content_length: int | None = None):
        self._stream = io.BytesIO(payload)
        self._url = url
        self.headers = {}
        if content_length is not None:
            self.headers["Content-Length"] = str(content_length)

    def read(self, size: int = -1) -> bytes:
        return self._stream.read(size)

    def geturl(self) -> str:
        return self._url

    def __enter__(self):
        return self

    def __exit__(self, *_args):
        return False


class RefreshContentTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary = tempfile.TemporaryDirectory()
        root = Path(self.temporary.name)
        self.settings = refresh.Settings(
            www_root=root / "www",
            signing_key=root / "not-a-real-key.pem",
            lock_file=root / "data.lock",
            bootstrap_urls=("https://bootstrap.invalid/blacklist.json",),
        )

    def tearDown(self) -> None:
        self.temporary.cleanup()

    @staticmethod
    def release_document(
        archive: bytes,
        *,
        digest: bool = True,
        size: int | None = None,
        draft: bool = False,
        prerelease: bool = False,
    ) -> tuple[bytes, str, str]:
        tag = "v1.2.3"
        archive_name = f"WarThunderUIDGuard-{tag}-win-x64.zip"
        checksum_name = archive_name + ".sha256.txt"
        archive_url = (
            f"https://github.com/{refresh.REPOSITORY}/releases/download/"
            f"{tag}/{archive_name}"
        )
        checksum_url = archive_url + ".sha256.txt"
        asset = {
            "name": archive_name,
            "browser_download_url": archive_url,
            "size": len(archive) if size is None else size,
        }
        if digest:
            asset["digest"] = "sha256:" + hashlib.sha256(archive).hexdigest()
        document = {
            "tag_name": tag,
            "draft": draft,
            "prerelease": prerelease,
            "published_at": "2026-08-31T12:34:56Z",
            "assets": [
                asset,
                {"name": checksum_name, "browser_download_url": checksum_url},
            ],
        }
        return json.dumps(document).encode(), archive_url, checksum_url

    def test_existing_blacklist_is_never_overwritten_and_is_resigned(self) -> None:
        original = (
            b'\xef\xbb\xbf{"SchemaVersion":2,"Language":"zh-CN",'
            b'"OneDriveSyncEnabled":false,"Players":[],"DeletedPlayers":[]}\r\n'
        )
        self.settings.www_root.mkdir(parents=True)
        blacklist = self.settings.www_root / "blacklist.json"
        blacklist.write_bytes(original)
        transport = FakeTransport({})
        signer = RecordingSigner()
        worker = refresh.ContentRefresher(
            self.settings, transport, signer, lock_factory=unlocked
        )

        worker.refresh_blacklist()
        worker.refresh_blacklist()

        self.assertEqual(original, blacklist.read_bytes())
        self.assertEqual([original, original], signer.payloads)
        expected = base64.b64encode(
            hashlib.sha256(b"test-signature:" + original).digest()
        )
        self.assertEqual(expected, (self.settings.www_root / "blacklist.sig").read_bytes())
        self.assertEqual([], transport.read_calls)

    def test_missing_blacklist_is_initialized_once_only(self) -> None:
        initial = (
            b'{"SchemaVersion":2,"Language":"zh-CN",'
            b'"OneDriveSyncEnabled":false,"Players":[],"DeletedPlayers":[]}'
        )
        transport = FakeTransport(
            {self.settings.bootstrap_urls[0]: initial}
        )
        signer = RecordingSigner()
        worker = refresh.ContentRefresher(
            self.settings, transport, signer, lock_factory=unlocked
        )

        worker.refresh_blacklist()
        transport.responses[self.settings.bootstrap_urls[0]] = b'{"Players":["replacement"]}'
        worker.refresh_blacklist()

        self.assertEqual(
            initial, (self.settings.www_root / "blacklist.json").read_bytes()
        )
        self.assertEqual([self.settings.bootstrap_urls[0]], transport.read_calls)

    def test_invalid_bootstrap_does_not_create_blacklist(self) -> None:
        transport = FakeTransport(
            {self.settings.bootstrap_urls[0]: b'{"notPlayers":[]}'}
        )
        worker = refresh.ContentRefresher(
            self.settings, transport, RecordingSigner(), lock_factory=unlocked
        )
        with self.assertRaises(refresh.RefreshError):
            worker.refresh_blacklist()
        self.assertFalse((self.settings.www_root / "blacklist.json").exists())

    def test_schema_v2_duplicate_active_uid_is_rejected(self) -> None:
        player = {
            "Uid": "123456",
            "Note": "",
            "Aliases": ["Alpha"],
            "CreatedAt": "2026-08-29T14:20:55+08:00",
            "UpdatedAt": "2026-08-29T14:30:58.9118707+08:00",
        }
        payload = json.dumps(
            {
                "SchemaVersion": 2,
                "Language": "zh-CN",
                "OneDriveSyncEnabled": False,
                "Players": [player, dict(player)],
                "DeletedPlayers": [],
            }
        ).encode()
        with self.assertRaises(refresh.RefreshError):
            refresh.validate_blacklist(payload)

    def test_blacklist_rejects_duplicate_keys_constants_and_wrong_client_types(self) -> None:
        valid_prefix = (
            b'{"SchemaVersion":2,"Language":"zh-CN",'
            b'"OneDriveSyncEnabled":false,"Players":[],"DeletedPlayers":[]'
        )
        payloads = [
            valid_prefix + b',"schemaVersion":2}',
            valid_prefix + b',"extra":NaN}',
            b'{"SchemaVersion":2,"Language":"zh-CN",'
            b'"OneDriveSyncEnabled":"false","Players":[],"DeletedPlayers":[]}',
        ]
        for payload in payloads:
            with self.subTest(payload=payload):
                with self.assertRaises(refresh.RefreshError):
                    refresh.validate_blacklist(payload)

    def test_release_archive_checksum_and_exact_metadata_bytes_are_published(self) -> None:
        archive = b"PK\x03\x04fake-release-archive"
        release_payload, archive_url, _ = self.release_document(archive)
        transport = FakeTransport(
            {
                refresh.GITHUB_LATEST_RELEASE_URL: release_payload,
                archive_url: archive,
            }
        )
        signer = RecordingSigner()
        worker = refresh.ContentRefresher(self.settings, transport, signer)

        worker.sync_release()

        updates = self.settings.updates_root
        archive_name = "WarThunderUIDGuard-v1.2.3-win-x64.zip"
        expected_hash = hashlib.sha256(archive).hexdigest().upper()
        self.assertEqual(archive, (updates / archive_name).read_bytes())
        self.assertEqual(
            f"{expected_hash}  {archive_name}".encode(),
            (updates / f"{archive_name}.sha256.txt").read_bytes(),
        )
        metadata_bytes = (updates / "latest.json").read_bytes()
        self.assertEqual(metadata_bytes, signer.payloads[-1])
        self.assertEqual(
            base64.b64encode(
                hashlib.sha256(b"test-signature:" + metadata_bytes).digest()
            ),
            (updates / "latest.sig").read_bytes(),
        )
        metadata = json.loads(metadata_bytes)
        self.assertEqual(1, metadata["schemaVersion"])
        self.assertEqual("v1.2.3", metadata["tag"])
        self.assertEqual(expected_hash, metadata["sha256"])
        self.assertEqual(len(archive), metadata["size"])
        self.assertEqual("2026-08-31T12:34:56Z", metadata["publishedAt"])

    def test_checksum_asset_is_used_when_github_digest_is_absent(self) -> None:
        archive = b"another-fake-archive"
        release_payload, archive_url, checksum_url = self.release_document(
            archive, digest=False
        )
        checksum_payload = (
            "\ufeff"
            + hashlib.sha256(archive).hexdigest().lower()
            + "  WarThunderUIDGuard-v1.2.3-win-x64.zip\r\n"
        ).encode("utf-8")
        transport = FakeTransport(
            {
                refresh.GITHUB_LATEST_RELEASE_URL: release_payload,
                archive_url: archive,
                checksum_url: checksum_payload,
            }
        )
        refresh.ContentRefresher(
            self.settings, transport, RecordingSigner()
        ).sync_release()
        self.assertEqual(
            checksum_payload,
            (
                self.settings.updates_root
                / "WarThunderUIDGuard-v1.2.3-win-x64.zip.sha256.txt"
            ).read_bytes(),
        )

    def test_unstable_or_oversize_release_is_rejected_before_download(self) -> None:
        archive = b"small"
        unstable, archive_url, _ = self.release_document(archive, prerelease=True)
        transport = FakeTransport(
            {refresh.GITHUB_LATEST_RELEASE_URL: unstable, archive_url: archive}
        )
        with self.assertRaises(refresh.RefreshError):
            refresh.ContentRefresher(
                self.settings, transport, RecordingSigner()
            ).sync_release()
        self.assertEqual([], transport.download_calls)

    def test_release_downgrade_and_same_tag_hash_change_are_rejected(self) -> None:
        archive = b"new-release-bytes"
        release_payload, archive_url, _ = self.release_document(archive)
        transport = FakeTransport(
            {refresh.GITHUB_LATEST_RELEASE_URL: release_payload, archive_url: archive}
        )
        self.settings.updates_root.mkdir(parents=True)

        (self.settings.updates_root / "latest.json").write_text(
            json.dumps({"tag": "v9.0.0", "sha256": "A" * 64}),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(refresh.RefreshError, "downgrade"):
            refresh.ContentRefresher(
                self.settings, transport, RecordingSigner()
            ).sync_release()
        self.assertEqual([], transport.download_calls)

        (self.settings.updates_root / "latest.json").write_text(
            json.dumps({"tag": "v1.2.3", "sha256": "B" * 64}),
            encoding="utf-8",
        )
        with self.assertRaisesRegex(refresh.RefreshError, "different archive hash"):
            refresh.ContentRefresher(
                self.settings, transport, RecordingSigner()
            ).sync_release()
        self.assertEqual([], transport.download_calls)

        oversize, archive_url, _ = self.release_document(
            archive, size=refresh.ARCHIVE_LIMIT + 1
        )
        transport.responses[refresh.GITHUB_LATEST_RELEASE_URL] = oversize
        with self.assertRaises(refresh.ContentTooLargeError):
            refresh.ContentRefresher(
                self.settings, transport, RecordingSigner()
            ).sync_release()
        self.assertEqual([], transport.download_calls)

    def test_hash_mismatch_keeps_existing_archive(self) -> None:
        destination = self.settings.updates_root / "archive.zip"
        destination.parent.mkdir(parents=True)
        destination.write_bytes(b"known-old-file")
        url = "https://github.com/example/archive.zip"
        response = FakeResponse(b"tampered-new-file", url)
        transport = refresh.HttpTransport(
            1, "test", opener=lambda *_args, **_kwargs: response
        )
        with self.assertRaises(refresh.RefreshError):
            transport.download_verified(
                url,
                destination,
                1024,
                hashlib.sha256(b"expected-new-file").hexdigest(),
            )
        self.assertEqual(b"known-old-file", destination.read_bytes())

    def test_transport_enforces_content_length_before_reading(self) -> None:
        url = "https://example.invalid/data"
        response = FakeResponse(b"x", url, content_length=101)
        transport = refresh.HttpTransport(
            1, "test", opener=lambda *_args, **_kwargs: response
        )
        with self.assertRaises(refresh.ContentTooLargeError):
            transport.read_bytes(url, 100)

    @unittest.skipUnless(os.name == "posix", "POSIX file modes are Linux-specific")
    def test_all_published_content_is_world_readable_but_not_writable(self) -> None:
        archive = b"public-mode-test-archive"
        release_payload, archive_url, _ = self.release_document(archive)
        transport = FakeTransport(
            {
                self.settings.bootstrap_urls[0]: (
                    b'{"SchemaVersion":2,"Language":"zh-CN",'
                    b'"OneDriveSyncEnabled":false,"Players":[],"DeletedPlayers":[]}'
                ),
                refresh.GITHUB_LATEST_RELEASE_URL: release_payload,
                archive_url: archive,
            }
        )
        refresh.ContentRefresher(
            self.settings, transport, RecordingSigner(), lock_factory=unlocked
        ).run()
        public_files = [
            self.settings.www_root / "blacklist.json",
            self.settings.www_root / "blacklist.sig",
            *self.settings.updates_root.iterdir(),
        ]
        for path in public_files:
            with self.subTest(path=path.name):
                self.assertEqual(0o644, path.stat().st_mode & 0o777)
        self.assertEqual(0o755, self.settings.www_root.stat().st_mode & 0o777)
        self.assertEqual(0o755, self.settings.updates_root.stat().st_mode & 0o777)

    def test_openssl_signer_uses_fixed_sha256_argument_vector(self) -> None:
        key = self.settings.signing_key
        key.write_text("test key placeholder", encoding="ascii")
        calls = []

        def runner(arguments, **kwargs):
            calls.append((arguments, kwargs))
            return subprocess.CompletedProcess(arguments, 0, stdout=b"raw-signature", stderr=b"")

        signer = refresh.OpenSslSigner(Path("/usr/bin/openssl"), key, runner=runner)
        self.assertEqual(b"raw-signature", signer.sign(b"exact payload"))
        self.assertEqual(
            [
                str(Path("/usr/bin/openssl")),
                "dgst",
                "-sha256",
                "-sign",
                str(key),
            ],
            calls[0][0],
        )
        self.assertEqual(b"exact payload", calls[0][1]["input"])
        self.assertFalse(calls[0][1]["check"])

    def test_systemd_units_share_deployment_paths_and_five_minute_timer(self) -> None:
        service = (MODULE_ROOT / "warthunder-uid-guard-refresh.service").read_text()
        timer = (MODULE_ROOT / "warthunder-uid-guard-refresh.timer").read_text()
        self.assertIn("User=wtuidguard", service)
        self.assertIn("Group=wtuidguard", service)
        self.assertIn("WorkingDirectory=/opt/wtuidguard", service)
        self.assertIn("/opt/wtuidguard/refresh_content.py", service)
        self.assertIn("/srv/wtuidguard/www", service)
        self.assertIn("/var/lib/wtuidguard", service)
        self.assertIn("OnUnitInactiveSec=5min", timer)
        self.assertIn("WantedBy=timers.target", timer)


if __name__ == "__main__":
    unittest.main()
