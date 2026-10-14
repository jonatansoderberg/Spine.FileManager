# Spike 5 — Node identity and QR pairing

**Issue:** [#36](https://github.com/jonatansoderberg/Spine.FileManager/issues/36) · **Run:** 2026-10-14

**Question:** is the trust model right? A computer shows a QR code, a phone scans it, and afterwards the two trust each other's keys. Reconnecting and revoking must work too, and the QR must never hold a permanent password (plan §19–20).

## The model

- **A node is a key.** Each installation creates a P-256 key pair (`NodeIdentity`). Its node id is the first 20 bytes of the public key's SHA-256 in base32 (32 characters). Names and addresses are hints; trust is in the key. The private key is exported as PKCS#8 for the platform's secure storage (Keychain, Android Keystore, DPAPI), which the Other Devices step (#38) wires up.
- **The QR code** (`PairingInvitation`):

  ```
  spinefm://pair?v=1&s=<session>&k=<32-byte one-time secret>&n=<computer's node id>&name=<name>&e=<host:port,…>&x=<expiry>
  ```

  The secret works for one pairing within two minutes. The node id is a fingerprint, not a key. Nothing in it is reusable.
- **The exchange** (`Pairing.Host` / `Pairing.Joiner`) is three messages and does not depend on the transport:
  1. **Hello**, phone → computer: session, phone's public key, nonce, name, and an HMAC with the secret over all of it.
  2. **Welcome**, computer → phone: computer's public key, nonce, name, an HMAC with the secret over the whole exchange so far, and the computer's signature over it.
  3. **Confirm**, phone → computer: the phone's signature over the whole exchange.
  
  The phone checks that the computer's key matches the QR's node id. That is what stops a device that intercepts the connection, even one that saw the QR. Every field is length-prefixed before it is hashed or signed, so two different exchanges cannot read as the same bytes. The computer accepts one Hello per code.
- **Trust** (`TrustStore`): a node id with its key, the name it gives itself (*source name*) and the name the user gives it here (*local alias*). The lifetime is Quick transfer, This session or Remember (§21); only Remember is written to disk. Revoke forgets the key.
- **Reconnecting** (`Reconnect`): each side sends a fresh challenge and signs the other's together with both node ids. A proof cannot be replayed later or to a third node, and a revoked or unknown node fails.

## Results

- 13 tests in `Spine.FileManager.Tests/DeviceLink` cover:
  - the happy path, with both sides learning each other's key and name
  - a device without the code
  - a device that saw the QR and answers with its own key (refused: *not the computer that showed the code*)
  - a changed Welcome
  - a second use of a code
  - an expired code
  - a forged confirmation
  - persistence of remembered nodes and their alias
  - reconnect with a fresh, a wrong and a misaddressed challenge
  - a stranger, and revoke
- Run inside the app on **Mac Catalyst, the iOS simulator and the Android emulator**: create an identity, export and re-import it, and complete a pairing — `ok … import same True, paired True` on all three. Windows (CNG has P-256) is not run.

## What it leaves for later

- **Channel encryption.** The pairing authenticates keys and nothing in it is secret but the HMACs, which do not reveal the 256-bit code. The connection itself is encrypted by the transport (Spike 6, #37): TLS whose certificates are the node keys, each side pinning the other's key from the trust store.
- **Secure storage of the private key** and the UI (Link Device, the QR with Spine's `BarcodeView`, scanning with `BarcodeScannerView`, alias and lifetime choices) are #38.
- **Post-quantum keys** (ML-DSA in .NET 10) can replace P-256 later behind `NodeIdentity`; the node id format would change with them.
