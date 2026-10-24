# M8 executed title matrix

## Coverage after R3

The immutable machine-readable M8 matrix below retains its original inputs and
expectations. R3 separately adds three unchanged gilyon v1.4 functional ROMs in
both NTSC and PAL; see [the R3 contract](r3-accuracy.md) for success markers,
counts and excluded behavior. The user reports successful local Super Mario
World and Zelda play. Those observations have no reproducible checkpoint/input
plan or physical-console comparison and are not promoted to formal compatibility
passes. Commercial ROMs and original firmware are not distributed.

Full hashes, sources, licenses, region/mapper/chips and separate functional statuses are in [the machine-readable matrix](../testdata/m8-title-matrix.json). Exact input plans and checkpoints are pinned in both console manifests. All images below are actual native software comparisons; none is a hardware certificate.

| External program | ROM SHA256 prefix | Region tested | Boot/diagnostic | Inputs | State | Audio / SRAM |
|---|---|---|---|---|---|---|
| hdma-repeat-entry | 645101f2618c | NTSC/PAL | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| hdma-indirect-repeating-pattern | 4aaddba42ca2 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| hdma-160scanline-hdma-entry | 4e68b08a40c6 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| hdma-to-cgram | 64f880cd2a6e | NTSC/PAL | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| textbuffer-hello-world | c2ddb219df2e | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vram-writes-without-dma | d237a766f2e7 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| window-mask-logic | 5d241b3f92a1 | NTSC/PAL | exact24/25 | 5 changed states | 3 -> 6 pass | sound untested / no SRAM |
| repeating_hdma_pattern | 1d647b663309 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-horizontal-scrolling | 0be05b45686f | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-vertical-scrolling | 4a509cdd0cd2 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vram-mid-scanline-test | fc04912719a8 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| inidisp_brightness_0 | 82143ec8b24c | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| inidisp_brightness_delay | e55f0bc62c91 | NTSC/PAL | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| inidisp_enable_display_mid_frame | d63b83ffa322 | NTSC/PAL | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| wrmpyb-in-flight | b938e1670f4b | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-2bpp-with-remapping | 5997d5290fe0 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-4bpp-with-remapping | c331e359c059 | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-8bpp-with-remapping | f6976a6f052a | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| vmain-mode7-image-with-8bit-remapping | c25fb702c399 | NTSC/PAL | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |
| inidisp_fadein_fadeout | d7c026579e7a | NTSC | exact24/25 | not asserted | 3 -> 6 pass | sound untested / no SRAM |

All20 programs are unchanged Zlib LoROM inputs with no enhancement chip. Their output is a diagnostic scene; boot success does not assert game compatibility. Window controls run40 fields; other checkpoints run24/25.

The unchanged MIT/CC-licensed HiROM **unnamed-snes-game** tech demo (`41a13ef6e25f8647a090efa33c0517307405b50d032f67a621cf1b1665ccd42e`) executes title120, file menu175, active room300 and segment600. Both references agree with native images and4096-byte SRAM. The numbered1/2 floor matches current source `sl_floor.png`; the older README screenshot is not an oracle. Title music is nonzero and snapshot130 -> 135 resumes exactly. Snapshot300 -> 305 covers input-created room/SRAM and its intentionally silent `blank` song. Raw battery persistence roundtrips; fault-injection/atomic frontend persistence was subsequently accepted in M9c/R1.

Initial32768 native stereo pairs match both references. Full sequences retain red nominal-frequency and unresolved same-frequency differences. Those rows are never labeled passed audio compatibility. See [M8 contract](m8-compatibility-contract.md) and [audio repro](../testdata/m8-audio-repros.json). This is roughly10 seconds of game flow, not a long-session or realtime claim.

Commercial games, other external mapper families, chips and peripherals are explicitly untested. Internal audio and SETINI diagnostics appear separately in the console reports and are excluded from this external-title table.
