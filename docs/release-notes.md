# SNESEmulator 0.1.0-preview.2

## Changes since preview.1

* Fix UI mailbox shutdown ownership and ensure pending work is completed or
  rejected consistently while the emulator closes.
* Restore the Manager's valid backup recovery path when a savestate's primary
  file is missing, and refresh the available actions after recovery.
* Tighten DSP fixture verification so changes to pinned reference inputs cannot
  silently pass verification.
* Add consistent Apache-2.0 source notices, retain third-party attribution, and
  document execution timing and ownership invariants.

Regression tests cover the shutdown, Manager recovery and fixture-validation
changes. Release publication requires the Windows/Linux laboratory jobs, full
external CPU/SPC vector checks, publication-content audit, verified clean-tag
package and isolated installer lifecycle checks. These checks do not establish
a fresh-VM, physical SNES or sustained native realtime acceptance result.

## Preview status

This is an experimental preview of the independently developed C# SNES emulator.
It follows the C64Emulator interface, setup and update interaction. It is not a
completed hardware-accuracy or uninterrupted realtime release certification.

## Downloads

* `SNESEmulator-<version>-win-x64-setup.exe`: Windows x64 setup, Start Menu entries,
  optional desktop shortcut, and optional launch after setup. Installs for the
  current user without requiring a separately installed .NET runtime or SDK.
* `SNESEmulator-<version>-win-x64.zip`: portable self-contained application,
  Manager and updater. Extract the whole archive before launching the executable.
* `SHA256SUMS.txt`: SHA-256 checksums of Setup, ZIP and `update.json`.
* `update.json`: versioned package manifest for explicitly configured ZIP sources.

The setup is unsigned. Compare its SHA-256 with the release asset digest and
`SHA256SUMS.txt`; checksums are distinct from an Authenticode publisher signature.
No commercial game, Nintendo/Commodore firmware, user save or private key is
included. Supply your own legitimately obtained media.

## Updates

Normal GUI startup checks official GitHub Releases in the background. When a
newer compatible release exists, the C64-style dialog offers a setup download,
progress, cancel/retry, Later, and explicit Install. Installation starts only
after successful normal emulator shutdown. Download completion alone installs
nothing. Headless/test/diagnostic modes do not make startup update requests.

F10 / CHECK FOR UPDATES uses the same source. SETTINGS / UPDATE SOURCE can disable
checks or choose a custom ZIP manifest. A saved opt-out is respected. Preview
installations can receive newer previews or final versions; stable installations
receive stable versions. User SRAM, settings, savestates and Manager metadata
are preserved by setup updates and uninstall. Setup may ask to close a running
Manager; it does not force processes to exit.

## Known limitations

The observed owner CPU deficit and strict NTSC/PAL native realtime acceptance
remain open. Raw hardware and full-console audio disagreements are documented
in the laboratory; functional CI success does not erase those independent limits.
Cartridge chips have explicitly documented coverage and unsupported features.
There is no general commercial-game or original-console certification.

See [the release/update guide](windows-releases.md),
[publication/license review](publication-audit.md), and
[current laboratory status](test-laboratory.md). This project is independent of,
and is not affiliated with or endorsed by, Nintendo.
