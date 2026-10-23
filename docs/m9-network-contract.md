# M9d network contract

M9d implements LAN TLS and the local-testable TLS/E2E relay path. Independent
root acceptance is pending. This is not M9, distribution or release acceptance.
The own C# hardware core and format9 bytes are unchanged. Formats1–8 remain
rejected. The18 M8 whole-console audio differences stay red;7000 raw SPC bus
mismatches retain their source adjudication. M9a sustained realtime remains red
(38/62 NTSC windows below1x; PAL sustained unexecuted). No physical SNES, input,
DAC, OS-focus, clean-VM or multi-hour soak result is claimed.

## Wire and ownership

The handshake requires protocol `0x534e0004`. Version3 peers receive a protocol
mismatch; old bytes are never interpreted as the new layout. The complete VID3
and AUD2 codec payloads remain unchanged inside a version4 media envelope:
little-endian signed64 console/worker epoch, signed64 stream epoch, then the
complete original codec bytes. VID3 retains every native ARGB pixel, parity,
width/height/stride, display/timing interlace, overscan, field index, master start/
end clocks and rational master/APU rates. Identical pixels are still transmitted
when field metadata changes. AUD2 remains lossless native32000Hz stereo PCM16,
with original sample index, DAC crystal timestamp and modulo32 sequence. Native
DAC timestamps begin at672 and advance768 crystals; a sample index is not a
master-clock timestamp. Other source rates keep running in the core and are
explicitly muted rather than labeled32000Hz. Equivalent rational rates work.

Message14 publishes a68-byte timeline. Offsets0/8 are the console/stream epochs;
16/24 the master numerator/denominator,32/40 the APU numerator/denominator.
Bytes48–51 are strict boolean paused/turbo/muted/hardware-picture-invalidation;
52/60 are signed64 hardware generation and input epoch. All identities and rates
are validated before use. Hardware generation and input epoch come directly
from the single core worker. Reset/Load/ROM advance both; pause/resume advances
input epoch; pure turbo retains held input. These host metadata are outside
hardware savestates. Coalesced Reset→Turbo/Load→Turbo/ROM→Turbo cannot conceal
hardware replacement. Every host epoch/mute/rate transition starts a fresh stream
and clears stale media. Hardware changes invalidate the UI picture even while
paused; mode changes may retain the owned last picture for overlays.

Only `EmulationWorker` owns the live machine. Network encoding copies detached
completed fields/samples outside every UI-facing lock and rechecks current worker
status before queue publication. A delayed old `FieldOutput` cannot become a new
stream. Host status and media encoding have separate exception boundaries from
local audio. No socket write runs on the core owner or UI. The network menu also
runs Stop/Leave/confirmed-pin persistence on its tracked background operation;
Close drains it. Existing F10 network rows and explicit changed-certificate review
remain. The row displays the actual LAN/relay fingerprint and recovery status.

## Input authority

All joins initially have observer permission, including a requested Player role.
The host explicitly grants port1/2/both. Observer permission never stores active
input. Each permission grant/revoke/port change or worker input-epoch change
atomically neutralizes held state and advances a32-bit permission generation.
Version4 InputState carries this generation in the frame Sequence field. Packets
from an earlier generation are ignored permanently. Both receiver and client
require a new neutral release before accepting held buttons after such a change;
the client cannot repackage the old physical hold with a new generation. Its
first neutral transition uses the critical FIFO, so latest-input coalescing cannot
erase it. Focus suppression uses the same release rule. Pure mute/catchup/stream
reanchor and turbo do not revoke unchanged controller authority. Pause/ROM/Load/
Reset/revoke/regrant, Kick and Disconnect neutralize the appropriate boundaries.

The reserved C64 keyboard message is unsupported by the SNES session. LAN Kick
retains the existing session-local IP ban, capped256; exhaustion rejects new joins
until server restart. Blind relay channels expose no stable peer IP, so Kick closes
that channel and does not accidentally ban every client named `relay`.

## Resource and shutdown policy

- At most16 active clients and4 concurrent SnesNet handshakes. Accepted sockets
  are registered with cancellation before TLS; pre-TLS/authentication uses8-second
  read and2-second write limits. Promotion/start/task registration is atomic with
  Stop's snapshot. Stop aborts sockets and fully joins handshake/send/receive/accept
  tasks; it does not abandon them after250ms.
