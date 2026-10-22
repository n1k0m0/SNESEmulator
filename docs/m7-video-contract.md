# M7 native video, raw fields and transport contract

Own C# core; independent parent acceptance pending. M6 was independently accepted
on `e4645770cea052885808ca5b27a45d60de470157`, full gate exit0,
`artifacts/root-m6-validation.log`, unchanged reports in `artifacts/root-m6-evidence/`.
No original console is owned or newly measured. M7 extends the existing analytical
two-chip 2/1/3 NTSC/PAL target; no additional silicon revision is invented.

## Graphics and timing

Modes2–6 use their individual BPP, palette and priority arrangements. Eight
four-master-clock fetch slots per32 clocks explicitly acquire map words, separate
plane pairs and BG3 OPT words. Mode3/4 BG1 has all8 planes; Mode5/6 have two
separately fetched subtiles per map group. The line-start fine offset and a
continuously advanced cursor determine pixel consumption, including Hires
BELOW/ABOVE halves; switching modes does not derive a new cursor from screen X.
Mode0/1's existing H58 output convention and default256×224 regressions remain.

OPT has separate horizontal/vertical latches, valid bits per BG, an unaffected
first group, Mode4 direction selection, BG3 tile dimensions and Mode6 coordinate
units. Sixteen-high tiles, whole horizontal/vertical flips, wrapping and 8bpp
direct-color groups retain their own map/plane acquisition deadlines. Mosaic
retains a countdown and sampled group; Mode7 retains separate horizontal source
coordinates, including midline enable and size changes. EXTBG vertical mosaic
uses BG1's enable input, while BG2 horizontal mosaic uses BG2's input.

True Hires samples BG BELOW at H56+4x and ABOVE at H58+4x. Palette, windows,
main/sub selection, math and output commit at H58+4x. Those are distinct stages:
a register or palette change after the BG-BELOW sample can still affect its
screen acquisition. CGRAM address ownership is updated by real palette lookups;
direct color bypasses that lookup. Hires/pseudo-hires expose the subscreen on
the even half and main on the odd half. The previous main color/math decisions
participate in subscreen math. The selected deterministic first-half value is
black, following the software comparison's explicitly unconfirmed initialization.
Its physical left-edge color remains unknown.

Mode7 uses signed16 matrix terms and signed13 centers/offsets. Offset-minus-center
clipping selects ten low bits using bit13 as the sign; this differs from normal
signed13 extension. The three origin products individually discard their low six
bits before addition; the horizontal integer step is added before the final
eight-bit shift. Wrap, outside transparency, tile0 fill, XY flips, direct color,
EXTBG seven-bit palette and separate BG2 priorities are implemented. Matrix/scroll
writes use the existing shared Mode7 write latch and signed multiply ports.
Mode7 map/character operations currently run at the analytical ABOVE endpoint.
Their internal electrical VRAM subslots are not established by this project's
evidence and are not labelled cycle-perfect.

