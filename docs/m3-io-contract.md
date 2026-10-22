# M3 5A22 I/O and DMA contract

M3 is independently accepted on `56fb6d49571875bb90f1b6b8efed49af5af2b6e7`,
full gate exit0 (`artifacts/root-m3-validation.log`). These tests are not acquired original-console traces.
M0–M2 were independently accepted by the parent full gate, including the corrected
read phases (`artifacts/root-m2-phase-validation.log`). No external core or GPL
implementation is embedded or transplanted into this own C# emulator.

## Bus and cartridge scope

The CPU map decodes WRAM mirrors, B-bus and internal 5A22 I/O, normal discrete
LoROM, HiROM, ExLoROM and ExHiROM. These are address wiring profiles, not support
for coprocessors, interleaved dumps, BS-X, Sufami Turbo or ambiguous board variants.
Other chipset IDs are rejected. Header metadata exposes selected offset,
map/chip/region bytes, declared size, checksum pair, copier header and candidate
scores. A header is metadata, not board identification. `CartridgeOptions`
permits explicit mapper/region selection; an explicitly supplied system hardware
profile takes precedence. Auto versus forced profile choice and cartridge
options are saved. Same ROM hash with a different mapper is rejected on load.

ROM mirroring recursively removes absent chip address lines: 3 MiB maps 3..4 MiB
onto its final 1 MiB rather than modulo onto the first chip. SRAM observes bank
windows and chip mirrors; WRAM wins in 7E/7F. MEMSEL affects only the M2-tested
high-bank FastROM windows.

CPU writes drive external MDR. Reads sample at the M2 latch and update external
MDR after trailing hold, except low-bank 4000–43FF, which never drive it. Internal
write-only/unmapped ports return MDR; RDNMI, TIMEUP, HVBJOY and JOYSER preserve
individual unspecified bits. DMA A/B reads drive MDR at their read phase,
including zero for forbidden reads. DMA A rejects only low-bank 2100–21FF,
4000–41FF, 4200–421F and 4300–437F. This differs from the CPU MDR exception.
2180 conflicts with WRAM selected on A: B reads become zero and B writes are
suppressed without incrementing the WRAM port. 2181–2183 remain address ports.

Complete PPU1/PPU2 MDR, VRAM/CGRAM/OAM read buffers, active-display restrictions
and full PPU side effects remain M5. The current PPU's generic read-zero fallback
is not claimed as accurate PPU open bus.

## Bus ownership and phases

A CPU MDMAEN write requests work. The next CPU bus cycle completes, then DMA
aligns by 1..8 clocks. Global overhead is eight clocks, channel overhead eight
clocks, and priority is 0..7. A byte samples its source after four active clocks
and writes after four trailing clocks. Both directions, all eight patterns,
fixed/increment/decrement, 16-bit bank wrapping and zero count as65536 are
explicit. Ending synchronization takes 1..CPU-access-clocks clocks. No future
access or copy happens at request time.

HDMA setup requests at V0 H12 plus divider phase; line requests at H1104 through
the visible lines. HDMA is considered at CPU and DMA global/channel/byte
boundaries. It suspends other DMA and cancels its shared channel. All channels
transfer first, then all active channels decrement/reload. A nonzero line counter
still incurs a dummy table read. Direct/indirect wrap, repeat, terminal descriptor
and the last completed indirect channel's low-pointer-only fetch have saved
phases. The field setup transfer seed follows the reference, whose source marks
it as needing hardware verification. Mid-frame enable is not independently
measured. Sleeping-CPU tests use a bus-idle convenience state, where requests can
acquire that idle bus; this is not measured physical STP behavior.

Refresh pauses active CPU/DMA clocks while asynchronous I/O continues and
produces five ALU edges eight clocks apart. Pending HDMA preempts only at a
completed DMA boundary. Completed HDMA channels produce no later line overhead.
Equal timestamp events precede CPU/DMA effects in priority/sequence order. This
is a source-informed analytical tie contract, not a new measured trace.

`DmaController.Observed` runs after phase/address/count commit. A snapshot there
does not repeat the effect on resume. Logical bus observers describe synchronous
calls; CPU/DMA phase observers are the snapshot boundaries. Headless bus JSONL
distinguishes CPU, DMA and HDMA phases with actual clocks.

## ALU and interrupts

Multiply takes eight CPU ALU edges; divide sixteen. Product, quotient, shift and
counters are saved. Reads run ALU AFTER trailing hold; writes BEFORE time
advance, once across budgets. Internal cycles, including E-RMW invalid-write pin rows, run it at completion;
the zero-clock terminal marker has no ALU edge. Busy operand
writes retain the operation but initialize result registers as documented.
Fixed arithmetic worksheets check intermediates, separately from final integer
results. DMA alone does not advance ALU; refresh does.

