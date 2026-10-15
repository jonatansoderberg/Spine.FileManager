# Issue #91 — Peer link: reach linked devices beyond the local network

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/91
**Branch:** issue/91-router-port (step 1)
**Status:** In Progress

## Plan
The plan is [docs/plans/peer-link.md](../docs/plans/peer-link.md); the owner approved its order on 2026-10-15: router port mapping, the tracker, the ladder in `LinkNode`, then pieces with resume, then hole punching and push.

**Step 1 — through the router:**
- The listening device asks its router to forward its TCP port: NAT-PMP first (Apple and Google routers), UPnP IGD otherwise. It renews the mapping at half its lifetime and takes it down when the app quits.
- The public address joins the node's endpoints, so a device that pairs or meets it on the local network learns it.
- Linked devices swap their current endpoints over the authenticated channel whenever they reach each other, so a device keeps a fresh public address without pairing again (the tracker takes this over in step 2 for devices that never meet locally).
- The status says how a device answered: "Local network" for a private address, "Via router" for a public one.

## Changes
- `PortMapping`: NAT-PMP (TCP), then UPnP IGD (SSDP, the WAN service's AddPortMapping); refuses a public address that is itself private (a router behind the operator's NAT). The owner's router (192.168.86.1) forwards 84.23.129.77:47821 to the Mac.
- `LinkNode.PublicEndpoint` joins `Endpoints`; `ExchangeEndpointsAsync` and the `U` connection swap current endpoints over the mutual-TLS channel.
- `DeviceService` maps the node's port at start, renews it at half its lifetime (every five minutes without a router), swaps endpoints when it meets a device (at most every five minutes) and takes the port down when the Mac app quits.
- The status "Via router" for a device that answered at a public address.
- **Within ten seconds (owner):** devices are checked every 10 seconds and at once when the network changes (Wi-Fi on or off), endpoints are swapped the moment two devices meet and then every two minutes, and a network change maps the router's port again.

## Decisions
- **Built in the file manager for now.** Where the link lives (a Spine package, as the plan proposes, or here) is still open with the owner; step 1 is moved together with the rest if the answer is Spine.
