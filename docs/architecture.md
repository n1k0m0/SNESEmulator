# Architecture and development status

## Current status (R4, October 24, 2026)

The current source uses **savestate format 9**. M9b worker/UI, M9c persistence,
M9d networking, M9e Manager and M9f distribution have independent historical
acceptance. [R1](r1-persistence-ci.md) records the later persistence correction
and exact successful remote CI run. [R2](r2-performance-soak.md) records the
SPC optimization and worker deadline correction; [R3](r3-accuracy.md) records
the indexed-indirect CPU fix and the remaining accuracy limits.

The reviewed CPU contracts execute 5,080,400 cases each for semantics and bus
behavior. Raw expectations still disagree in 246 semantic and 264 bus cases;
the separate, reviewed 264-case supplement explicitly completes unspecified
flat RAM with zero. SPC has 256,000 approved cases, including 7,000 separately
adjudicated raw bus differences. These are software-reference contracts.
Three unchanged gilyon v1.4 functional ROMs pass in NTSC and PAL; they exclude
interrupt/cycle/MMIO behavior specified by their upstream authors.

All 18 default-profile M8 full-sequence audio comparisons remain accuracy
failures. The isolated DSP input timeline matches 89,524 stereo pairs and
191,408 timed echo writes; the laboratory-only 186-master-clock CPU-release
experiment explains the bsnes mismatch under its source-defined reset epoch.
It does not change the product reset/frequency or establish physical timing.
Warm PPU reset, ares timing, analog output and unmeasured revisions remain open.

The previous `97a05a7` core passed 37,000 NTSC fields (61 complete 600-field
windows, minimum 1.00668155x) and 31,000 PAL fields (51 windows, minimum
1.18235382x). The narrow NTSC margin is not transferable to later builds.
Both old one-hour native runs completed but exposed post-ready catchup.
The corrected worker passed strict 360-second NTSC/PAL probes; these scenes'
source PCM was entirely zero, so they do not demonstrate audible stereo.
**Final-source core duration gates, strict native 3600-second NTSC then PAL,
package verification and final remote CI remain due after M10.**

The sections below retain milestone measurements and their original scope.
An old pass, open item or package identity describes that snapshot; it is not
a claim that the current source or package has undergone the same validation.

## Relationship to C64Emulator

The starting point is the local C64Emulator at commit
`f01750e17a2c3f4cabfd57dc0c470dac93a91a23`. The unversioned Manager/IPC work in
the C64 working directory was not copied.

| C64 | SNES |
|---|---|
| `C64System` / `SystemBus` | `SnesSystem` / `SystemBus` |
| 6510 | Independently developed `Cpu65816` |
| VIC-II / C64 framebuffer | Independently developed `Ppu`, native 256/512×224/239 raw fields |
| SID | Independently developed `Spc700`/`Apu`/`SnesDsp`; native 32 kHz stereo output |
| CIA joysticks | `ControllerPorts`, serial and automatic polling |
| PRG/D64/EasyFlash | `Cartridge`, LoROM/HiROM and battery-backed SRAM |
| C64Window | `SnesWindow`, split across several frontend files |
| C64Net | `SnesNet`, reused TLS/relay transport, adapted controllers and video codec |
| StateSerializer | Independently developed, versioned SNES serializer with ROM hash |
| SharpPixels | The same rendering library as a local, standalone copy |

The hardware depends on neither OpenTK, NAudio nor Windows.
`SNESEmulator.Runtime` owns the active console on exactly one worker. The UI and
transport receive completed raw fields/sample blocks with independent ownership;
bounded commands and field-bound input replace direct UI access to the core.
Rational monotonic deadlines preserve every hardware tick independently of
UI/VSync. The [M9b contract](m9-worker-contract.md) defines epochs, ownership,
audio and error rules; its historical acceptance is distinct from final-source
performance and release validation.

## Hardware foundation

