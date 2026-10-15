# Plan — Peer link: linked devices that find each other anywhere

**Status:** In progress — order approved by the owner on 2026-10-15 · **Last updated:** 2026-10-15

Builds on [Spike 7](../spikes/peer-link.md). Today two linked devices reach each other only on the same local network, at the address they swapped when they linked.

## Goals

- **It just works.** A linked device is reachable at home, at work and on mobile data whenever the network allows it, without settings.
- **Direct first.** The local network is preferred, then the other ways in a fixed order.
- **The user sees how.** Each device says how it is reached now, and a transfer says how it travels.
- **No data traffic through our server.** The Azure service only puts devices in touch, like a BitTorrent tracker. Files always go device to device.
- **Torrent-style transfers between two devices:** pieces with checksums, resume from the last good piece. One source at a time; several sources do not apply here (owner, 2026-10-15).

## The ladder

A connection tries every rung it knows at once and keeps the first that answers, preferring the higher one when several answer within a moment.

| Rung | How | Badge | Works when |
|---|---|---|---|
| 1. Local network | TCP + TLS to the device's local addresses, as today | Local network | Same network |
| 2. Through the router | The receiving device asks its router to open a TCP port (NAT-PMP, PCP or UPnP IGD) and registers the public address with the tracker; TCP + TLS to it | Via router | The receiver's router allows mapping (the owner's does) |
| 3. Hole punching | ICE (SIPSorcery) through both routers, signalled through the tracker; data over KCP on that path, TLS inside it | Direct over the internet | Neither side behind a strict NAT |
| — | Nothing answers | Offline | The device is off, asleep, or the app is closed (iOS) |

- **Rung 2 keeps today's transport.** A mapped TCP port is an ordinary address, so the TLS channel and the transfer protocol work unchanged. It is the cheapest rung to build and covers the case the spike proved (phone on 5G, Mac at home).
- **Rung 3 is the fallback** when no router maps. It needs ICE + KCP inside the app, and its speed on the internet is still to be measured.
- **No relay.** When no rung answers, the app says so plainly; a relay could be added later behind a monthly cap, but the owner does not want to pay for traffic.

## The tracker

A small ASP.NET Core service in Azure.

- **Register:** a device opens a WebSocket and proves it owns its node id by signing a challenge with its node key. It then reports its current local addresses and its mapped public address, and keeps the socket open.
- **Look up:** a device asks where a linked node is and gets its latest addresses and when it was last seen. It asks only for node ids it has linked; the tracker cannot tell who trusts whom, so a lookup is answered only to a caller who proves a node id the target lists as linked (the target registers the node ids it trusts, hashed).
- **Signal:** passes ICE offers, answers and candidates between two registered devices for rung 3.
- **Wake (later):** asks a sleeping phone to open the app, through a push notification (APNs, FCM). iOS stops an app listening in the background, so this is what makes "send to my phone" work without opening the app first.
- **What it stores:** node id, addresses, last seen, the hashed trust list. No names, no file data.
- **Cost:** an always-on host. The free App Service tier (5 WebSockets) is enough to develop with; a real one is about 130 SEK a month. Traffic is a few kilobytes per connection.

## Transfers

- **Pieces:** a file is offered with a manifest of pieces (1 MB each, larger for big files), each with its SHA-256.
- **Resume:** the receiver answers with what it already has. A cut transfer goes on from there, on any rung, even after the app was closed.
- **Verify:** a damaged piece is fetched again, not the whole file.
- **Progress:** per device in the sidebar and on Home, per transfer in the status bar and the operations panel, with pause, resume and cancel.

## Status on Other Devices

Already on master (#90): a dot and "Local network / Offline", checked every 30 seconds and after each transfer. The ladder adds "Via router" and "Direct over the internet", and the tracker's last seen ("Seen 3 minutes ago") for a device that is offline.

## Where the code lives

In this repository (owner, 2026-10-15: nothing else is to share it now). The client stays in `Spine.FileManager.Core` (`DeviceLink`) and the app's `DeviceService`; the tracker is its own small ASP.NET Core project here. The link code stays separate from the explorer, so it could move later. The WebRTC findings also feed the Spine AI plan's Q31 (WebRTC for realtime voice).

## Steps

| Step | What | Depends on |
|---|---|---|
| 1 | Router port mapping (NAT-PMP, PCP, UPnP) for the node's TCP port; the badge "Via router" — **done** (#93): the phone on 5G sent a file to the Mac at home | — |
| 2 | Tracker: register with a signed challenge, look up, last seen; free tier in Azure — **built and tested locally** (#95); runs in Azure once the subscription is chosen | — |
| 3 | The ladder in `LinkNode`: local, router, the tracker's addresses; the badge per rung | 1, 2 |
| 4 | Pieces with checksums and resume; pause, resume, cancel in the operations panel | — |
| 5 | Rung 3: ICE + KCP through the tracker's signalling, TLS inside | 2, 3 |
| 6 | Wake a phone through push | 2 |
| 7 | Speed on the internet (KCP tuning, several streams) | 5 |

## Open questions for the owner

1. **Azure subscription.** The one signed in on the Mac is the Knowit subscription. Which subscription should the tracker live in?
2. ~~Spine or the file manager.~~ The file manager (owner, 2026-10-15).
3. **Order.** Steps 1–3 first (the case the spike proved), then 4 (pieces), then 5 and 6?
4. **Wake through push.** Worth it in this round? It needs the app's push setup (APNs key, FCM).
