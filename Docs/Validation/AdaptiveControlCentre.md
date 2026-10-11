# Adaptive Control Centre Release Candidate

11 October 2026. VAL v5.1.0 is prepared locally under `artifacts/review/v5.1.0-adaptive`. No production installer was executed, and no commit, push, tag or release publication was performed. Installed VAL v5.0 and its data were not modified.

This is historical evidence for the first adaptive-layout payload. The newer [Control Centre keyboard candidate](ControlCentreKeyboard.md) corrects native keyboard focus transfer and panel focus restoration, and has separately fingerprinted installers and validation reports. Use that record for current candidate identity; do not combine these payloads' live results.

## Placement And Interaction

- Automatic placement finds the visible right-hand header action cluster without depending on English labels. The launcher sits 10 CSS pixels to its left, centred vertically; crowded headers use a non-overlapping second row. Signed-out Log in/Sign up and signed-in Share/Summary/menu layouts are covered by fixtures.
- The panel is right-aligned beneath the header, defaults to 560 CSS pixels wide, and uses available height up to 720 pixels. It clamps within the browser viewport and wraps at narrow widths. Sidebar changes, header mutations, viewport resizing and native owner movement trigger layout refresh.
- Drag the launcher directly. Drag the panel's Control Centre title strip to move it; drag its lower-right grip to resize. Right-click no longer enters layout mode. Existing custom placements migrate conservatively and remain custom until reset.
- Reset, or Ctrl+Alt+Shift+L, restores automatic placement. Focused panel move/resize grips accept arrow keys (8 pixels, or 32 with Shift); the native launcher accepts Alt+arrow keys. Escape dismissal, focus trapping, real commands, disabled conditions and privacy settings remain intact.
- The approved shared gunmetal/glass skin is unchanged. The title grip, Reset control and resize grip are intentional functional additions, not reference-scaffolding controls. Header observations carry only finite rectangles through the existing nonce/source/origin-protected command route; they do not contain conversation content or write layout state on every observation.

## Changed Files

Application assets: `MAIN/Dock/Dock.Layout.js` (new), `Dock.main.js`, `Dock.styles.css`, `Dock.module.json` (1.0.4), and `MAIN/Manifest.txt`.

Native shell: `MAIN/VAL.App/MainWindow.xaml.cs`, `ControlCentreOverlay.xaml`, `ControlCentreOverlay.xaml.cs`; `Host/Services/ControlCentreHeaderLayout.cs` (new), `ControlCentreOverlayHost.cs`, `IControlCentreOverlayHost.cs`, `MainWindowOverlayController.cs`, `MainWindowShellStateController.cs`; `Host/Commands/DockCommandHandlers.cs`; `State/ControlCentreUiState.cs` and `ControlCentreUiStateStore.cs`.

Tests and fixtures: `MAIN/VAL.Tests/Hosting/ControlCentreHeaderLayoutTests.cs` (new), `MainWindowOverlayControllerTests.cs`, `MainWindowShellStateControllerTests.cs`, `MainWindowShellTimingControllerTests.cs`, `State/ControlCentreUiStateStoreTests.cs`; `Build/Verify_ClientScripts.js` and `Build/Fixtures/VAL.DockLayout.html` (new).

Lifecycle validation: `Build/Verify_UpgradeRollback.ps1`, `Build/Verify_DisposableMachine.ps1`, and `Docs/Validation/DisposableMachine.md`. Readiness and approved-design records link this newer candidate separately from earlier payload evidence.

## Verification

- Release build: zero warnings/errors; formatting verification passes; source manifest is current and deterministic.
- 372 .NET tests and 74 client syntax/behavior checks pass. `tests/adaptive-ui.trx` records the .NET results. Twelve build scripts parse; the production-installation refusal guard and three payload-digest checks pass.
- Dependency audit reports no known vulnerable packages across ten projects.
- Six published startup/native-theme/deferred-cleanup gates, five isolated install/path-rejection/repair/uninstall gates, and nine archive compatibility and isolated upgrade/rollback gates pass. All report the exact payload fingerprint below.
- Real production Dock scripts in the offline browser fixture pass signed-in/out anchoring, direct pointer movement/resizing, keyboard movement/resizing, reload persistence, focused-grip appearance, a 420-pixel viewport, and long labels without horizontal overflow. Existing approved-control fixtures separately cover normal, hover, pressed, selected and disabled appearance.
- Live isolated VAL confirms signed-out automatic anchoring, panel opening/closing, restart persistence, six loaded modules and approved shared CSS tokens. Launcher and panel pointer dragging passed on the interim candidate before the final grip/security cleanup. Final pointer resizing passed in the production-script fixture, not as a live native desktop test. Native live keyboard/sidebar automation was unreliable; these attempts are not counted as successful live tests. `control-centre.jpg` shows the final running candidate.
- Capture, Capture & Stage and Continuum logging remain Off, with zero staged images. No cloud conversation was sent, uploaded, changed or deleted during this pass.

The initial in-place downgrade check found the newer `Dock.Layout.js` and `VAL.Controls.css` left behind by the baseline installer. The supported test sequence now uninstalls the candidate while retaining data, waits for Inno's self-removal helper, and reinstalls the baseline. Exact application-file lists/hashes and unchanged synthetic archive/preferences are verified. This is explicit uninstall/reinstall rollback, not an automatic updater or proof that an in-place downgrade is clean. The disposable-machine script uses the same sequence and now verifies the original baseline's complete file inventory after rollback.

## Artifacts And Identity

- Customer candidate: `artifacts/review/v5.1.0-adaptive/InstallerOutput/VAL_Setup_v5.1.0.exe`, with adjacent SHA-256 file. Installer digest: `8356a8c6d5a81c22eee2c24bb47c30e36c8a57526257ec03c54079510cef3f8b`.
- Executable digest: `e12e85bc9cb0acb3c931c25f831524556e2c7bab321d78a8eae926f81ae4b16f`.
- Complete published payload digest: `87dcb2faa7797c703607e4e7163b1f6f42e205cc1398f3bbb79ae1f0e0355af2`.
- Published and installer reports: `published-validation.json` and `installer-validation.json` beneath the candidate root. Successful rollback report: `UpgradeRollback/5d5878a794a140e4a44bf7748d6b728d/upgrade-rollback-validation.json`.
- Disposable-machine kit: `MachineKit/aa985bb0db254970a5e9a7abb56cc00b`, staged only. Its candidate fingerprint matches these artifacts; no production-machine script was run on this development PC.
- Installed v5.0 executable still has SHA-256 `61FC1B42359448A963DE1CB676723A9070615B140281D76E69B3882777868ED1`.

The working tree is uncommitted. Embedded Git SHA `1d8d835` identifies the base commit, not the changed source revision; the full payload fingerprints distinguish this candidate. Do not treat that base SHA alone as release provenance.

## Remaining Release Gates

The candidate is unsigned. Signing/timestamping, appropriate Inno distribution licensing, original-production-installer testing on disposable supported Windows machines, actual legacy runtime rollback, final authenticated/browser and provider checks, native keyboard accessibility, DPI/multi-monitor, long-session/performance and exact-revision CI validation remain outstanding. Earlier authenticated workflow evidence is historical and does not by itself certify this changed payload. See `Docs/ReleaseReadiness.md` for the wider commercial-release obligations.

ChatGPT header markup is external and may change. The implementation uses geometric discovery and a safe fallback rather than assuming stable labels, but future markup revisions still require monitoring and testing. This candidate is ready for controlled local testing, not a claim of complete commercial certification.
