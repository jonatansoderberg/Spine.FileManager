# Issue #37 — Spike 6: transport between two devices

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/37
**Branch:** issue/37-transport
**Status:** Completed

## Plan
Find out whether QUIC is available on every platform. Build a channel authenticated by the node keys from Spike 5 and a resumable, verified file protocol over it. Test a cut transfer and measure one big file and many small ones. Findings in `docs/spikes/transport.md`.

## Changes
- `DeviceLink/NodeIdentity.CreateCertificate`: a self-signed TLS certificate on the node key, loaded as PKCS#12 for Apple.
- `Transfer/SecureChannel`: mutual TLS with the peer pinned by node id.
- `Transfer/FileTransfer`: offer, accept, bytes and done, with part files, resume from an offset and an incremental SHA-256.
- 5 tests in `Spine.FileManager.Tests/Transfer`.
- `docs/spikes/transport.md`; plan updated.

## Decisions
- **TCP + TLS 1.3, not QUIC:** `QuicConnection.IsSupported` is false on the Mac, iOS and Android.
- **No certificate authority:** trust is the node key's fingerprint, as in pairing.
- **The receiver hashes as bytes arrive** instead of reading the file again at the end. A resume reads the kept part once.
- **The receiver never overwrites:** a name clash is the transfer engine's conflict question, as for local operations.