- Each server client has32 FIFO controls,8 audio blocks (each at most4096 native
  pairs), and one latest video field. Critical controls are never arbitrarily
  dropped. Control overflow disconnects the whole slow connection visibly. Audio
  pressure drops oldest presentation blocks, which the receiver detects through
  sequence/sample/crystal gaps; native hardware execution is complete. Video is
  latest-only. Codec work runs outside the queue lock.
- The client has32 FIFO controls plus one latest input and one send worker. Input
  and latency calls do no stream I/O. Socket cancellation interrupts pending sends.
  Callback-initiated cleanup is explicitly owned through `CleanupCompletion` and
  external Dispose joins it. Auto-reset handles are disposed after their loops
  finish; no fire-and-forget disposal task is left behind.
- Relay registration allows16 channels,4 E2E handshakes,16 accepted channels;
  each raw channel caps2MiB/128 fragments. An opaque frame/record caps approximately
  1MiB, checked before allocation. Close controls have a bounded16-task owner and
  are drained with the connection. Over-budget channels disconnect the affected
  relay transport visibly; one multiplexed connection failure can end its session.
- The Python relay admits at most64 connections **before** TLS,16 sessions and16
  clients per session. TLS/register has an8-second watchdog and writer drain/close
  a2-second limit. Its password-failure tracker caps1024 entries with global30-minute
  TTL cleanup; an unknown failing address at capacity is rejected without eviction
  of existing bans. Isolated auth fixtures do not connect to arbitrary IPs.
- The client playout queue caps32 actions. Source buffering caps8000 native pairs,
  driver-source mapping8192 segments. Missing/expired mappings are unknown, never
  invented playheads. Epoch invalidation and final A/V callback commit share a
  short commit gate; a callback selected before Reset cannot republish afterward.
  The UI has a separate atomic owned-frame/epoch publication gate and rechecks the
  current identity when drawing. New TCP sessions clear it after the old session
  drains, so reused numeric epochs cannot inherit a disconnected host's picture.

## Software A/V boundary

Video and audio use the same source seconds: `master*masterDen/masterNum` and
`crystal*apuDen/apuNum`, respectively. The actual integer timestamps and rational
identities remain on the wire. The client anchors this timeline to its monotonic
clock with a40ms software scheduling target; gross drift/queue pressure reanchors
and clears stale device/provider history. Late audio/actions and stale video are
counted. Before the first AUD2, video-only sessions use source-clock scheduling.
Muted/unsupported/device-unavailable sessions remain usable as silent viewers.

Remote audio uses an owned final48kHz stereo provider with exact3:2 phase and
linear interpolation. This differs from the local WDL presentation filter: linear
interpolation is not a bandlimited reconstruction filter and can attenuate high
frequencies/retain spectral images. This is a declared frontend tradeoff; native
PCM, hardware DSP filtering and every golden remain unchanged. Packet partitions
with sufficient prefill give identical output. Missing interpolation lookahead
produces explicitly timestamp-less padding, then resumes the retained phase when
the neighbor arrives. Startup padding, jitter gaps and later refill cannot make
new samples appear already played.

The provider records real/padding spans at its final output. WinMM's consumed-byte
position (including source/resampler/device buffering) maps back through those
spans to a source crystal position. The32-bit byte cursor is unwrapped modulo32
and tested across multiple hours; an unexplained backward cursor fails visibly.
Video waits for a valid audio source playhead and is compared at the delivery/UI
publication callback. Metrics include presented audio/video, number of actual
non-null driver comparisons, queue residence, scheduling error including late
rejected actions, dropped actions, reanchors and maximum software A/V distance.
This is a software/driver boundary. Subsequent UI redraw/vsync/display transport
and physical DAC latency are not measured. A bounded queue alone is not an A/V
sync result. Local WDL output and its existing larger source/device buffer are
unchanged and are not retroactively called synchronized by this work.

## TLS trust and persistence

