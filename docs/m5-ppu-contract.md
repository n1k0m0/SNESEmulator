# M5 two-chip PPU contract and evidence

Own C# implementation. M4 was independently accepted on
`0b1139436727c835c4fdbf944c74e3d0067c3944`, full gate exit0,
`artifacts/root-m4-validation.log`. M5 independent parent acceptance passed on
`2dc8cf021ef7394f38df1db9932b443a23543b30`, full gate exit0,
`artifacts/root-m5-validation.log`. M6 supersedes format6 with format7 DSP state.
No SNES hardware is owned or newly measured by this project. Published research,
a third-party test program and software comparison are distinguished below.

## Implemented target

Mode0/1 low-resolution backgrounds, all128 objects and basic two-chip PPU MMIO
run inside the master-clock scheduler. The old end-of-line renderer is removed.
CPU, DMA, HDMA, APU and typed events share the same time; already emitted pixels
are never redrawn after a register write. Modes2–7, hires, direct color,
pseudo-hires, EXTBG and dynamic interlace/overscan output remain M7.
The proposed NTSC/PAL separate-chip2/1/3 target is not a certified motherboard.

PPU1 and PPU2 have separate output MDRs. PPU2 counter/status reads in `CpuIo`
use the *same* PPU2 MDR as CGRAM reads. CPU writes do not overwrite either PPU
output latch. Unreadable ports return the documented CPU or PPU1 MDR, according
to the explicit $21x4–6/$21x8–A mask (x0..2); they do not return a generic zero.
CPU internal $4000–43FF MDR rules retain the M3 contract.

VRAM is64KiB, word addressed, with all three remaps, increments1/32/128,
low/high increment selection and a word read buffer. Both address writes
prefetch; the incrementing read returns the old buffer, refills from the current
address, then increments. This intentionally gives the familiar duplicated first
word across the first two word reads. Writes update neither read buffer nor the
other byte. Active-display VRAM writes are suppressed even in HBlank; address
increments still occur. Active reads refill zero but still advance. Forced blank
or VBlank permits memory access. Address and byte-memory wrapping are explicit.
The buffer/address behavior follows the inspected pinned comparison reference;
Anomie's older blanket dummy-read explanation is incomplete about prefetch.

CGRAM has a shared read/write byte flip, low-byte write latch, high-byte15bit
commit and PPU2 bit7 retention on high reads. OAM low-table writes commit pairs
only on the CPU byte-address odd phase; the high table writes immediately and
mirrors32bytes. Reads and writes share its10bit byte pointer. Either address
write reloads the whole pointer. OAM reload occurs at VBlank entry outside forced
blank, and a write to INIDISP while forced blank on that first VBlank line.
Active OAM accesses redirect each individual byte operation to the evaluator/
fetcher's current address, preserving the CPU pointer's table/parity decision.
In particular, an active low-table odd write may write both bytes to the same
redirected byte. CGRAM active accesses use the renderer's palette address only
at V>0 and H88..<1096; CPU high/low selection still controls the read byte.
These address-ownership rules are source-informed, not newly captured chip buses.

BG offset writes share PPU1 and horizontal-fine input latches, independent of the
Mode7 register latch. The Mode7 matrix/offset interface and signed16×signed8
24bit multiplication read ports are implemented; Mode7 rendering is not.
Counter low/high toggles, software/PIO latching and status read-clear behavior
retain M3, with shared PPU2 MDR. Range/time flags are sticky until the next field.

## Fetch phases and raster writes

Every scheduled PPU boundary is a two-master-clock microstep. At a timestamp,
all typed events run first, then the complete PPU edge, then APU effects in native
crystal order, then CPU/DMA MMIO. Active-time bookkeeping is committed before
public callbacks. Boundaries are composed with the existing APU boundary hook;
no APU callback is replaced. Restoring runs due-now events and device edges
before allowing a CPU bus access. No restored MMIO is executed twice.

The explicit internal pipeline convention follows the independently inspected
cycle renderer at bsnes revision `78474e5d39f58872dc21aca18cf9eb583b147fe7`:

| Operation | Master clocks H within the line |
|---|---|
| Fine-scroll and object-first-index latch |0 |
| OBJ range evaluation, one candidate |0,8,...1016 |
| BG fetch groups, eight four-clock slots |0..1052, repeating every32 |
| Mode0 slot order |BG4/3/2/1 map, BG4/3/2/1 planes0–1 |
| Mode1 slot order |BG3/2/1 map, BG3 planes0–1, BG2 planes0–1/2–3, BG1 planes0–1/2–3 |
| Below-stage clear |56+4x |
| Low-resolution BG sample, main/sub palette, windows/math/output |58+4x, x0..255 |
| OBJ fetch for next displayed line |1080..1352, separate low/high pairs4clocks apart |

