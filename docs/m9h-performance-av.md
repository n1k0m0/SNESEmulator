# M9h: performance and software A/V

Work in progress. No overall acceptance for M9 or the release.

The starting point is `2c6b3567095636594fcf5e5f92686631465f775c` with
SDK 10.0.402/Runtime 10.0.13 on NILSPC1. New evidence is under
`artifacts/m9h`; existing Root and milestone evidence freezes are unchanged.

Independent M9g acceptance exceeded the unchanged cumulative 40 ms limit with
49.9420215 ms. Two isolated followup attempts missed the 30 s readiness budget.
A passing 13-group run in the original order does not explain these historical
failures. Both findings explicitly remain open; details are in
[M9g followup](m9g-followup.md).

## Targeted work

1. Capture fresh bounded observations of the loaded slow-scene state, the
   allocation/CPU/GC profile and actual driver A/V limits.
2. Fix selection/commit errors demonstrated deterministically. The cold phase
   remains part of cumulative metrics; discarded frames and the measurement
   phase are reported separately. A slow callback must still visibly fail.
3. Check each core candidate in isolation against complete console control runs
   and exact video/PCM/state outputs. Earlier unsuccessful candidates are not
   adopted. Allocation improvements alone are insufficient.
4. Run all unchanged accuracy regressions for each adopted core change. Both
   NTSC/PAL runs lasting >10 emulated minutes are performed and reported honestly
   on the resulting product version even if no candidate is adopted. The
   acceptance criteria remain unchanged: every 600-field window >=1x, managed
   memory <256 MiB and 0 lost core outputs.
5. Run an actual hours-long app/network/Manager soak with normal pacing and
   bounded hashes, checkpoints and error buffers, without hours of full traces.
   Report emulated duration and real wall-clock time separately.

The earlier M9a result remains failing: 38/62 NTSC windows below 1x, PAL
unmeasured. The 18 known full audio comparison rows, warm PPU retention and other
hardware limits remain separate accuracy work. No physical DAC/display latency
measurement is claimed here.

## Concrete A/V correction so far

The earlier policy allowed video distances of up to 100 ms and, after the
callback, still used the driver sample from before selection and commit. The
existing injected case therefore delivered a frame that was 50 ms old at
commit while reporting 0 ms.

The new commit check rereads the driver after acquiring the commit lock and
discards frames at distances of 40 ms or more. The cumulative metric includes
both this fresh sample and the actual post-callback sample. It is neither
capped nor reset when measurement begins. An injected 60 ms callback remains
visible as a 60 ms violation. Underflow between selection and commit discards
the frame when audio is required. Cold-phase and later frames still count
toward the same cumulative metric. This is a policy correction supported by a
causal test, not retrospective proof of the cause of the Root failure.

Initial targeted check: 16 portable network groups and the 13 actual Windows
groups passed. Cold phase 2.1889347 s, measurement phase 5.0132814 s, 294 video/
301 audio/294 driver comparisons, cumulative 16.6702374 ms, 25 drops including
the cold phase, Fault null. The subsequent reporting extension separates cold-
phase and measurement-phase drops; readiness and 40 ms limits are unchanged.

## Bounded core experiments: none adopted

All following 2400-field experiments load the same existing state at field
6600 and finish at 9000. The core assembly actually loaded is hashed in the
process. Native video/PCM streaming hashes and complete compressed end states
match exactly between control and candidates; all retain 0 presentation losses.
This identity is a regression against the existing core, not an independent
hardware measurement.

| New isolated candidate | Candidate A | Original control B | Candidate C | Decision |
|---|---:|---:|---:|---|
| Public cycle value type |0.9570x|1.0271x|1.0833x|No reliable speedup; not adopted|
| Create the cycle record only after timing admission |1.4270x|1.1720x|0.9649x|Early improvement did not repeat; not adopted|
| Privately packed suspension point |1.1376x|1.0781x|1.0424x|No stable improvement; not adopted|
| Direct journal resume for simple branch/MOV forms |0.9434x|0.8980x|0.9292x|Performance windows still fail; not adopted|
| Privately packed entry/suspension point plus late cycle record |0.9586x|0.9428x|0.9663x|~60% fewer allocations, only ~1.7–2.5% overall improvement within observed variation; not adopted|

For journal resume, the 18,952 frozen suspension cases passed. The final
candidate separately passed the same 18,952 legacy cases and 18,952 cases
through its new fast timing admission, plus the explicit fast/legacy/observer/
post-latch composition. These test sources and binary objects remain exclusively
under the experiment artifacts; the product core and immutable original
harnesses were not changed. Earlier M9a candidates remain rejected.

An early quoting error in a preparation helper script accidentally launched
original copies under candidate names. The three runs are retained and explicitly
marked as invalid candidate evidence in
`artifacts/m9h/experiment01-disposition.json`. No result from them determines
candidate selection.

## Fresh profile and next technical decision

The additional 3600-field run from 6600→10200 on the unchanged original core
produced six passing windows at 1.0795/1.0154/1.0384/1.0115/1.0097/1.0079x.
It explicitly does not reproduce the earlier failing windows. Actual execution-
thread CPU time in these windows is 96–98% of wall-clock time; separate failing
control windows are close to 99–100%. This measures these windows and does not
prove a general host or scheduling cause.

