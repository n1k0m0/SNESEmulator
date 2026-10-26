# Source license headers and explanatory comments

Maintained C#, C++, GLSL, Python, PowerShell, shell, CMD and installer source files
carry Apache-2.0 SPDX headers and retain Nils Kopal's copyright attribution.
Existing full C64 headers and all third-party/table provenance remain intact.
File introductions describe responsibilities; comments at important interfaces
explain timing, ownership, continuation, file recovery and update authorization.
Repository comments are written in English. Comments should explain a constraint
or decision rather than restate an assignment or promise unverified accuracy.

Generated SPC700 headers come from `tools/generate-spc-hotpath.py`; regenerate
the generated file and use `--check` to verify exact agreement. Designer-generated
SharpPixels files retain both their generator markers and license notices.

## Historical source pins

Several old fixture manifests also pin original source-file bytes. Their hashes
remain unchanged. The exact 29 pre-notice sources are retained under
`testdata/source-notice-baseline/`, with lengths and hashes, following the existing
M8/M9 historical-adapter approach. These snapshots intentionally retain their
original comments and line endings. They are covered by the root LICENSE/NOTICE
and the adjacent README, and are excluded from maintained-source header coverage.

The six affected fixture verifiers resolve only this explicit historical source
inventory. Existing historical M8/M9 mappings take precedence. Actual ROM,
vector, state, video and audio fixtures still use their original paths and
unchanged expected hashes. Current compilation and execution always use the
active commented sources; current-source provenance hashes are not redirected.

## Checks

Run `python tools/verify-source-notices.py --negative-controls` to check maintained
header coverage, exact historical snapshots, and rejection of missing attribution,
wrong license, byte mutation, truncation and changed line endings. This runs with
the normal laboratory checks as well. Use `python tools/generate-spc-hotpath.py
--check` for the generated decoder and the existing fixture-verification commands
for independent immutable input identities.

The implementation verification additionally compares every non-trivia C# token
against the pre-edit source bytes and builds the entire solution. Comments do not
change emulator timing, instructions, formats or acceptance thresholds. Source
fingerprints and rebuilt binaries may change and are not claimed byte-identical.

The source-header audit currently covers 214 maintained files. The SDK Roslyn
comparison covers all 150 C# files and 374,405 tokens against the pre-edit
`e9e7e7a` sources with zero executable token differences. Twelve header/generator/
fixture verification commands pass, and 33 Python files retain identical syntax
trees. Reviewed script changes are limited to the generated C# notice, historical
source-pin resolution and running the new notice check with the laboratory.
The solution build succeeds, and all 144 Core and 23 distribution/update regression
groups pass. The build retains one nullable-dereference warning in the
existing GitHub release test assertion. No core accuracy or realtime acceptance
claim is added by this documentation work.