This table uses the reference's operation-before-step convention. H is the
master-clock counter, not a host X coordinate. Anomie's older timing document
says dots22–277 **with a question mark** and conjectures a2–3tile lookahead.
It does not prove that its visible/analog epoch equals this reference's internal
output epoch. We do not silently equate H58 to dot22 or claim a hardware-certified
absolute pixel origin. The chosen analytical deadlines have literal clock/pixel
oracles, including writesH43/H45 around BG1's H44 map fetch, H57 between the
below and output stages, and H0 fine-scroll changes. Console trace acquisition
and revision-specific absolute pipeline alignment remain M8 evidence work.

Name fetches capture map attributes, scroll, subtile/address and priority for
that fetch; character plane pairs sample separately at their slots. Fine scroll
is latched per line; coarse/vertical scroll follows subsequent map fetches.
Main/sub enables, windows, fixed color, math and brightness apply at the output
edge. In low resolution **both** palettes are sampled at H58+4x: belowH56 does
not perform a premature CGRAM read. Forced blank stops fetch/evaluation and blacks
future output; re-enabling can expose stale fetch data until refilled. We model
that retained digital pipeline, not an analog guarantee for every blanking glitch.

OBJ selection retains up to32 entries, including signed-X clipping and the
special X256 contribution. Fetch runs backward through selected OAM entries,
with at most34 eligible8pixel tiles. Each object's OAM descriptor/OBSEL is latched
when that object begins fetching, not all at H1080. Plane data is latched at two
distinct edges; its line buffer is serializable between them. Hidden columns do
not consume tiles except the documented X256 case. Range/time flags are generated
whether OBJ is enabled on either screen. On the last visible line (224 or239),
object fetch consumes its empty slots without reading patterns or setting a new
time-over for the non-displayed next line. OAM order determines the top object
before its priority is compared against BG; later high-priority objects cannot
rescue an earlier low-priority opaque object. All size pairs include rectangular
16×32/32×64 variants; vertical flip treats each square half separately.

One explicit research decision differs from that pinned software reference:
Anomie's odd OAM priority example says three reads after word-address0 and
rotation enabled select sprites63..70 on successive sprite-coordinate lines
when all objects are atY63. We implement the example as byte-pointer&3==3,
first=(bytePointer/4 + spriteY)&127. The surrounding formula in that publication
is internally inconsistent about word/byte parity. The literal three-read example
is tested; its revision/physical capture is unverified. This is not reported as
bitwise agreement with the software comparison reference for that case.

Mosaic uses a saved vertical countdown (enable edge reloads size+1; size-only
writes retain countdown) and per-BG horizontal countdown/sample latches. Line1
establishes the vertical epoch. Size changes and enable changes within a line
retain their phase, rather than restarting blocks on every write. Mosaic acts
before windows/math and does not affect OBJ. Its detailed midline model follows
the pinned comparison behavior; Anomie's reset-on-write description is uncertain.

## Digital output and state

Native output is RGB555 **before** INIDISP brightness. A separate byte per pixel
stores brightness0..15. `ApplyBrightness` exposes the chosen digital5bit rounded
scale independently; ARGB conversion follows it. This is a digital presentation
model, not a measurement of the SNES analog pedestal, DAC transfer curve or a TV.
For example, published tests show physical brightness0 need not look fully black.
`PpuFrame` carries explicit width/height/stride/field metadata, preparing a future
variable geometry contract; current arrays/output remain256×224 until M7.

Modes0/1 include8×8/16×16 whole-tile flips, name/base/palette addressing,
32/64 tilemap quadrant layout and wrapping, exact published BG/OBJ rank order and
Mode1 BG3 priority override. Independent main/sub masks, TMW/TSW, both windows,
inversion, empty intervals and OR/AND/XOR/XNOR are evaluated before selecting a
pixel. The color window clips main to black before math and independently masks
math. Fixed/sub operand selection, transparent-sub fixed fallback, add/subtract,
saturation and halve are lane-wise5bit operations. Halve occurs before addition
saturation, is suppressed on clipped main or transparent-sub fallback, and OBJ
palettes0–3 cannot participate. Backdrop remains a real math-designated layer.

Savestate **format6** rejects formats1–5. It includes native pixels/brightness,
VRAM buffer, every input/MDR latch, BG fetched tiles/planes/fine position,
selected objects, current descriptor/column/next phase, partial object planes,
both object line buffers, mosaic counters/samples and color-stage state.
Existing CPU/SPC/DMA/HDMA/event/firmware identity state is preserved. Public PPU
notices flush only after *all* operations on the edge commit; notices at one edge
observe the same committed state. They are telemetry, not additional queued
hardware actions. The APU no-effect fast path advances only the crystal position
when a currently pending segment ends after the requested time; timers/MMIO and
SPC semantic continuation remain untouched until their actual edge.

## Tests and reproduction

