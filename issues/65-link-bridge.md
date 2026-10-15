# Issue #65 — Link two computers through the phone; confirm on the computer before linking

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/65
**Branch:** issue/65-link-bridge
**Status:** In Progress (built and unit-tested; not yet tried between real devices)

## Plan
Jonatan asked to link computer to computer, and to confirm on the computer when a device wants to link. He chose:
- the phone as a bridge
- discovery on the local network (#39, later)
- a typed code (#39, later)

## Changes
- **Confirmation:**
  - `LinkNode.Confirm` is asked once a device has proved it knows the code, before it is trusted.
  - The host answers with its endpoints only on a yes. A no, or no answer within `ConfirmTimeout` (two minutes), sends none, and the joiner fails with "turned the link down" and keeps nothing.
  - The app asks with an alert: "Link with “iPhone 17”?", with Link and Don't Link.
- **Introduction (`'I'`):**
  - `LinkNode.IntroduceAsync(to, introduced)` hands computer A's invitation to computer B, with an HMAC made with B's code's secret (`Pairing.IntroductionMac`).
  - B checks its current offer and the HMAC, uses its code up, and joins A with its own offer's lifetime. A asks its user as usual.
  - The phone is linked to neither.
- **Link Device on a phone or tablet:** Link Two Computers scans both codes and introduces them. Scanning the same computer twice is caught.
- **Tests:** a declined link leaves no trust; a phone links two computers with each one's lifetime; an introduction without the receiving computer's secret is refused.
- **Link Device starts with a choice:** show this device's code, join another device's (a phone scans at once; a computer starts with the link entered and offers Scan with Camera when it has one), or, on a phone, link two computers. a back chevron, a header page action in the leading slot, returns to the choice; a code is only offered while it is shown. The choices are buttons, so they answer to the keyboard and accessibility.
- **Addresses read when used:** a node's endpoints are the machine's addresses at the moment a code is shown or a link is made, not at start. The Mac moved from 192.168.68.x to 192.168.86.x while the app ran, and its codes kept the old address, so the phone could not reach it.

## Decisions
- The receiving computer (B) does not ask: its user showed its code to be scanned, the same consent a phone's join has. The computer it joins (A) asks, as for any device.
- The HMAC binds the whole introduced invitation, so a device that saw B's code cannot hand B a different computer.
