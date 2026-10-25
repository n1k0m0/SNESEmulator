# M10b: SuperFX / GSU digital model

This stage adds an independently authored C# GSU interpreter and shared-bus
cartridge integration. No external emulator is linked into the product. Passing
functional diagnostics does not certify commercial games or original silicon.
SA1 and the remaining enhancement families are separate work.

## Supported board selection

Cartridge types 13/14/15/1A are accepted with an explicit configuration. The supported
subset has up to 2 MiB LoROM and 32/64/128 KiB shared work RAM. GSU1 and GSU2 select
an explicit identity but currently share the documented digital instruction model;
revision-specific errata are unmeasured. Separate battery RAM and CPU-only ROM
extensions are not implemented: nonzero BackupRamBytes/CpuOnlyRomBytes are rejected.
BRAMR is latched for register/state completeness; there is no independent backup
memory for it to enable. Work RAM uses the existing SRAM persistence machinery.

Place `<ROM filename>.hardware.json` beside the actual ROM path:

```json
{
  "Revision": "Gsu2",
  "RamBytes": 131072,
  "CrystalHzNumerator": 945000000,
  "CrystalHzDenominator": 44
}
```

The oscillator is independent of the console's PAL/NTSC master clock. These are
explicit nominal digital targets, not measured board tolerances. CLSR=0 divides
this oscillator by2 for the processor; CLSR=1 uses its full rate. The programmatic
configuration is immutable and retained across temporary Runtime/Manager loads.
Unsupported sizes, unknown fields/revisions and unrepresentable phase domains
fail before installing a new machine.

## Execution and bus invariants

`Gsu` implements all256 instruction bytes and ALT1/ALT2/prefix selection, source
and destination registers, flags, one-byte pipeline and branch delay slots. A
512-byte cache has 32 independently valid 16-byte lines. CPU writes to a line's
last byte validate it. CBR changes/PBR writes and forced GO0 invalidate cache;
auto STOP retains it. STOP still prefetches its following byte, including a
potentially inaccessible next line.

Execution phases are 0(begin pipeline fetch),1(opcode fetch),2(operand planning),
3(operand fetch),4(micro-operation execution). Cache fill indices, each pending
transfer deadline, ROM buffer, delayed two-byte RAM write, source/destination,
pixel rows and graphic read/modify/write byte are serialized. There is no CLR
iterator continuation or instruction-at-once budget overshoot. All immediate
transitions at a timestamp drain before exposing the next positive deadline.

Only shared-memory boards install extra peripheral boundaries. Ordinary boards
retain their old clock callbacks. CPU and DMA accesses synchronize the chip before
the cartridge bus effect; same-clock chip effects precede the CPU/DMA endpoint.
The ownership flags pause denied ROM/RAM transfers, preserving remaining latency.
Blocked ROM reads return the manufacturer's16-byte dummy vector pattern; blocked
RAM reads preserve CPU open bus and blocked RAM writes do not change work RAM.
This write-ownership choice differs from the pinned software reference's CPU write
path and still needs physical confirmation.

STOP sets an internal IRQ flag. CFGRbit7 masks the output separately. Reading the
high SFR byte acknowledges it. The restored chip output must agree with the
serialized CPU I/O line, or the isolated load is rejected.

The state writer synchronizes the chip before copying shared RAM. Ordinary carts
retain format 9 and its exact field layout. Enhanced carts use existing format 10,
a version 1 typed envelope and a separately versioned GSU payload. Board/oscillator,
phase/deadline, micro-operation shape and IRQ consistency are checked before a
candidate machine can replace the running one. Warm reset commits elapsed work,
retains cartridge RAM and resets the chip/host epoch before untimed CPU vector
reads. Undefined power-on registers use a deterministic seed, not a measured RAM
pattern.

## Primary sources and unresolved contradictions

