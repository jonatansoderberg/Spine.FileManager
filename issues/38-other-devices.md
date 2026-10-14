# Issue #38 — Other Devices: link, name, lifetime, revoke, send

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/38
**Branch:** issue/38-other-devices
**Status:** Completed

## Plan
Plan §19–21, on top of Spike 5 (pairing) and Spike 6 (transport).
- A node that listens on the network, pairs through a QR code or a link, and sends files to devices it trusts.
- The sidebar's Other Devices: rename (local alias), forget (revoke), and drop files on a device to send them.

## Changes
- **`DeviceLink/LinkNode`:** a TCP listener whose connections say what they are for in their first byte.
  - A pairing runs Spike 5's exchange; the invitation's secret authenticates it.
  - A transfer runs mutual TLS on the node keys (Spike 6), from a node this one trusts.
  - It joins an invitation, sends files and folders, and receives them into an inbox, with free names on a clash.
  - After a pairing both sides tell each other where they listen.
  - A node trusted for one transfer is forgotten after it.
  - 4 tests with two nodes on loopback: pairing, files and folders, one-transfer trust, wrong code and unknown sender.
- **`TrustedNode.Endpoints`:** where a node last listened, kept in the trust store.
- **`SecureChannel` and `FileTransfer`:**
  - work over a stream that has already said what it is for
  - the receiver chooses each file's path
  - the message framing is shared
- **`PairingInvitation`:** a 16-byte secret, a shorter session id and endpoints without URL escaping. The QR code is easier to scan from a screen, which was checked with a phone.
- **`Services/DeviceService`:**
  - **Identity:** the node identity sits in secure storage, falling back to a file only the user can read.
  - **Listening:** the node listens on port 47821, or any free port, and starts only once something is linked.
  - **Name:** the Mac's computer name; Mac Catalyst reports "iPad".
  - **Inbox:** Downloads on a computer, Documents/Received on a phone.
- **`Pages/LinkDevicePage`:**
  - the QR code (Spine's `BarcodeView`, low error correction), renewed every two minutes
  - how long the other device is trusted (`SegmentedControl`: one transfer, this session, always)
  - Copy Link
  - Join by pasting a link, or by scanning on a phone (Spine's `BarcodeScannerPage`)
- **`spinefm://` links:** registered on iOS and the Mac. Opening one shows Link Device and joins (`DeviceLinks`, `JoinRequest`).
- **The sidebar's Other Devices:**
  - linked devices, with their lifetime as a tooltip
  - Rename… and Forget on right-click
  - Link Device…, also in the File menu
- **Dropping files and folders on a device sends them.** This works from the app's lists and from other apps. The status bar shows sending, sent, received and failures.
- **Platform settings:**
  - Android's minimum is now API 23 (Spine's scanner needs it).
  - Camera and local-network usage strings.

## Decisions
- **TCP with a purpose byte rather than two ports.** Pairing needs no TLS, since its secret and signatures authenticate it; transfers need mutual TLS.
- **Each side picks how long it trusts the other.** The host picks on its sheet before the code is scanned, the joiner on its own.
- **The node starts only when needed:** a Mac that links nothing opens no port.
- **Mac identity in a 0600 file for now.** `SecureStorage` needs a keychain access group, which a Developer-ID build without a provisioning profile does not have. Moving it to the Keychain is a follow-up.
- **Verified end to end:** the Mac and an iOS Simulator on the same machine paired through a `spinefm://` link. iPhone 17 appeared under Other Devices, and a file dropped on it arrived in the phone app's inbox.

## Open
- **Discovery (#39):** a restart that changes a device's address or port (DHCP, a taken port) needs discovery to find the device again.
- **Received files on the phone** are not yet shown in the app.
- **Browsing another device's files** belongs to milestone D.
