# M8 console comparisons and published hardware evidence

M8 keeps the independently implemented C# product core. Neither external core is
linked, translated or installed in the product. WSL is an isolated laboratory
build tool. M7 was independently accepted on `439a655`; this milestone explicitly
corrects two timing assumptions that its synthetic worksheet did not detect.
Final acceptance by the parent remains separate from implementation validation.

## Reproducible programs and references

`testdata/m8-roms/manifest.json` identifies20 unchanged Zlib programs from
undisbeliever/snes-test-roms at `ac6ef8006809c0ff5dabc8ff137a4623967697fe`.
`python tools/m8-rom-build.py` uses the previously pinned ARM9 bass18 and unchanged
ISC strict65816 architecture. Resource generation and checksum completion follow
the upstream makefile. Source/ROM hashes and the license are retained. The earlier
M5 VRAM fixture remains unchanged.

`testdata/m8-game/manifest.json` identifies the unchanged unnamed-snes-game tech
demo from `1c06fee8fbaffbc720ca5fee3dd8c5f2f5838254`, including recursive submodules,
Cargo.lock and compiler hashes. `python tools/m8-game-build.py` builds with
Rust1.85.1, Cargo `--locked`, GCC13.3 and the original Wiz compiler. GCC receives
`-include cstdint` for the old compiler's missing SIZE_MAX declaration; WERR=0
permits upstream warnings, without editing the source. The compiler bootstrap
checks the official rustup archive hash. MIT, Zlib, CC BY-SA4.0, CC0/public-domain
and Apache sample attributions are retained beside the ROM. No commercial ROM
was acquired. LoROM and HiROM are exercised; other mappings, enhancement chips
and peripherals remain untested.

`python tools/m8-reference-build.py` builds these two independent consoles:

| Reference | Commit | Actual execution path |
|---|---|---|
| bsnes | `78474e5d39f58872dc21aca18cf9eb583b147fe7` | Cycle PPU, accuracy DSP, FastPPU/FastDSP/hotfixes disabled; runtime checks verify the selected path |
| ares | `9408cb43d4948fc3ea6e152a307a34348df3fe04` | PROFILE_ACCURACY, Pixel Accuracy, actual implementation pointer checked against ppuImpl |

Ubuntu24.04.4/GCC13.3 versions, exact build commands, binaries, objects and all six
observation overlay diffs/hashes are retained in the build identity and logs.
Fresh clones explicitly detach the pin. Existing unexpected or modified tracked
checkouts fail. Only full builds are accepted; stale observer objects cannot be
identified with a new overlay hash. Upstream checkout files remain unchanged.
CPU hooks record actual2-master-clock ticks, parity edges and MMIO. DSP hooks copy
integer DAC pairs immediately before stream submission. They do not alter clocks,
registers, filters, return values or scheduling. Ares video is copied synchronously
from `screen->pixels(true)` in the existing refresh hook, before presentation.

## Coordinate, input and audio domains

All fixtures retain the full upstream buffers. Fixed source-derived extraction
uses visible SNES line1 and the selected parity, without image search or shifts:

| Source | Native extraction |
|---|---|
| Own | RGB555 before brightness plus separate4-bit presentation plane |
| bsnes | 512×480 uint16, row `2*(y+1+(overscan?0:7))+interlaceParity`; lores X×2; swap outer5-bit channel groups, then compare with own digital brightness projection |
| ares | 564-wide uint32 RGB555 plus brightness in bits15–18; row `(y+1+(overscan?0:8)+(PAL?20:0))*yScale+interlaceParity`; X border26 NTSC/22 PAL, lores X×2 |

Asymmetric red, blue and mixed-color controls prevent a channel-order mistake
from passing. Ares retains its brightness register during forced blank; own
presentation brightness is zero there. Black RGB therefore does not assert full
19-bit equality. Dynamic late overscan cannot always be recovered from bsnes's
480-row buffer and is not forcibly cropped out of bounds.

Replay files name physical zero-based raw fields, counted from executed CPU tick
parity edges, rather than refresh callback counts. Bsnes actually polls all12
inputs before and after its video callback and checks equality in that field.
Five external window replays produce five distinct reference images. Both actual
parities and explicit PAL/NTSC profiles are checked. Named game checkpoints are
title120, file menu175, active room300 and long segment600. Source
`dungeons.json` selects playground/00-01-test and `sl_floor`; the numbered1/2 tiles
are the unchanged prototype floor asset, not a rendering-error allowance.

Default APU frequency remains24,576,000Hz (32000 native pairs/s). Both unmodified
references use24,606,720Hz (32040 pairs/s). Separate explicitly forced own
`ntsc-reference-crystal` cases isolate that choice. Reports contain exact rational
frequencies and rates. All three sides use the same explicitly selected firmware
within each dataset: freely distributable Own IPL for CI; local original IPL for
the additional full gate. Original firmware is never redistributed. Empty battery
input is explicitly zero-filled on all sides; differing undefined power-on RAM is
not silently treated as a product defect.

