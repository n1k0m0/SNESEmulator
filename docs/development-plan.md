# Development plan: a highly faithful SNES emulator

As of October 25, 2026. Historical starting point: commit `7145184`.

## Ordered completion work

The user requested one implementation agent at a time, with tests, useful code
comments, current documentation, and a final commit/push. Sequence and results:

1. **R1 complete:** persistence/CI correction; exact remote run
   [40042721554](https://github.com/n1k0m0/SNESEmulator/actions/runs/40042721554)
   passed on `b840f105`.
2. **R2 implementation complete:** SPC execution optimization, deadline correction,
   counter-based soak diagnostics and scoped performance evidence. The actual
   final build's duration/soak gates have run; strict timing acceptance remains
   open, as recorded below.
3. **R3 complete within documented scope:** CPU pointer correction, old-state
   rejection, six published functional ROM runs, isolated DSP timeline and an
   explicit reset-epoch experiment. Eighteen whole-console audio rows remain red;
   physical reset/revision claims remain unverified.
4. **R4 complete:** comments/readability and current documentation; SDK Roslyn
   token proof, solution builds, immutable fixtures and 65 targeted groups pass.
   See [the R4 evidence](r4-readability.md). Hardware behavior is unchanged.
5. **M10a implemented:** [DSP1](m10a-dsp1.md), including own uPD7725, explicit
   firmware/board setup, format 10, tests and honest source-conflict inventory.
   **M10b implemented:** [SuperFX](m10b-superfx.md), own GSU instruction/pipeline/
   cache/buffer/graphics model, explicit shared boards, CPU-DMA-PPU integration,
   arbitrary-budget replay and clean Windows/Ubuntu functional evidence.
   **M10c implemented within scope:** [SA-1](m10c-sa1.md), with own second 65816, MMC, DMA/CC1/CC2, arithmetic, timer, bit reader, state continuation and authored integration tests. Seven published first-page hardware timing differences remain open. SuperFX physical
   uncertainties and the original FMULT-R4 ROM disagreement remain open.
   Other cartridge hardware/peripherals require their own scoped evidence.
6. **Final integration executed; strict timing acceptance remains open:**
   [final integration](final-integration.md) records the actual implementation
   `affae58` and tested package. Both long core runs retained slow complete
   600-field windows. Both actual native 3,600-second runs completed: PAL passed;
   NTSC retained 288 post-ready muted fields and 127 remote drops. Scoped
   Windows/Ubuntu functional checks and distribution checks passed; the exact
   tested 972-file package is active and the previous package is preserved.
   Exact implementation-head CI
   [40100657828](https://github.com/n1k0m0/SNESEmulator/actions/runs/40100657828)
   passed on `affae58`. Each later documentation commit has its own exact-commit
   CI status; this implementation result does not certify a different commit.

See [R1](r1-persistence-ci.md), [R2](r2-performance-soak.md),
[R3](r3-accuracy.md) and the [current laboratory status](test-laboratory.md).
M9/release and general original-hardware certification are not complete.
The original milestone checklist below remains a historical development record.

## Goal and evidence

We are developing the entire SNES hardware core in C#. The user interface remains
modeled on C64Emulator. The goal is a deterministic, hardware-faithful core with a
complete base console, PAL and NTSC, and gradually added cartridge chips and
peripherals.

Accuracy is reported per hardware function and console profile. A correct title
screen or a passing opcode test does not establish correct behavior of the entire
console. The target is observable digital behavior, including bus accesses,
timing, graphics and audio. Analog differences between TVs, capture devices and
individual consoles are handled separately.

For unresolved effects, we keep an evidence record: observed behavior, source,
hardware model/revision, test-ROM hash, result and open questions. Comparisons with
other emulators help with debugging. A finding labeled as confirmed on original
hardware requires actual measurements or reproducibly published hardware tests.
Where those are absent, the status remains explicitly unresolved.

## Starting point

The existing suite was rerun on October 22, 2026: **17 passed, 0 failed**,
including the local Python relay.

An independently developed CPU decoder, cartridge mapping, WRAM, DMA foundations,
Mode 0/1 backgrounds, an original diagnostic ROM, menus, savestates and TLS/relay
transport are present. The 17 tests protect this foundation; they are not evidence
of general SNES compatibility.

Historical gaps in the starting point (gradually replaced by the milestones
below; this is not the current M5 status):

- `Cpu65816.Step()` mainly uses fixed instruction cycle counts.
- `SnesSystem.RunFrame()` multiplies these by six and renders an entire line
  after CPU execution. Accesses within a raster line are therefore not yet
  visible at their correct time.
- Historical starting point: DMA copied immediately; M3 replaces this with serializable bus phases.
- M3 adds ALU edges, automatic controller polling and timer IRQ.
  PPU read ports and physical hardware comparisons remain open.
- M4 implements SPC700 and real boot/port sequences; M6 adds S-DSP and stereo.
- PPU read ports, sprites, additional graphics modes and many details are missing.
- The frontend uses rational field times; M6 versions network audio as lossless native stereo.

These foundations are being systematically replaced. Title-specific shortcuts
will not become the standard implementation.

## Architectural decisions

1. **One deterministic emulation timeline.** CPU, PPU and DMA use precise
   master-clock timestamps. The APU has its own clock source, coupled through an
   integer/rational time accumulator without floating-point drift.
2. **Advance time at every relevant event.** Bus read/write cycles, internal CPU
   cycles, DMA, interrupt edges, PPU latches and DSP steps must occur at the correct
   point. No blanket `Step() * 6` in the target core.
3. **Separate semantics from bus sequencing.** The instruction tester may still
   execute exactly one instruction. Internally, however, the CPU must be able to
   advance at appropriate cycle boundaries for DMA and interrupt edge cases.
4. **Hardware profiles instead of scattered special cases.** Region, CPU/PPU
   revision, clock ratios and documented errata belong in an explicit profile.
   Start with a documented NTSC two-chip PPU profile, then PAL; specific revision
   IDs are tied to sources and test evidence in M0.
5. **Presentation follows emulation.** The core supplies raw SNES pixels and
   native stereo samples. GPU filters, upscaling, display refresh rate and host
   audio resampling do not change hardware state.
6. **One core for local and network use.** The host executes the same machine.
   Clients receive video/audio and supply input. Network waits and slow clients
   must not distort emulated time.
7. **Complete states.** Every new component includes reset, tracing,
   serialization and deterministic continuation from the start.

## Milestones

The sequence follows technical dependencies. Sizes indicate relative complexity,
not calendar commitments. PPU timing, DSP and enhancement chips in particular
will each be split into several verifiable changes.

| Phase | Result | Prerequisite | Size |
|---|---|---|---|
| M0 | Test laboratory and reliable baseline measurements | Current state | Medium |
| M1 | Independently checked CPU semantics | M0 | Large |
| M2 | Master clock, bus cycles, raster counters | M1 | Very large |
| M3 | 5A22 I/O, DMA/HDMA, IRQ and controllers | M2 | Large |
| M4 | Independently developed SPC700 with real boot/port sequences | M2 | Large |
| M5 | Sprites and complete basic PPU | M2, M3 | Very large |
| M6 | Independently developed S-DSP with stereo audio | M4 | Very large |
| M7 | All base-console graphics modes and PAL/NTSC details | M3, M5 | Very large |
| M8 | Hardware comparisons and broad game regression | M3–M7 | Ongoing |
| M9 | Production-ready interface, networking and distribution | Ongoing; completion after M8 | Large |
| M10 | Enhancement chips, further revisions and peripherals | Stable base console | Several large projects |

### M0 – Test laboratory and evidence tracking

Work:

- Preserve the existing suite as a fast regression and split it into separate
  hardware, conformance, ROM, audio and network test runs. Use standardized test
  results, such as TRX/JUnit plus machine-readable JSON.
- Allow core checks to run independently of the GUI/audio hardware.
- Extend the headless runner with frame/master-clock limits, exit codes, watchdog,
  input replay, memory dumps and CPU/bus/raster traces.
- Create a test-data manifest with source, license, commit/version, SHA-256,
  profile, starting conditions, seed, success signal and timeout.
- Obtain external data locally at pinned versions. A missing dataset is
  `not run`, never a passing test. Provide small permitted fixtures separately
  for offline checks.
- Store reference images, raw samples and traces with their provenance. Obtain
  expected results independently; current output from our own implementation
  must not automatically become the expected values.
- Prepare a full local validation-script run and CI configuration. Activate
  remote CI only once a remote actually exists.

Acceptance:

- A deliberately wrong register value and a deliberately wrong frame are
  reliably detected, with the first difference and a reproducible command.
- Timeout, crash, skipped test and behavioral failure can be distinguished.
- The existing 17 cases remain green. New accuracy tests may initially fail,
  but must appear visibly in a separate inventory.

### M1 – Fully verify 65C816 semantics

Work:

- Create an adapter for [SingleStepTests/65816](https://github.com/SingleStepTests/65816):
  load the initial state, execute exactly one instruction, and compare registers,
  status and relevant memory changes. The dataset also contains bus activity;
  precise acceptance of that activity follows in M2.
- Cover all 256 opcodes and available native/emulation cases.
- Add targeted cases for E/M/X changes, the hidden accumulator high byte, BCD,
  bank/page boundaries, Direct Page, stack wrapping, long accesses, RMW,
  MVN/MVP, WAI/STP and Reset/IRQ/NMI/BRK/COP.
- Separate generic 65C816 expectations from SNES-specific 5A22 behavior.
  Record disputed test vectors with their source and rationale.

Acceptance:

- All approved semantic vectors from the pinned dataset pass.
- No opcode is untested. Documented exceptions appear individually with reasons,
  rather than ignoring an entire opcode group.
- Failure reports include opcode, E/M/X, initial state and first difference.
- Randomized additions use saved seeds; discovered bugs are reduced to small,
  permanently checked-in regressions.

### M2 – Timebase and bus sequencing

Work:

- Introduce `MasterClock`, `HardwareProfile`, raster counters and a deterministic
  scheduler. Explicitly model clock ratios, line/field special cases and reset phase.
- Convert the CPU to Read/Write/Internal-Cycle sequences; check valid dummy
  accesses, page crossings, FastROM/SlowROM, memory-access times and WRAM refresh.
- Trigger PPU, APU and I/O events at defined points before/after an access.
  Keep the origin of each access, such as CPU or DMA, traceable.
- Correctly connect IRQ/NMI edges, latching, masking, polling and WAI wakeup to
  CPU sequencing; do not simulate IRQ only at the end of a frame.
- Keep the Windows timer out of the hardware core. A frame ends according to
  emulated hardware events, not a rounded 1/60-second timer.

Acceptance:

- Approved CPU bus traces match in address, direction, value and order;
  SNES access times are checked additionally with profile tests.
- Exact expected clock/raster events for the selected profile, including
  frame/field transitions and accesses immediately at event boundaries.
- A long headless run reaches the time calculated from the profile without
  accumulator drift. The simulation does not wait in realtime to do so.
- Different execution blocks, such as one access versus a full-frame request,
  produce the same state at the same hardware time.

### M3 – Bus, DMA/HDMA, interrupts and input

Independently accepted by the Root full gate on `56fb6d4`, exit 0,
`artifacts/root-m3-validation.log`. Contract and hardware-evidence limits:
[m3-io-contract.md](m3-io-contract.md). The work below describes the intended M3
scope; it does not claim measurements from an original console.

Work:

- Complete the memory map, ROM/SRAM mirroring, non-power-of-two ROMs, MEMSEL,
  MDR/open bus and register-read side effects.
- Add multiply/divide with actual latency and intermediate states.
- Implement DMA as a timed state machine with channel priority, start/end phases,
  transfer restrictions, address wrapping and CPU stalls.
- Implement HDMA initialization, line counters, repeat behavior, direct/indirect
  tables and overlaps with DMA/refresh.
- Check H/V/HV IRQ, NMI edge cases, H/V latches, serial controllers and timed
  automatic polling, including busy status.
- Extend cartridge detection with traceable metadata and manual profile selection
  for ambiguous headers; a header alone is not hardware evidence.

Acceptance:

- Small ROMs test each DMA mode and critical start positions around HBlank/VBlank,
  IRQ and refresh. Read success from memory/logs so an incomplete renderer cannot
  conceal failures.
- Bus order and clocks match the approved references.
- Input replay reproduces the same JOY results at the same times.
- Cartridge/SRAM tests check mirroring, bank boundaries and reset/persistence.

### M4 – SPC700 and APU communication

Independently accepted on `0b1139436727c835c4fdbf944c74e3d0067c3944`, exit 0,
`artifacts/root-m4-validation.log`. The [contract](m4-apu-contract.md) separates
raw/adjudicated SPC bus cases, our own boot routine and local original-IPL input.
S-DSP audio remains M6.

Work:

- Independently develop SPC700 with all opcodes, flags, addressing modes and bus cycles.
- Add 64 KiB APU RAM, timers, control/test registers, DSP register access,
  IPL mapping and both directions of the communication ports.
- Actually execute IPL boot and program upload; document firmware source and
  hash separately. Distribute firmware only once rights have been clarified.
- Correctly synchronize the independent APU timebase with the CPU and later DSP.
- Combine [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700) with
  our own timer/port/boot ROMs; account for the provenance of test data.

Acceptance:

- All approved SPC700 instruction cases pass, including bus sequencing.
- A small SNES ROM uploads an SPC program that executes and demonstrably changes
  RAM and return ports. No simulated responses.
- Port ping-pong, timer overflows, read-to-clear and clock ratios have reproducible
  timing tests.

### M5 – Complete basic PPU

Implemented and independently accepted on `2dc8cf021ef7394f38df1db9932b443a23543b30`.
The [PPU contract](m5-ppu-contract.md) separates source-informed fetch/MMIO phases,
native expected images and hardware-evidence limits. No console measurements of
our own. Modes 2–7 remain M7.

Clean implementation gate on `9e5a7817af00428915e36f1171d748577e28953e`, exit 0,
`artifacts/m5-full-validation.log`: 129 portable/5 Windows groups, 42 new PPU
groups, 114 original ROM variants, 334 every-clock continuations; full
CPU/SPC/local original-IPL evidence preserved. [Baseline](baselines/m5.json).

Work:

- PPU registers and their individual latches, VRAM read buffer, CGRAM/OAM
  accesses, counter latches and access rules during active display/blanking.
- Refine Mode 0/1, 8×8/16×16 tiles, scroll/tilemap wrapping and priorities.
- Implement OBJ/sprites with sizes, name selection, mirroring, priority, rotation,
  line/tile limits and documented range-over/time-over effects.
- Main/subscreen, windows, fixed color, add/subtract/half and mosaic.
- Make raster effects visible at the documented fetch/latch times. Where
  scanline-only output is insufficient, switch to corresponding dot/fetch steps.
  A single register snapshot per line is insufficient.

Acceptance:

- Isolated ROMs for each register function, sprite case, priority and color operation.
- Compare digital output as native color values first; host color profiles,
  CRT filters and screenshots remain additional presentation tests.
- Image comparisons report the first wrong pixel and affected line; an associated
  register/bus trace explains when the difference originated.
- Mid-scanline write tests pass for functions marked complete.

M5 was independently accepted on `2dc8cf021ef7394f38df1db9932b443a23543b30`:
`python tools/test-lab.py --windows --full`, exit 0,
`artifacts/root-m5-validation.log`; unchanged Root reports are under
`artifacts/root-m5-evidence/`.

### M6 – S-DSP and complete audio path

M6 implements the independently developed phase-clocked DSP and stereo through to
the device and TLS/relay. Contract, source conflicts and digital-evidence limits:
[m6-dsp-contract.md](m6-dsp-contract.md). M6 was independently accepted on
`e4645770cea052885808ca5b27a45d60de470157`:
`python tools/test-lab.py --windows --full`, exit 0,
`artifacts/root-m6-validation.log`; unchanged reports are under
`artifacts/root-m6-evidence/`. The full gate passed on clean `feb978b` with exit 0
(147 portable and 8 Windows groups); [measurements](baselines/m6.json).
It runs in separately supervised DSP/AV suites in addition to the unchanged
129 M0–M5 groups.

Work:

- Independently develop a DSP with voices, BRR decoding, looping, pitch,
  interpolation, ADSR/GAIN, key-on/off, noise, pitch modulation and echo/FIR.
- Check arithmetic, saturation, rounding, register timing, RAM accesses and
  shared SMP/DSP time steps with isolated cases.
- Generate stereo output at the DSP's native timing. Apply device-specific
  resampling and volume only in the frontend.
- Version the network format: stereo, sample rate, sample count, timestamp and
  packet-loss/overflow behavior. Offer lossless reference transmission;
  check lossy compression separately.

Acceptance:

- BRR/envelope/filter/mixing cases with independent digital expected values.
- Sample-exact comparison of raw DSP output before the host resampler; timing
  and phase must match, not just a generous audio-similarity score.
- Check audio ROMs and suitable SPC snapshots; snapshots do not replace a
  combined CPU↔APU test of a running console.
- An extended A/V run, reset, pause and savestate without loss of emulated time;
  no lasting drift between video and audio.

### M7 – Graphics modes, regions and hardware details

M7 is implemented and independently accepted on `439a655199647f325bef6b6b3074e67948a105dc`
(Root full gate exit 0). The clean full gate on `7cf7cc1` passed with 182 portable
and 10 Windows groups. Only the format 8 error text and lab-options adapter were
subsequently corrected and received targeted checks; final product/tool revision
`adef880`. Native raw fields, separate display/timing interlace metadata, source
uncertainties and measurement scope: [M7 contract](m7-video-contract.md),
[baseline](baselines/m7.json). No new hardware-measurement evidence or realtime claim.

Work:

- Complete Modes 2–6 with offset-per-tile, hires and direct color; Mode 7 with
  transformation, repeat/outside behavior and EXTBG.
- Interlace, pseudo-hires, overscan, field alternation and PAL/NTSC differences.
- Carry variable image geometry from the core to the GUI, preview and network;
  remove fixed 256×224 assumptions.
- Add documented differences and errata of supported revisions as profile
  behavior. Treat unsupported power-on values as defined test initialization;
  do not describe genuinely undefined states as guaranteed zero initialization.

Acceptance:

- A functionality and timing matrix with targeted ROMs for each mode and region.
- Interlace/hires tests compare native pixels and field order without lossy scaling.
- The same inputs and hardware profile produce reproducible clock, image and
  sample sequences, including after savestate restoration.

### M8 – Original hardware and game compatibility

Work:

- Inventory suitable tests from
  [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms),
  classify their prerequisites and integrate pinned versions.
- Build minimal ROMs with machine-readable results for disputed effects in our
  implementation. Have them run on documented original hardware once suitable
  hardware/measurements are available.
- Use bsnes and ares at pinned versions/configurations for differential comparisons.
  Agreement between their results alone does not count as independent hardware evidence.
- Maintain a local title matrix: ROM hash/region, required chips, boot, menu,
  gameplay scene, audio, input, SRAM, savestate and an extended gameplay section.
  Commercial ROMs remain local test inputs.
- Reproduce test cases with recorded input. Select suitable titles for at least
  every supported hardware family before claiming compatibility.

Acceptance:

- Every known difference has a reproducible test package and a status.
- No silently disabled accuracy tests, automatic updates of expected images or
  title-specific handling in the standard profile.
- Hardware-verified, reference-compared and internally tested only remain separate
  labels. Missing measurement capabilities are listed visibly.

### M9 – Interaction, savestates, networking and distribution

This work accompanies the core phases; final approval requires the base-console
regression to pass.

Work:

- Move emulation to its own controlled thread. The GUI and transport receive
  immutable frames/sample blocks; input is adopted at defined hardware times.
- M5 benchmark: active APU and clocked PPU reach 21.62 diagnostic fields/s locally;
  after M6, including DSP, 18.33. The separate two-voice/forced-blank test measures
  19.40 and is not a direct comparison. M9 must profile budget/cycle continuation
  and achieve realtime with identical digital output. This is not evidence of
  60 fps compatibility.
- Decouple hardware-accurate speed from the rendering limit, VSync, turbo, network
  latency and audio device. Optimize only after profiling and with identical
  digital output on the accuracy path.
- Systematically compare C64 interaction behavior: F10/F11/F12/F9, menu return
  paths, key repeat, gamepad menu actions, complete configurable bindings, focus
  loss, device changes, browser, drag-and-drop and error messages.
- Extend savestates with scheduler, intermediate cycles, DMA/HDMA, PPU latches/field
  phase, SPC700, DSP, echo RAM and later enhancement chips. Use clear versioning
  and targeted migration or understandable rejection of older formats.
- Save battery-backed data robustly during extended sessions too; atomic files,
  recovery/backups and a clear distinction between reset, power cycle and SRAM erase.
- Complete networking with stereo, dynamic geometry, timestamps, bounded queues,
  permission changes, spectators and visible host-pause status.
- Add Windows x64 publishing, installer, uninstallation and a verifiable update
  path targeting SNES releases. Build the library Manager only on stable
  cartridge/IPC contracts; do not offer a C64 catalog as SNES content.

Acceptance:

- Save→continue and Load→same input produce identical future core states, raw
  images and raw stereo samples, including during DMA, HDMA, interrupt/line
  boundaries and DSP echo. Output buffers are defined separately.
- UI smoke tests for menu paths, fullscreen, gamepad and real window launches.
- LAN and relay with at least two clients: permission revocation neutralizes held
  buttons; disconnect/focus loss does not leave inputs stuck.
- Network checks for certificate changes, wrong passwords, kick, slow clients,
  bounded memory, invalid packets, format changes and A/V sync.
- Targeted persistence failures/interrupted writes do not corrupt previously
  valid SRAM/savestate files.
- The release package starts on a clean Windows test system without an SDK.
  Version changes and uninstall preserve the intended user data.

### M10 – Enhancement chips, peripherals and further console revisions

After the base console is stable, develop each chip family as its own package
with its own timebase, memory map, states and tests. Prioritize by available
verifiable tests and desired game coverage: DSP-1 and Super FX first, then SA-1,
followed by the remaining families.

The full long-term backlog includes DSP-1/2/3/4, Super FX/GSU, SA-1, CX4, S-DD1,
SPC7110 with RTC, S-RTC, OBC1 and ST010/011/018, plus associated cartridge mappings
and required firmware data. Different variants are recorded explicitly, rather
than grouped under a blanket check mark.

Multitap, mouse and Super Scope follow with their own input tests. Satellaview,
Sufami Turbo and Super Game Boy are separate extension projects; Super Game Boy
in particular also requires Game Boy emulation. They do not prevent approval of
the fully tested base console, but remain visible in the scope.

Acceptance per extension:

- Function/bus/timing tests and at least suitable integration ROMs.
- Save/load determinism and regression of the standard base console.
- No blanket “supported” label while only a title screen works.

## Test operation and release rules

| Run | Content | When |
|---|---|---|
| Fast | Targeted component tests, small fixed CPU/SPC selection, diagnostic ROM, savestate regression | Every relevant change |
| Full | All approved opcode vectors, hardware ROMs, profiles, digital image/audio references | Before milestone approval and regularly locally/in CI |
| Integration | LAN/relay, GUI, persistence failures, dynamic video/audio formats | Affected changes and before releases |
| Extended | Several emulated hours, A/V drift, memory usage, replay and performance | After timing/audio/threading changes and before releases |
| Hardware | Pinned ROMs on a documented physical console | New/disputed timing findings and available measurement opportunities |

Every run records commit, profile, test-data revision, executed/skipped cases and
artifacts. An existing known failure may be recorded as expected, but remains a
failure in the accuracy report. A milestone is complete only after meeting its
own acceptance criteria.

Code coverage helps find gaps. Approval is based on observable behavior: correct
data, bus order, timing, native pixels, native samples and reproducible states.
Performance budgets are set on a named test machine; the accuracy core should
execute the selected profiles there in realtime without growing queues.

## First concrete work packages

- [x] Define the test-data/reference manifest and result format (M0).
- [x] Document proposed hardware profiles with region and open/unverified revisions (M0; hardware evidence outstanding).
- [x] Create the CPU test adapter with arbitrary initial state and logical trace bus (M0; cycle acceptance follows in M2).
- [x] Read the pinned 65816 sample dataset and generate a failure report per
      opcode/mode; initially do not invent an accuracy percentage.
- [x] Fix the first independent CPU failures and add small regressions (M1).
- [x] Specify an implementable contract for master clock, bus access and event
      order using small timing tests.

Only then do the complete scheduler redesign and further hardware components
begin. No reliable duration estimate for the whole project is possible before
these measurements. After M0/M1, reassess effort and risks using actual failures
and execution times.

## Source foundation

- [WDC W65C816S datasheet](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf): CPU foundation; check SNES-specific differences separately.
- [SingleStepTests/65816](https://github.com/SingleStepTests/65816): external state and bus vectors for individual instructions.
- [SingleStepTests/spc700](https://github.com/SingleStepTests/spc700): external SPC700 vectors.
- [undisbeliever/snes-test-roms](https://github.com/undisbeliever/snes-test-roms): open-source ROMs as candidates for hardware and graphics checks; specific selection in M0.
- [SNESdev: Version differences](https://snes.nesdev.org/wiki/Version_differences): starting point for profiles and revision-dependent findings.
- [bsnes CPU timing](https://github.com/bsnes-emu/bsnes/blob/master/bsnes/sfc/cpu/timing.cpp): comparison implementation for synchronization, refresh and DMA/controller events.
- [ares SMP timing](https://github.com/ares-emulator/ares/blob/master/ares/sfc/smp/timing.cpp): comparison implementation for APU timebase and timers.
- [blargg snes_spc](https://github.com/blarggs-audio-libraries/snes_spc): additional audio reference for isolated differential checks.

Reference source code supports analysis and independent comparison. Importing
third-party code is not part of this plan; the independently developed C# core
and existing license boundaries remain intact.


M8 implementation full gate: clean commit `47f91cb`,
`python tools/test-lab.py --windows --full` with exit 0, 209 portable and
10 Windows groups. Independent Root acceptance on `10164c7` passed with exit 0
(`artifacts/root-m8-validation.log`). Accuracy failures remain red in separate
reports; [M8 baseline](baselines/m8.json) and the
[M8 contract](m8-compatibility-contract.md) record reproductions and evidence limits.
M9a is incomplete: the full accuracy gate on `2dbeb5f` passes; the sustained-performance
target fails in 38/62 NTSC windows. Historically, worker/UI followed here; it has
since been accepted as M9b. NTSC/PAL sustained performance and the remaining M9
product phases must be completed before M9/release acceptance.

M9b implements the controlled worker, field-bound input and separate video/audio
handoff; [contract and evidence limits](m9-worker-contract.md). Clean
implementation 43c566d: full Windows gate exit 0, 209 existing portable,
16 separate runtime and 10 Windows groups plus a native app probe.
[Three 60-second app measurements and unchanged limits](m9-worker-contract.md)
and [baseline](baselines/m9b.json) document independent Root acceptance on
`1074c36`. NTSC/PAL sustained performance, a multi-hour A/V/threading soak and
further product/distribution phases remain open before M9/release approval.


M9c implements atomically flushed, validated state/SRAM/settings generations,
visible recovery, bounded periodic dirty-SRAM snapshots, transactional ROM-session
leases and bounded state readers. The [persistence contract](m9-persistence-contract.md)
and [baseline](baselines/m9c.json) list commit, files, hashes and limits.
Clean implementation `ed62a20`: full Windows gate exit 0, 209 existing portable,
16 runtime, 14 persistence and 10 Windows groups; actual native periodic operation,
preview/recovery/close and negative SRAM/settings/log failure cases passed.
Additional native repetitions checked 28 build identities each, captured immediately
before/after. Three archives/7,744 files (5,613,078,150 decompressed bytes) were
rechecked against CRC/length/SHA256 and all disk originals. Independent M9c Root
acceptance on `cfe4ed2` passed (239 portable/10 Windows groups, both native probe
types, 122,368 pixels/5 checks and unchanged stereo SHA). This completes neither
M9a sustained performance nor networking, distribution/Manager, multi-hour soak
or M9/release as a whole.

M9d implements versioned LAN/TLS/relay operation with neutral permission changes,
bounded queues/resources, drainable shutdown, trust recovery and a measured
software A/V timeline; [network contract](m9-network-contract.md). Independent
M9d acceptance on `2a5ae39` passed. New groups were reported separately; the
existing 239 portable and 10 Windows groups and complete external corpora remain.
M9a sustained performance, distribution/Manager/CI, multi-hour soak and M10 remain open.

### M9d clean implementation gate and frozen handoff

Implementation `b432a976e3315ecf45f55a7c5db2ba35dc54eaf2` passed
`python tools/test-lab.py --windows --full` with exit 0 on a clean checkout:
256 portable groups (previous 239 +14 network +3 relay) and 23 Windows groups
(previous 10 +13 real socket/TLS/relay/driver groups). Full CPU semantics and bus
each executed 5,080,400 cases; 26,184,846 bus cycles and 256,000 SPC cases passed
their existing gates. Native runtime and all four persistence modes passed;
the same 28 DLL/EXE/deps/runtimeconfig identities bracket every final native start.

The real A/V run retained 2.3852309 s cold readiness separately, then observed
5.0086057 s with 297 video, 301 audio and 297 actual driver/video comparisons;
maximum software A/V distance 33.308853 ms, 23 presentation drops, no provider fault.
Maximum queue residence 265.064900 ms and scheduling error 173.170717 ms include
late rejected/cold work; they are not physical device-latency claims.

The three frozen archives contain 8,771 entries / 5,745,970,051 decompressed bytes.
Every archive and entry has SHA256/CRC32/length identities; every entry was read
back and compared to both staged and live originals. No nested ZIP or active log
is included. The separate developmental archive retains available failed-run
captures and explicitly records early overwritten child-report limits. A first
incomplete archive attempt was rejected for selecting its own active supervisor
log; only that owned freezer was stopped and the corrected run was verified.
Three prepared Root helper scripts are ancillary copies in the report archive;
they were not run as implementation evidence.

See [M9d baseline](baselines/m9d.json) for exact hashes, counts and raw paths.
M9d was independently accepted on `2a5ae39` (Root full gate exit 0,
256 portable/23 Windows groups). M9a sustained performance, M8 audio failures,
source-adjudicated raw SPC differences, distribution/Manager/CI, prolonged soak
and M10 remained open at this M9d snapshot; publish was then PRE-M0. No physical
SNES/input/DAC, OS-focus or clean-VM claim is added.


M9e adds the separate WPF Manager, portable bounded user-local IPC, SQLite library
metadata and State9 management. Independent Root acceptance passed on `2534dba`
(267 portable/31 Windows groups); implementation `a397fca` and frozen evidence
remain separately identified. See the [Manager contract](m9-manager-contract.md);
distribution/CI, sustained performance and prolonged soak remain open.

M9f implements self-contained Windows packages for both apps, package
manifest/notices/locks, an explicitly configured update path, isolated Inno
install/repair/uninstall checks and actual Linux portability checks.
[Distribution contract](m9-distribution-contract.md). Full Windows gate on clean
`9db1edd`: 276 portable/31 Windows groups; after changes limited to test-harness
and documentation hardening on `9058878`, targeted 11 portable/8 native Manager
groups and all package probes passed. Full Linux gate on clean `9058878`:
276 portable groups and all complete corpora. Root subsequently independently
passed the full Windows gate on `0f8aadc` with 276 portable/31 Windows groups and
1,194 unchanged native build identities.

`publish/` now contains the verified current package; the complete PRE-M0
directory is retained as `publish-pre-m9f-9058878/`. The general CMD launcher
passed another 20-second native run with the local game. Its separate shortcut,
all 412 files of the previous playable copy and the original ROM remain
unchanged. Evidence: 4,581 unique objects/4,987,831,365 bytes,
66,951 original references/29,513,259,971 bytes; the archive and all originals
were read back in full. [Exact baseline](baselines/m9f.json). Independent M9f
acceptance, including package/update/isolated installer probes, passed;
sustained performance/soak, interaction parity, open accuracy questions and M10
remain outstanding. M9/release are incomplete.

M9g implements parity for applicable C64 interactions:
[feature matrix and input contract](m9-interaction-contract.md). The implementation
extends keyboard/frontend bindings, direct capture, context priority, event
order, display options and existing warm-reset/power operations. Full gate
before the final WPF-only fix: `7effa47`, 285 portable/31 Windows groups.
Final `2ab409d`: Manager 12 portable, 32 native Windows groups and 151 additional
interaction stages with 1,194 identical build identities; Linux 59 is targeted
portability coverage. [M9g baseline](baselines/m9g.json). The independent full
Root gate on 8daf478 fails because of a 49.942 ms software A/V distance.
[Separate follow-up](m9g-followup.md): label corrected, two cold-readiness failures
and the Root A/V cause remain open; a new 13-group run is not evidence of a fix.
Sustained performance/soak, open Linux persistence findings, accuracy and M10
remain open.


M10b adds the scoped own C# SuperFX shared-bus model and resumable microphases.
See [M10b SuperFX](m10b-superfx.md) for explicit board configuration, tests and
retained manufacturer/software/source-author disagreements. Its functional gate
is separate from the red raw accuracy inventory. SA1 and final integrated long
performance/native-hour gates remain pending stages.
