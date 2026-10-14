# Spike 6 — Transport between two devices

**Issue:** [#37](https://github.com/jonatansoderberg/Spine.FileManager/issues/37) · **Run:** 2026-10-14

**Question:** what carries files between two paired devices? It has to be encrypted and authenticated by the node keys from Spike 5, carry one big file and many small ones, and survive a cut connection without losing or corrupting data (plan §23, §26–27).

## QUIC or TCP + TLS

`System.Net.Quic` needs msquic. A probe reports `QuicConnection.IsSupported`:

| Platform | QUIC |
|---|---|
| Mac Catalyst (Mac) | false |
| iOS (simulator) | false |
| Android (emulator) | false |
| Windows | true with msquic in the OS (Windows 11); not run here |

**Decision:** TCP + TLS 1.3 everywhere. One code path for every platform. Several streams over one connection are not needed for files sent one after another. If QUIC arrives on Apple and Android, it can sit behind the same channel later.

## The channel

`Transfer/SecureChannel`: mutual TLS with no certificate authority.
- Each node shows a self-signed certificate on its own node key (`NodeIdentity.CreateCertificate`). macOS and iOS need it loaded as PKCS#12; an ephemeral key fails there.
- Each side checks that the other's certificate key has the expected fingerprint. The client pins the node id it meant to reach. The server accepts only node ids in its trust store.
- A device with another key is refused on both sides, with a message that says which node was expected and which was reached.

## The protocol

`Transfer/FileTransfer`: length-prefixed JSON messages, with file bytes raw in between.
1. **Offer**, sender → receiver: id (stable across reconnects), name, size and SHA-256.
2. **Accept**, receiver → sender: the offset it already has.
3. The sender sends the bytes from that offset.
4. **Done**, receiver → sender: verified or not.

- **Writing:** the receiver writes into a hidden part file, `.name.<id>.spine-part`, and flushes each 1 MB chunk to the OS. A cut connection leaves what arrived on disk.
- **Hashing:** the receiver hashes incrementally as the bytes arrive. After a resume it reads the kept part once first.
- **A mismatch** deletes the part file, so the next attempt sends the whole file again.
- **A match** renames the part file to the file's name. The receiver never overwrites an existing file; that is the transfer engine's conflict question.

## Results

5 tests in `Spine.FileManager.Tests/Transfer`:
- both ends authenticate by their node keys
- a device with another key is refused on both sides
- a file arrives whole under its name
- a transfer cut off midway resumes where it stopped, and only the rest goes over the wire
- many small files go over one connection

Throughput on the Mac, sender and receiver in one Release process over loopback TLS (a scratch benchmark, not in the repo):

| Load | Result | Sender hashing beforehand |
|---|---|---|
| One 1 GB file | 2.73 s, **393 MB/s** | 0.80 s |
| 10 000 files of 100 B–4 KB (19 MB) | 8.92 s, **~1,100 files/s** | 0.63 s |

Mutual TLS plus 4 MB sent and verified, in-process over loopback on each platform:

| Platform | Time |
|---|---|
| Mac Catalyst | 120 ms |
| iOS simulator | 135 ms |
| Android emulator | 483 ms |

**Exit criterion met:** a cut transfer resumes without data loss and every file is verified, on every platform the app targets except Windows (not run).

## Not done here

- **LAN and Wi-Fi throughput** between two real devices. That needs discovery (#39) or a typed address; it is measured with the transfer engine.
- **Small files** run at the cost of a round trip per file (offer, accept, done). A batch offer, or pipelining the next offer before the last done arrives, is the transfer engine's first optimisation if LAN numbers ask for it.
- **The sender hashes every file before offering it.** That costs a full read up front (0.8 s per GB here). The alternative is hashing while sending and sending the checksum after the bytes; it changes the protocol and is left for the engine.
