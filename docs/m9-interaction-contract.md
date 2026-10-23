# M9g interaction parity contract

Status: implementation on2ab409d; label followup4a35520 corrected.
Independent Root gate on8daf478 failed the software A/V bound; no overall pass.
Whole-M9 and release remain incomplete.
Baseline: M9f independently accepted on0f8aadc; this phase changes host input,
settings and presentation, not hardware timing, State9, VID3, AUD2 or Protocol4.

## Source-based applicability matrix

| C64 source / roadmap promise | SNES before M9g | M9g behavior and applicable distinction |
|---|---|---|
| C64Window.cs429-448/503-521; persisted UI settings | Filter/upscale/fullscreen only | Persist render0/60/120Hz, VSync, presentation zoom and turbo; hardware worker clocks/pacer unchanged |
| C64Window.cs2047-2093; render cap | Fixed60 UI updates |0 unlimited,60,120; never change native field or audio clock |
| C64Window.cs4525/4742; video zoom | Fixed4:3 presentation | Optional centered10% presentation crop; no C64 border dimensions applied to SNES and no hardware overscan claim |
| C64Window.cs2895ff; controller actions |12 game mappings; fixed Start+Select and L+R+Select | Separate MenuSelect/MenuBack/MainMenu/Turbo/SaveStates/Fullscreen/Diagnostic actions; retain safe SNES chords |
| C64 settings MenuSelect Button1, MenuBack Button2, Main Button7;Button9, Saves Button6;Button8 | Semicolon OR game tokens | C64 semicolons are alternatives, never chords. SNES retains semicolon OR and adds ampersand AND. Defaults refer to mapped game buttons using @Start&@Select and @L&@R&@Select; all16/64 old OR combinations remain possible |
| Original roadmap full configurable bindings | Hardcoded keyboard plus12 gamepad fields |12 keyboard game buttons and seven frontend actions;19 gamepad entries, manager/live IPC/offline settings editors; direct release/hold/release capture plus manual expression edit |
| F10/F11/F12/F9; keyboard repeat | Fixed hotkeys with callback repeat | Configurable defaults retained; raw sampling generates toggle edges once; navigation repeats after350ms then100ms; state/text operations have fresh key edges |
| Menu return routes and input isolation | Partial pad neutral lease | Main closes to game; submenus return Main; Delete returns Saves. Menu/focus/device/configuration transitions require neutral input; held game Start cannot auto-select a new menu |
| Focus/hotplug/mapping changes | Existing pad source lease | Independent keyboard/pad source leases; keyboard/pad actions and games suppressed in background; reconnect/identity/mapping changes need release; capture cancelled on focus loss and restarted on device change |
| Frontend/game conflicts | Hardcoded | One routing stage: frontend action, navigation, contextual editor. Completed actions reserve their members; independent held game directions continue under turbo/fullscreen. Main/Saves/Diagnostic use neutral-until-release. Fixed priority Saves,Main,Fullscreen,Turbo,Diagnostic then Back,Select |
| Controller capture | First pressed token | Enter: release all, hold desired combination, release all to commit; supports buttons, signed axes and hats. Escape cancels; M opens manual editor (also supports assigning Escape). Delete clears. Maximum4 chord members and4 alternatives |
| C64 ResetMode Warm/Reload/Power | One Reset() operation | Warm or Power selected explicitly; both preserve current dirty cartridge SRAM. Power calls existing PowerOn(), Warm calls existing Reset(). No disk Reload analogy; no SRAM-delete action bundled into reset |
| Roadmap reset vs battery erasure | Reset does not erase cartridge SRAM | Explicit SRAM-retention label; no new SRAM-delete UI. Separate battery deletion remains outside reset and would need its own confirmation/recovery policy |
| Media browser, drop, errors | Directories, .sfc/.smc, parent/search; async errors | Preserve directory/search/filter/drop paths and display client drop denial; malformed local media retains prior session and shows error |
| Network/Manager | M9d/e accepted contracts | Input rights and host-owned turbo retained; configurable local presentation independent; settings revision/atomic persistence stay in place |

## Grammar and migration

Pad atom: Button0..255, Axis0..255 followed by+ or-, Hat0..255 followed by
Up/Down/Left/Right. Keyboard atoms use the explicit platform-neutral compatibility
key vocabulary. `;` means any alternative, `&` requires every simultaneous member.
Examples: `Axis1+;Hat0Down`, `Axis1+&Button2`, `ControlLeft&F7`.
Only frontend pad actions may use mapped game references such as `@Start`.
There is no escaping ambiguity with the legacy positive-axis suffix.
Empty bindings deliberately disable an action. Unknown top-level JSON settings
roundtrip through extension data and remain read-only in Manager; existing
integer button mappings migrate without changing their buttons. Existing bounds
(64KiB settings,128-character expressions, four alternatives/four members) apply.

