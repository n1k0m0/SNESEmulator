# SNESEmulator

This project is an independently developed C# SNES emulator, architecturally
derived from the sibling C64Emulator. Preserve the original C64 repository.
Do not replace the SNES hardware core with a native/external emulator core.

Build: `dotnet build SNESEmulator.sln -c Release`
Tests: `python tools/test-lab.py --windows` (current separately reported core/runtime/persistence/network/manager suites; see docs/test-laboratory.md).
Portable: `python tools/test-lab.py`. See `docs/test-laboratory.md` for the separate non-passing accuracy inventory.
The integration tests require Python 3 for the local TLS relay test.

Keep hardware execution in SNESEmulator.Core independent of Windows/UI/audio.
Keep emulation deterministic and version savestate formats when state changes.
Document hardware limitations honestly. A diagnostic ROM is not a compatibility
test for commercial games. Do not distribute commercial ROMs or C64 firmware.
Preserve the Apache license and attribution for reused C64 frontend/transport.

## Temporary Windows Codex browser crash workaround (2026-10-13)

Use external Chrome/Edge for browser automation. Never call `tab.close()` on an
in-app-browser (`iab`) tab or create disposable `iab` tabs for testing. Leave any
existing in-app preview for the user to close manually. If external control is
unavailable, use non-browser checks and disclose the visual testing limitation.
Remove this workaround only after a fixed app version has been verified.
