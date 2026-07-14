# Threat Model

## Protected Assets

- Conversation archives and generated continuity artifacts.
- WebView profile data and authenticated browser session state.
- User preferences, module state, screenshots, diagnostics, and logs.
- Integrity of host commands sent between JavaScript modules and the desktop process.

## Trust Boundaries

1. Remote web content runs inside WebView2 and is untrusted until origin and command validation succeed.
2. JavaScript modules are local executable content and must be protected as application files.
3. The desktop process can access the user's VAL data directory and therefore runs with the user's trust.
4. Files selected for injection or opening cross from local storage into user-visible browser actions.
5. Build dependencies and release artifacts cross the software supply-chain boundary.

## Primary Threats And Mitigations

- **Forged WebView commands:** origin allowlists, session nonce support, canonical command registration, and schema-lite parsing constrain dispatch.
- **Path traversal:** safe path resolution and chat-directory ownership constrain file access.
- **Partial or concurrent archive writes:** atomic replacement, repair logic, operation coordination, and a single desktop instance protect persistence.
- **Unobserved background failures:** supervised background tasks observe exceptions, propagate cancellation, and drain during shutdown.
- **Malicious module replacement:** manifests constrain declared assets; release signing and installer integrity are required before public distribution.
- **Sensitive log disclosure:** log sanitization and local storage reduce exposure; support collection must remain opt-in.
- **Dependency compromise:** central versions, lock files, Dependabot, CI audit, and checksummed artifacts reduce supply-chain drift.

## Residual Risks

- The ChatGPT page and WebView2 runtime are external dependencies outside VAL's control.
- An attacker already running as the same Windows user can generally read or alter local VAL data and module files.
- Unsigned ZIP distribution cannot establish publisher identity or protect installation integrity.
- Local archives may contain sensitive conversation content; disk encryption and Windows account security remain user/environment responsibilities.
- A formal penetration test and installer/update threat review are still required for commercial release.
