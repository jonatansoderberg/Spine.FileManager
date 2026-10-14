# Issue #20 — Operations journal and Operation Center

**GitHub:** https://github.com/jonatansoderberg/Spine.FileManager/issues/20
**Branch:** issue/20-operations-journal
**Status:** Completed

## Plan
Make the queue from #8 persistent (plan §10): every operation journaled so that, after a quit or a crash, the app knows what was copying, how far it had come and which file was half written, and can resume without redoing or duplicating finished files. Add pause, continue, resume/retry, queue order and the full Operation Center (§12).

## Changes
- Core:
  - `OperationJournal`: one JSON file per operation, written through a temporary file on every change of state and at most once a second while running. Damaged files are kept aside. The history is limited to 50 finished operations.
  - `OperationQueue` rewritten:
    - a worker that takes queued operations in list order
    - `Pause`/`Continue` at chunk and item boundaries
    - `Resume` for interrupted, failed or cancelled operations
    - `Move` to reorder queued operations, and `Remove`
  - The journal records the finished target items, the folders the operation created and the file being written. A resume deletes the half-written file, skips finished items without asking, and goes on inside the folders it created.
  - Streamed files are written as hidden `.name.spine-part` and renamed when complete (§27). A cancel removes the part; a crash leaves it for the resume.
  - `OperationState.Paused` and `Interrupted` (unfinished at start), `Attempts`, `OperationError`.
  - Four new tests (70 in all), among them a run cut off inside a big file and resumed by a new queue from the journal, with no conflict question and the files whole. Waits in the tests time out instead of hanging.
- App:
  - `Pages/OperationsPage` (Spine sheet): newest first, grouped running → queued → stopped → history. Each row has a progress bar and state text, and the actions its state allows: Pause, Continue, Resume or Try Again, Up/Down, Cancel, Show (opens the target folder), Remove. A page action clears finished operations.
  - Opened from the copy strip, from View › Operations (⌥⌘L on the Mac, Ctrl+Shift+J on Windows) and from an amber strip shown at start when operations were interrupted.
  - The journal lives in `AppPaths.Data/operations`. English and Swedish.

## Verification (Mac Catalyst, screen locked, scratch harness)
1. A 1.5 GB copy was paused at 32 % and the app killed with `kill -9`; the `.stor.bin.spine-part` file and the journal entry remained.
2. On the next start the amber strip showed "En kopiering eller flytt avbröts när appen stängdes." and the Operation Center listed "Kopiera ”stor.bin” till Mål2 — Avbröts · 479 MB / 1,5 GB" with Återuppta, Visa and Ta bort (window captures).
3. Resuming finished it: "Klar · 1,5 GB / 1,5 GB". The file is byte-identical to the source (`cmp`) and the part file is gone.

## Decisions
- A resume restarts the half-written *file* from its first byte; resuming inside a file (verified chunks) is milestone C's resumable transfer (§27), where it matters for network copies.
- Pausing waits at the next chunk or item, not mid-write.