`PpuTests` uses original Apache-2.0 CPU programs assembled by `PpuDiagnosticRom`.
Registers, palettes, tiles, maps and OAM are written by real CPU/DMA execution.
Literal color/priority/window/bitflip/timing worksheets are independent expectations;
they are not snapshots blessed from the current renderer. Reports enumerate
unique executed original ROM hashes. Native comparison reports the first X/Y,
hardware line and supplied register/bus context. Lab ROM runs emit RGB555LE,
brightness bytes and ARGB separately; `--expect-native` uses that same comparator.

334 every-master-clock checkpoints cover CPU raster writes/OBJ fetch and actual
DMA/HDMA with IRQ and active APU. Each restoration continues against a separately
run **unloaded, uninterrupted** machine, checking complete serialized system state
and, in the transfer case, timed bus traces. Public BG/pixel/APU observer captures
exercise committed edge state. Additional original M0–M4 continuation checks remain.
Fresh palette/map/tile/clock/enable mutations use the same native verifier;
full-frame pixel mutations test first-pixel/line/context reporting. Old formats
and malformed pipeline structure are rejected.

The included **Zlib** undisbeliever VRAM mid-scanline ROM is external test software,
not a core component or our own work. Immutable source/license/ROM/tool hashes are
in `testdata/m5-ppu-manifest.json`. Its unmodified source says white means ignored
illegal VRAM writes, red means VRAM changed, blank means crash. The test runs24
actual emulated fields, asserts repeated active VRAM write loops execute, and
checks all57344 native pixels against independent RGB5557FFF. Building the same
source with pinned bass18 plus the unchanged ISC strict architecture table,
font conversion and upstream checksum script reproduces the ROM byte for byte.
This single source-derived run is not the broad M8 public hardware-test matrix.

```powershell
python tools/verify-ppu-testdata.py
python tools/rebuild-ppu-fixture.py  # optional network rebuild; Windows+Pillow
 dotnet run --project SNESEmulator.Core.Tests -c Release -- --ppu-only
python tools/test-lab.py --windows --full
```

## Sources and remaining evidence

- [Anomie's register research](https://wiki.superfamicom.org/registers),
  [backgrounds](https://wiki.superfamicom.org/backgrounds),
  [sprites](https://wiki.superfamicom.org/sprites),
  [rendering/color math](https://wiki.superfamicom.org/rendering-the-screen) and
  [timing](https://wiki.superfamicom.org/timing): published primary research.
  Their explicit uncertainties and the odd-priority formula conflict remain open.
- [SNESdev open-bus research](https://snes.nesdev.org/wiki/Open_bus): separate
  CPU/PPU output buses and unreadable-port masks.
- [Pinned cycle renderer](https://github.com/bsnes-emu/bsnes/tree/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/ppu):
  independently inspected timing/IO/background/object/mosaic/screen comparison,
  not newly acquired hardware evidence. No external implementation was transplanted.
- [Unmodified external VRAM test source](https://github.com/undisbeliever/snes-test-roms/blob/ac6ef8006809c0ff5dabc8ff137a4623967697fe/src/hardware-tests/vram-mid-scanline-test.asm):
  Marcus Rowe, Zlib. Original program, expected white/red/blank signal and retained
  license are separately pinned from the emulator.

Still unverified: console-specific absolute pipeline origin; analog brightness;
physical power/reset/open-bus decay; one-chip/revision bus conflicts; all undocumented
forced-blank/SETINI early-read glitches; the broad native image/hardware corpus and
commercial-game matrix. M7 implements modes2–7 and dynamic video geometry; M6
implements S-DSP audio. M9 profiles performance. None of these is silently marked
passing by a Mode0/1 diagnostic or a deterministic save/load test.


## Clean implementation measurement

Full gate `python tools/test-lab.py --windows --full` passed with exit0 on
`9e5a7817af00428915e36f1171d748577e28953e`; all reports record a clean tree.
129 portable groups include42 M5 groups and114 unique original CPU-ROM variants.
Five Windows/network groups,11 laboratory mutations, the full5,080,400 CPU
semantic/bus cases,256,000 SPC semantic/approved bus cases and4 original local
IPL checks pass. Raw SPC still has249,000 passes and7,000 individually described
adjudicated mismatches. The external VRAM program executes86,016 active writes,
then passes its57344pixel source-derived white oracle. Native CLI2/2 and all five
UI smoke scenes pass; screenshots were inspected, with save data isolated.

The core takes126.56seconds; its earlier120second timeout is retained as a real
red result, and the expanded suite now has a240second supervisor. Actual per-group
times, all original ROM hashes, preserved report hashes and the intentional
reporter restart are in [M5 baseline](baselines/m5.json). Local120field benchmark:
21.62 fields/s, active SPC700 and full PPU accuracy path, no features disabled.
This is below realtime and stays on M9's optimization backlog. Parent independent
acceptance is still pending. No claim in this measurement is a new console trace.
