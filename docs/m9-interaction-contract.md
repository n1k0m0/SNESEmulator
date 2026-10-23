# M9g interaction parity contract

Status: implementation in progress; no M9g, whole-M9 or release acceptance yet.
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

New portable groups are reported separately from the prior276/31. Actual native
window/Manager checks, original regression suites, Linux portability and a clean
full Windows gate are required before handoff. All raw failures and development
limits remain visible. Independent Root acceptance is a later step.
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
