# Original source fingerprints before license/comment additions

These 29 own-source snapshots retain the exact pre-notice bytes from
`e9e7e7ad579c92d669c52960e489edca41ccd78b`, including original line endings.
They are historical inputs to the existing local/M5/M7/M8/M9/DSP1 fixture
manifests, not maintained application code. Apache-2.0 applies to these original
project sources; see the repository LICENSE and NOTICE. Existing third-party
reference and data attribution remains applicable.

The active sources received license headers and explanatory comments. Existing
expected fixture hashes were not changed. `tools/historical_sources.py` resolves
only this explicit source inventory when a verifier checks an old source pin.
Earlier M8/M9 snapshot mappings take precedence. Actual ROMs, vectors, captured
video/audio and state fixtures continue to be checked at their original paths.
Current builds, execution and source-provenance hashes always use active sources.

`manifest.json` records each archived file's original length and SHA-256.
`python tools/verify-source-notices.py --negative-controls` checks this archive
and maintained source headers. Do not regenerate snapshots from later sources
or add new headers to these immutable historical bytes.
