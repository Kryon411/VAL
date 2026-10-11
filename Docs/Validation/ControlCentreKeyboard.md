# Control Centre Keyboard Release Candidate

Historical candidate record. The current artifact identity, native F6 routing, readable typography, compact-header correction and owner-confirmed Enter/Space activation are recorded in [Native Keyboard Access](NativeKeyboardAccess.md). Evidence below remains tied to its own payloads.

11 October 2026. The current local VAL v5.1.0 candidate is under `artifacts/review/v5.1.0-keyboard`. It supersedes the executable and asset identity in [Adaptive Control Centre](AdaptiveControlCentre.md), while retaining that placement algorithm and the approved gunmetal/glass design. No production VAL installation was run or changed. No commit, push, tag or release publication was performed.

## Corrections

- Accepted, trusted native launcher clicks activate the owner and focus WebView2 before delivering the Dock open/close message. DOM focus alone cannot transfer keyboard input out of the owned native launcher window.
- Closing the panel restores its previously focused visible, connected, enabled page control before hiding the panel. Restoration is verified against `document.activeElement`, not assumed from a successful `focus()` call. A valid DOM launcher is a fallback; otherwise hidden-panel focus is cleared. Focus already moved outside the panel is not stolen, and composer content is not changed.
- Disarmed or debounced native launcher clicks do not transfer focus. Authentication-page bridge restrictions and existing click debounce remain intact.

Changed application files: `MAIN/VAL.App/Host/Services/MainWindowShellBridgeController.cs`, `MAIN/VAL.App/MainWindow.xaml.cs`, `MAIN/Dock/Dock.main.js`, `MAIN/Dock/Dock.module.json` (1.0.5). Regression coverage is in `MAIN/VAL.Tests/Hosting/MainWindowShellBridgeControllerTests.cs` and `Build/Verify_ClientScripts.js`. `MAIN/Manifest.txt` and readiness records are refreshed.

## Completed Verification

- Release build: zero warnings/errors. Formatting verification passes.
- 375 .NET tests pass without skips; report: `tests/keyboard-ui.trx` beneath this candidate root.
- 80 client syntax/behavior checks pass, including focus restoration without draft changes, stale/hidden/disabled origins, safe DOM-launcher fallback, controls that refuse/throw on focus, and preserving focus already outside the panel.
- Twelve build scripts parse; production-installation refusal and three payload-digest checks pass. Dependency audit reports no known vulnerable packages across ten projects.
- Six published startup/native-theme/deferred-cleanup gates, five isolated install/path-rejection/repair/uninstall gates, and nine archive compatibility/isolated upgrade/rollback gates pass for the exact fingerprint below. Rollback remains explicit candidate uninstall with data retention followed by baseline reinstall, not a clean in-place downgrade or automatic updater.
- The rendered offline fixture uses the production Dock scripts and styles. It verifies signed-in Share/Summary/menu anchoring, 560-pixel default width below the header, forward/backward Tab wrapping, skipping disabled Portal controls, 8-pixel arrow movement/resizing and 32-pixel Shift movement/resizing. Escape restores the opening page control. At 420 pixels, automatic placement remains beneath the header, long labels wrap without horizontal overflow, and the focused resize grip is visible. Screenshot: `keyboard-narrow-fixture.png`. This fixture run preceded the final focus-refusal fallback tightening; layout and style assets did not change afterward. The final focus-refusal cases pass the production-function tests, and normal focus return was repeated in the final desktop payload.
- Manual sign-in completed in the isolated session. Live inspection verified the empty-chat header (Temporary chat) and the previously approved synthetic chat's Share/More header, without opening personal conversations or sending/uploading anything. The orb remains beside the visible right-hand action cluster. Current ChatGPT does not display a Summary button in these views; that variant is fixture evidence only.

## Live Keyboard And Placement Evidence

