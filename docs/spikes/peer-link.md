# Spike 7 — Reaching a linked device beyond the local network

**Run:** 2026-10-15 · **Code:** branch `spike/peer-link` (`spikes/PeerLink`: a switchboard, a console peer, and a hook in the app that runs on the phone)

**Question:** how do two linked devices find each other and move files when they are not on the same network, without paying for data traffic and without native code? The owner's terms: direct on the local network first, other ways in order after it, the user sees which way a link uses, a router service in Azure only to put devices in touch, never to carry files.

## What was built

- **Switchboard:** an ASP.NET Core WebSocket endpoint. Each peer keeps a socket open under its id; a message names the peer it is for. It carried offers, answers and ICE candidates only. Run on the Mac and reached from the phone's 5G through a Microsoft Dev Tunnel.
- **Peer:** the same code as a console app on the Mac and inside the app on the iPhone.

## Measurements

| Way | Where | Connected in | Throughput |
|---|---|---|---|
| WebRTC data channel (SIPSorcery 10.0.17) | Mac ↔ Mac | 0.2–0.3 s | 0.3–0.45 MB/s, whatever the chunk size or queue |
| ICE from SIPSorcery, data over KCP (KcpSharp 0.8.8) | Mac ↔ Mac | 0.13–0.26 s | 30–33 MB/s |
| ICE + KCP, plain hole punching | iPhone on 5G ↔ Mac at home | — | failed: timed out |
| ICE + KCP with a NAT-PMP port on the home router | iPhone on 5G ↔ Mac at home | 0.56–0.6 s | 0.18 MB/s (Debug), 0.31 MB/s (Release, KCP without congestion control) |

## Findings

- **SIPSorcery runs on iOS and the Mac without native code.** Its ICE agent finds a path quickly: host, server-reflexive (STUN at Google and Cloudflare) and peer-reflexive candidates.
- **WebRTC's data channel is too slow for files.** SIPSorcery's SCTP has its limits as compile-time constants (4 packets per 50 ms burst without a SACK, a 256 KB receive window). They cannot be tuned without a fork.
- **KCP over the ICE path is fast** on a local network. Two traps:
  - **First byte:** SIPSorcery takes a packet whose first byte is 0–3 for STUN and drops it. KCP opens each packet with its conversation id, low byte first, so the id must have a first byte in the RTP range (0x9C was used).
  - **`IsController`:** its setter is internal; it was set by reflection.
- **Plain hole punching failed** between the operator's carrier-grade NAT on 5G (public 90.129.212.x) and the home router (84.23.129.77). Both sides gathered candidates and checked; no pair answered.
- **A port the router opens is what got through.** The home router (192.168.86.1) answers both NAT-PMP (RFC 6886) and UPnP IGD. The Mac asked for a UDP port, offered it as one more candidate, and the phone on 5G reached it in 0.3 s after the checks began. The router kept the port numbers.
- **5G throughput is not settled.** 0.31 MB/s over KCP may be the phone's uplink. The owner put speed work off for later; the next measurement is the uplink itself (a speed test) against KCP with several streams or larger windows.
- **The switchboard's traffic is a few kilobytes per connection.**

## Bugs found on the way, fixed on master

- **A Mac that changes networks** kept offering its old address in pairing codes: the node read its addresses once at start. Fixed in #89.
- **A phone offered its mobile-data addresses** (pdp_ip, 192.0.0.x) and duplicates, and connections tried them one by one, three seconds each. Fixed in #90 with parallel attempts.
- **iOS stops an app listening in the background.** A phone receives only while the app is open; the sender now says so (#90).

## Trouble during the spike

- **An endless restart loop of the Mac peer** appended to a log and filled the disk. A loop must be bounded and overwrite its log.
- **A Release build for the phone crashed at launch** on a stale AOT module (`ShimSkiaSharp` out of date). Delete `obj/Release` and `bin/Release` before every Release build for a device.
- **`devicectl` reaches the phone only over Wi-Fi or a cable.** A test on 5G must start by itself when the app opens and show its result on the screen.

## What it means for the design

See [the peer link plan](../plans/peer-link.md). In short: TCP + TLS stays the transport. The router's port mapping makes a device reachable on the internet with the transport it already has. UDP hole punching with ICE + KCP is the last rung, for when no router mapping is possible. A tracker in Azure tells devices where the others are and passes the few messages hole punching needs.
