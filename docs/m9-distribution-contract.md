# M9f distribution and update contract

The hardware core and State9/VID3/AUD2 are unchanged. SDK10.0.402 and
runtime10.0.13 are explicitly pinned; the SDK may be installed privately.
`global.json` rejects silent feature-band changes. Existing portable locks remain;
separate `packages.win-x64.lock.json` files are required by locked publish.

## Package and launch

`scripts/publish-win-x64.ps1 -OutputDirectory <new-directory>` publishes the
emulator, the standalone WPF Manager in `Manager/`, and the update helper in
`Updater/`. All three contain their own win-x64 runtime. An existing destination
is rejected. Development-copy targets are disabled during publishing. The package
contains LICENSE/NOTICE, available dependency license/nuspec files and both lock
sets. Its manifest records source commit/dirty state, SDK/runtime/RID, version,
file lengths and SHA256 hashes. No user ROM, firmware, SRAM or State9 is included.

`Start-SNESEmulator.cmd` supports the repository and standalone package layouts,
forwards quoted arguments and returns the actual application exit code. The
repository starter accepts only a manifest-bearing publish directory. The old
PRE-M0 publish is preserved under a distinct backup before first activation.
The user's playable-build snapshot and supplied ROM stay untouched.

## Installer

The Inno script uses its own SNES AppId, per-user installation and no associations,
automatic application termination, Run section or UninstallDelete section.
Normal uninstall removes installer-owned product files; it preserves unrelated
files and all external AppData. Tests compile a separate TEST AppId and install
only in a fresh isolated directory with no shortcuts. They execute a second setup
as actual repair/update and then the real uninstaller, checking retained SRAM,
State9, settings and Manager metadata bytes. This is not a clean-VM test.

Existing compiler: `%LOCALAPPDATA%/Programs/Inno Setup 6/ISCC.exe`, observed Inno6.7.3,
SHA256 `0a8757031b33777e4c9cbffee40f11a5062b36d25cbe144c1db73b6102b80ad7`.
No compiler or production application is installed by tests.

## Explicit updates

F10 has CHECK FOR UPDATES. The shipped `update-policy.json` is disabled and has
no release URL. No unsolicited network check or invented release endpoint exists.
An operator can explicitly configure policy `{schema:1,enabled:true,manifestUrl}`;
`SNESEMULATOR_UPDATE_POLICY` selects a separate policy for controlled local tests.
The UI performs bounded work off its dispatcher and drains it at shutdown.
Current version comes from its verified package, not a hardcoded release number.

A release manifest supplies schema1, version, packageUrl, bytes and SHA256.
Only HTTPS, or explicitly configured loopback HTTP tests, are accepted; redirects,
credentials/fragments and cross-origin package URLs are rejected. Policy <=8KiB,
release JSON <=64KiB, ZIP <=512MiB, expanded files <=1.5GiB, <=10000 ZIP entries,
paths <=240 characters. Unsafe separators, traversal, Windows reserved names,
control characters, duplicate case aliases and link entries are rejected. Every
listed product file is verified by hash/length; unlisted incoming files fail.
Archives are only data during verification, never automatically executed.

A verified newer package exposes INSTALL PREPARED UPDATE as an explicit second
menu action. The window completes the existing M9c normal shutdown AND disposal
before starting its verified helper copied outside the install directory. The
helper binds the parent PID/starttime/actual process handle and sends READY before
the parent may return. It requires the bound parent's ExitCode0. PID disappearance,
identity mismatch, timeout and nonzero exit cancel installation. This prevents
settings/SRAM/last-error failures from being mistaken for successful shutdown.
The controlled native test also forces a real settings-lock Exit1 and confirms
byte-identical installation and retained settings.

Stage/target must be distinct siblings on the same volume. The old product is
verified, unknown user files are copied without overwrite, and renames preserve a
full previous directory. Injected failures before/after backup and after install
restore the previous generation. Links and user-file conflicts fail closed. The
backup and failed stages are retained for inspection; no automatic recursive
cleanup of user directories occurs. Helper failures create a result error log and
`last-update-error.log` in the chosen user-data root, visible on next emulator
start. File replacement is atomic at rename boundaries, not a physical power-loss
or directory-fsync guarantee. Multiple running instances/file locks may prevent
replacement; they are never killed. The standalone update CLI's four-argument
install mode is for explicit offline administration; the product always uses the
bound-parent six-argument mode.

## Linux and CI boundaries

The portable core/runtime/contracts/management/tests execute on actual Linux
.NET under WSL Ubuntu24.04, not Windows dotnet.exe under WSL. The product GUI,
OpenTK/NAudio and WPF remain Windows-only. Linux process identity uses the exact
procfs starttick token and explicit `linux-proc-startticks` kind; Windows keeps
`utc-ticks`. Foreign kinds fail. Four persistent pipe listeners avoid unlinking
.NET's shared Unix socket while parallel peers are queued; normal Disconnect
reuses them, and shutdown still cancels and joins all four.

Linux GZip bytes differ.36 independent raw-state hashes were derived only from
accepted preoptimization baseline states inside immutable `m9a-history.zip`
SHA256 `475ac577f9b37f5145a8d244dba2d6f2d6179c5da5867bc42098c6071b13dccf`.
Every compressed source hash matches the unchanged old baseline. Windows keeps
both compressed and raw checks; Linux checks pinned raw bytes and every other
hardware/PCM/image/trace value unchanged. No current output became an oracle.
The old240s core functional watchdog was measured separately on Linux after a
timeout near the last PPU snapshots; Linux uses a bounded600s snapshot-suite
watchdog. Windows240s and all realtime/sustained thresholds remain unchanged.

CI pins402 and separates hosted portable/headless package work from opt-in actual
native tests requiring an explicitly configured interactive self-hosted runner.
There is no configured remote and no remote CI execution claim. Local selfcontained
starts hide SDK discovery and inspect PE/runtime assets; no fresh Windows VM was
available or modified. Native apphooks are synthetic; OS focus, physical pads,
DAC/display latency and original-console measurements are outside this phase.

M9a sustained performance, PAL measurement, multi-hour soak, known18 M8 audio reds,
7000 source-adjudicated raw SPC bus differences and M10 remain open. M9/release
remain incomplete. Final implementation/gate/freeze identities belong in baseline
m9f.json after completed execution; development attempts do not substitute for it.
