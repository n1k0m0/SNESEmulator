# M9b worker, input and presentation contract

M9b moves the live own C# console into `SNESEmulator.Runtime`, a portable .NET10
host assembly. The hardware scheduler, digital renderers and format9 hardware
bytes are unchanged. M9a sustained NTSC/PAL realtime remains an open gate; this
package is not M9 or release acceptance. The18 M8 whole-console audio differences
and physical INIDISP/analog/revision questions remain unchanged.

## Owner, control and shutdown

`EmulationWorker` constructs one live console before publishing it and owns all
later CPU/bus/PPU/APU/controller/cartridge/state access on one named background
thread. UI has no live `SnesSystem`. Its public commands are Insert, Reset, Load,
Capture, Battery, Mode and deterministic paused Step. Commands use a FIFO bounded
at32, complete asynchronously and reject excess requests with an observable
exception. Cancellation before execution prevents mutation; an executing command
is atomic and completes before shutdown. Stop cancels the remaining queue and
joins the owner. Core faults stop advancement and expose full fault text; failed
load validates a temporary console and leaves the valid live machine intact.

The UI has one tracked media operation, one preview read and one network setup
operation. Disk reads, battery writes, state compression and preview decoding
run outside UI/core advancement. Capture serializes uncompressed format9 bytes
at an owner command boundary; later compression/I/O uses those owned bytes.
Large capture commands can delay advancement; they are not hidden as a realtime
success. Closing waits for owned I/O before final SRAM capture/write, then joins
presentation and worker and closes transports. The same-ROM reload flushes the
current SRAM before reading its target battery. Backup/recovery/fault-injection
and periodic durable SRAM saving remain the next persistence package.

Critical UI completions have a16-slot backpressured mailbox. Network/audio
status text coalesces independently; a status burst cannot discard a completion
or strand `mediaBusy`. Tests exercise saturated completions and cancellation.

## Time and input

The owner uses a monotone clock, cumulative Int128 rational deadlines and actual
master clocks elapsed by each `RunFrame`. NTSC short fields, PAL and interlace
are not rounded to60Hz. UI60Hz/Vsync is presentation only. The ordinary wait
uses cancellable event waits with ceiling-millisecond conversion and at most50ms
sleep. Input wakes can interrupt waits but do not change deadlines. A slow
machine retains its deadline debt and executes every hardware tick; it never
skips CPU/PPU/DSP work to catch up. The UI shows lateness over250ms, derived each
draw so it clears when caught up. `UI FPS` explicitly names the window rate.

Pause/resume, reset, load and cartridge replacement start new host epochs,
rebase host pacing and neutralize the host input mailbox. M9d additionally gives
hardware/input discontinuities explicit identities; pure turbo retains held input
while changing its pacing/presentation epoch. All these boundaries
clear queued presentation/audio history. Pause/turbo retain the last owned
image for overlays; reset/load/ROM replacement invalidate old images. Epochs
and host queues are not hardware savestate fields. Load/Capture therefore
preserves even held controller hardware bytes exactly until the next defined
input application.

Live input is a bounded latest-value mailbox with monotonically increasing
submission sequence. The owner samples both ports atomically immediately before
each `RunFrame`; a loaded partial field uses its actual current masterclock.
`FieldOutput` records epoch, sequence, exact input-application masterclock and
both applied ports independently of raw-field start/end clocks. Paused Step
accepts explicit per-field inputs for deterministic offline replay. Arrival
relative to the owner boundary determines live ordering; host input arrival
timestamps are not retroactively inserted into already executed hardware.

Local gamepad uses the selected device (or first connected device), not an OR
of all devices. Twelve SNES actions accept button, arbitrary signed-axis and
hat-direction bindings. Old eight-button settings migrate without losing their
assignments; directions get conventional defaults. Capture waits for neutral
then learns one raw control, and Delete clears a binding. Focus/device/config/
port transitions require neutral before new actions. Background input cannot
open menus or capture controls. Menu-era held buttons stay suppressed until
released. Keyboard/gamepad local input is neutral in menus/background; remote
client sends neutral immediately. The prior integration cases still run. M9d closes the revoke/regrant held-input
gap with generation-tagged input and a fresh neutral-release boundary; see
[the network contract](m9-network-contract.md), pending independent root review.

## Video/audio handoff and limits

