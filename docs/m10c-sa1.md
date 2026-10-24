# M10c: independently developed SA-1 digital model

The cartridge now executes a second real C# 65816, using the repository's own
restartable CPU implementation. There is no HLE game routine, external emulator
core, firmware download or game-specific title detection. This is a functioning,
scoped digital implementation; published hardware timing still disagrees in the
explicit inventory below. It is not general commercial compatibility certification.

## Selecting a board

An SA-1 ROM uses the MMC header at `$007FC0`, map mode `$23`, chipset `$34/$35`.
The ROM header does not identify all board memory details. Place a sidecar named
`<complete ROM filename>.hardware.json` beside the local ROM:

```json
{"RamBytes":131072,"Revision":0}
```

Supported BW-RAM capacities are 32768, 65536, 131072 and 262144 bytes, with up to
8 MiB ROM. `Revision:0` selects this digital model; it is not a measured chip
revision. This model returns S-CPU open bus at `$230E`, consistent with the pinned
[SpeedTest BF/BE-bank detector](https://github.com/VitorVilela7/SnesSpeedTest/blob/475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e/src/text.asm)
and `SA-1 VER:--` in the
[SHVC-1L8B-10 photograph](https://github.com/VitorVilela7/SnesSpeedTest/blob/475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e/img/hardware/v51-1L8B-10/v51-1.JPG).
This does not certify every physical variant or SA-1-side reads.
No additional firmware is required. Configuration is immutable in worker/Manager
cartridge clones and state loading; a running load does not reread the sidecar.
Normal cartridges retain their existing behavior and format 9 state layout.

## Execution and memory

`Sa1CartridgeHardware` owns the SA-1 CPU, 2 KiB I-RAM, MMC registers, DMA engines,
timer, bit reader, math unit and both processors' shared-memory arbitration.
BW-RAM remains cartridge-owned for battery persistence. Both processors retain
separate MDR/open-bus histories. The SA-1 board explicitly consumes unselected
cartridge addresses; those cannot fall through to the normal LoROM SRAM decoder.
The main console still decodes its WRAM, PPU, APU and internal I/O first.

The own 65816 retains its complete register/interrupt/continuation journal. A
pending SA-1 bus cycle and its remaining master clocks are separate state. Work
can stop at an arbitrary clock without finishing an instruction beyond the budget.
The common CPU's all-low terminal observation is not charged as an oscillator
cycle. SA-1-only ROM control-flow refill cycles leave generic 65816/S-CPU cycle
streams unchanged. The selected refill/parity model follows original hardware
research and the independent software timing account; it is not a complete
transistor-level 16-bit prefetch implementation.

MMC CX/DX/EX/FX projection, direct and windowed BW-RAM, 2/4bpp bitmap packing,
per-256-byte I-RAM permissions, BW protected prefix and independent writer enables
are implemented. SIWP/CIWP reset to zero: **zero disables writes, one enables**.
Stack writes before CIWP enable therefore do not silently succeed. This polarity
is in Nintendo Book II 1-4-18/19 and agrees with the real SA1-Root v1.8 startup fix.
SA-1 reset/hold, WAI, NMI/IRQ masks, pending flags, mailboxes and S-CPU IRQ/NMI
vector overrides use the actual CPU paths. SNV/SIV are SA-1-writable, not host
writable. Acknowledging one interrupt source does not erase another source.

## Timing and DMA

SA-1 internal/ROM accesses use the console master clock divided by two; BW-RAM
accesses use four master clocks. The S-CPU cartridge ROM/BW-RAM access takes eight
master clocks even when MEMSEL requests FastROM. A selected shared-memory portion
of each S-CPU/DMA cycle reserves four master clocks. The reservation is announced
before clock advancement, including actual DMA/HDMA A-bus reads and table/pointer
reads; its release is an explicit eligibility boundary. Consequently a large
budget and one-master-clock slices produce the same pending engine state.

This is a source-supported digital arbitration model, **not a fitted oscillator**.
The author-published SpeedTest describes four-master-clock ROM reservations,
observes contention and distinguishes observed results from theories. Its README
speculates that BW-RAM has a 16-bit bus, while the same author's oscilloscope/
pinout investigation of modified SHVC-1L8B-10 identifies an 8-bit SRAM bus. That
conflict is retained; the theory is not promoted into a hardware fact. Refresh,
phase overlap and prefetch effects still cause the reported numerical differences.

Normal DMA supports ROM→I-RAM, ROM→BW-RAM, BW-RAM→I-RAM and I-RAM→BW-RAM.
Read and write endpoints retain a byte latch and independent remaining phase.
The selected uncontended transfer rates are respectively two/four/four/four
master clocks per byte, as Book II 1-9-4 specifies. Source/destination addressing
and DMA controls are latched. Host DMA wins its selected memory grant; SA-1
DMA priority suspends the SA-1 CPU, while CPU priority allows transfer progress
on a different selected memory resource. Same-device/invalid-source transfers,
control-register races and analog arbitration remain outside certified scope.

Character conversion 1 has two timed tile buffers, ready/tile identities, packed
row latch, source-read phase and planar-write phase. Source addresses normalize
through the same physical BW-RAM mirror as host direct/window access. A completed
host tile frees its buffer for the next tile. No host read fills an entire tile
synchronously or advances the chip beyond the current host clock. The current
read/write timings are four/two master clocks per byte, and the standby IRQ is
raised after the first tile is ready. Book II 1-6-9 calls this IRQ 'standby' and
specifies request-driven conversion, but does not establish these exact internal
latencies. Those latencies, initial readiness and buffer overlap need hardware
measurement and are explicit model choices.

Character conversion 2 latches each BRF row and writes 2/4/8 planes with a
restartable read/write sequence. Sixteen rows alternate both eight-row I-RAM tile
buffers. Plane pairs occupy offsets 0/1, 16/17, 32/33, 48/49; the fixed tile base is
separate from normal DMA's advancing source. Per-plane read/write timing is a
selected digital sequence, not physically measured conversion throughput.

The free-running timer has uniform four-master-clock dots, 341-dot horizontal
wrap and 262/312-line regional vertical wrap; linear mode wraps 512 × 512. Enable
bits gate compare interrupts, not counter advancement. Reading H-low latches
both counters. Reset/compare and tied event state are deterministic. PPU short/
long lines and interlace are deliberately not copied into the SA-1 timer; actual
physical phase/reset/interlace behavior remains unverified.

Signed 16×signed 16 multiply and signed 16 / unsigned 16 division expose results after
five SA-1 clocks; cumulative 40-bit multiply/add after six. Modes 2 and 3 both select
cumulative sum per Nintendo's ACM=1, M/D=don't-care table, despite nocash listing
mode 3 reserved. Negative division uses floor quotient and unsigned remainder;
divide-by-zero returns zero/zero per the manufacturer. Overflow detection and
explicit sum-clear are tested, while undocumented overflow-clear races remain
unmeasured. The bit reader supports lengths 1..16, fixed/manual advancement,
auto-increment after high-port reads, and crossing byte/bank boundaries. Fine
undocumented bit-reader behavior has no new physical capture. Retained/destroyed
operand contents when software omits the required parameter reloads are not
physically reconstructed; programs must reload the documented parameters.

## State and integration

Enhanced cartridges use the existing format 10 generic version 1 hardware envelope.
SA-1 payload version 1 contains configuration, full CPU/interrupt journal, pending
bus phase, I-RAM, MMIO, separate SA-1 MDR, host grant, both DMA/conversion pipelines,
math result/deadline, bit position and timer/latches. Restore validates domains and
cross-field ready/queue/IRQ/control invariants before copying I-RAM. The outer
serializer restores into an independent cartridge, so even a late malformed chip
payload cannot change distinct nonzero live BW-RAM. No normal format 9 bytes,
R3 obsolete-pointer-journal guard, DSP1 or SuperFX state contract is repinned.

Only an inserted SA-1 board adds its shared-clock boundary/advance callbacks.
Normal boards retain the existing PPU/APU path. Bus reset synchronizes the old
chip epoch, resets the shared clock before untimed reset-vector reads, then starts
the new epoch. Warm I-RAM retention is a reproducible selected model; unknown
physical power-up data is not invented as measured behavior.

## Evidence and reproduction

The public authored inputs are four 32 KiB ROMs: NTSC/PAL × CC1/CC2. Each runs both
real processors, multiplies 7 × 9, produces planar bytes 40/40, transfers them through
actual S-CPU DMA A/B to PPU VRAM $0100/$0101, enters actual S-CPU IRQ/NMI override
handlers, and displays exactly one native white pixel. These are authored diagnostics, not commercial-game acceptance.

Clean implementation acceptance passes 32 functional groups on Windows and Ubuntu. It includes 2400 complete
machine snapshots at 2 MC cuts, 104 normal-DMA snapshots at 1 MC cuts, 896 CC2 snapshots
across every plane and both tile buffers, CC1 read/write/ready phases, grant
partition invariance, actual WAI/IRQ/NMI handlers, independent writer/MDR sentinels,
math/bitmap/MMC/bit-reader literals,29 invalid state mutations and late malformed
state rejection with distinct filled live/saved BW-RAM. Reported phase inventories
must witness actual pending CPU read/write/idle, CC2 read/write, arithmetic and
contention; counts alone do not pass. Five isolated fixture/manifest mutations
must reject. Clean Windows/Ubuntu acceptance and exact binary/report identities
are recorded in [the M10c evidence manifest](m10c-evidence-manifest.json).

```text
python tools/verify-sa1-testdata.py --negative-controls
dotnet run --project SNESEmulator.Lab -c Release -- sa1-proof artifacts/sa1
# Optional local-only original author source checkout:
dotnet run --project SNESEmulator.Lab -c Release -- sa1-published-rom artifacts/sa1-author <checkout>
```

The optional unmodified `VitorVilela7/SnesSpeedTest` commit
`475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e`, ROM SHA256
`21c24e4b0ec2cb32eeba25c5311aa39bd3228fc1327d2719c0b88671884c2541`,
runs all four pages: 18/20/20/9 real test entries, a page-end footer/counter event,
and no sticky author error-handler writes. Its source increments the list offset
before reading the terminal FF sentinel, so the execution contract subtracts that
sentinel entry. This benchmark has a repeating page loop, not a stable PASS screen.
Raw numerical rows from every page and previous failing harness/model reports are
retained. With no original local input the command reports skipped and exits 2;
the ROM/image/source is not redistributed because no explicit license was found.

The original author's QcRetro SHVC-1L8B-10 first-page photograph has SHA256
`38590af0850f408f79cb669b835725b8ffedf282a16dc448aea3ee360c52edf4`.
Comparing its first twelve measured rates with tolerance 0.02 MHz currently yields
**five matching observations and seven differences** (rows 2, 3, 7, 8, 9, 10, 11).
For example shared ROM is 5.18978 versus 5.04284 MHz, I-RAM/I-RAM 3.80061 versus
3.72218 MHz and BW/BW 2.76842 versus 2.68999 MHz. Tolerance is scoped to this single
observation; it does not certify physical correctness. No calibration changes
clock frequency to conceal those differences. Source/theory, functional execution
and physical observations remain separate evidence.

Primary references: [Nintendo Book II](https://floating.muncher.se/bot/manual/book2_text.pdf),
[Vitor's hardware investigation](https://github.com/VitorVilela7/SNES-SA-1-doc),
[original SpeedTest](https://github.com/VitorVilela7/SnesSpeedTest/tree/475a1b87a35bfcbe1b96ddf5042c73bbd5ed297e),
[SA1-Root releases](https://github.com/VitorVilela7/SA1-Root/releases), and
[nocash original research](https://problemkaputt.de/fullsnes.htm).
A pinned bsnes SA-1 source was consulted independently for comparison, never linked
into the application; agreement between software models is not hardware evidence.

Other chips/peripherals (DSP2/3/4, CX4, S-DD1, SPC7110/RTC, S-RTC, OBC1, ST010/11/18,
mouse, multitap, Super Scope) remain separately tracked. Final integrated long
performance/native-hour/package/remote-CI gates remain the next root stage.

## Clean implementation acceptance

Both platforms built and tested clean implementation commit
`0944634ab9f94c57e2c6f0db62723168f44c693d` with SDK 10.0.402/runtime 10.0.13.
The subsequent comments/documentation commit changes no executable tokens or fixture.
Its comment-token proof and separate source identities are retained; the accepted
DLLs remain attributed to the tested commit above, not to a later rebuild.

| Gate | Windows x64 | Actual Ubuntu x64 |
|---|---:|---:|
| SA-1 functional groups | 32 pass | 32 pass |
| SuperFX / DSP1 functional groups | 34 / 30 pass | 34 / 30 pass |
| Worker / Manager groups | 27 / 16 pass | 27 / 16 pass |
| Compatibility-state / persistence groups | 27 / 15 pass | 27 / 15 pass |
| Existing complete Core groups | 129 pass | 129 pass |
| Frozen normal console outputs | 14 exact | 14 exact |
| Frozen SPC resumes / horizons | 18,952 / 128 exact | 18,952 / 128 exact |
| Original SpeedTest page execution | 4 complete, no sticky author error | 4 complete, no sticky author error |
| First 12 photographed timing rows | 5 within scope, 7 differences | 5 within scope, 7 differences |

The four authored binary inputs match both checkout and Git-blob bytes. All five
isolated integrity mutations reject. The Windows solution build succeeded with
three NU1900 warnings because NuGet vulnerability metadata was unreachable; an
online vulnerability audit is not claimed. The Ubuntu build uses an isolated
artifacts path and the unchanged root testdata through its recorded symlink.

Each platform's 14 performance-producer reports independently hashes
`typeof(SnesSystem).Assembly.Location`. All 14 match its actual Lab-sibling Core
DLL, also matching the Core-project copy, driver provenance and SA-1 producer.
These are dependency identities, not assumed `DOTNET_ROOT` identities. Separate
owned-process probes observed the actual private CLR and JIT module paths/hashes;
those probes ended through their retained handles. The probe scope does not imply
that every individual test child was enumerated.

Actual accepted Core DLL identities:

- Windows: `c4839db06b61ea9091f84ab4b30014959254a42fa9a705703b1c42fb19fb76af`
- Ubuntu: `ad562e8da4ada58937b9e2205fa4bf2167e3cda3c67a0dd26409f4d8de66bf7e`

The Linux frozen driver redirects both the Lab executable and its provenance
Core path before execution. Original fixture expectations and raw/ZIP state
oracles remain unchanged. These short regression workloads do not replace the
final 37,000/31,000-field performance windows or either strict native one-hour run.