On the interim payload `043faff371f333c5ccbb1d39f0d0bf79a0ea7c204d4c84e95be2d3d28c53e9ae`, native orb opening moved keyboard input into the web panel. Tab reached Reset; Shift+Tab reached the move grip, then wrapped to the resize grip. Forward Tab wrapped back to the move grip. Arrow resizing changed the default 560-pixel width to 552, then 520 with Shift. Move-grip arrows changed X from 696 to 688, then 656 with Shift. Keyboard activation of the focused Reset cleared custom placement. Escape closed the panel; the next Tab visibly focused ChatGPT's composer-area thinking-effort control, proving focus had returned to the page. Live Tab traversal reached the enabled Portal privacy toggle, then Abyss Search, skipping disabled Capture & Stage and Send. Both privacy toggles stayed Off.

The synthetic chat's sidebar was closed, and the orb/panel followed its Share/More header. Maximizing and restoring the native window retained right-hand orb anchoring and an automatic panel beneath the header. These geometry, traversal and window-state checks occurred before the final focus-refusal fallback change; no placement, native shell, event-handler or style code changed in that last tightening.

The final payload below was reopened in the same isolated session: authentication, synthetic-chat navigation, automatic placement, saved panel-open state and disabled privacy preferences persisted. The native orb was used to open the panel again; the first Tab visibly focused Reset. Escape dismissed it, and the next Tab visibly focused the composer-area thinking-effort control. Thus the final helper's normal native-to-web focus handoff and focus return were rechecked live. `control-centre-signed-in.jpg` was refreshed from this final running payload, with the personal-chat sidebar closed, six loaded modules, Capture & Stage Off and 0/10 staged images.

Automation limits are not passes: input was paused during manual authentication; the native launcher sometimes required owner activation and one fresh-state retry because it is a separate owned window. Escape immediately after activating a hidden-start validation process did not reach the restored panel until web focus was established. Normal-launch initial focus, keyboard-only launcher discovery/movement/activation, and recovery-pill keyboard routing remain separate checks. Native accessibility observation reported the WebView document as focused; visible focus rings and persisted geometry, not that coarse field alone, established the successful panel keyboard results. No authentication input or privacy-setting change was automated.

## Candidate Identity

- Customer installer: `InstallerOutput/VAL_Setup_v5.1.0.exe`, with adjacent SHA-256 file. Installer SHA-256: `de9bee6e08a71e79624fd5c729d52593f3a1fe565fa1a95a2415dfce646b2b2b`.
- Executable SHA-256: `ecef37f30a002eac59bb3b0f32ad8b2a3e33c29db590d4195ef11d44945b6c5a`.
- Complete payload SHA-256: `36c2a89b67f4ef0eacca3b93bb22eef25ad9a6a59bd0d9e28f5d0e3de3706f3c`.
- `published-validation.json`, `installer-validation.json`, `UpgradeRollback/0b7ea7a20f39490cbc1bd7a84af9cd4e/upgrade-rollback-validation.json`, installer build metadata and `MachineKit/d722a63d704d41fb8d384beb634c9b43/machine-kit.json` all match this payload. The disposable-machine kit is staged, not executed. Earlier machine-kit and rollback directories in this root belong to the interim payload, not this final candidate.
- Installed VAL v5.0 executable remains SHA-256 `61fc1b42359448a963de1cb676723a9070615b140281d76e69b3882777868ed1`.

The working tree remains uncommitted. Embedded Git SHA `1d8d835` is the base commit, not the changed source revision. Full payload hashes distinguish this candidate. The installer is unsigned, and the compiler reports non-commercial use only.

## Pending Live And Release Gates

Complete normal-launch initial focus, keyboard-only discovery/movement/activation of the native launcher, screen-reader interaction, recovery-pill keyboard routing and broader live viewport/sidebar transitions. Keep Continuum logging, Portal capture and Capture & Stage Off; use an empty chat or an explicitly approved synthetic thread. No cloud send, upload or deletion is authorized by these placement/keyboard checks. Final geometry/traversal reruns beyond the focused handoff/return recheck above should remain distinct from the interim results.

Signing/timestamping, appropriate distribution licensing, original-production-installer tests on disposable supported Windows machines, legacy desktop runtime rollback, exact-revision CI, DPI/multi-monitor, accessibility and long-session/performance checks also remain release gates. Earlier authenticated feature results are historical and do not certify every behavior in this new payload. See [Release Readiness](../ReleaseReadiness.md).
