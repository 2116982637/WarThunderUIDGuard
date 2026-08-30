# Deferred code signing policy

## Current status

Trusted Authenticode signing is currently deferred. Public releases are unsigned and may trigger Windows SmartScreen. This document is retained as a proposed policy for a future signing rollout; no release may be described as signed until its Authenticode signature has been verified successfully.

## Project

- Repository: <https://github.com/elainasamae/WarThunderUIDGuard>
- License: MIT
- Maintainer: [@elainasamae](https://github.com/elainasamae)

The public repository contains the complete project-owned source and release workflow. The application has no commercial or proprietary edition.

## Team roles

- Author, committer, and reviewer: [@elainasamae](https://github.com/elainasamae)
- Signing approver: [@elainasamae](https://github.com/elainasamae)

All people in these roles must use multi-factor authentication for both GitHub and SignPath. Changes submitted by other contributors must be reviewed by the maintainer before merge. Every production signing request requires maintainer approval in SignPath.

## Eligible artifact

Only `WarThunderUIDGuard.exe` built from this repository by `.github/workflows/release.yml` for an official `v*` tag is eligible for signing. Local builds, manually uploaded binaries, pull-request artifacts, forks, debug builds, third-party libraries, and artifacts without a verifiable source commit must not be signed.

## Trusted build and release process

1. The release is built entirely on a GitHub-hosted Windows runner from the tagged public source.
2. The application self-tests run before the unsigned executable is uploaded as an immutable GitHub Actions artifact.
3. The GitHub artifact is submitted through the official SignPath GitHub connector with origin verification.
4. SignPath applies Authenticode signing only after approval under the release signing policy.
5. The workflow verifies a valid timestamped signature issued by SignPath Foundation before packaging.
6. SHA-256 checksums and server-signed updater metadata are generated only from the final signed package.

If trusted signing is enabled later, the release workflow must fail when its signing configuration is absent or signature verification fails. Until then, the active workflow intentionally produces unsigned packages and labels them accordingly.

Maintainer setup instructions are documented in [SIGNPATH_SETUP.md](SIGNPATH_SETUP.md).

## Privacy

The application does not collect telemetry. Network transfers occur only for the user-visible functions documented in [PRIVACY.md](PRIVACY.md). This program will not transfer information to other networked systems unless specifically requested by the user or the person installing or operating it.

## Verification and incident response

Users can verify a release executable in PowerShell:

```powershell
Get-AuthenticodeSignature -LiteralPath '.\WarThunderUIDGuard.exe' |
    Format-List Status, SignerCertificate, TimeStamperCertificate
```

A signed release must report `Status: Valid` and identify SignPath Foundation as the signer. If certificate misuse, an unexpected signed artifact, or credential compromise is suspected, the maintainer will stop publishing, preserve workflow evidence, notify SignPath, request revocation when appropriate, and issue a corrected release or security notice.