PFX and pin JSON use M9c `AtomicFile` validation/flush/backup/replace. A separate
cross-process transaction lease covers the complete read/modify/write or
create-if-absent operation, so simultaneous emulator instances cannot overwrite
one another's new pins or return different initial PFX identities. JSON is bounded
64KiB/depth8/256 pins and strict64-hex fingerprints. Invalid existing certificate/
trust data cannot silently become fresh TOFU. A valid backup reports recovery;
known backup pins must still match. An unknown endpoint after backup recovery is
rejected for explicit review, because its more recent pin may have been lost from
the primary. Changed certificates are rejected with old/new SHA256 and the existing
explicit review/reconnect path. Known-recovery status survives ordinary welcome/
permission updates and is shown in the network UI. Atomic file guarantees retain
the M9c software/flush boundaries; no physical power-loss guarantee is added.

## Validation

`Core.Tests --network-only` is14 separately reported portable groups; the Python
`tools/test-network-relay.py` adds3 parser/auth-contract groups. Windows
`Tests --network-only` has13 real local Socket/TLS/Relay/WinMM groups, in addition
to the unchanged original10 Windows groups. `tools/test-lab.py --windows --full`
adds both network supervisors and captures28 product/Core/Runtime DLL/EXE/deps/
runtimeconfig identities before and after the native starts. Its M8 integrity pin
changes only for these adapter invocations/identity checks. No hardware fixture,
golden, known-red threshold or old evidence archive is changed.

The socket tests include two LAN clients and two local relay clients, fragmented
handshake/oversized relay headers, strict malformed names/roles/booleans/counts,
wrong passwords, rights under held/stale packets, observer, focus-neutral, Kick/
Disconnect, changed certificates and recovered unknown pins, two overlapping real
persistence processes, handshake/Stop and callback/reset barriers, slow control
clients, a slow server with100000 nonblocking input submissions and concurrent
shutdown, actual pre-TLS relay admission pressure, dynamic geometry/parity/clocks,
old output rejection and unsupported source rate. Negative input checks use actual
receive/Pong or processed-input barriers, not sleep-only snapshots.

The real A/V observation retains cold-start/mute rows, waits for bounded stable
unmuted driver readiness, then measures a separate5-second window. It requires
at least30 audio callbacks,30 video callbacks and30 actual driver comparisons,
maximum40ms software A/V distance and no provider fault. Cold JIT/catchup is
separate, never reclassified as unmuted playback. The original native runtime and
persistence probes remain real OpenTK/GL processes with synthetic app callbacks;
Windows stays locked, with no OS input or browser automation. Final clean-commit
counts/raw reports/identities and frozen hashes are recorded after execution in
[the M9d baseline](baselines/m9d.json). Independent root acceptance is still pending.

### M9d clean implementation gate and frozen handoff

Implementation `b432a976e3315ecf45f55a7c5db2ba35dc54eaf2` passed
`python tools/test-lab.py --windows --full` with exit0 on a clean checkout:
256 portable groups (previous239 +14 network +3 relay) and23 Windows groups
(previous10 +13 real socket/TLS/relay/driver groups). Full CPU semantics and bus
each executed5,080,400 cases;26,184,846 bus cycles and256,000 SPC cases passed
their existing gates. Native runtime and all four persistence modes passed;
the same28 DLL/EXE/deps/runtimeconfig identities bracket every final native start.

The real A/V run retained2.3852309s cold readiness separately, then observed
5.0086057s with297 video,301 audio and297 actual driver/video comparisons;
maximum software A/V distance33.308853ms,23 presentation drops, no provider fault.
Maximum queue residence265.064900ms and scheduling error173.170717ms include
late rejected/cold work; they are not physical device-latency claims.

The three frozen archives contain8,771 entries /5,745,970,051 decompressed bytes.
Every archive and entry has SHA256/CRC32/length identities; every entry was read
back and compared to both staged and live originals. No nested ZIP or active log
is included. The separate developmental archive retains available failed-run
captures and explicitly records early overwritten child-report limits. A first
incomplete archive attempt was rejected for selecting its own active supervisor
log; only that owned freezer was stopped and the corrected run was verified.
Three prepared Root helper scripts are ancillary copies in the report archive;
they were not run as implementation evidence.

See [M9d baseline](baselines/m9d.json) for exact hashes, counts and raw paths.
M9d still awaits independent Root review. M9a sustained performance, M8 audio
reds, source-adjudicated raw SPC differences, distribution/Manager/CI, prolonged
soak and M10 remain open; publish remains PRE-M0. No physical SNES/input/DAC,
OS-focus or clean-VM claim is added.
