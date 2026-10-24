# Historical M9 adapter baseline

These exact source bytes are pinned by the unchanged M9 performance manifest.
`provenance.json` identifies their verified original Git blobs. The active
suspension runner now reports per-instance hot/replay completion counts; its
existing frozen reference comparisons are unchanged. The active performance
runner delegates immutable-input verification to `tools/verify-m9-testdata.py`,
which maps exactly these two historical source paths to these snapshots. All
other manifest paths are checked directly. A shallow checkout requires no old
Git history. No manifest hash, frozen suspension vector, digital output, or raw
state oracle is repinned. These are own-core regressions, not hardware evidence.
