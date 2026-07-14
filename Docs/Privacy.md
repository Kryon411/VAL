# Privacy And Local Data

VAL stores application data under `%LOCALAPPDATA%\VAL` by default. This can include conversation archives, generated continuity files, module state, preferences, WebView profile data, diagnostics, logs, and user-requested captures.

VAL's in-process "telemetry" is a local session-size monitor used to show maintenance guidance. The current application does not include a VAL-owned analytics or crash-upload endpoint.

The embedded ChatGPT website communicates through WebView2 and remains subject to the website provider's own account, network, and privacy behavior. Content the user sends or injects into the page leaves VAL's local storage boundary through that website.

Privacy controls can pause Continuum logging, disable Portal capture, open the local data directory, and wipe VAL-managed data. Before commercial release, these behaviors require legal review, retention documentation, and clean-install/upgrade/uninstall verification.
