# M6 digital S-DSP and stereo transport contract

Own C# product implementation; independent parent acceptance pending. M0–M5
were independently accepted, most recently M5 on
`2dc8cf021ef7394f38df1db9932b443a23543b30`, full gate exit0,
`artifacts/root-m5-validation.log`. This project has no original console capture
equipment. Digital reference agreement is distinguished from measured hardware.
Final clean gate identities and counts are recorded in [the baseline](baselines/m6.json).

## Hardware execution

`SnesDsp` is a separately clocked micro-operation engine attached to the same
64KiB physical RAM as SPC700. Its clock is always24 crystal ticks, independent
of TEST waits, SLEEP and STOP. Thirty-two phases produce one stereo pair;
nominal24576000/768=32000 pairs/second. Phase0 first executes at crystal24;
the first phase27 DAC pair has index0/crystal672, then increments by768.
This deterministic epoch is a selected test initialization, not a measured
reset-release phase or the tolerance of a particular physical resonator.

The data timeline independently schedules each of8 voices' operations, with
voice0's pitch-high/BRR/interpolation operations split across22/25/30.
Directory, SRCN, pitch bytes, ADSR, volumes and global register latches have
separate read deadlines. BRR bytes and echo words use actual shared RAM at
their phases; no whole-sample RAM/register snapshot drives rendering.
Directory addresses and all RAM byte accesses wrap at16bits. Voice history is
a twelve-entry circular buffer, separate from echo's eight stereo history slots.

BRR uses signed nibbles, invalid-range13–15 handling, all four recursive filters,
arithmetic shifts before additions, signed16 saturation followed by doubled
signed16 wrap. Four-point Gaussian interpolation wraps the first three terms
before the fourth, saturates and clears the low bit. Pitch modulation uses the
previous voice's envelope-scaled output; voice0 excludes PMON. Noise replaces
the interpolated input. End flags, loop directory pointers, key-on preparation,
five setup samples, alternating key windows and key-off release all continue
to run even for silent voices. ENVX/OUTX commit at their own phases.

The envelope has attack/decay/sustain/release state, all five GAIN behaviours,
the hidden candidate envelope and the unsigned bent-GAIN comparison. Envelope
candidate/mode changes can occur while the rate gate prevents envelope commit.
The30720-state counter decrements once per sample, not once per SMP cycle.
Rates use periods2048..1, with1040/536 offsets; rate0 never fires. Noise has a
15bit LFSR, clocked by the same rate network. Signed voice/master/echo/feedback
volumes and saturation/wrap at each individual mixing stage are explicit.

Echo reads left/right at22/23, spreads eight FIR taps across22–25, wraps the
first seven tap sum before adding the signed eighth, and clears its low bit.
Master left is computed at26, master right/pair output at27. Feedback is
prepared at26; FLG's echo-write flag is separately sampled at28 and29 for
left/right writes at29/30. ESA is latched at29. EDL is acquired only when
the previous echo offset is zero; delay0 cycles a single stereo word, while
delay15 traverses30720bytes. Echo writes can overwrite code, directory, BRR,
I/O underlay or IPL underlay because they access physical RAM, not SMP MMIO.
FLG mute suppresses digital pair output; reset clears envelopes through the
scheduled voice logic. The reference's rapid DAC-mute pulse limitation remains
an explicitly unverified physical effect, not a claimed bit-exact analogue model.

## Integration, observation and state

At a shared console timestamp the existing order remains typed events, PPU,
APU crystal effects, then CPU/DMA effects. DSP precedes an SMP bus effect on an
identical crystal endpoint in this selected digital timing model. F2 retains
all8 address bits; F3 read mirrors mask bit7 and writes with bit7 set remain
suppressed. M6 corrects the old storage placeholder: ENVX/OUTX are writable,
including their pending output buffers; ENDX writes clear its buffer/register.
The existing M4 interface test now asserts that source-supported behaviour.

The scheduler splits at DSP boundaries as well as existing SMP and PPU
boundaries. No masterclock/APU rational ratio or TEST timing was rounded.
Public `Apu.DspPhaseObserved` fires after DSP state advances, and after a tied
SPC cycle has appended its completed bus record. It includes crystal, phase,
sample count, memory read/write rows and encoded voice/stage operations
(`voice*100+stage`;30/31/32 represent the split third stage).
Observers may save the complete console. There is no core callback into a
sound device or network thread.

