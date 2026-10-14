# Issue #36 — Spike 5: node identity and QR pairing

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/36
**Branch:** issue/36-pairing
**Status:** Completed

## Plan
The trust model as code in Core, tested including tampering, and run on the platforms at hand. Findings in `docs/spikes/pairing.md`.

## Changes
- Core `DeviceLink/`: `NodeIdentity`, `PairingInvitation`, `Pairing` (Host, Joiner, the three messages), `TrustStore` (TrustedNode with source name, local alias and lifetime) and `Reconnect`.
- 13 tests (94 in all); `Microsoft.Extensions.TimeProvider.Testing` for expiry.

## Decisions
- P-256 ECDSA, because .NET has it on all four platforms through the system's crypto (Apple Security, Android, CNG). Ed25519 would need a library.
- No key agreement in the pairing itself: the transport's TLS, with the node keys as pinned certificates, encrypts the channel (Spike 6). That keeps one place for encryption.
