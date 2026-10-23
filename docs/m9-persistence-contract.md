# M9c persistence contract

M9c implements local persistence. M9a sustained realtime remains red (38/62 NTSC
windows; PAL sustained unexecuted), M9/network/distribution/release remain open.
The 18 M8 whole-console audio differences and 7000 adjudicated raw SPC bus
mismatches are unchanged. No expected hardware output or acquired fixture changes.

## Files, commit point and recovery

`AtomicFile` normalizes full paths, uses 64 fixed process lock stripes and an
exclusive OS `.lock` file per logical path. Another process gets a retryable I/O
error; it cannot mix backup and primary transactions. Empty lock files remain on
disk deliberately. They are not save candidates. This is a cooperating-writer
policy; an unrelated program can still edit data outside these locks.

Each transaction creates a unique same-directory temporary, completes its writer,
calls `FileStream.Flush(true)`, closes and validates it. A valid old primary is
copied to a separate unique backup temporary, flushed, and atomically renamed to
`.bak`. Only then is the new primary renamed over the logical path. A corrupt or
incompatible primary is retained at a unique `.corrupt-<id>` path before replacement;
it never replaces a validated backup. Cancellation before the primary rename
retains the previous primary. After that rename the commit has occurred; later
cancellation cannot undo it. Exception injection after replacement reports an
ambiguous completion while both resulting generations remain valid.

Readers consider only primary and `.bak`, validate the complete candidate against
its expected identity and return an explicit recovery signal. Abandoned `.tmp`
files from a terminated process remain evidence and are never loaded. The strict
`StateSerializer.Load(cart,path/stream)` never recovers silently. `ReadForLoad`
is the separate recovery API. Frontend loads and previews visibly report recovery;
invalid or missing candidates cannot mutate the live machine. Backup-only logical
saves remain listed. Deletion removes exactly the primary and its backup under
the same transaction lock; corrupt evidence/locks are retained and a deleted
logical save cannot silently reappear from its former backup.

This uses actual OS file flushes and same-volume replacement, tested on this host.
It does not flush directory metadata through a portable directory-fsync API and
does not prove storage-controller, network filesystem, physical disk or power-loss
guarantees. Injected exceptions/cancellation and termination of our own writer
process test software boundaries; no physical power-cut experiment occurred.

## SRAM session ownership and periodic saves

The frontend writes `battery/<ROM SHA256>.srm.v1`. This is not a raw SRAM export:
BinaryWriter UTF-8 length-prefixed `SNESSRAM1`, ROM hash, signed little-endian
32-bit byte length, 32-byte SHA256 of the following raw SRAM, then exact SRAM bytes.
The maximum SRAM remains the mapper's 512 KiB. Backup uses the same envelope.
Identity binds ROM bytes and SRAM size, not display region or an APU lab profile.

An old `.srm` raw file can be imported by its existing ROM-hash filename and exact
expected SRAM size. It has no historical checksum: the UI says so. Migration
creates the protected envelope and preserves the legacy input byte-for-byte.
External tools must explicitly decode the envelope for raw SRAM interoperability;
new writes do not update that historical raw input.

One `BatteryPersistence` coordinator serializes capture itself, dirty comparison,
validation, disk I/O and ROM installation. Autosave cannot capture an old snapshot
and later overtake a newer reload/close save. The UI's actual 30-second timer has
at most one pending task; a busy interval coalesces. A clean comparison also checks
that the canonical disk file remains valid. There is one current-ROM byte cache,
not an ever-growing ROM dictionary. Backup recovery sets `needsRepair`: the next
successful flush repairs even unchanged SRAM, preserving corrupt evidence first.
A read by itself does not claim durable repair.

An exclusive `.session.lock` lease prevents two emulator instances from persisting
competing live sessions for one ROM. A ROM switch keeps the old lease while
acquiring the tentative target, validating/importing its SRAM and installing it
through the worker. Only successful installation commits the lease switch. A
corrupt/occupied target or rejected owner command retains old ownership and live
hardware. Same-ROM reload always flushes the live battery before reading it.