The bounded EventPipe recording mainly indicates SPC cycle/capture residency on
the main thread (around 42%/23% of attributed stack time). These are not hardware
CPU percentages. GC suspension of 1.791 s in 59.806 s and early Tier1 loads are
reported separately; correlation alone does not explain the shortfall. An
initial trace profile contained no sample/allocation events and was retained;
the subsequent explicit provider recordings supply these data.

No further selection of favorable runs follows the bounded series of variants.
The next structural core change would need to modify the consuming SPC
replay/capture path while preserving all public suspension points, journals,
MMIO/observer/guard ordering and State9 exactly, and demonstrate a stable
whole-console improvement. The allocation and simple resume changes tried so
far do not provide that evidence.

## Actual sustained runs

The NTSC run of 37000 measured fields completed: 615.652749 emulated seconds,
678.052 core wall-clock seconds, 0.90797x. 54 of 61 complete 600-field windows
fail; including the partial window, 55 of 62 fail.
The minimum is 0.858353x at field 17410. The whole process, including hashing,
checkpoints and other work, took 687.5728643 real seconds. Maximum managed memory
52,607,072 B, 0 lost DSP outputs. Performance acceptance still fails.

The PAL run of 31000 fields was subsequently completed independently:
619.913473 emulated seconds, 586.7127 core wall-clock seconds, 1.05659x overall.
Nevertheless, 17 of 51 complete 600-field windows fail, or 18 of 52 including
the partial window. The minimum is 0.917209x at field 25810. Real process
wall-clock time 594.3624915 s, maximum managed memory 49,230,888 B, 0 lost DSP
outputs. Both regions miss the unchanged performance acceptance criteria; a
passing overall average does not replace checking each window.

## Prepared native soak test

The explicit `--ui-soak` mode uses the actual native application with normal
pacing, synthetic application inputs, localhost TLS, the actual WinMM provider
and its own WPF Manager. Manager status requests are observed; small shared
SQLite scans and validated State9 checkpoints run within limits alongside them.
At most two rotating state paths with their backups, 64 field-hash comparison
entries, 4096 software A/V diagnostic rows and 16 error messages are retained.
Progress is written every 10 s; there is no hours-long raw-frame/CPU trace.
Audio gain 0 retains PCM generation, the provider and device operation.

The full test duration runs even after a 30 s readiness failure. The cold phase
remains in the cumulative 40 ms metric. After readiness, the existing 100 ms
catchup/mute boundary, lack of audio/driver progress and missing Manager requests
remain visible. Actual wall-clock time and hardware time are reported
separately. Render polling hashes only observed source fields; no complete
source-field coverage or independent hardware accuracy is claimed. Mapped
remote ARGB fields are compared against their actual source hashes; unknown
mappings are counted separately.

The plan is 3600 actual seconds each for NTSC and PAL, totaling at least two
real hours. The PAL application case uses an explicitly documented region-byte
derivative of the pinned freely distributable tech demo; the original is
unchanged. This is a PAL workload case, not an original-hardware measurement.
Both revised 35 s short probes after completion of the two sustained core runs
passed, including an actual validated checkpoint per region, a shared SQLite
scan and real WPF status requests. NTSC: 36.232 s process duration including
cleanup, 34.427 hardware seconds, ready at 7.172 s, cumulative 33.297 ms; PAL:
35.201 s, 34.415 hardware seconds, ready at 6.856 s, cumulative 20.005 ms.
1809/1518 mapped ARGB fields match exactly, with 0 owner drops and 0 observation
gaps; observed managed-memory maxima were 44.0/25.1 MB. This explicitly does
not yet establish hours-long operation.

The first development probe included normal queue discards during disconnect
in the drop counter labeled as the measurement phase. Its originals are
retained. The revised probe freezes A/V counters before close and separates
shutdown discards: both measurement phases 0, shutdown 21/17. It also flushes
the final partial diagnostic segment, saves the maximum cold/steady
observations and at most the last 128 rows. No metric is reset or capped. The
delivered NTSC short-phase PCM is silent; PAL contains a few nonzero stereo
samples. Amplitudes are counted honestly and are not presented as audio that
was heard or physically verified.

The soak test starts only on the verified clean implementation version.

## Failures in the earlier runtime smoke test

The full run on c470d7c passed the core, CPU, bus and SPC corpora but aborted
at the first native runtime smoke test with `mapping menu path`. This overall
run remains failed; it did not reach the subsequent Windows groups. The smoke
test sent F11 and immediately followed it with more synthetic keys while
HandleKey checked actual desktop focus. Focus can change during a native
fullscreen transition; focus at the time was not recorded, so this is not a
proven historical cause. The test now explicitly pins its synthetic focus and
neutral inputs, as InteractionSmoke already does. Actual focus-loss logic is
still tested separately there; product logic is unchanged. Failure reports
will include menu, selection and runtime. Only the missing native stages are
rerun for this purpose, without rerunning unchanged full CPU corpora.

The subsequent persistence probe on f982ae9 exited its native window after
around 36 s, before the expected completion. Its completion stage at the time
was not logged; neither an external close action nor a product cause is proven.
This failed run is also retained. The persistence probe now also uses synthetic
neutral focus and records stage, elapsed time, menu and collected metrics on
assertion failures. This changes neither persistence policy nor time budgets.