Savestate **format7** contains DSP voice history, key/envelope state, every
pipeline latch, fractional pitch, FIR history, echo position, counter/noise,
phase/next crystal and sample index. Echo contents are already in APU RAM.
State validation checks lengths, arithmetic widths, clock/phase/sample
consistency and firmware identity before the temporary machine is installed.
Formats1–6 are rejected with a format7 explanation; there is no invented
migration of missing pipeline state. M3's explicit electrically isolated
APU fixture freezes *both* processors; its frozen DSP clock is validated as
suspended, rather than silently advancing RAM or weakening the normal check.

Power-on initializes deterministic zero DSP registers with FLG=E0 and empty
pipeline/history. This intentionally does not imitate random power-up patterns.
Warm Reset uses the selected reference's soft-reset contract: retain voice/BRR,
echo history and most registers, set FLG=E0, reset noise/rate/echo-pointer/key
window/phase epoch. Console Reset starts a new crystal/sample index epoch and
clears presentation. Actual external reset-pin behaviour remains M8 evidence.

The core output queue is a bounded8192-pair presentation queue; overflow drops
the oldest pairs and counts the drops. Draining or clearing it never changes
hardware. It is not serialized and starts empty on restore. Hardware sample
count continues regardless of output consumption, mute, pause presentation or
network clients. Calling RunUntil at the current boundary consumes no new time.

`LoadLaboratorySpcSnapshot` accepts an intentionally limited own SPC v0.30
container convention: TEST0A, IPL disabled, full physical RAM underlay in the
RAM field, zero64byte extension, header CPU registers and128 DSP registers.
Both port directions start from RAM F4–F7. Hidden timers/dividers and DSP
history start at the declared reset epoch. Ambiguous IPL-enabled/XRAM
conventions are rejected. This is a test playback initialization; generic SPC
music playback and exact reconstruction of an omitted hidden pipeline are not
claimed. Format7 is the exact running-console state format.

## Presentation and networking

`StereoPcmPresentation` accepts signed16 little-endian interleaved native stereo,
applies user volume and WDL resampling to48kHz, and limits host buffering to250ms.
A stable synchronized sample-provider wrapper resets both the buffered source
and resampler/prefetch history on clear/overflow. Menu pause, ROM insertion,
reset and load clear old sound. The frontend splits valid core queues into
at most2048-pair transport/device blocks. The audio diagnostic menu launches
actual65816→IPL→SPC code, which creates a looping BRR sample and two different
pitched left/right voices. It contains no artificial host oscillator.

Network handshake version is `0x534E0002`. Audio payload is40bytes followed
by **lossless** stereo PCM: magic AUD2, rate32000, channels2, frame count1..4096,
first sample index, crystal timestamp, uint32 audio sequence, codec0. Exact
payload length, signed/overflow bounds and native format are checked before
allocation/playback. Legacy mono and implicit IMA-ADPCM are removed; no lossy
codec is silently selected. The transport message's wallclock timestamp is
separate from the payload's emulated crystal timestamp.

Per-client outgoing audio remains bounded to eight packets. A receiver rejects
duplicate/backward sequences, handles uint32 wrap and flushes presentation on
sequence/sample/crystal discontinuity (including host reset/load). It never
fills a gap by advancing emulated hardware. TLS, encrypted relay, video and
input retain their existing transport. Two LAN clients and relay delivery
check exact left/right PCM and metadata. Further product threading, dynamic
video geometry, device hotplug and latency policy remain M9.

## Independent evidence and reproduction

`testdata/dsp-reference/manifest.json` pins71 synthetic input/reference-output
pairs rendered by the **external executable only** Blargg accurate DSP at
`ec8ee2bbe30451614c1d02a83f7af1c97d497d45` (LGPL-2.1-or-later, Shay Green).
The original adapter/generator and all compressed fixture bytes are hash-pinned.
No reference DSP code is copied, translated, linked or shipped in the product.
The reference repository/compiler output stays in gitignored local test data.
The redistributed LGPL notice, original synthetic data and oracle provenance
are retained. No music, commercial ROM or Nintendo firmware is in this corpus.

The corpus compares453726 stereo pairs (387047 nonzero), final RAM and all128
registers. It **executes**14527424 DSP phases; that count is not a claim to
have independently observed every electrical RAM access. One short case
independently compares262144 register observations plus2048 complete ARAM FNV64
digests, one set after every phase. It contains writes before every phase,
including split left/right windows. Final RAM is also compared byte-for-byte.
Long cases cover the complete slowest attack, increasing GAIN at rate1, a
full fast noise period, maximum echo-delay traversal and active soft reset.

