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

Mode transitions (pause/resume/turbo), reset, load and cartridge replacement
start new host epochs, rebase host pacing, neutralize the host input mailbox
and clear queued presentation/audio history. Pause/turbo retain the last owned
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
client sends neutral immediately. Server permission/disconnect neutralization
continues through the existing transport contract.

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