`Ppu.CompleteField` already copies native RGB555, brightness and ARGB into three
owned arrays. The worker shares only that readonly completed object, never
`Ppu.Frame` or its mutable live planes. Arrays are not pooled or reused. Tests
retain old fields while many later fields run and compare all three planes.
Native256/512×224/239, field parity/display/timing interlace and rational clocks
pass through unchanged. The host draws raw fields in the existing4:3 viewport.

One output FIFO contains at most8 completed fields and owned native sample
blocks. If presentation stalls, it clears stale entries, counts each omitted
presentation field and retains the newest. Hardware continues completely.
A separate presentation task performs network encoding and local audio writes.
It drains each field's native DSP queue once; UI consumes only Latest video.
Clock gaps/epoch changes clear source queues and resampler prefetch/history.
Local source buffering remains250ms/32000bytes. Turbo and >100ms deadline debt
mute local/AUD2 presentation and clear audio history, while native samples and
their timestamps remain available in `FieldOutput`. Catchup-muted fields are
counted. When debt clears, presentation resumes at the current hardware time.

AUD2 and the existing device adapter require exactly32000Hz. Arbitrary profile
rates are compared rationally (`APU crystal /768`); an equivalent unreduced
ratio works. Other rates, including32040Hz, keep emulating correctly but are
explicitly muted locally/on AUD2 with a visible lab-rate status. No profile is
silently labeled32000 and neither VID3 nor AUD2 is changed. Full wire epochs,
host pause packets, client clock alignment and broader A/V protocol work remain
the later network package.

Video/audio sink errors are isolated; one failed sink does not stop the other
or the hardware. The presentation handoff reports and mutes that sink until the
next timeline epoch. A local device failure leaves network audio working;
unavailable local devices stay muted until application restart. Current metrics
record output counts, omitted fields, native sample counts, epoch/clock gaps,
catchup muting, source-buffer bytes and overflows. They do not measure soundcard
DAC latency or prove continuous physical sound output.

## C64 read-only comparison and validation

C64 `f01750e17a2c3f4cabfd57dc0c470dac93a91a23` was inspected read-only, especially
the main menu, first-device input, configurable axes/hats, controller actions
and suppression-until-release paths. Its preexisting Manager/IPC files remain
untouched. SNES keeps F10 Settings/Network/Media/Reset, F11 fullscreen, F12
Save/Load/Delete/Preview, F9 turbo, F6 diagnostic, media initial search/page keys,
drag/drop, A confirm/B back, X save/Y delete, Start+Select main menu and
L+R+Select saves. Full keyboard-to-button remapping and arbitrary configurable
UI action chords are not claimed here.

`Core.Tests --runtime-only` compares actual direct and worker hardware bytes,
native planes, timestamped stereo and SRAM for NTSC/PAL/interlace/hires, including
partial-state continuation. It also tests rational million-field deadlines,
live controlled-clock pacing, pause/cancel/fault/pressure/shutdown, old fields,
same-ROM SRAM order, UI mailbox saturation and synthetic focus/device/chord
behavior. Software replay equivalence is not console hardware evidence.

`tools/test-ui-runtime.py` runs an actual native OpenTK app with isolated data,
synthetic app-key callbacks, real window/fullscreen/menus/state I/O and GL
screenshots. It closes during an outstanding Save and fully parses every final
state after shutdown. Periodic raw app rows separate wall time, masterclock,
field count, deadline debt, UI intervals and audio/queue counters. `--seconds60`
extends actual app pacing observation; it is not an hours-long soak.

An attempted independent OS-key/focus check found the Windows lock screen after
the prescribed activation recovery. Computer Use stopped without attempting
to bypass it; only the owned empty probe process was terminated. Real OS focus,
physical hotplug/gamepads, physical SNES, clean-VM/no-SDK launch and several
emulated hours of A/V/threading operation remain unexecuted. Existing native
app instrumentation and synthetic focus tests are separate evidence.

`tools/test-lab.py` adds the runtime supervisor and native UI probe for Windows.
Its M8 integrity source pin changes solely for those additional gate invocations.
No acquired fixture, golden image/audio, known-error expectation or M8 adapter
changed. `publish/` remains PRE-M0 and must be rebuilt in a later distribution
step before the preferred launcher publish path is current.


## Clean implementation result

Clean implementation `43c566d3874bb9828ecde9ccdf4c9a56c209ce5f` passed
`python tools/test-lab.py --windows --full`, Exit0:209 prior portable groups,
16 separate runtime groups,10 Windows groups and the additional actual native
runtime app probe. All14 frozen M9 console/observer/state comparisons pass.
Core DLL SHA256 is `37916f053a79dc8d10f5ca20e399a239d0bb7fc1d12b97a0b035c6306469e0b6`.
The final document commit changes no executable source or expectations.

