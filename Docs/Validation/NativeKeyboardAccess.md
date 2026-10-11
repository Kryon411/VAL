# Native Keyboard Access And Control Centre Readability

11 October 2026. The current local VAL v5.1.0 candidate is under `artifacts/review/v5.1.0-native-keyboard-final`. It supersedes the artifact identity in [Control Centre Keyboard](ControlCentreKeyboard.md), while preserving its native-to-web focus restoration, the [adaptive placement](AdaptiveControlCentre.md), and the [approved control treatment](ApprovedControlDesign.md). No production VAL installation was run or changed. No commit, push, tag or release publication was performed.

## Implementation

- F6 and Shift+F6 focus the available native VAL control: the Control Centre icon on a trusted, bridge-ready app page, or Return to ChatGPT when native recovery is available. This is an app-local WPF accelerator, not a global shortcut or script injected into authentication pages.
- Focus transfer is deferred until WebView2's synchronous accelerator callback returns. Repeated keys, hidden/disabled controls, inactive/minimized owners, closed windows, and changed bridge state cannot steal focus through the queued callback.
- Enter and Space retain normal WPF button activation. From either native control, Tab, Shift+Tab, F6, Shift+F6 and Escape return keyboard focus to the browser without navigating. Existing icon movement and placement-reset shortcuts remain unchanged.
- Accessible names, F6 accelerator metadata, help text and tooltips expose the route. Initial browser focus is established after startup only while the owner is active.
- Alt+F4 on the native icon routes through the main window's existing close guard instead of removing only the icon. The Return pill already follows that ownership rule. These specific live Alt+F4 paths remain pending.
- Control Centre actions, toggles and counters use 14-pixel text; headings use 15 pixels; the title uses 16 pixels. Secondary text uses 13 pixels with the approved muted color at full opacity instead of compounding that color with additional fading. The title uses the approved full text color. Rim, seam, blue reflection and glass-face colors in `VAL.Controls.css` are unchanged.
- Below a 350-pixel panel width, section toggles move beneath their headings and the title uses 14 pixels, avoiding split headings and truncation. Long labels wrap and the panel stays within the viewport.
- A compact-header regression was corrected: adjacent actions left of the viewport midpoint remain part of the right-hand action cluster. The native icon no longer overlaps the leftmost Share action in the 320-pixel fixture. Dock module version is 1.0.6.

## Changed Files

Native routing: `MAIN/VAL.App/MainWindow.xaml.cs`, both native overlay XAML/code-behind pairs, `Host/Services/ControlCentreOverlayHost.cs`, `IControlCentreOverlayHost.cs`, `NavigationRecoveryOverlayHost.cs`, and the new `NativeControlKeyboardNavigation.cs`.

Readability and compact placement: `MAIN/Dock/Dock.styles.css`, `Dock.Layout.js`, and `Dock.module.json`. Regression coverage: `MAIN/VAL.Tests/Hosting/NativeControlKeyboardNavigationTests.cs`, the existing overlay-controller test fake, and `Build/Verify_ClientScripts.js`. The source manifest, README and validation/readiness records are refreshed. Other dirty-tree changes belong to the preceding review work and are not attributed to this pass.

## Completed Verification