Native audio starts at the first DAC pair after power, with no alignment search.
4096 internal or32768 external pairs form named initial checkpoints. The entire
overlapping sequence is also compared; a differing sample there remains an
accuracy failure. Only terminal lengths are separated because bsnes captures at
V224, ares atV240 and own output at field end. This is whole-console evidence,
distinct from M6's accepted isolated DSP vectors. A silent room is not labeled a
sounding test: this demo explicitly selects song `blank`; its title music has a
separate nonzero stereo and uninterrupted-state test.

## Core corrections and save contract

WAI now keeps its six-master-clock idle oscillator running. The old fast event
jump lost an unfinished0/2/4-clock idle phase at IRQ assertion. The unchanged
brightness-delay program and both executed reference MMIO traces establish the
correct V88/V184 H392 endpoints. STP still suspends the CPU until reset.

Pinned cycle implementations synchronize PPU with strict `<` before CPU/DMA MMIO.
Own endpoint phases therefore commit after the bus effect at the same timestamp;
this is a precise tie ordering, not a global raster shift. H0 field allocation,
fine-scroll and mosaic latches follow that ordering. Pending H0 is derived from
serialized CPU/DMA/raster rest state. Observer snapshots cover typed field start,
APU, CPU write latch, DMA and committed PPU, against an unloaded uninterrupted run.

The V128 counter input is the display latch captured at V0, not live SETINI. Both
sources explicitly return that latch. An own minimal program changes SETINI at
V64; actual independent field edges are NTSC357368/714736 and PAL425568/851140.
Old M7 first-field constants358732/716100 and426932/852504 were incorrect.

Savestate format9 rejects formats1–8. Older WAI saves did not contain the skipped
idle remainder, and the previous H0/interlace meanings cannot be migrated
unambiguously. New format9 retains complete execution, DMA/HDMA, SPC/DSP, native
field planes and pipeline state. VID3/AUD2 transport stays unchanged.

## Hardware and regression limits

[Hardware ledger](../testdata/m8-hardware-evidence.json) separates primary published
measurements, specific programs, software comparisons and our results. Published
INIDISP corruption, early-read/DMA errata and analog transients remain failures or
untested; agreeing digital software images do not close those defects. Exact
capture PCB/binary identities unavailable from a source are null, not inferred.
The oscillator article's real measurements are distinct from its later Mesen
simulation. Six rounded extrema execute real IPL/port flows as rational parameter
tests, not as reproduced console measurements or a universal32040Hz claim.

`python tools/test-lab.py` runs fixture integrity,27 M8 state groups and the Own-IPL
offline comparisons. A missing original IPL is an explicit skipped local gate;
`--windows --full` requires it. No reference build or network is needed in CI.
Missing/corrupt fixtures, crashes and timeouts remain errors. Negative controls
exercise changed channels, coordinates, pixels, lengths, samples and child failures.
`--accuracy-only` returns1 for observed accuracy errors; `--verify` checks the
separately reviewed known-difference contract while preserving red accuracy rows.
There is no automatic golden or reviewed-error update and no compatibility rate.
M9 retains product performance, UI, persistence fault handling and distribution;
M10 retains enhancement chips/peripherals. No real-time or release claim is made.


## Remaining digital audio findings

Both datasets have exact native images at every selected checkpoint and exact
SRAM on the game cases. Full audio remains red on11 original-IPL and7 Own-IPL
comparison rows. Nominal32000-vs32040 cases first differ at pair42296. With the
explicit same-frequency profile, ares first differs at pair42895 with Own IPL or
42338 with original IPL; bsnes first differs at88915 with either firmware. The
references disagree with one another. Own/bsnes match every register and all
ARAM bytes at the first Own-IPL ares mismatch. At88915 ARAM agrees with bsnes and
only voice2 OUTX differs; nearby pitch/volume writes cross different sample bins.
This is an unresolved command/poll/register/voice-phase discrepancy, not proof of
a specific arithmetic defect. Unused ares power entropy is not treated as failure.

`python tools/m8-audio-probe.py --short` actually runs82/168-field reproductions
with both independent references and own core. Its side-effect-free register,
ARAM, CPU-port and DSP-write observations have explicit before-stream versus
after-same-crystal-commit epochs. The original300-field probe was byte-identical
to the fixture video, PCM and SRAM. [Repro inventory](../testdata/m8-audio-repros.json)
contains every red row, first stereo pair/crystal, firmware, replay and cause
status. No sample shifting or tolerance makes these findings green.