Separate literal worksheets verify BRR shifts/filters/overflow, Gaussian
three-term wrap, ADSR/all GAIN/release, unsigned hidden envelope, rate periods,
noise, FIR intermediate wrap and signed mixing. Six native sample comparator
mutations and four phase/address/direction/value/order mutations must fail.
ROMs and the own SPC container have separately reported identities. The two
pre-M6 upload ROMs remain byte-identical to the independently compiled generator
from2dc8cf (short60384889… and stress93ca50b7…). No old CPU/SPC/PPU expected
values are regenerated. Only the extended generator and format-rejection test
source identities are advanced in their manifests.

768 every-crystal,96 every-masterclock and106 public full-console observer
snapshots resume against uninterrupted references. The full-console checks
include active SPC register/RAM writes, DSP, DMA, IRQ, PPU and due events at
normal NTSC and a clearly labelled synthetic24-crystal/master tie profile.
The native sample tail is compared as well as the complete serialized state.
A separate bounded run executes ten emulated seconds each for NTSC/PAL with
integer crystal/sample counts, video fields, pause and reset. Earlier M2's
one-hour rational-oscillator worksheet remains separate from this executed run.

```powershell
python tools/dsp-reference.py --verify
python tools/verify-dsp-gaussian.py
dotnet run --project SNESEmulator.Core.Tests -c Release -- --dsp-only
dotnet run --project SNESEmulator.Core.Tests -c Release -- --dsp-long
python tools/test-lab.py --windows --full
# Optional independent regeneration; requires network, git and C++ compiler:
python tools/dsp-reference.py --rebuild
dotnet run --project SNESEmulator.Lab -c Release -- rom --diagnostic audio --frames 8 --output artifacts/audio
dotnet run --project SNESEmulator.Lab -c Release -- benchmark-apu
dotnet run --project SNESEmulator.Lab -c Release -- benchmark-dsp
```

The17 DSP groups and ten-second AV group are separately supervised (120/180s),
leaving the129-group M0–M5 core watchdog at240s. Digital comparison and literal
worksheets are software/research evidence, not our own hardware acquisition.
Blargg reports more than100 tests run on hardware; those raw console captures
are not bundled or independently reacquired here. M8 retains that limitation.

## Primary sources and uncertainty

- Anomie/jwdonal S-DSP document, gist revision
  `8b1df7dbf426171a79464765194fc38c9d26861c`, SHA256
  `21b793b10a70ac87e245ec2da4938ec7206e6eb5cdc67b6689770f9c5e0361bc`:
  <https://gist.github.com/nyanpasu64/a0d916ce6924912a7116682bf778e9a0>.
  Published research text is analysed, not redistributed wholesale.
- Shay Green's accurate DSP and research account:
  <https://github.com/blarggs-audio-libraries/snes_spc/tree/ec8ee2bbe30451614c1d02a83f7af1c97d497d45>.
  This is an isolated software oracle and has published mute/reset limitations.
- Exact512 hardware coefficients agree with both Anomie's numeric table and
  the ISC ares mathematical construction at
  `9408cb43d4948fc3ea6e152a307a34348df3fe04`. Only integer coefficient data is
  retained in the product; independent floating-point reconstruction is a lab
  check. Source hashes and the retained ISC notice are in
  `testdata/dsp-sources.json` and `testdata/dsp-reference/ares-ISC.txt`.
- SPC container convention conflict: <https://dgrfactory.jp/spcplay/id666.html>
  describes extension XRAM, while the pinned Blargg state writer uses a different
  ROM/underlay convention. The limited own lab loader explicitly avoids that
  ambiguous case rather than claiming a universal exact SPC restore.

The jwdonal prose places the counter/echo-offset housekeeping on29/30, while
the hardware-tested accurate reference executes them on30/29. This selected
model follows the pinned reference with dynamic phase-level register/RAM
comparison. Physical sub-SMP electrical access slots, reset release, analog
DAC behaviour and chip-revision differences have no new console measurement.


Clean implementation gate: `feb978b22ed73ecef9237146bbfad760ddec3a92`, exit0,
`artifacts/m6-full-validation.log`;208 frozen artifacts and exact hashes are
in `artifacts/m6-implementation-evidence/`. All147 portable and8 Windows groups
pass; source/external corpus and original IPL counts remain unchanged. On
NILSPC1/.NET10.0.12, the comparable active PPU/APU/DSP diagnostic measures18.33
fields/s (M5:21.62); two active BRR voices with forced-blank PPU separately
measure19.40. Neither meets NTSC realtime; M9 must profile without removing
accuracy. The raw CLI audio diagnostic emits4259 stereo pairs, first0/672,
last4258/3270816, no dropped pairs; its hash is a measurement, not an oracle.
The existing `publish/` is still pre-M0; this milestone creates no release.