NMI begins with delayed VBlank and a four-clock flag hold before normal CPU
transition. RDNMI cannot clear during hold. Enabling while the flag is set creates
an edge. H/HV include the reference's one-dot offset and ten-clock comparator
delay; delayed events may cross lines. V-only is a level comparator at H10 or
the next four-clock poll after register changes. Mid-line enable and read-clear
without repeated rising edges are distinct. TIMEUP has a four-clock hold.
Control writes and DMA completion apply IRQ lock to generic CPU polling. Actual
CPU wires use `SetInterruptLines`; an external IRQ input can also assert it.
Generic old-I/RTI/WAI/arbitration tests remain separate.

SLHV/WRIO falling counter latches, low/high H/V reads, counter-latched status and
status toggle reset are implemented. Full PPU MDR sharing/errata remain M5/M7.
Timer writes in the comparison reference are themselves marked unverified.
Historical counter taps cross the selected profile's real short/long lines and
interlaced field lengths. The IRQ inhibit tests counter6 H0/V0, not a fixed
current-H condition; normal/short1360/long1368 NTSC/PAL field transitions have
explicit last-dot suppression and adjacent allowed-dot cases.
23-bit IRQ tokens remain live at most one line plus ten clocks; hardware writes
cannot reuse a live token across rollover.

## Controllers and reset

Standard pads use data-1, data-2 stays low. Explicit auxiliary electrical fixture
lines exercise JOY3/4; this does not implement a multitap/four-pad adapter. High
strobe reads live B; falling strobe samples. Twelve buttons precede four zero
signature bits, then data-1 returns one. JOYSER1 has its fixed bits. Auto-joy
begins on the global256-clock edge in VBlank H130..384, separately from NMI.
Latch release follows128 clocks. Sixteen samples and result shifts alternate
at128-clock intervals, finishing counter33 after4224 clocks. Busy, partial
results, CPU/auto strobe and all four results are saved. Input changes during
high are sampled on falling. Disabling terminates polling per reference.
Concurrent manual reads share the serial model; the reference itself marks that
electrical interaction unmeasured. The scrolling diagnostic now waits for BOTH
busy assertion and completion; busy=0 immediately on NMI meant previous-field
data and was an invalid diagnostic assumption.

PowerOn defines zero WRAM as a reproducible test seed, not hardware randomness.
Warm Reset retains CPU WRAM, SRAM and pad input. WRIO FF, timer1FF and DMA FF
include deterministic undefined-register seeds, not measured guarantees.
PPU/APU convenience reset still initializes its memories; full physical reset
pins remain future work. Epoch stays H0/V0/field0 after convenience reset.
Savestate4 rejects1–3 rather than inventing missing DMA/ALU/JOY state.

## Test and evidence scope

IoDmaTests adds21 groups to45 existing portable groups. It executes80 original
CPU ROMs: eight modes, two directions, five start classes. Programs configure420B
and publish WRAM signatures after DMA. Forward cases use WRAM ports including
address-register target patterns; reverse cases read idle APU latches into WRAM.
The unfinished renderer and invented APU execution cannot satisfy the oracle.
HBlank/VBlank are polled; IRQ cases execute/acknowledge a timer vector. Refresh
asserts actual DMA spanning line1 refresh rather than merely naming a case.
These are original Apache-2.0 fixtures, not hardware-certified external ROMs.

Additional tests cover masks, forbidden ranges, priority,65536 bytes, every HDMA
mode, two-pass order, indirect terminal fetch, cancellation, ALU worksheets,
IRQ holds/tokens/line spill, counters and JOY. Every-clock bus-idle DMA saves (1260), running-CPU DMA saves (181), and
HDMA setup/line saves (301) compare to an independently uninterrupted final state.
Direct observer saves cover side-effectful2180 and table/pointer phases. Chunking
also compares complete state/timed traces. Three negative mutations use the same
positive protocol verifier. The existing11 laboratory mutations stay separate.

## Independently inspected references

- [Pinned bsnes memory](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/memory.cpp): phase/MDR/ALU placement.
- [Pinned DMA](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/dma.cpp): independently analysed restrictions/order.
- [Timing](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/timing.cpp), [IRQ](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/irq.cpp), [I/O](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/io.cpp), [initialization](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/cpu/cpu.cpp), [gamepad](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/sfc/controller/gamepad/gamepad.cpp): comparison implementation, not console evidence. No transplant.
- [Anomie's registers](https://wiki.superfamicom.org/registers) and [DMA/HDMA research](https://wiki.superfamicom.org/dma-and-hdma): published independent research; conjectures remain conjectures.

No original-console capture, licensed external M3 hardware ROM execution or
commercial-game result was acquired. SPC700/DSP/native sound remain absent;
whole-line graphics and remaining PPU accuracy are later milestones.

The pinned [modify-instruction reference](https://github.com/bsnes-emu/bsnes/blob/78474e5d39f58872dc21aca18cf9eb583b147fe7/bsnes/processor/wdc65816/instructions-modify.cpp)
identifies the RMW middle cycle as idle. An additional200 every-clock snapshots
check this phase and colliding refresh against uninterrupted final states.