Keyboard and controller input share the same frontend-session implementation.
Native raw KeyDown/Up callbacks enqueue at most256 ordered edges, processed
through the same router before the current snapshot. Short taps and multiple
text characters between updates retain event order and per-event modifiers. Native
focus callbacks discard old event leases even when focus returns within one
update; mapping and close transitions discard queues too. Overflow visibly neutralizes
input until release; callbacks do not dispatch shortcuts ahead of configured
bindings. Existing semantic app probes invoke explicit ProbeKey commands;
M9g adds raw-snapshot injection through the actual window polling/routing path.
These are software integration observations, not physical-controller or OS-focus
measurements. No input, presentation or reset claim is a new hardware oracle.

## Reset accuracy boundary

This phase exposes existing core operations. Existing warm PPU reset clears
VRAM/CGRAM/OAM; its original-hardware retention behavior requires the separate
accuracy audit. No speculative hardware fix or new retention claim is made.
Worker reset and power invalidate publication/input epochs and queued output.
Cartridge SRAM remains live and is later persisted by the existing owner/battery
flow; resetting never reloads stale disk battery bytes over dirty live SRAM.

## Validation and remaining work

Prior276/31 groups remain preserved. Eight new portable input groups and one IPC
boundary group give285 portable groups. The separate unknown-settings native
Manager group raises Windows groups to32;151 interaction stages are additional.

The complete clean Windows full gate on7effa47 passed285/31, all CPU/bus/SPC
corpora and1194 exact native build identities. This is the **pre-UI-fix** gate.
The final delta on2ab409d changes only WPF readonly controls/text and its native
test: unknown bool/string/object values are disabled and preserved exactly by
actual offline Apply, including lexical JSON number1.2300. Final clean checks
passed Manager12 portable, all32 native Windows groups,151 interaction stages,
runtime and all four persistence modes;1194 identities matched before/after.
The subsequent independent Root gate on8daf478 failed the software A/V bound;
the followup below keeps that failure open.

Actual Ubuntu WSL/private SDK10.0.402 passed59 targeted portable groups plus a
second Distribution9 run on7effa47. This is not a repeated full Linux hardware
gate. Original 2s settings-writer busy and named-mutex mkdirat ENOENT failures
remain open; unchanged passing repeats do not establish a fix or a cause.
No settings budget or production workaround changed.

The delta freeze reuses protected M9f objects by archive SHA/entry rather than
copying old corpora.912 new objects /35,579,041 expanded bytes,1,849 selected
objects and4,773 disk originals were reread after the corrected freezer exited.
The first freeze was rejected for selecting its active output log; final
selection excludes active logs and the rejected archive. Root helpers, SDKs,
original local ROM/IPL bytes and the user game snapshot are excluded. Exact
commits, hashes, raw reports, retained failures and limits are in
[the M9g baseline](baselines/m9g.json). C64, the user game/shortcut and publish
remain unchanged. Independent overall acceptance was not achieved.
Known7000 source-adjudicated SPC raw mismatches,18 full-audio comparison rows,
38/62 failed NTSC sustained windows, unperformed PAL and hours soak remain open.
M10 follows the later performance and base-accuracy phases.

Linux targeted development on clean dee54e4 passed interaction8/runtime16/persistence14/Manager11. An additional unchanged distribution fixture failed8/9 during its own canceled Accept loop (SocketException22 at Dispose after listener.Stop). Its guard now accepts socket shutdown exceptions only when the fixture owns an already-canceled token, and always disposes that token after joining. Product update/network code is unchanged. Original report and source commit remain retained; this is test-lifecycle hardening, not a hardware or product fix. An initial Linux wrapper CRLF parsing failure is retained separately.

## Manager compatibility boundary

IPC is explicitly version2. The new structured keyboard/frontend settings and
persisted host-owned turbo/reset semantics cannot be safely applied by an old
version1 Manager. Discovery/Hello reject that peer before any settings mutation;
all messages use one shared version constant. Both apps must come from the same
build. Hardware State9, VID3, AUD2 and the unrelated network Protocol4 stay exact.
One separate portable Manager group covers actual IPC1 requests/discovery.

The first Windows full-gate attempt stopped at the M8 adapter integrity check:
the expanded tools/test-lab.py pin had not yet been updated. Only that adapter's
length/SHA changed in m8-integrity.json; no hardware fixture or expected output
was changed. The original failed gate log remains retained.

Discovery intentionally filters incompatible instances; they are absent from the
instance list. Direct requests report an explicit incompatible-version error;
this phase does not add a cross-version discovery UI. Old-manager source at
0f8aadc only special-cases GamepadControls; its generic editor parses other
non-string settings with int.Parse, including the newly structured dictionaries.
The version boundary prevents older full snapshots from resetting new bindings.
New client/old server response rejection and old client/new server request
rejection both run over real named pipes; no command is forwarded after a bad
Hello. New portable totals are prior276 plus8 interaction plus1 IPC boundary.

## Independent Root result and bounded followup

The Root full gate on8daf478 passed285 portable/31 of32 Windows groups and151 interaction stages, then failed cumulative software A/V distance49.942ms against unchanged40ms. The separate label correction is visually accepted; opt-in diagnostics and one new13-group pass do not explain or fix that original failure. Two isolated30s readiness failures also remain open. [Followup details and next A/V scope](m9g-followup.md); no further product or Linux run was made after the final ordered13-group observation.
