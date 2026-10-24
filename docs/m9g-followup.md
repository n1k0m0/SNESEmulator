# M9g followup: labels and outstanding software A/V acceptance

Independent Root acceptance on `8daf478` **failed**: 285 portable groups,
31 of 32 Windows groups and 151 interaction stages passed; the software A/V
group exceeded 40 ms. A later passing individual run does not resolve this
finding. M9 and the release remain incomplete.

## Labels

`4a35520` only sets `UseMnemonic=false` on the `BindingEditor` help text.
WinForms therefore displays the literal `&` as the chord separator. The existing
151-stage probe using real windows passed; the implementation agent and Root
checked the corrected image
`artifacts/m9g-followup label interaction/manual-binding-editor.png`.
No further label tests were requested or performed.

## Separate observations

| Run | Result and actual scope |
|---|---|
| Full Root gate on 8daf478 | Failed: cumulative software A/V maximum 49.9420215 ms; cold phase 2.4943818 s, then 5.0049301 s, 294 video/301 audio/294 driver comparisons, 22 cumulative drops, Fault null. Whether the maximum occurred during the cold or steady phase is unknown. |
| Separate isolated observation run 01 | Missed the unchanged 30 s readiness budget, with no valid driver comparison. The early implementation did not export detailed rows before this abort; the raw log/report are retained. |
| Separate isolated observation run 02 | Missed the same readiness budget; 1757 video selections recorded, with no valid driver sample. Did not reproduce Root's 49.942 ms finding. |
| Separate instrumented 13-group run on a6c6c91 | 13/13 passed. Cold phase 2.833526 s, then 5.0089651 s, 288 video/301 audio/288 driver comparisons, 24 cumulative drops, Fault null. 414 video selections recorded, including 321 with a valid selection sample. |

Only the final new run allows the maximum to be assigned to a phase: its
cumulative maximum of 16.6594747 ms occurred **before** measurement began, at
Source 1.996711619 s and Playhead 2.013371094 s. During measurement, the maxima
were 7.9438285 ms at selection, 7.8813285 ms before commit and 7.7782493 ms after
the callback. The longest observed selection→commit interval was 10 µs, and the
longest callback was 1.0234 ms. These data do not reconstruct the original Root run.

## Observation tooling and limits

`52fbbcc`/`a6c6c91` add only opt-in diagnostics and their test.
`SNESEMULATOR_AV_OBSERVE=1` enables recording in the actual network test, limited
to 4096 video rows: source, arrival, due time, generation, selection/commit/
post-callback times, and the corresponding driver playheads and result.
Export also runs after readiness failures. The captured playout owner is
retained for export after a successful disconnect.

A new portable group uses injected time to show that the existing cumulative
metric continues using the sample captured at selection after the callback.
The 15 portable network groups passed **on Windows** on clean 52fbbcc; the
subsequent a6c6c91 change affects only Windows test export. No new Linux probe
was run; Linux 59 on 7effa47 remains an earlier targeted portability check.

The selection and measurement policies are unchanged, as is the cumulative
40 ms gate: audio separately checks 100 ms wall-clock lateness (`error > .1`);
video checks a 100 ms source/playhead distance (`Abs(playhead-source) > .1`).
The permitted video range between 40 and 100 ms is a concrete item to investigate
in the next A/V phase, not retrospective proof of the cause of the Root failure.
The cold phase was not removed from the metric. There were no priority,
affinity, power, core or DSP changes, no unrelated processes were stopped, and
the product was not run again after the final 13-group run.

## Handoff to the performance/A/V phase

Root's 49.942 ms result and both cold-start readiness failures remain open.
Followup work should use these bounded observations to reproduce the
measurement/commit ordering and the permitted age of selected video, retain
both cold and measurement phases, and assess changes only after obtaining
concrete evidence. Unsupported claims about host causes or blind reruns until
a gate passes do not replace this work. Sustained performance with 38/62 failing
NTSC windows, PAL, the hours-long soak, 18 audio comparison rows, warm PPU
retention, the base console audit and M10 remain open.

The Root originals under `artifacts/root-m9g-*` were only read and are not part
of the followup collection. The existing M9g evidence freeze is unchanged. The
small followup freeze under `artifacts/m9g-followup-evidence` contains only new
followup evidence, immutable commit sources, an incremental source bundle and
required new binary objects; earlier objects are referenced by hash. All
selected objects and on-disk originals were fully reread after process exit.
Exact hashes and limits are in [the baseline](baselines/m9g.json).
