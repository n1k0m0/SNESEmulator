# M9e Manager and local IPC

The own C# core, format9, VID3 and AUD2 remain unchanged. This phase adds a
separate Windows/WPF application, portable Contracts and Management services.
The sibling C64 concept informed the architecture; all C64 files remain read-only.
M9d was independently accepted on2a5ae39. M9a sustained performance,18 known
M8 audio differences,7000 source-adjudicated raw SPC differences, prolonged soak,
distribution/installer/CI execution and M10 remain open. publish/ remains PRE-M0.

## Apps and user actions

Build `dotnet build SNESEmulator.sln -c Release`. Run
`SNESEmulatorManager/bin/Release/net10.0-windows/SNESEmulatorManager.exe`.
The emulator development output also receives a relative `Manager/` directory.
F10 MANAGER starts that relative executable with `--connect <pid>`, closes its
menu and resumes according to the explicitly chosen pause mode. A missing
executable produces visible MANAGER NOT INSTALLED. Both apps run independently.
This build-copy is not an installer or refreshed publish distribution.

The dashboard requires an explicitly selected PID plus discovered start time;
there is no fallback to another running instance. It shows cartridge identity,
field/epoch, pause/turbo/menu and actual local/host/client mode. Recently played
items and favorites use SQLite metadata. Local .sfc/.smc scans run off the UI,
ignore reparse points, and have10000-entry/1024-directory/depth32 limits with
cancellation at each visited entry. An entire scan is one transaction; a failure
or cancellation after real inserts rolls it back. File reads use opened-stream
bounds. Unsupported/invalid cartridges are not advertised as playable.

Canonical ROM identity uses the existing Cartridge loader, including removal of
an actual512-byte copier header. Distinct paths to the same payload share notes,
favorites and last-played time. Missing files stay visible and can be relinked
only to the same canonical hash. Folder selection is explicit per scan; there is
no background filesystem watcher or online catalog. The catalog stays disabled
because no cleared SNES provider has been configured.

All active persisted SNES options have typed controls: checkboxes, named filter/
scaler/controller choices, numeric limits, text paths/hosts and12 gamepad binding
selectors (editable for supported axis/hat/chord tokens). Legacy button-only
migration data is preserved. There are no invented C64 drive/SID options. Current
options apply immediately or at the next network connection; active-session
network configuration is locked. No current host option requires a hardware
reset/restart, and this UI does not pretend to configure unimplemented chips.
A full snapshot carries ExpectedRevision. The emulator validates and rejects a
stale revision before changing anything; the Manager marks applied only after
its acknowledgement. Unapplied drafts survive discovery refreshes. A deliberate
target change requires discarding a draft. Multi-step actions disable target
selection and bind to the selected process identity. The running emulator owns
settings; offline edits are explicitly for the next start and are refused while
an unselected emulator is already discovered.

State9 files are listed independently of the current cartridge. The Manager
resolves matching library ROMs from their actual state identity for offline
preview, import/export and a state-only startup. Preview errors are isolated.
Manual display name/game association lives in SQLite and never rewrites state
bytes. Groups use the assigned game title. Import/export validates the existing
StateSerializer and preserves compressed bytes. Strict load never silently
falls back. Explicit visible recovery validates a backup and uses M9c AtomicFile,
retaining the corrupt generation. Logical delete removes primary plus backup;
retained corrupt evidence stays. The Manager cannot remove a foreign ROM lease.
Cross-cartridge state starts fully validate an owned state copy before changing the ROM. ROM and state installation remain separate acknowledged transactions: a later
concurrent emulator action can invalidate the state epoch and must be retried.

SQLite uses Microsoft.Data.Sqlite10.0.10, SQLitePCLRaw bundle3.0.5 and native
SQLite3.53.4, recorded in lockfiles/NOTICE. Journal DELETE, synchronous FULL,
explicit transactions and quick_check protect metadata. Corruption does not
silently recreate the DB: a visible recovery action preserves its corrupt file
before creating an empty metadata DB. ROM/state files are independent. This is
not a physical power-loss or portable directory-fsync guarantee.

One Manager mutex per Windows SID is paired with a CurrentUserOnly activation
pipe. A second process forwards at most16 arguments of1024 characters; the UI
queues at most8 activation batches and acknowledges queue acceptance. During a
running action, queued batches are processed afterward in order. --connect,
--rom and --state select/load only after instance readiness. Process.Start uses
UseShellExecute=false and ArgumentList. The emulator remains alive if Manager
startup fails or the Manager closes. Close drains owned tasks before exiting.

## IPC version1

Contracts has no WPF/OpenTK/native-audio dependency. The local byte-mode Named
Pipe is CurrentUserOnly. Discovery contains PID, UTC process-start ticks, random
pipe name, version and executable path; dead/reused PIDs are ignored. Discovery
reads are bounded to8192 bytes and listings to256 candidate records. Stale files
are ignored rather than deleting another process's possibly renewed record.