Three separate60-second actual app observations ran sequentially on that clean
commit, with isolated data and own IPL. NTSC/PAL diagnostics cover actual
region-driven field pacing; the unchanged M8 game runs neutral title/title-music.
App key callbacks operate menus and do not set the polled core keyboard state.
Their per-second samples, complete state files, fully parsed close-with-save
results, GL screenshots and hashes are frozen. End-of-window metrics:

| App workload | Wall seconds | Fields / hardware seconds | Initial → final debt seconds | Catchup-muted fields | Device overflows / queue drops | Maximum observed UI interval ms |
|---|---:|---:|---:|---:|---:|---:|
| NTSC diagnostic |60.0063374|3668 /61.0328185|0.9490 →0.00150|235|0 /0|143.02|
| PAL diagnostic |60.0057463|3048 /60.9514925|0.8355 →−0.00272|160|0 /0|229.26|
| NTSC game title/music |60.0057579|3684 /61.2990467|1.3962 →0.00015|419|0 /0|171.78|

Hardware seconds exceed the measured subwindow because it includes recovery of
initial startup/JIT debt; there is no deadline rebase or skipped hardware tick.
Last approximately15-second sampled windows are0.9994/0.9998/0.9994x, including
whole-field sampling quantization. These are paced app observations, not an
unpaced throughput or sustained realtime acceptance. Output queues reached one
entry in the sampled rows. Audio source buffers remained bounded. UI maxima
include startup, GL/fullscreen and screenshot instrumentation; no physical
key-to-display or DAC latency claim is made. Earlier unmuted development runs
with4/5 device overflows remain unchanged as negative presentation evidence.

A separate native preview probe waits for the actual asynchronous preview,
records its visible thumbnail and fully parses the resulting state; its small
source/project and output are archived. A separate actual fullscreen main-menu
snapshot is2560×1440. The older `--ui-smoke native` hook with a startup ROM does
not wait for a completed field, and legacy startup menus can race async load
completion. Those modes are not readiness-safe image evidence. Use the new
runtime probe (which waits for20 worker fields) for startup-ROM/Hires checks.

The first full attempt caught a compressed-byte regression from a BinaryWriter
leave-open SyncFlush. Its1,550,497 decompressed bytes remained identical, SHA256
`e1b7efc30d5e9ddb44fb357c35b494a3123be9a7e3966bfca8b7de0f252675fc`.
The direct Save path now closes GZip exactly as before and retains all frozen
compressed hashes. Capture→off-thread compression may choose different Deflate
boundaries while preserving every raw format9 byte. The overstrict initial
helper test and both red attempts are retained; no frozen expectation changed.

Frozen final evidence: `artifacts/m9b-implementation-evidence/acceptance.json`,
three archives and7131 files, reread with CRC/length/SHA256 checks against every
source. Manifest SHA256:
`c1aae8e27297792907d6521c2f3b9dd2b2158ee0b9248ffa220b3cd4fae0ab55`;
acceptance SHA256:
`71fd15da09f4d2ecd366b89bda3b150f9dc2facb7a15f6987d3ee01f3576d0e6`.
The earlier preliminary freeze remains unchanged separately; its active output
log was excluded from the final payload. The parent independently reread all
three final archives/7131 files (3,018,207,938 decompressed bytes); its archive
verification was followed by independent product acceptance on1074c36: full
Windows gate Exit0, five literal native checks/122368 pixels, unchanged audio
SHA256 and actual Hires/async-preview probes. Root evidence:
`artifacts/root-m9b-evidence/acceptance.json`, manifest SHA256
`83e689cefdd343d1f11f16dd7a286e05c348601145f60701b13b03ad3e5cd6f7`,
acceptance SHA256 `f33acdf207a5061199d9c7ce17f8f70908d4cdd68d700ab2e74fc74bb560dcec`.
Its3 archives/1020 files/43,635,978 bytes were independently reread.
See [M9b baseline](baselines/m9b.json). M9a sustained realtime, several emulated
hours of A/V/threading, physical OS focus/gamepads, transport and
current distribution remain open as listed above. Persistence is now the separate
[M9c implementation](m9-persistence-contract.md), independently accepted on `cfe4ed2`.
