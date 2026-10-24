# DSP1 cartridge implementation and evidence (M10a)

October 24, 2026. This is a usable own C# uPD7725 interpreter executing explicitly
selected local DSP1 firmware, with cartridge ports, time and savestates. It is not
an HLE command approximation, a linked external core, general game acceptance,
or original-console certification. SuperFX and SA1 follow as separate stages.

## Setup

Next to your own ROM, create `<ROM filename>.hardware.json`, for example
`C:\Games\example.sfc.hardware.json`:

```json
{
  "Variant": "Dsp1B",
  "Board": "LoRom1MiB",
  "Firmware": "dsp1b.rom",
  "FirmwareSha256": "D789CB3C36B05C0B23B6C6F23BE7AA37C6E78B6EE9CEAC8D2D2AA9D8C4D35FA9"
}
```

The firmware path is relative to that configuration file. Supply your own
8192-byte **little-endian `.rom`**: 6144 program bytes then 2048 data bytes. Firmware
is never bundled, downloaded by the application, or written into a savestate.
The loader rejects missing files, unknown identities and big-endian `.bin` data.
`Dsp1` and explicitly selected `Dsp1A` bind to SHA256
`91E87D11E1C30D172556BED2211CCE2EFA94BA595F58C5D264809EF4D363A97B`.
The [original dump author](https://caitsith2.com/snes/dsp/) identifies the
DSP1/1A firmware as identical; that does not certify their physical revisions or
oscillators. DSP1B is a different identity. Chipset03/05 alone is ambiguous:
DSP2/3/4 are never silently treated as DSP1. A sidecar must explicitly select the
board and variant; the header cannot prove the PCB is physically that board.

| Configuration | Published PCB example | Banks | Data port | Status port |
|---|---|---|---|---|
| LoRom1MiB | SHVC-1B0N-01 |30–3F|8000–BFFF|C000–FFFF|
| LoRom2MiB | SHVC-2B3B-01 |60–6F|0000–3FFF|4000–7FFF|
| HiRom1MiB | SHVC-1K0N/1K1B/1K1X |00–1F|6000–6FFF|7000–7FFF|
| HiRom2MiB | SHVC-2K0N/2K1X/2K3X |00–0F,20–2F|6000–6FFF|7000–7FFF|

The same bank windows with bit7 set are decoded. Other addresses retain the
ordinary ROM/SRAM/open-bus rules. Maximum ROM sizes and LoROM/HiROM selection
must match the configuration. Port ranges and PCB examples come from the
[author-published DSP1 board table](https://www.crazysmart.net.au/dsp/dsp1.html).
The latter is not a new measurement of every mirror or board revision.

## Core, ordering and state

`Upd7725` implements OP, RT, JP and LD, both accumulator flag banks, cross-carry,
signed multiplication, pointers, stack and the RQM/DRS/DRC host interface.
Serial input/acknowledgement and external INT are unwired for these boards;
this is not a complete general-purpose NEC peripheral emulator.

The manufacturer source actually read is NEC's 1989 DSP and Speech Products
Data Book, document NECEL-001050, printed 2-25..2-35, mirrored at
[the manufacturer-book archive](https://bitsavers.trailing-edge.com/components/nec/_dataBooks/1989_DSP_and_Speech_Products_Data_Book.pdf).
SHA256:`2f0190523de99938cc1da1c138f2c5b7f71573eb11bcb3b0dcdd1da1267e85ce`.
Printed2-25 / PDF index 73 gives one instruction per external clock;
2-29 / index 77 states a four-level program stack;2-31 / index 79 says a move to the
selected accumulator suppresses that ALU operation. Those last two literal
rules differ from the shared bsnes/MAME processor lineage. Tests retain the
separate manufacturer expectations, rather than claiming software unanimity.
Stack overflow behavior is not certified by the four nested-call test.

The Nintendo developer-book DSP1 diagram 3-4-4 labels a 7.6 MHz clock:
[Book2 scan](https://floating.muncher.se/bot/manual/book2_text.pdf), SHA256
`887340a415e2c5f377074019cabbb9154b60d69149cb69b4fceb84d3c33d2751`.
The current profile uses that **nominal instruction frequency** with an integer
rational carry against the selected console master frequency. The first
instruction completes at the first elapsed instruction boundary, and a chip
instruction at a CPU-port endpoint precedes that host access. Physical phase,
oscillator tolerance, reset pulse width and per-PCB clock differences remain
unverified. No claim transfers this nominal profile to all DSP variants.

The chip has no shared CPU memory or interrupt output in these configurations.
It therefore catches up to the actual host-port timestamp, capture, or warm
reset without a callback on every normal-cart CPU/PPU event. It executes the
whole elapsed interval, including RQM wait loops; it never stops early when a
request bit happens to become set. Warm reset commits the old interval before
retaining chip RAM/registers and starting a new epoch. Cold RAM is a deterministic
zero seed. The detailed warm reset register selection follows MAME's cited
NEC 3.6.1 interpretation; physical DSP1 reset retention remains unmeasured.

Normal carts continue writing **format 9, byte unchanged**. Enhanced carts use
**format 10** with a versioned kind/payload envelope and version 1 DSP1 payload:
firmware SHA256, selected variant/board, instruction rate, last master clock,
rational remainder, all registers, working RAM, flags and return stack.
No microcode bytes or local firmware path are serialized. A load creates an
isolated cartridge and machine, validates oscillator consistency and complete
identity, and only then permits worker installation. Format9 is not accepted
for an enhanced cart, and format 10 is not accepted for a normal cart.
Manager catalog, preview, import/export and source-path relinking understand
both versions. Hardware options are separate from the unchanged normal-cart
options JSON.

Two internal phase questions remain explicitly software-derived: simultaneous
RAM read/write uses the old RAM value for the ALU, and K/L writes produce the
multiplier latch available to the following instruction. The manufacturer's
coarse move/multiply wording does not specify every internal sample edge.
The implemented ordering agrees with the separately executed reference and
actual dumped microcode command results; that is not an oscilloscope capture.
ADC/SBB equality ties retain the reference's truncated comparison for carry.
For A=0000, P=FFFF, cross-carry 1, that differs from ordinary full-adder carryout;
physical flag behavior remains unresolved. Comments do not call this a
manufacturer-proven formula.

## Executable evidence and open accuracy results

Beyond multiplication, optional local tests run the thirty documented command
families in `command-plan.json`, including trig/vector/rotation, all three matrix
setups and transformations, projection setup/object/screen outputs, memory test,
version and a bounded raster stream. The raster test consumes two complete rows
plus three words of a third, then replaces its final D transfer with 8000 to exit.
A first attempt that sent 8000 after the final D read failed subsequent version
and DATA-ROM identity checks and remains ignored historical evidence.

The accepted reference transcripts contain 4297 DSP1 / 4298 DSP1B timestamped SR/DR
operations. Local own-core replay matches every recorded status/data value and
resumes from 65 chip snapshots per variant. Negative controls change a parameter
input, a mathematical output sample and truncate an event; each must fail.
Command 1F reads 2048 bytes but its values are **not in public testdata**: read
operations carry a zero placeholder, with only byte count and SHA256 compared
to the exact local firmware data section. No ROM reconstruction data is hidden
in the public trace. This is bounded software-reference input sampling, not
complete input-domain coverage, electrical timing or general game acceptance.

The published Suzuka DSP-interface log was also inspected locally (archive SHA256
`e20e56a2f197b8069373c9b3e5ddf500e53e0a0915d341dc5752fa74426a8a97`).
Its page attributes the 2003 capture to The Dumper and grants attributed binary/source
reuse. The archive provides one unannotated byte stream without reproducible
read/write directions, timestamps or a precise chip/board identity. It was not
executed as an oracle or redistributed. Inferring those fields to get agreement
would not establish independent hardware evidence; that acceptance stays open.


The offline functional gate runs the authored diagnostic through the actual
65816 bus on all four boards in NTSC and PAL. It tests byte/word handshakes,
status-write behavior, literal PCB boundaries, multiplier/ALU ordering,
manufacturer accumulator/stack rules, eager/lazy timing equivalence, retained
warm RAM and malformed configuration/state rejection. It resumes at 401 two-master-clock stops per region/board, including host halfwords and fractional clock
remainders: 3208 full state continuations. These diagnostics are original,
redistributable programs, not commercial games or external Nintendo firmware.

The separately built laboratory reference is pinned bsnes commit
`78474e5d39f58872dc21aca18cf9eb583b147fe7`. The adapter includes the unmodified
processor instruction/port files; no reference executable or core is linked into
the product. Input bytes, expected outputs, source identities and exact sample
coverage are pinned in `testdata/dsp1/manifest.json`.

**The raw 8192-case software audit has 49 visible failures**, all narrowly classified
as hidden DRS retention when an internal SR write leaves 8-bit mode. The current
implementation preserves the latch per the manufacturer's protectedDRS
statement; bsnes's status conversion loses it in that state. The audit still
reports those raw failures. An inventory guard checks 49 scoped / zero unexpected
differences; it does not turn them into accuracy passes. The raw sample covers
6144 OP, 1024 LD, 1024 JP, all 16 ALU operations/sources/destinations and 38 branch codes.
Selected-accumulator destinations were excluded in authored generation and have
separate literal-manufacturer tests. This is a sample, not exhaustive ISA proof.

An earlier 8192-case constrained experiment cleared initial hidden DRS whenever
DRC8 was selected. It and 1024 RT derivatives remain byte-preserved as historical
inputs/outputs; they are **not the functional acceptance corpus**. An automatic
approval review rejected a later attempt to narrow that rewritten input set to
49 cases, because changing states to obtain agreement conflicts with preserving
raw/source-conflict evidence. That rewrite was not executed or retried. The
safer adopted path preserves the original raw audit, its 49 failures, all earlier
bytes/reports, and separates functional acceptance from disputed accuracy.
The additional 1024 constrained RT cases are identified separately in the audit.

Optional local tests execute each hash-verified DSP1 and DSP1B microprogram for
72multiply commands and its version response. Command 00's 36 inputs agree with
signed mathematical multiplication. Command 20 agrees with both interpreters
but differs from the published `M+1` HLE formula in 14/36 cases per variant.
An ignored PC witness shows an OR of M with A=1 at DSP1B PC 1B0, followed by
old accumulator data transferred to DR. All 36 tested results fit `M | 1`.
This explains the observed discrepancy without modifying firmware, carry logic,
clock, or a golden result. It is evidence about those exact dump bytes under two
software executions, not a new original-hardware capture or universal command
suite. The first failed formula reports are retained under ignored artifacts.

The default public invocation explicitly reports that proprietary firmware was
**not executed**. Optional local pass reports are separate. No title screen,
commercial-game compatibility or complete DSP1 command coverage is certified.

## Reproduction

```powershell
python tools/verify-dsp1-testdata.py
# Functional/spec/host/bus/state tests, no proprietary firmware required:
dotnet SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll dsp1-proof artifacts/dsp1-functional
# Intentionally returns 1: raw 49 accuracy differences remain visible.
dotnet SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll dsp1-source-audit artifacts/dsp1-raw
# Optional, local-only firmware execution (neither firmware file is distributed):
dotnet SNESEmulator.Lab/bin/Release/net10.0/SNESEmulator.Lab.dll dsp1-proof artifacts/dsp1-local testdata/local-firmware
```

The normal portable/Windows laboratory runs the public functional and raw audit
as separately supervised commands with their respective expected exits.
`tools/generate-dsp1-vectors.py` reproduces only authored input bytes and never
writes expected results. The C++ reference adapter is laboratory-only and
requires the separately obtained pinned source; its build command is recorded
in the manifest. Fixture/manifest mutations must fail integrity checks.

Windows/Ubuntu executed report identities, binary/source hashes and exact
remaining gates are recorded in the ignored `artifacts/m10a-dsp1/` handoff.
Final long NTSC/PAL core performance, strict native hours, final package and
remote CI run after the remaining M10 stages, not on every intermediate chip.