The manufacturer source is [Nintendo Development Manual BookII](https://floating.muncher.se/bot/manual/book2_text.pdf),
SHA256 `887340a415e2c5f377074019cabbb9154b60d69149cb69b4fceb84d3c33d2751`.
Sections 2-3 through 2-9 cover mappings, registers, execution/cache, buffering, graphics
and instruction definitions. Specific literal tests cover2-5-3 STOP R15,
2-5-5 dummy vectors/IRQ,2-6-2 branch-in-delay operands and2-6-12 STOP prefetch.

The cache timings in the instruction tables count prefix bytes. The model includes
RAM-read latency: slow cached LDW 7 cycles and LDB 6 cycles including ALT1. High-speed
cached LM takes 14 cycles including ALT1 and operands, matching2-7-5's bulk example.
Slow prefixed loads include one extra internal cycle to match the instruction
entries. The manual's external-ROM/RAM execution tables contain differences which
this implementation does not claim to have resolved. Bus overlap, wait phases and
physical timing need original captures. The known CACHE/R14 overlap caution in
2-7-1, and fast-multiply invalid-result errata, remain unmodeled.

The manual's2-8-17 FMULT destination-R4 caution does not establish a physical
result for this unsupported destination. The current deterministic policy leaves
R4 untouched for FMULT; LMULT destinationR4 receives the high word. This is a
model choice for an unsupported/uncertain corner, not proof of physical retention.
The original PeterLemon FMULT ROM expects a numeric result in R4 and visibly
fails case 9 under this choice. Its unmodified failure is retained as an accuracy
contradiction; it is not reclassified as a passing ROM. Independent developer
[Randal Linden's guide](https://github.com/RandalLinden/DOOM-FX/blob/master/docs/sfx.txt)
also cautions against this destination but supplies no captured resulting value.

The primary2-8-13 transparency diagram checks the effective2-bit color in2bpp,
4bits in4bpp and8bits (or frozen low nibble) in8bpp. The model follows those masks,
including prefilled-pixel controls. Transparency checks the selected drawing color after dithering, following the
nocash author research and diagram. BookII2-8-11 and shared bsnes instead test
before dithering; the contradiction stays red in the primary-source audit. The
pinned software also checks4bits in2bpp, unlike both primary effective-bit masks.
No new physical capture resolves these claims.

The nocash original research explicitly identifies ordinary STOP R15=address+2,
consistent with the prefetch/postincrement pipeline; this is the default model.
Transfer/jump delay-slot STOP retains destination+1. BookII's ordinary address+1
claim remains red in a separate primary-source audit. Earlier32 raw STOP
disagreements are preserved as historical reports, not relabeled green. No input
or expected software output was edited during this source adjudication.

## Laboratory contracts

`superfx-proof` is a separately bounded public/offline functional command. It
includes 256x4 decoding smoke samples, 32768 independent integer arithmetic cases,
literal source examples, all graphics depth/height layouts, active CPU/GSU cache/
RAM/IRQ integration in both regions and arbitrary-budget continuations. Continuation
checks compare both complete processor state and final RAM. Timing-phase coverage
is reported, with explicit witnesses for cache fills, both transfer buffers, pixel
cache and every timed micro-operation kind. Runtime/direct comparisons include
RGB555/ARGB/brightness, timestamped PCM and raw state; Manager preview/import/export
checks format 10 and mismatching configurations.

`testdata/superfx` contains authored 8192 inputs and separately executed unmodified
bsnes instruction outputs. The reference commit is
`78474e5d39f58872dc21aca18cf9eb583b147fe7`; the adapter is
`tools/superfx-reference.cpp` and is lab-only. Each opcode/ALT combination has 8
register seeds, source/destination default 0, zero RAM/ROM, stubbed graphics and no
cycle comparison. These vectors do not cover every selector, graphics or full
outer-device behavior.

`superfx-source-audit` keeps full raw register/status comparison red: 112 ROMbusy
flag differences because the processor-only adapter excludes the reference outer
SuperFX R14-update hook. An additional explicitly scoped processor comparison
omits that one peripheral flag and has no mismatches in that narrower scope.
Neither view is presented as full conformance. The separate primary-source audit
also retains three disagreements. The functional
suite and expected-red inventory are separate gates. Fixture/manifest corruption
controls verify that mutation cannot silently become new expected data.

Optional original source-author ROMs are local-only. With no input checkout,
`superfx-published-roms` reports skipped/exit2. Original files from
[PeterLemon/SNES](https://github.com/PeterLemon/SNES/tree/master/CHIP/GSU/GSUTest),
commit `350b394e86ec5d62f600b5cbf64cdce3721bb6ef`, are not redistributed because no
explicit grant was found. The author's general README says realSNES/SD2SNES tests;
that is not a case-specific captured GSU revision/timing transcript. The runner
checks exact final-page PASS addresses from each source, retains any earlier FAIL,
requires positive instruction count, stopped GSU, stable fields and the terminal
BRA-self loop. ROM/source hashes and causal failure state accompany results.
Earlier reports with an incorrect all-historical-page terminal contract remain
preserved separately and are not relabeled as implementation failures or passes.

Final clean-commit Windows/Ubuntu reports and exact identities are listed in the
stage evidence manifest. Whole-core long performance and native hour acceptance
belong to the final integrated stage after SA1; historical shorter runs are not
claimed as final hour acceptance.

The pinned nocash original [Fullsnes](https://problemkaputt.de/fullsnes.htm) bytes
retrieved2026-10-24 have SHA256
`3853b65a7331bf12eea431ff494a2948a1c78ef22f7e819db4b139fd5a79f6c6`.
Its explicit STOP, effective-color, dither and masked-IRQ accounts inform the
default model; this author research is not a new hardware capture. Its uncertain
MC1/maybeGSU1 write-before-STOP hang is not modeled as a precise invented rule.
Running access to general registers/cache/CLSR/CFGR outside documented legal
setup sequences has deterministic behavior here, but is not hardware-certified.
Functional setup configures those before GO; active SFR/SCMR WAIT controls are
covered separately.

SCMR depth values0/1/3 represent documented2/4/8bpp. Reserved value2
currently follows a deterministic4bpp software policy and is tested as such,
not presented as a fourth documented color depth. VCR reads4 for both selected
revisions in the common model; it is not a measured revision identifier.

## Clean acceptance and evidence identity

[The evidence manifest](m10b-evidence-manifest.json) pins canonical Git source
bytes, exact reports and the separately executed reference. Implementation1d8faec
and exact fixture-byte47f7997 are followed by diagnostic5ff6e26. Windows129Core
and frozen normal regressions were accepted on47f7997; final Windows targeted
and all Ubuntu gates ran on clean5ff6e26. The two commits have identical product
Core source bytes. Different SourceRevisionId assembly metadata means their
binaries are not called identical.

Windows and actual Ubuntu each pass 34 SuperFX functional groups, 26 Runtime,
15 Manager, 27 compatibility-state,15 persistence and 129 Core groups. Both normal
frozen regressions retain 14 outputs, 18952 SPC resumes and 128 horizons. The
strengthened authored cartridge produces planar RAM0114/0115=40/40, exercises
actual DMA0 A-side reads70:0114/0115 and B-side writes2118/2119, then exposes
VRAM0100/0101=40/40. Each region renders exactly one white native pixel and a
stable complete subsequent field.602 whole-system2MC snapshots and1402 per-tick
processor/RAM continuations cover the documented pending phases and buffers.

Both OSes execute 31 unmodified original source-author ROMs: 30 reach their exact
stable final-page PASS/terminal-loop contracts; FMULT remains red at case 9.
Source=destinationR4, initialFFFF, multiplierR6=0000 produce a zero product;
the current unsupported-destination policy retainsFFFF, which the S-CPU reads
with flags8002 (internalIRQ set despite masked output). The original source
expects numeric0000. This is causal software evidence of a source contradiction,
not new silicon evidence. Three primary-source rows and 112 full-status software
adapter rows remain red in separate inventories. Optional originals explicitly
report skipped/exit2 when their local checkout is not supplied.

Private 10.0.13 CLR/JIT identity was observed through separate owned retained-handle
Windows and Ubuntu probes. Test children use the same explicit private hosts;
the probes do not claim per-test loaded-assembly tracing. Ubuntu frozen execution
uses the isolated Ubuntu Lab, but its original provenance `coreDllSha256` field
incorrectly hashes the standard Windows path. That report is preserved unchanged.
An explicit supplemental correction hashes the actual sibling Ubuntu dependency,
its deps/runtimeconfig and build-ledger match. This managed dependency identity is
**inferred from isolated binding**, not an observed loaded-managed-assembly trace.
No before-run managed-DLL hash inventory was captured. This limitation is recorded
rather than silently rewriting the original report.

Normal-cart regression/performance contracts do not establish sustained real-time
GSU3D or commercial-game performance. The authored GSU job stops after completion;
source-author ROMs are bounded functional workloads. Sustained GSU performance
remains unmeasured. [Final integration](final-integration.md) now records the
actual normal-cart long/native-hour results, package verification and exact
implementation-head remote CI. Independent strict timing release failures remain.
No NuGet vulnerability audit completion
is claimed when package metadata access emits NU1900 warnings.
