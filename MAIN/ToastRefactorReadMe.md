# VAL — Toast Architecture Full Refactor (ToastHub)

This snapshot implements a **centralized toast policy + routing layer** so toast behavior (grouping, cooldowns, once-per-chat gating, launch-quiet suppression) lives in one place.

## What changed

### ✅ New central files
- `Host/ToastKey.cs`  
  Typed toast keys (no more scattered “string message” identity).
- `Host/ToastDefinition.cs`  
  Declarative defaults (title/subtitle, duration bucket, grouping, gating flags).
- `Host/ToastHub.cs`  
  The **single entry point** for showing toasts. Applies policy + gates, then delegates rendering to `Host/ToastManager.cs`.

### ✅ Updated call sites (no direct ToastManager usage)
- `MainWindow.xaml.cs`
  - Uses `ToastHub.Initialize(this)` instead of `ToastManager.Initialize(this)`
  - Void on/off toasts go through `ToastHub`

- `Modules/Continuum/Pipeline/00_Common/ContinuumHost.cs`
  - All Continuum toasts now route through `ToastHub`
  - Chronicle prompt toast gating moved into `ToastHub` (cooldown + once-per-chat)
  - Operation guard toasts (`OperationInProgress`, `OperationCancelled`) centralized + cooldowned

- `Modules/Continuum/Pipeline/06_Telemetry/Telemetry.ThresholdMonitor.cs`
  - Telemetry nudges now use `ToastHub` keys (ToastLedger-backed)

### ✅ Bonus stability fix
- `Modules/Continuum/Client/Continuum.Client.js`
  - Navigation watcher now re-attaches only when **chatId OR pathname changes**.
  - Query/hash churn (model pickers, UI panels) no longer triggers reattach loops.

## The new rule
**No module calls `ToastManager` directly**. Everything goes through:

- `ToastHub.TryShow(...)` (catalog toasts)
- `ToastHub.TryShowActions(...)` (action toasts)

`ToastManager` remains the renderer.

## Toast policy highlights (in ToastHub)
- **Launch quiet period** suppresses passive nudges.
- **Cooldowns** prevent rapid repeats.
- **Once-per-chat** toasts use `ToastLedger` so they don’t reappear across restarts.
- Grouping/replace behavior is centralized (pulse group, chronicle group, continuum guidance).

## Keys you’ll care about first
- `ToastKey.ChroniclePrompt`  
  Once-per-chat + 45s cooldown.

## If you apply this to your real repo
You uploaded `.txt`-suffixed files earlier; this build has **clean extensions restored**.

**Recommended integration path:**
1. Copy these new files into your repo:
   - `Host/ToastKey.cs`
   - `Host/ToastDefinition.cs`
   - `Host/ToastHub.cs`
2. Apply the edits shown in:
   - `MainWindow.xaml.cs`
   - `ContinuumHost.cs`
   - `Telemetry.ThresholdMonitor.cs`
   - `Continuum.Client.js`
3. Build/run and test:
   - No repeated Continuum toast spam on settings clicks
   - Chronicle prompt appears at most once per chat
   - Pulse toasts replace within the pulse group cleanly
