# Approved VAL Control Design

The 11 October 2026 design from `val-ui-handoff/CODEX_HANDOFF.md`, `val-ring.css` and the rendered `approved-reference.html` is implemented through shared web and native controls. The frozen approved colours, gradient stops, gunmetal rim, recessed seam, restrained blue reflection and dark glass face are retained. Reference showcase layouts, tweak controls and demonstration handlers are not shipped.

## Implementation

- `MAIN/Dock/VAL.Controls.css` supplies the shared web skin. Only VAL-created nodes marked `data-val-ui="1"` receive it; embedded ChatGPT controls do not. Noninteractive pseudo-elements preserve click targets and handlers.
- `MAIN/Dock/Dock.main.js` and `MAIN/Modules/Abyss/Abyss.main.js` mark their own controls. Abyss keeps its original input, ref and keyboard handlers inside a visual frame.
- `MAIN/Dock/Dock.module.json` registers the shared sheet; both changed web modules increment their versions to 1.0.3.
- `MAIN/VAL.App/UI/BezelChrome.cs` and `VALWindowTheme.xaml` provide the native counterpart, used by the Return pill, circular launcher, buttons, toggles, text inputs, tool-window frames and toast actions.
- `MAIN/VAL.App/ControlCentreOverlay.xaml`, `DiagnosticsWindow.xaml`, `UI/Truth/TruthHealthWindow.xaml` and `Host/Services/DesktopToastVisualFactory.cs` adopt the shared native construction while retaining real controls and behavior.
- `MAIN/VAL.Host/ModuleStylesheetScript.cs` and `ModuleLoaderService.cs` fix the discovered per-module stylesheet-ID collision. Every module/style path now has a stable distinct identity. Existing top-level origin and authentication-route guards remain in place.
- `MAIN/VAL.Tests/Hosting/NativeRecoveryStyleTests.cs`, `Modules/ModuleStylesheetScriptTests.cs` and `Build/Verify_ClientScripts.js` cover geometry, native rendering/states, style injection, scope and existing input behavior.
- `Build/Fixtures/VAL.Controls.html` uses production CSS for offline visual checks, including compact controls, anchored close controls, narrow panels and long labels. It is not part of the published application.
- `MAIN/VAL.App/Host/Services/SmokeTestRunner.cs` validates the actual native startup template. `ValidationBrowserObserver.cs` records content-free style counts and frozen token values only during isolated validation.
- `MAIN/Manifest.txt` includes the new source assets. Release-readiness and authenticated-workflow documentation identify this newer candidate separately from earlier installer evidence.

## Verification

The self-contained candidate is kept under `artifacts/review/ui-gunmetal`, separate from the installed VAL 5.0 and earlier lifecycle-test output. Release build, formatting verification, 355 C# tests, 67 client checks, source-manifest verification and six published process/storage/native-startup gates pass. The running isolated Control Centre was compared with the approved reference; all six modules load, and capture/logging remain Off.

Web and native fixtures cover normal, hover, pressed, selected, disabled and keyboard-focus appearance. Real keyboard focus receives a 2 px blue outline and skips disabled buttons. The production-CSS fixture passes at 420 px viewport width with 18 px text and at a 360 px panel width, without horizontal overflow or clipped button labels. The circular panel close control remains absolutely anchored inside its original panel. Actual app checks cover Control Centre opening/closing and Abyss query opening, Enter search and Escape dismissal, without sending a cloud message.

Evidence files are `tests/gunmetal-ui.trx`, `published-validation.json`, `native-states.png`, `control-centre.jpg`, `web-states.jpg` and `narrow-controls.jpg` beneath the validation output root. Fixtures are not evidence of a new authenticated provider round-trip, accessibility audit or DPI/multi-monitor certification. Earlier installer/upgrade/rollback reports apply to their recorded payload fingerprints, not this newer appearance candidate; rebuild and repeat those gates before release.

The final executable SHA-256 is `b125850dd0bab296d5b514dc1bd2c183416221809ebfc013661a63ba392e0d05`; complete payload digest is `8d771786f21b80e92e0718bccd9c26f23eafb06659e4108bc60ff8258907aee7`. The content-free running observer confirms two Dock stylesheets, a `4px` rim and face-top token `#152130`. Installed VAL 5.0 retains SHA-256 `61FC1B42359448A963DE1CB676723A9070615B140281D76E69B3882777868ED1`.

## Deliberate Adaptations

- Existing compact control dimensions and layouts are retained rather than adopting the preview's larger showcase sizing. Small padding/min-height adjustments leave room for the fixed rim; long labels wrap instead of clipping.
- The web face is a noninteractive pseudo-element instead of a new inner span. Native WPF uses a shared drawing decorator, not an HTML layer.
- WPF uses native gradient/shadow drawing with the approved colours and geometry. Its blur and antialiasing are not pixel-identical to Chromium; translucent values round to native 8-bit colour channels.
- Existing original VAL icon artwork, labels, fonts, commands, privacy permissions and disabled states remain. Destructive actions retain a pale-red label for meaningful warning semantics while sharing the approved rim/face.
- Native toast action rows wrap only when needed for narrow windows or long labels. No new runtime styling dependency is added.

No normal installation, commit, push, tag or publication is performed by this design pass.

## Newer Candidate

The [adaptive Control Centre candidate](AdaptiveControlCentre.md) retains this treatment, updates Dock to 1.0.4, adds direct move/resize controls and automatic header anchoring, and refreshes the installer and local lifecycle checks. Its separately recorded fingerprints and test counts supersede the older `ui-gunmetal` payload for release preparation without rewriting this historical evidence.
