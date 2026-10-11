# Privacy And Local Data

VAL stores application data under `%LOCALAPPDATA%\VAL` by default. This can include conversation archives, generated continuity files, module state, preferences, WebView profile data, diagnostics, logs, and user-requested captures.

VAL's in-process "telemetry" is a local session-size monitor used to show maintenance guidance. The current application does not include a VAL-owned analytics or crash-upload endpoint.

The embedded ChatGPT website communicates through WebView2 and remains subject to the website provider's own account, network, and privacy behavior. Content the user sends or injects into the page leaves VAL's local storage boundary through that website.

Privacy controls can pause Continuum logging, disable Portal capture, open the local data directory, and request deletion of VAL-managed data folders. A wipe requires native confirmation, refuses active Pulse/Chronicle operations, atomically disables both capture settings, persists a cleanup request, and closes VAL. It refuses broad targets, program/state folders, external configured folders, and junction/symbolic-link ancestors or descendants.

On the next launch, cleanup runs in a fresh process before background capture, private-text caches, logging sinks, or WebView2 start. It waits for the recorded browser process to exit (checking its creation time against PID reuse), deletes and verifies the managed folders, and removes the request only after success. A locked, changed, malformed, or unsafe target keeps the request for retry and blocks runtime startup. A completed cleanup produces a completion notice. Preferences, window geometry, configuration, and startup state are retained; capture remains disabled.

This is verified logical deletion of the managed folders, not forensic secure erasure. Windows clipboard contents, file-system snapshots/backups, externally configured folders, and content already submitted to ChatGPT are outside its scope. The browser profile is deleted, so a subsequent launch may require signing in again. Check custom data paths before scheduling cleanup; changing those paths while cleanup is pending will make the request fail closed.

On first run, missing privacy settings retain the existing capture-enabled defaults. Empty, malformed, incomplete, or unsupported settings fail closed. A failed settings write leaves the previous setting unchanged and produces a native error message. First-run consent and retention policy are product decisions still required before commercial release.

Archives are plain-text local files, protected by the user's Windows account permissions rather than VAL-owned encryption. Crash reports and diagnostics may contain paths or exception details. Before commercial release, these behaviors require privacy/legal review, retention documentation, and clean-install/upgrade/uninstall verification.