Reset uses the existing C64-style confirmation menu and preserves SRAM. ROM reload
inserts the validated battery then performs the existing PowerOn path; it also
preserves that battery. A separate PowerCycle or Clear-SRAM menu command is not
offered. Reset never implicitly deletes battery data.

## State bounds and owner boundary

Format9 bytes, VID3 and AUD2 are unchanged. Direct Save retains frozen Gzip bytes.
`SaveCaptured` now requires a detached matching Cartridge so every old/new
candidate is independently fully parsed; comparison with new raw bytes is not
used as a substitute for validating an older state. Off-thread compression can
still choose different Deflate boundaries while preserving all raw hardware bytes.

The reader bounds compressed input to 16 MiB, decoded input to 32 MiB, each UTF-8
length-prefixed string to 8 MiB, JSON depth to 32, an individual JSON array to
512*239 elements, and one JSON document to 1,000,000 nodes before deserialization.
Lengths are checked before allocation. Current format9 has at most 512 KiB SRAM,
128 KiB WRAM, fixed PPU/APU memories and 122,368 raw pixels per native field;
its current/previous three planes plus numeric JSON pipelines are a few MiB
(normal captured diagnostics around 1.55 MiB). These budgets leave explicit
headroom for the existing representation, including worst-case numeric widths.
They do not permit arbitrary extra laboratory event queues. Future hardware or
formats must review and explicitly extend budgets, rather than trusting sizes
from files. Gzip trailer CRC32 and decoded length are checked explicitly, including
truncation that a streaming inflater alone might accept.

Load is single-flight before copying input. The worker captures detached ROM
identity, then decoding/JSON restore runs outside both UI and core owner. Only an
unchanged epoch/ROM can install the prepared machine. Reset, ROM changes, cancel
and shutdown reject stale preparation. Shutdown cancels and drains this one owned
load. Existing profile, mapper, firmware, format and every component's semantic
validation remain in force; saved valid profiles are restored as recorded.

## Settings and close errors

Settings have a 64 KiB / depth16 input limit, recoverable atomic backup and visible
normalization/recovery/unavailability notices. Null text/maps, invalid numeric
ports, presentation enum indexes and gamepad device indexes normalize to supported
values. Only known control names and bounded Button/Axis/Hat tokens (including
semicolon alternatives) survive. Pre-M9 integer bindings remain migratable.
Settings disk writes have one active and one coalesced latest JSON snapshot.

Close drains owned media, preview, network, settings, periodic battery and state
preparation work, pauses the owner, then attempts final battery/settings writes
before disposing owned resources. An unreported outstanding media failure and a
failed final flush survive as an aggregate error. Ordinary GUI launches, including
a ROM argument, show the existing error dialog; automated `--...` modes are dialog
free and return Exit1. `last-error.log` is best effort; even failure to write that
log preserves the original stderr error and exit status. Successful Close is not
reported when required final persistence failed. Previous valid generations remain
available for retry after resolving the storage error.

## Validation commands

```
dotnet SNESEmulator.Core.Tests/bin/Release/net10.0/SNESEmulator.Core.Tests.dll --persistence-only
python tools/test-ui-persistence.py --output artifacts/<fresh-directory>
python tools/test-lab.py --windows --full
```

Tests use isolated actual files, faults/cancel around write/flush/backup/replace,
parallel writers, an owned second process, an abruptly terminated owned writer,
leases, dirty coalescing, delayed autosave/final/reload interleavings, failed switch
rollback, strict/recovered states, explicit parser-budget reasons and unchanged
live hardware. The native probe waits for 20 real fields and observes two actual
30-second frontend intervals with CPU-written SRAM, asynchronous corrupt/missing
previews/load/delete, close during save, and final SRAM/settings/log failure cases.
Synthetic callbacks do not prove physical gamepads, OS focus, clean-VM launch,
hours-long A/V, physical SNES behavior or sustained performance. The Windows
lock-screen/browser workaround remains in force; no new OS automation was used.

The M8 `test-lab.py` pin changes solely to add the persistence supervisor and native
persistence probe. Existing acquired fixtures, reference adapters, golden outputs
and known-error thresholds remain unchanged. Root independent acceptance is a
separate pending step; final clean-commit measurements are recorded in the baseline.