UTF8 JSON has a32-bit little-endian positive length prefix,2MiB maximum, depth16,
150000 nodes,122368 array elements,64 object members,65536-character strings and
no duplicate/unmapped envelope members. UTF8 is strict. Requests require a32-hex
requestId, command length1..64 and object/array payload. Hello/version is mandatory;
Hello and each response are correlated by requestId and version, including the second-instance activation client. Hello capability payloads are bounded and validated; Manager activation advertises only Activate, while the emulator advertises its actual commands. Unknown commands
return UNSUPPORTED, version mismatch PROTOCOL_MISMATCH, missing Hello
HELLO_REQUIRED. Malformed framing closes only its connection. Other stable codes
include REVISION_CONFLICT, STALE_EPOCH, NETWORK_CLIENT_LOCKED, BUSY, INVALID_DATA,
CANCELLED, CLOSING and FAILED. Raw OS error messages remain bounded.

There are four fixed client loops, at most32 requests/connection,30s connection
lifetime,15s per-request budget and20s client budget. Requests are sequential;
pipelining is a protocol violation. EOF during an operation cancels its handler
and joins it before reusing the loop. Server shutdown cancels and joins all loops
and handlers. Cancellation cannot undo an already committed atomic rename or
worker command; an interrupted caller must refresh status. There is no event
stream:1s polling coalesces status and introduces zero unbounded event queues.

All product requests enter the existing bounded UiMailbox; live hardware remains
exclusively on EmulationWorker. ROM operations use the M9c battery/session lease
transaction. State decode stays detached and installs only at the expected worker
epoch. Media is single-flight and rejected as BUSY when occupied. Remote clients
actually reject hardware/state operations. IPC failure does not stop local audio
or emulation. The optional Close command is acknowledged before a deferred normal
window shutdown; it drains the same existing persistence/network lifecycle.

Concurrent legitimate settings writers use a path-scoped named mutex with a2s
acquisition limit around the existing M9c atomic writer. The coalesced host writer,
final host save and offline Manager use the same path. Permanent lock/storage
errors remain visible; there is no unbounded IOException retry.

## Evidence and boundaries

New portable groups are separate from the previous256 portable groups; new actual
Windows Manager groups are separate from the previous23 Windows groups. They
exercise real separate processes/pipes, malformed/null/UTF8/depth/count/size input,
handshake/version/unknown commands, parallel payload correlation, PID reuse,
actual15s partial-frame timeout, handler-entry/exit cancellation, joined shutdown,
real inserts before DB rollback/cancellation, canonical copier identity, explicit
DB recovery, byte-preserving State9 metadata/import/export/recovery/delete, and
two simultaneously released settings-writing processes.

Actual native tests start two emulator windows, select exact instances, mutate a
real menu between settings reads, reject stale epochs, compare paused hardware/
SRAM state bytes after rejected loads/leases, preserve SameROM SRAM, run the real
TLS client lock, launch F10's relative Manager, deliver a second Manager's arguments
while another action is pending, and start a fresh emulator from a state-only
Manager launch. WPF's actual Dispatcher/window lifecycle and RenderTargetBitmap
render all five populated pages; shared UI handlers apply settings and recover a
corrupt state, verifying the restored state bytes and persisted metadata name.
DLL/EXE/deps/runtimeconfig identities bracket native runs, including nested Manager
outputs. All apphooks are synthetic; Windows was locked. No OS focus, physical
keyboard/gamepad, SNES/DAC, clean VM, hours-long soak or performance acceptance is
inferred. Published hardware tests remain the user's chosen route for later
console validation. See baseline m9e.json for the final clean gate and archive IDs.

Offline settings reads and discovery checks execute on a bounded operation worker rather than the WPF dispatcher. Recovery, unavailable/default and normalization notices remain visible in a dedicated settings warning area across status refreshes. The Settings IPC DTO also carries the live instance persistence notice. Three separate WPF processes verify actual recovered values (23), unavailable defaults (80), normalized values (100), retained source/backup bytes and rendered warning areas. Native product identities are recorded immediately before and after every owned process start.

## Frozen implementation handoff

Implementation commit `a397fca7e49ffd4bcbe97920cd400755d097d458` passed
`python tools/test-lab.py --windows --full` with actual Exit0 (session92715):
267 portable groups and31 Windows groups, including11/8 separate Manager groups.
All previous256 portable and23 Windows groups remain present. Both earlier
successful full gates and all development failures remain separately retained;
none substitutes for the third clean implementation gate.

`artifacts/m9e-implementation-evidence/` contains three nonnested ZIPs with56,524
entries and27,390,121,287 uncompressed bytes. Every archive and entry was checked
for SHA256, CRC32 and length; every entry was decompressed and its staged and
live original independently reread. The native identity boundary covers367
DLL/EXE/deps/runtimeconfig files. `files.json` is a top-level array of per-entry
archive/path/original/staged/hash/CRC/length records, matching the prior evidence
manifest shape. Archive identities and exact counts are in baseline m9e.json.

Acceptance SHA256:
`3601576de14eed4e3df38b61b51ed80181f9e3cd792746d92ec718a09133732c`.
Manifest SHA256:
`f93b6504f11edd2811bdccf2dd058dfa4cb4b3c3fe2e8f6e066fc32765b319e2`.

The active freeze log, user-owned ROM directory and `artifacts/user-smw/` are
excluded. No user ROM, installation, file association or distribution release is
part of M9e evidence. After freezing, only this contract and baseline m9e.json
change in the docs-only handoff commit; final source/bin audit is separate.
Independent Root acceptance remains pending. Existing performance, audio,
physical-hardware, prolonged-soak and later distribution/CI limits remain open.
