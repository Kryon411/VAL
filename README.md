# VAL

VAL is a locally run UI overlay for ChatGPT.com, built using Microsoft’s WebView2 (Edge/Chromium) platform, designed to improve how users work with AI assistants.

Rather than automating behavior, VAL provides a stable, user-controlled environment that hosts explicit modules for tasks such as continuity, organization, and context management. All actions are user-invoked, and all state remains local.

## What VAL is
- A local, per-user Windows application that runs alongside ChatGPT.com
- A controlled UI environment for hosting user-invoked modules
- A tool focused on continuity, context stability, and explicit user control

## What VAL is not
- No telemetry
- No cloud backend
- No background automation
- No data collection beyond what you explicitly initiate

## Installation
Download the installer from the **Releases** section and run it.

VAL installs per-user to `%LOCALAPPDATA%\VAL`.

No administrator privileges are required unless you explicitly choose an all-users install.

> **Note:** VAL requires the Microsoft Edge WebView2 Runtime. On most up-to-date Windows systems this is already installed. If not, Windows will prompt to install it automatically.

> **Note:** VAL is currently unsigned. Windows may display an “Unknown publisher” warning on first run.

## Uninstall
VAL can be removed normally via **Windows Settings → Apps → Installed apps**.

## Project status
VAL v4.0 is the first stable public release.

This project is intentionally focused and opinionated. Feedback is welcome, but changes are made selectively to preserve the overall design philosophy.

## Support
If you find VAL useful and would like to support its continued development, you can leave an optional tip here:

https://ko-fi.com/valv4
