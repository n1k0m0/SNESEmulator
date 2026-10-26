# Windows setup, portable packages and updates

`VERSION` is the release version for the App, Manager, updater, package inventory,
setup metadata, asset names and tag. `Directory.Build.props` supplies SDK project
versions. The first public channel is an experimental preview, not stable native
timing acceptance.

## User flow

Download the matching Windows x64 setup or portable ZIP from
[GitHub Releases](https://github.com/n1k0m0/SNESEmulator/releases).
Setup follows the C64 wizard interaction: Start Menu, optional desktop shortcut
and optional launch. SNES retains its established per-user LocalAppData location
and stable AppId, allowing both setup and existing ZIP updates without elevating
the emulator. It deliberately preserves user data on repair/update and uninstall.
It does not copy C64's recursive previous-install or AppData deletion.

As with C64, normal startup checks GitHub in the background and offers a newer
setup. F10 / CHECK FOR UPDATES checks manually. SETTINGS / UPDATE SOURCE controls
opt-out and official/custom source selection. See
[the application update flow](release-update-flow.md) for verified versions,
digest/redirect rules, cancellation and clean-exit helper behavior.

Preview discovery uses the Releases list because GitHub's `releases/latest`
endpoint excludes prereleases. Stable installations ignore previews. Official
checks need no token once the repository/releases are public. No access token is
bundled. A custom versioned `update.json` must use
`/releases/download/v<version>/update.json`; `/releases/latest/download` is not a
preview channel. The official setup flow discovers future versions automatically.

## Building locally

Use pinned SDK 10.0.402 and Inno Setup 6.7.3. Existing Inno Setup 6 paths or an
explicit `-CompilerPath` are supported. The release contains runtime 10.0.13 for
all applications; no SDK is required to run it.

```powershell
./scripts/build-release.ps1 -OutputDirectory artifacts/my-new-release
# Local implementation experiments only, never publication:
./scripts/build-release.ps1 -OutputDirectory artifacts/my-unreleased-test -AllowDirty
```

Both output directories must be new. The script builds the three applications,
collects complete dependency/runtime notices, verifies package/version identity,
compiles Setup, writes a file-only ZIP, and checks its entire package inventory.
It creates exactly these four files under `assets/`:

* `SNESEmulator-<version>-win-x64-setup.exe`
* `SNESEmulator-<version>-win-x64.zip`
* `update.json`
* `SHA256SUMS.txt`

`release-info.json` and `package/package-manifest.json` record source/version,
runtime and dirty state. The verifier rejects dirty packages unless explicitly
running the unreleased local test mode. Runtime media, keys and saves are excluded.

For native setup testing, compile with `build-installer.ps1 -IsolatedTest` and
run `tools/test-installer.py` in a fresh short temporary target. This uses a
separate test AppId, proves install/repair/uninstall preservation, and leaves the
production installation alone. The production setup is not executed by tests.

## Publishing subsequent versions

Update `VERSION`, release notes and relevant tests, then commit and push. After
review, create and push the matching tag (for example `v0.1.0-preview.2`). Do not
reuse or move published tags. The `Windows setup and portable release` workflow
also supports manual dispatch for an existing matching tag.

The workflow runs the full portable laboratory on Windows and Ubuntu, full
external CPU/SPC vectors, the publication-content scan, a clean self-contained
build, exact release-asset verification and a real isolated-AppId setup test.
Only after those pass does the publish job upload all four assets to a draft and
publish it. Versions containing `-` become GitHub prereleases. The release notes
state the still-open accuracy/realtime limits. The ordinary laboratory workflow
continues to run independently; a hosted green job does not substitute for native
hour or physical hardware evidence.

Licenses and copyright notices travel with both ZIP and Setup. Review
[publication-audit.md](publication-audit.md) when adding dependencies, reference
code, firmware, media or other external files. The audit script is a technical
scan, not an automatic license determination.
# Local release integration evidence (October 26, 2026)

The complete preview package built locally with 985 files and passed the release
asset verifier. Actual self-contained runtime, quoted launcher, explicit update
opt-out, custom verified update installation and failed-shutdown protection
passed. An isolated installer identity passed installation, repair and uninstall,
preserving SRAM, State9, settings and Manager metadata. Application-level tests
passed 23 distribution cases, four STA update-dialog cases, 15 Windows integration
cases and 16 Manager cases. These checks do not establish a fresh-VM or physical
SNES acceptance result. GitHub builds the public artifacts again from the clean
tag; local dirty-tree packaging evidence is never uploaded as a release.