`Cpu65816` is an independently developed C# core with separate Read/Write/Internal
cycles, emulation/native mode and serializable continuation. Completed MMIO
effects are not repeated when execution resumes. The current full CPU contracts
include the explicitly reviewed R3 indexed-indirect supplement and expose raw
corpus disagreements separately. Historical M2 measured 26,184,846 generic bus
rows. Neither source establishes physical 5A22 electrical conformance.

`MasterClock` owns integer time, raster counters and stable typed events.
`RunUntil(t)` stops exactly at t, including during CPU read hold, DMA, HDMA and
refresh. CPU reads latch after the access duration minus four clocks, followed
by four hold clocks. The external CPU MDR adopts the value only afterward.
Internal CPU read ports 4000–43FF receive MDR. MEMSEL controls only the documented
FastROM address ranges. Explicit manual hardware-profile selection continues to
take precedence over the header region. The H0/V0 epoch remains a deterministic
convenience reset, not a measured physical reset release.

`SystemBus` connects 128 KiB WRAM, standard LoROM/HiROM/ExLoROM/ExHiROM mappings,
recursive chip mirroring, SRAM, PPU/APU ports and 5A22 I/O. `CpuIo` contains ALU
intermediate states, timer IRQ/NMI holds, counter latches and serial/automatic
JOY polling. `DmaController` takes over the bus with its own saved phases:
A/B read after four clocks, write after another four, priority, start/end sync,
HDMA tables and preemption. Savestate v9 stores these intermediate states and
binds the mapper as well as the ROM hash. See the [M3 contract](m3-io-contract.md)
for precise limits and sources. PowerOn defines RAM test seeds; Reset preserves
CPU WRAM and SRAM, but does not fully model all PPU reset pins.

`Ppu` executes independently developed Mode 0–7 backgrounds and all 128 OBJ
through fetch/pixel phases in the master-clock scheduler. Individual
VRAM/CGRAM/OAM and scroll latches, separate PPU1/PPU2 MDRs (PPU2 shared with
counter ports), windows, main/subscreen, color math and mosaic are serialized.
Native RGB555 and brightness phase are output before ARGB; the lab compares
native pixels with the first X/Y/line difference and register/bus context. PPU
observers run after the complete timestamp commit. M7 carries variable native
raw-field geometry up to 512×239, field parity, separate display/timing interlace
flags and rational time domains through to the GUI, preview, lab and network.
Broad hardware evidence remains outstanding. Absolute PPU phases are an
analytical model informed by sources, not new console measurement evidence.
See the [M5 contract](m5-ppu-contract.md).

`Apu` executes an independently developed, complete `Spc700` with 64 KiB RAM,
timers, CONTROL/TEST, a DSP register interface and separate CPU/APU port latches.
The native 24.576 MHz timebase is rationally coupled to the master clock; SPC
port reads have separate latch/hold phases. Intermediate cycles are serializable.
An independently developed open 64-byte boot routine executes the IPL upload
dialogue as SPC code. An optional local original IPL remains undistributed and
is bound to savestates by its hash. A real 65816 ROM tests uploads of up to
1,048 bytes, page/index wrapping and executed SPC responses. M6 adds an
independently developed DSP with 32 microphases per stereo pair, eight BRR voices,
Gaussian interpolation, envelopes, noise and echo in shared RAM. The
[M4 contract](m4-apu-contract.md) distinguishes 256,000 semantic/approved bus cases
from 249,000 raw bus passes and 7,000 source-supported cycle adjudications.

`DiagnosticRom` generates an original LoROM: native-mode setup, DMA of a 2-bpp
tile, CGRAM colors, a Mode 0 background, an NMI handler and an animated scroll
register. Tests verify that CPU code executed, DMA copied data and NMI was
serviced; the image is not a frontend demo animation.

## Further development

The required work sequence and planned acceptance criteria are in the
[development plan](development-plan.md). It describes future work and does not
change the implementation status documented above.

