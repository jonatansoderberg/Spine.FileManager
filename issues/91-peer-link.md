# Issue #91 — Peer link: reach linked devices beyond the local network

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/91
**Branch:** issue/91-router-port (step 1), issue/91-ask-before-receiving, issue/91-tracker (step 2)
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
- **"Send to" in the file menus:** files, folders, apps and several selected items get Send to › each linked device, on the phone too (it had no way to send a file).
- **This Device's places on Home without a sidebar** (the phone): Documents, where received files land, and the others.
- **A device that only reaches this one** (a phone on 5G) shows green with "Reaches this device": each check is now the authenticated endpoint swap, and the side that is reached raises `Contacted`; within 25 seconds of it the device counts as there. Sending to it waits for a way back (step 3).
- **Within ten seconds (owner):** devices are checked every 10 seconds and at once when the network changes (Wi-Fi on or off), endpoints are swapped the moment two devices meet and then every two minutes, and a network change maps the router's port again.

- **Ask before receiving (owner):** a transfer opens with a `TransferRequest` (top-level names, files, bytes) and waits for a `TransferAnswer`. Each linked device has "Ask Before Receiving" in its menu, on by default (`TrustedNode.AskBeforeReceiving`); with it on the receiver asks "“iPhone” wants to send 3 items" with Accept and Decline, and no answer within two minutes is a no. The sender says "iPhone declined the files" or "iPhone didn't answer". The whole suite failed once on the first run after this change and passed eleven runs after it; the failing test was not caught.

**Step 2 — the tracker:**
- `Spine.FileManager.Tracker`: an ASP.NET Core WebSocket endpoint at `/link`. A device signs in by signing the tracker's nonce with its node key (the node id is the key's fingerprint), then reports its endpoints and the hashed ids of the nodes it trusts. It can look a node up (answered only when that node trusts it; to anyone else every node looks unknown) and call a node (passed on only when that node trusts the caller). Kept in memory: node id, endpoints, last seen, hashed trust list.
- `TrackerClient` in Core: signs in, keeps the line open and opens it again after a cut (1 s doubling to 30 s), publishes, looks up, calls.
- `DeviceService`: publishes on start, on a changed router port, a changed trust list and a network change. When a device does not answer at its stored endpoints it asks the tracker: new endpoints are stored and tried at once; a device connected to the tracker but unreachable from here is called, and reaches out to this one ("Reaches this device"). An offline device shows when the tracker last saw it: "Offline · seen 5 minutes ago".
- **In Azure** (owner's Visual Studio Enterprise subscription, free tier): `wss://spinefm-tracker-bcaa2e.azurewebsites.net/link`, set as `DefaultTracker`. From the Mac: sign-in in 0.2 s, a lookup in 20 ms. `TrackerClient.LastError` keeps why the tracker could not be reached, with its address (a test script failed quietly at first: file-based C# apps turn reflection-based JSON off).
- Three tests against a real tracker on localhost: lookup with trust and a stranger who finds nothing, a call that reaches only a device that trusts the caller, endpoints published later.
- A flaky test was seen again once, on the first run after a build; five runs with fresh builds passed. Test output is now kept to catch it.

**Step 3 — the return path, and things the phone lacked:**
- **Return path:** when a device cannot be reached, the sender asks it through the tracker (`TrackerCall` with purpose `send`) to connect back. It connects, writes `R` and its node id, then acts as the receiver (TLS server, the usual request and answer); the sender pins that node id in TLS, so a stranger who connects first only spoils the attempt and gets no files. `LinkNode.WaitForCallbackAsync` / `ConnectBackAsync`, a test where the computer reaches the phone only this way. The tracker in Azure was published again for the purpose field.
- **Settings:** "Linked devices" shows the tracker's state (connected, or why not, with its address), this device's local addresses and the router's port with its method.
- **The phone:** a tap opens a folder or file (a long press gives the menu); Settings sit on Home after the places, as there is no sidebar.

**Verified 2026-10-15:** the phone on 5G showed the Mac as "Via routern", the Mac showed the phone as "Reaches this device", and a file sent from the phone over 5G arrived in the Mac's Downloads.

## Decisions
- **The link lives in this repository** (owner, 2026-10-15): nothing else is to share it now, and changes reach the phone faster without a Spine release in between. The tracker is a project here too.
