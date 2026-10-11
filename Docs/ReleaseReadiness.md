# Release Readiness

On 11 October 2026 the owner approved publishing v5.1.0 for user testing. The [release notes](Releases/v5.1.0.md) disclose unsigned artifacts and outstanding commercial-distribution gates. Publication approval does not turn those incomplete gates into passes; final packaging is rebuilt from the committed release revision.

## Automated Gates

The current [native keyboard/readability candidate](Validation/NativeKeyboardAccess.md) retains the [approved control design](Validation/ApprovedControlDesign.md) and [adaptive placement](Validation/AdaptiveControlCentre.md), adds app-local F6 access to native controls, improves Control Centre text, and corrects compact-header anchoring. It passes 389 .NET tests, 82 client checks and 20 published/isolated lifecycle gates. Final live observation verified native icon focus and panel focus transfer; the owner confirmed F6/Enter and F6/Space opening with Escape dismissal. On Google sign-in, owner-confirmed Return-pill F6 focus, Tab/Escape focus return without navigation, and Enter/Space activation pass. Alt+F4 from the focused native icon closed the isolated app; the provider pill's close path remains pending. Reports and the refreshed installer are bound to the final payload; earlier geometry/traversal and authenticated feature evidence stays separately identified. Rollback is explicitly candidate uninstall with data retention followed by baseline reinstall; an in-place downgrade was found to leave newer assets behind. Supported-machine and remaining authenticated checks are separate gates.

- Locked NuGet dependency restore with centralized versions.
- Recommended .NET analyzers and code-style enforcement with warnings as errors.
- Unit, real-storage/runtime and strict production-composition integration, architecture, module asset, and JavaScript command tests, plus client syntax and focused behavior checks.
- Shared host/document authentication-route guards with production-template fixtures for module/nonce exclusion on providers, guarded routes and child frames.
- Native authentication-page recovery, separate from the web Dock, with a standalone Return pill, fixed-home navigation, busy-operation refusal and bridge-gated launcher visibility.
- Fresh-process native-theme validation against the actual main window and its transparent, pill-only owned recovery window, including resolved typography/template and CI mode.
- Fail-on-findings dependency vulnerability audit across all solution projects and transitive packages.
- Clean self-contained `win-x64` publish with required-content verification.
- Versioned per-user Inno Setup installer with stable upgrade identity and data-retaining uninstall behavior.
- Isolated current-version installer path-rejection, payload-hash, startup, repair, and uninstall/data-retention checks.
- Exact prior-release archive compatibility and isolated payload upgrade/rollback matrix, with explicit scoped evidence.
- Guarded original-production-installer machine kit and isolated manual authenticated-workflow instructions.
- Published executable smoke and fresh-process deferred-cleanup tests, including locked-file and invalid-request failure gates.
- SHA-256 checksum and versioned ZIP release artifact with separate support symbols.
- Complete payload fingerprints bind installer builds, lifecycle reports and machine kits to executable, asset and filename changes.
- Optional Authenticode signing through release environment variables.

## Required Before Public Commercial Release

- Run the expanded lifecycle and published-cleanup gates for the exact release revision; the October 2026 wipe, Pulse, and overlapping-search findings have been corrected locally.
- Complete [authenticated desktop workflow validation](Validation/AuthenticatedWorkflows.md); the current test collection is not end-to-end validation of ChatGPT interactions. Sign-in and any privacy-setting changes remain manual.
- Keep live and fixture evidence distinct: isolated sign-out/wipe and Google-provider bridge disarming passed, but same-origin authentication-route traversal and the remaining feature failure paths still need live validation.
- Native Return to ChatGPT passed owner live checks on the identified standalone-pill candidate. The first SoftGlass candidate's startup/theme failure was corrected through deferred window creation and fresh-process checks. The current native keyboard candidate adds F6 routing without provider-page scripts; Control Centre Enter/Space activation, provider Return F6, Tab/Escape and Enter/Space checks, and idle icon Alt+F4 owner shutdown are owner-confirmed. The provider pill's Alt+F4 path remains pending. DPI, multi-monitor, screen-reader and normal-launch checks remain separate gates.

- Obtain the appropriate Inno Setup license before any commercial distribution.
- Obtain and protect an Authenticode certificate; sign and timestamp both the application and installer.
- Complete privacy, data retention, security threat-model, and legal/license review.
- Select and publish the intended software license and third-party notices; no license is inferred by this repository.
- Establish supported Windows/WebView2 versions and test clean install, prior-version upgrade, repair, rollback, and uninstall on that matrix.
- Run the [production-installer kit](Validation/DisposableMachine.md) on disposable machines. Local repackaged payload tests do not validate original installer behavior or legacy desktop runtime rollback.
- Add crash/diagnostic consent policy and a support process without collecting conversation content by default.
- Define semantic versioning, release notes, support lifetime, and rollback ownership.
- Run accessibility, localization-readiness, performance, long-session, and recovery testing.

The repository can produce a checksummed installer release candidate. An unsigned installer is suitable for controlled testing but is not equivalent to a fully trusted commercial distribution.