BG fetching and shifting continue during forced blank in the selected software
timing model. Blank suppresses output and has separate OBJ rules. This resolves
an earlier mismatch with that comparison; it does **not** reproduce the published
physical mid-frame activation corruption. [Undisbeliever's INIDISP research](https://undisbeliever.net/snesdev/registers/inidisp.html)
shows corruption on consoles and discusses uncertain internal latching. Those
effects, early-read glitches and analogue brightness transients remain M8 evidence
work. Digital brightness remains a separate0..15 plane, not an analogue capture.

## Fields and geometry

`PpuFrame` is a **raw field**, never a woven448/478-row frame. Its exact arrays
have `Stride*Height` entries: Width/Stride256 or512, Height224 or239. Field is
the physical parity; FieldIndex increases at field boundaries. Overscan describes
the retained239-row storage extent. StartClock/EndClock are master-clock boundaries
in the included rational master frequency; the included rational APU crystal
frequency maps them to native audio without a50/60Hz host assumption.

`Interlace` records the display input at field start. `TimingInterlace` separately
records the actual counter latch controlling short/long lines and field duration.
They can differ after a mid-field SETINI write. BG and OBJ interlace address
generation use their live register inputs at their own fetch phases, so a raster
transition can mix addressing within one raw field. The metadata does not claim
that every row used an identical register snapshot. OBJ interlace halves coverage
height and selects parity-specific source rows, including vertical flip.

The counter samples its interlace input at V128/H0 in this selected model. The
pinned comparison itself calls the precise capture time unimportant before the
affected lines; V128 is therefore a reproducible analytical convention, not a
verified silicon deadline. Field-end projections before V128 use the current
input, while actual execution still waits for the latch. NTSC noninterlaced odd
fields have a1360-clock line240; PAL interlaced odd fields have a1368-clock
line311. An interlaced even field adds one line. All other lines have1364 clocks.

Field-start allocation may be256-wide, but the first live Hires/pseudo-hires
segment promotes it losslessly to512. Earlier lores samples are duplicated;
returning to lores also duplicates subsequent samples. No previously emitted
native half-pixel is discarded. Enabling live overscan can similarly extend
storage to239; disabling it blanks the newly inactive rows without deleting
earlier output. `CompletedFrame` is captured before BeginField prepares the
next field, so RunFrame consumers retain the completed geometry and pixels.

The GUI presents raw fields in a fixed4:3 destination, independently of source
width. This is field stretching/bob presentation, with no hardware weave or
discard of source half-pixels. Previews use the saved image's exact geometry.
Lab ARGB/RGB555/brightness dumps and comparators use its stride. Native comparisons
include first pixel/line, brightness, field metadata and timestamp domain;
`--expect-geometry` and `--expect-brightness` are separate CLI oracles.

## State and transport

Savestate **format8** stores exact array lengths/geometry, completed field,
all plane/OPT/half-screen/mosaic/affine/math latches and the existing complete
CPU/DMA/HDMA/SPC/DSP phases. Formats1–7 are rejected with an explanation because
their missing pipeline data cannot be recovered reliably. Presentation queues
remain separate. Public PPU notices publish after the same timestamp's APU
commit; public PPU/APU/DSP snapshots resume against unloaded uninterrupted
references, including active CPU, real DMA/HDMA and native DSP tails.

Handshake `0x534E0003` uses VID3 lossless deflated full ARGB fields. The88-byte
little-endian header carries magic, width, height, stride, flags, decoded byte
count, transport frame ID, field index, start/end masterclocks and both rational
frequency pairs. Flags are parity1, display-interlace2, overscan4, raw-field8
and timing-interlace16. Only native256/512 ×224/239 geometries are accepted.
Unknown flags, inconsistent stride/count, negative/overflowing time metadata,
invalid rates and truncated/overlong decompressed data are rejected before
installation. Buffer reuse requires the exact new element count. Every frame
is independently decodable; no old-size delta base exists, and visually equal
fields still carry their distinct parity/time metadata. The existing bounded
per-client queue, TLS/relay and input permissions remain.

AUD2 audio's40-byte native stereo payload and integer sample/crystal timestamps
are unchanged within handshake3. Device resampling, volume, replay/menu pause
and clearing on load remain presentation behavior. M9 still owns full threading,
pacing, latency and performance work.

## Evidence and reproduction

Sources, immutable comparison commit, original generator/worksheet identities
and license boundaries are pinned in `testdata/m7-video-manifest.json`. Primary
research is [Anomie's backgrounds](https://wiki.superfamicom.org/backgrounds),
[registers](https://wiki.superfamicom.org/registers),
[rendering](https://wiki.superfamicom.org/rendering-the-screen) and
[timing](https://wiki.superfamicom.org/timing). The separately inspected
[cycle-renderer comparison](https://github.com/bsnes-emu/bsnes/tree/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/ppu)
is software evidence. No external core is embedded, translated or transplanted.

Original65816 ROMs initialize MMIO and DMA; expected values come from literal
coordinate, bitplane, priority and clock worksheets. Region/field tests use
different even/odd source rows. Full native fields compare all pixels and
brightness without rescaling. Genuine build-time mutations of Mode7 integer
steps, OPT valid bits, Hires sampling and completed-field geometry must turn
targeted ROM tests red. A first attempted start-height mutation was **ineffective**
because correct later overscan promotion repaired it; the mutation runner
correctly failed rather than counting that as detection. Its original reports
remain in `artifacts/m7-first-mutant-geometry/`.

The unchanged licensed external VRAM program and full CPU/SPC/DSP corpora remain
separate evidence. No own output is promoted to a golden image. No commercial
game compatibility, new console measurement, physical reset model, additional
revision accuracy or realtime speed is implied. Performance measurements and
the final clean gate identity are recorded in `docs/baselines/m7.json` after
completion. The pre-M0 `publish/` directory is deliberately not a new release.

```powershell
python tools/verify-video-testdata.py
dotnet run --project SNESEmulator.Core.Tests -c Release -- --video-only
python tools/video-mutations.py
python tools/test-lab.py --windows --full
```