- Release build: zero warnings/errors. Formatting verification passes.
- All 389 .NET tests pass, with no skips; report: `tests/native-keyboard.trx` beneath this candidate root. Coverage includes native shortcut policy, unchanged activation/movement keys, hidden-control focus refusal and accessibility metadata.
- All 82 client syntax/behavior checks pass, including the typography contract and compact-header regression.
- Twelve build scripts parse; production-installation refusal and three payload-digest checks pass. The dependency audit reports no known vulnerable packages across ten projects; dependencies did not change in this pass.
- Six published startup/native-theme/deferred-cleanup gates, five isolated install/path-rejection/repair/uninstall gates, and nine archive-compatibility/isolated upgrade/rollback gates pass. Rollback is explicit candidate uninstall with data retention followed by baseline reinstall, not an in-place downgrade or automatic updater.
- The production-script/style fixture was inspected at normal width, 420 pixels and 320 pixels. It retains the approved gunmetal/glass treatment, has no horizontal panel overflow, wraps long labels, and keeps narrow headings intact. The 320-pixel fixture includes the corrected full header-action cluster. This is fixture evidence, not a live mobile ChatGPT certification.
- Final live observation on the approved synthetic chat, with its personal-chat sidebar closed, showed Shift+F6 focusing the native icon with a clear blue ring. Native mouse activation opened the panel and transferred focus to its title; Tab reached Reset with a visible focus ring. Images: `native-launcher-focus.jpg` and `control-centre-typography.jpg` beneath the final candidate root.
- The owner subsequently confirmed keyboard-only F6 then Enter opens Control Centre, Escape closes it, and F6 then Space reopens it. This is owner-attested activation evidence; automation could show native focus but reactivated the main window before its next key, so that automation limitation was not treated as an application failure or a pass.
- On Google sign-in, the owner confirmed a clear F6 focus ring on the Return pill, Tab and Escape returning focus without leaving the provider page, and Space returning to ChatGPT. In a second manual provider visit, F6 then Enter also returned to ChatGPT. Authentication navigation and all keyboard input in these provider checks were manual.
- After returning to ChatGPT, the owner pressed F6 then Alt+F4 and confirmed that the entire isolated VAL window closed, not just its native icon. Process inspection subsequently found no running VAL executable. No busy-operation warning appeared. This verifies the idle Control Centre ownership path, not the provider pill's Alt+F4 path or a new busy-exit run.

The earlier `v5.1.0-native-keyboard` interim root shares the final executable but not its final Dock assets. Earlier forward-F6 and initial-page-focus observations remain interim evidence. Historical panel traversal, resizing and viewport tests are detailed in [Control Centre Keyboard](ControlCentreKeyboard.md); they are not silently reassigned to this payload.

Authentication and privacy settings remain manual. No authentication input, browser-profile inspection, cloud prompt, upload or deletion was automated during these keyboard/readability checks. Continuum logging, Portal capture and Capture & Stage remained Off in the observed candidate.

## Candidate Identity

- Customer installer: `InstallerOutput/VAL_Setup_v5.1.0.exe`, with adjacent SHA-256 file. Installer SHA-256: `1253fff59d4c9adaa13a4bdd099708df0d6ce1bf2a5083ec850f92727d9d2606`.
- Executable SHA-256: `7fae65357b844a4fd4c3ce8675415d529be8a1ce56f6e293a704c75d47b694b3`.
- Complete payload SHA-256: `0385547b626998afcf1d279d76d3a24da7e3b4e47ae448f89855335e798e0ad4`.
- `published-validation.json`, `installer-validation.json`, installer build metadata, `UpgradeRollback/dd6dbb7efe5746feb3592065fa38041d/upgrade-rollback-validation.json`, and `MachineKit/c13a375a56914667a7b71daff4a4f19a/machine-kit.json` match this payload. Customer and isolated validation installers have distinct hashes and registration identities. The disposable-machine kit is staged, not executed.
- Direct source/publish hash comparisons match the final Dock layout, styles, module metadata and approved control CSS. All reported payload/executable/customer-installer identities and the customer checksum were cross-checked against the actual files.
- Installed VAL v5.0 executable remains SHA-256 `61fc1b42359448a963de1cb676723a9070615b140281d76e69b3882777868ed1`.

The working tree remains uncommitted. Embedded Git SHA `1d8d835` is the base commit, not the changed source revision. Full payload hashes distinguish this candidate. The installer is unsigned, and the compiler reports non-commercial use only.

## Remaining Gates

The provider pill's Alt+F4 route remains pending. Normal-launch focus, native icon movement, screen-reader interaction, broader viewport/sidebar transitions, DPI and multi-monitor checks also remain separate gates. Native button metadata and startup/template tests do not replace these interactions. The provider keyboard results and idle native-icon close result above are owner-attested rather than independently captured keyboard observations.

The requested type scale and removal of extra text fading are deliberate deviations from the former small/muted Control Centre text; approved material colors and existing labels/actions are retained. Narrow-width stacking is a deliberate responsive adaptation, not a desktop layout redesign.

Signing/timestamping, distribution licensing, original-production-installer tests on disposable supported machines, legacy runtime rollback, exact-revision CI, accessibility and long-session/performance validation are still required. Historical authenticated feature results do not certify every behavior in this new payload. See [Release Readiness](../ReleaseReadiness.md).