The test laboratory, independent CPU checks and master clock are M0–M2; M3 adds
DMA/HDMA and I/O. M4 has independent acceptance; M5 implements the clocked
Mode 0/1 PPU. M5 was independently accepted on 2dc8cf (Root full gate exit 0).
M6 implements the independently developed S-DSP; M6 was independently accepted
on `e4645770cea052885808ca5b27a45d60de470157`:
`python tools/test-lab.py --windows --full`, exit 0,
`artifacts/root-m6-validation.log`; unchanged reports are under
`artifacts/root-m6-evidence/`. PAL/NTSC, hardware comparisons, product readiness
and enhancement chips follow with their own evidence. Savestate, frontend and
network regressions accompany hardware development from the start.

## References

- [WDC W65C816S datasheet](https://www.westerndesigncenter.com/wdc/documentation/w65c816s.pdf)
- [SNESdev hardware documentation](https://snes.nesdev.org/wiki/SNESdev_Wiki)
- [SNES PPU registers](https://snes.nesdev.org/wiki/PPU_registers)

The references support further development and independent verification. The
existing smoke suite does not replace full hardware validation.


M5 local performance observation: 21.62 diagnostic fields/s with the independently
developed SPC700 and clocked PPU active, 7.72 million master clocks/s. This is
below NTSC realtime; M9 optimization must preserve the digital accuracy path.
Full counts, group times, clean report identities and evidence limits:
[M5 baseline](baselines/m5.json).


M6 separates native digital DSP output (sample index/crystal timestamp) from a
bounded presentation queue, stereo device resampling/volume and networking.
Format 7 serializes all DSP intermediate latches and echo history; physical
echo RAM is part of APU RAM. Network format 0x534E0002 carries lossless stereo
PCM16 blocks at 32,000 Hz with sequence and native time; clients discard old
sequences and clear their presentation on gaps/reset. Menus, ROM changes and
Load also clear resampler prefetch/filter history. The original audio diagnostic
executes 65816→IPL→SPC→DSP. Headless ROMs additionally provide native stereo PCM
and DSP phase traces. Sources, independent digital oracle and limits:
[M6 contract](m6-dsp-contract.md). Audio-device/ROM hardware measurements are
still absent; published research does not replace our own console recording.


M6 clean full gate passed on feb978b with 147 portable/8 Windows groups and all
external corpora. Comparable full PPU/APU/DSP performance is 18.33 fields/s,
versus 21.62 at M5; the separate two-BRR-voice forced-blank case is 19.40. Both
remain below realtime. [M6 baseline](baselines/m6.json) records exact measurements;
M9 must retain digital results while profiling. Independent M6 acceptance passed
on e464577; see root-m6-validation.log.


M7 adds independently developed 8-bpp/OPT/hires fetch schedules, integer-affine
Mode 7, EXTBG and DirectColor. A continuous cursor consumes native pixels at
separate BELOW/ABOVE phases; host weaving is not hardware state. CompletedFrame
preserves the completed raw field before the next field starts. Format 8 stores
geometry/array lengths and all additional latches. VID3 carries complete lossless
ARGB with field/time metadata; AUD2 stereo is preserved.

The full gate on 7cf7cc1 passed with 182 portable and 10 Windows groups. Later
changes limited to error text and the lab adapter on adef880 received targeted
checks. Comparable full diagnostic PPU/APU/DSP workload: 23.64 fields/s; separate
two-BRR workload with ForcedBlank: 24.29. This remains below realtime and is a
single-host measurement. M9 must profile without losing accuracy. Precise
contract, field boundaries and hardware uncertainties: [M7](m7-video-contract.md),
[measurements](baselines/m7.json). M7 was independently accepted on
`439a655199647f325bef6b6b3074e67948a105dc`, full Windows gate exit 0,
`artifacts/root-m7-validation.log`; unchanged evidence is in
`artifacts/root-m7-evidence/`.


## M8 independent console comparisons

The [M8 contract](m8-compatibility-contract.md) and
[title matrix](m8-title-matrix.md) distinguish published hardware measurements,
our own parameter checks and executed bsnes/ares comparisons. The external cores
are isolated laboratory tools only, not product dependencies. CI uses licensed
offline fixtures and our own IPL; the local full gate also checks the original IPL.

WAI executes continuous six-master-clock idle cycles. At the same timestamp,
CPU/DMA MMIO effects precede the relevant PPU phase; H0 latches and field
allocation also follow this commit. V128 adopts the display-interlace value
captured at V0. Format 9 rejects old formats 1–8 because their WAI/H0 states
cannot be reconstructed unambiguously. New observer saves are checked against
uninterrupted reference runs without loading. VID3 and AUD2 are unchanged.

Product profiles remain analytical separate-chip 2/1/3 targets. Published
forced-blank corruption, early-read/DMA errata and analog transients are still
not modeled. Measured SMP frequencies provide additional parameter cases, not
new board certification. The default 32000 profile and the explicitly forced
32040 reference profile are separate. M9 adds worker/UI, persistence, networking,
Manager and distribution; sustained performance and soak testing remain open.


The clean M8 full gate on `47f91cb` passed with 209 portable and 10 Windows
groups. The additionally measured reference-thread values are separate from
nominal system frequencies: bsnes truncates the NTSC CPU value; ares rounds it
and uses its own integer CPU/SMP/DSP scalars. The executed parameter comparisons
therefore do not explain the first audio difference. This diagnosis changes
neither a product timebase nor an acquired expected image.
[M8 baseline](baselines/m8.json) links to the unchanged evidence freeze;
independent acceptance remains a separate step.


M9a preserves format 9 and all digital output while reducing scheduler suspension
and observation allocation costs; see the
[performance contract](m9-performance-contract.md). M9b now runs the live console
on a portable controlled worker; remaining persistence/network/distribution work
and sustained realtime acceptance stay open. M8 was independently accepted on
10164c7.


M9b Worker/UI was independently accepted on 1074c36; the documented hardware,
input, soak and sustained-performance limits remain. M9c adds bounded, atomically
flushed persistence with validated backups, visible recovery, 30-second dirty-SRAM
snapshots and transactional ROM-session leases. State decoding runs on a detached
machine outside the hardware owner and installs only if the epoch is unchanged.
Format 9/VID3/AUD2 are preserved. The full gate on `ed62a20` passes; M9c was
independently accepted on `cfe4ed2`. See the
[persistence contract](m9-persistence-contract.md) and
[baseline](baselines/m9c.json). M9a sustained performance, networking, current
distribution and M9/release remain open.

M9d adds protocol 4 with unchanged VID3/AUD2 codec bytes in explicit console/stream
epochs, atomic permission/neutralization boundaries, bounded transport and
handshake resources, drainable tasks and atomic pin/certificate persistence.
A/V is measured at an explicitly limited software/driver boundary; the physical
DAC and display transport remain unmeasured. Hardware/input generations are
worker metadata outside format 9. See the [network contract](m9-network-contract.md).
M9d was independently accepted on `2a5ae39`; distribution and M9/release remain open.

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
metadata and State9 management. Independent Root acceptance passed on `2534dba`,
267 portable/31 Windows groups; see the separate implementation and Root evidence
in baseline m9e.json. See the [Manager contract](m9-manager-contract.md);
distribution/CI, sustained performance and prolonged soak remain open.

M9f freezes self-contained Windows packages for the emulator/relative
Manager/Updater, an explicit bounded update workflow, isolated preservation-first
installer tests and actual Linux portability. Package defaults remain immutable;
operator policy lives in user data and is edited through F10 SETTINGS UPDATE
SOURCE. The current publish is verified; its complete PRE-M0 predecessor is
retained. Full Windows 276/31 belongs to 9db; test-only lifecycle hardening 905
has targeted 11/8 Windows Manager evidence, all real package probes and a complete
276-group Linux gate. Independent Root acceptance passed on `0f8aadc`; see the
[M9f baseline](baselines/m9f.json). M9 and release acceptance remain incomplete.
