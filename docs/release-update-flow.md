# Application release update flow

Normal GUI launches schedule a nonblocking check after 2.5 seconds. No check is
scheduled by any `--` diagnostic, headless, test or soak mode. Merely constructing
`SnesWindow` does not enable official network requests. F10 / CHECK FOR UPDATES
uses the same official source in a normal GUI launch. Startup failures are silent;
user-initiated failures appear in the status line and download dialog.

The centralized default repository is `n1k0m0/SNESEmulator`.
`SNESEMULATOR_RELEASE_REPOSITORY=owner/repository` can select a separately hosted
public download repository without changing source visibility. Only valid GitHub
repository identifiers are accepted. No token or repository visibility is changed
by the application.

The bounded unauthenticated Releases API query inspects at most 100 releases and
4 MiB of metadata within 15 seconds. Drafts are excluded. Stable installations
consider stable releases; preview installations consider newer previews and final
versions. SemVer 2.0 precedence handles numeric prerelease identifiers, final over
preview, and ignores build metadata. It is shared with the custom ZIP updater;
current/older/build-only versions do not request a payload.

The exact installer asset name is
`SNESEmulator-{version-without-v}-win-x64-setup.exe`. Its URL must identify that
asset/tag in the configured official repository. Size must be positive and no
more than 512 MiB. Downloads have a ten-minute total cancellation deadline and
stream into a fresh private staging directory. Advertised length, actual length
and a provided `sha256:<64 hex digits>` GitHub digest must agree. Malformed or
unsupported non-null digest values are rejected, never treated as absent.
When GitHub provides no digest, the dialog explicitly says only size and a local
file hash were checked; this does not verify publisher authenticity. A GitHub
asset digest is not an Authenticode signature.

The GitHub-specific transport follows at most five HTTPS hops, restricted to the
selected initial official asset and GitHub-owned release storage hosts
`release-assets.githubusercontent.com` / `objects.githubusercontent.com`.
Credentials, fragments, arbitrary redirect hosts and protocol downgrades are
rejected. The same transport supports explicitly configured official
`/releases/download/<SemVer-tag>/update.json` and ZIP URLs. Arbitrary custom
manifest sources keep the existing same-origin payload restriction and do not
admit redirects. Version/hash/package-inventory validation, executable-free ZIP
staging, user-file preservation and rollback remain in place.

The Settings / Update source editor can enable/disable checks and select official
setup releases or a custom ZIP manifest. A saved user opt-out wins. Historical
packaged disabled ZIP policies do not disable the fresh official default. Existing
explicit custom configurations continue using their configured manifest. The
optional `useOfficialGitHub` policy member defaults false when reading old JSON.
Policy data stays in the existing user-data directory.

The C64 update interaction is adapted with Apache attribution retained in
`GitHubUpdateDialog.cs`: prompt, progress, cancellation, retry, Later and explicit
Install. Download completion alone never schedules installation. Closing during
a download cancels and drains it while the WinForms message pump still exists;
cancellation also wins a race with download completion. Setup launch happens only
after the emulator's normal window shutdown and SRAM/settings disposal succeed.
An owned hidden copy of `SNESEmulator.Updater` binds a retained process handle and
checks UTC creation time before acknowledging READY. It waits for that exact
process to exit zero, reusing the same ownership protocol for ZIP updates. Setup
mode keeps a read-only verified installer handle through launch to prevent a
Windows file replacement between hash verification and execution. It performs no
uninstall, user-directory cleanup, forced process termination or host adjustment.
The interactive setup itself is launched only after explicit Install.

## Tests

`SNESEmulator.Distribution.Tests` injects HTTP handlers for GitHub metadata,
setup downloads and official manifest/ZIP CDN hops. It covers version precedence,
drafts/channels, unrelated and malformed assets, foreign/protocol/credential URLs,
size/hash failures, truncation, malformed/missing digest distinctions, cancellation,
no payload for old/current versions, official defaults, user opt-out and retained
PID/creation-time ownership. Owner tests start only inert owned fixture children
and cover zero/nonzero shutdown. Existing local custom-ZIP rollback tests remain.
These application-level tests do not contact GitHub or execute an installer.
Separate packaging integration may exercise an isolated test installer.

`SNESEmulator.Tests --update-dialog-only` runs four short STA WinForms tests with
injected download functions: cancellation, cancellation/completion race, Later,
and failure/retry followed by explicit Install. The same groups run in the normal
Windows integration suite. An inert prepared record is returned; no setup starts.
The retry-ready dialog was captured and visually inspected. Initial cancellation
failure and corrected passing reports remain separately under
`artifacts/release-update-flow/`; historical evidence is not overwritten.

Primary contracts:
[SemVer 2.0](https://semver.org/spec/v2.0.0.html) and
[GitHub release assets](https://docs.github.com/en/rest/releases/assets).
This note describes the application flow; packaging, licensing/publication and
release workflow are maintained separately by the integration owner.
