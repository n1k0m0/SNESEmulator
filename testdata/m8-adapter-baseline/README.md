# Historical M8 adapter baseline

These snapshots preserve the exact historical adapter bytes pinned by
`testdata/m8-integrity.json`. Their provenance and unchanged hashes are recorded
in `provenance.json`; the snapshots were extracted from the indicated Git commit
and verified against the existing manifest before being written.

The active `SNESEmulator.Lab/Program.cs` and `tools/test-lab.py` continue to evolve.
The R2 additions only add SPC continuation commands and tests; the existing M8
observation and comparison branches remain unchanged. The original verifier is
also snapshotted because the manifest pins its own source bytes; adding this
explicit path mapping necessarily changes that verifier source. The active verifier maps
exactly the five historical paths listed in its mapping to these snapshots. All other paths
still resolve to their original files. Neither the integrity manifest nor any
reference output is repinned. Checking these snapshots requires no Git history,
so a shallow CI checkout has the same offline verification coverage.

These sources document software evidence generation. They are not independent
hardware measurements and must not be used to regenerate expected results from
the emulator under test.

R3 also preserves the unchanged historical `m8-console-suite.py` from `c999c7f`.
The active script now prints initial and complete overlapping audio differences
separately; extraction, comparisons, reviewed differences and all reference bytes
remain unchanged. The fourth mapping preserves that original evidence identity.

R5 adds the unchanged historical `m5-ppu-manifest.json` from `14c150b`.
The active M5 manifest now pins the additional PPU regression source coverage;
all earlier test lines and expectations remain unchanged. The historical M8
integrity manifest continues to verify its original manifest snapshot byte for
byte, while the active M5 verifier checks the current test source. No M8 input,
reference output or expected hash is repinned.
