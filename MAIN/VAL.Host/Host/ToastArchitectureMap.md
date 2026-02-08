# VAL — Toast Architecture Map (current)

This is the **single reference** for how VAL toasts are defined, gated, and shown.

Last updated: 2026-01-03

---

## 1) High-level flow

1. **A module decides a toast should be shown** (e.g., ContinuumHost, Telemetry monitor, Void toggle).
2. The module calls **`ToastHub.TryShow(...)`** (or `TryShowActions(...)` for button toasts).
3. **ToastHub applies policy** (launch-quiet suppression, cooldown, once-per-chat ledger, grouping/replace).
4. ToastHub delegates rendering to **`ToastManager`** (WPF popup/stack renderer).

**Rule:** *No module should call `ToastManager` directly.* `ToastManager` is renderer-only; `ToastHub` owns policy.

---

## 2) Core files

### `MAIN/VAL.Host/Host/ToastManager.cs`
**Role:** WPF toast renderer (Popup + StackPanel).  
**Owns:** how toasts *look* and *dismiss*.  
**Does not own:** toast identity/gating/business logic.

Key renderer behaviors you’ll see referenced elsewhere:
- **Burst de-dupe** (short window) to prevent identical spam.
- **Grouping / replace-group** to replace an earlier toast in the same group.
- **Duration buckets** (S/M/L/Sticky) used by definitions.

### `MAIN/VAL.Host/Host/ToastKey.cs`
**Role:** The canonical list of toast keys.

✅ Note: **`ToastKey.ContinuumArchivingEnabled` was removed** (toast fully deleted).

### `MAIN/VAL.Host/Host/ToastHub.cs`
**Role:** The **only entry point** for showing toasts.

Responsibilities (typical):
- Resolve `ToastKey → ToastDef` (from toast definitions).
- Apply:
  - Launch-quiet suppression (for passive nudges)
  - Cooldowns
  - Once-per-chat gating via `ToastLedger`
  - Grouping / replace-group
- Call `ToastManager` to render the final toast.

### `MAIN/VAL.Host/Host/ToastArchitectureMap.md`
**Role:** This document (the canonical wiring map).

---

## 3) Persistence: Once-per-chat gating

### `MAIN/Modules/Continuum/Pipeline/00_Common/ToastLedger.cs`
**Role:** Persist “shown” markers per chat so certain toasts only appear once per chat across restarts.

Typical storage:
- `<ChatDir>/ToastLedger.json` (or equivalent chat folder location)

Used by:
- ToastHub policy (for `OncePerChat` toasts)
- Telemetry threshold nudges

---

## 4) Primary toast producers

### A) Continuum (host-side)
**`MAIN/Modules/Continuum/Pipeline/00_Common/ContinuumHost.cs`**

ContinuumHost emits toasts for:
- **Prelude guidance** (new-chat coaching toast with actions)
- **Chronicle prompts** (action toast suggesting Chronicle when no truth exists)
- **Chronicle lifecycle** (started/sticky, completed/replaced)
- **Pulse lifecycle** (initiated / ready / unavailable / already-running)
- **Archiving lifecycle**
  - ✅ **Paused** toast remains (when logging is toggled off)
  - ❌ **Enabled** toast removed (no longer exists anywhere)

ContinuumHost should call **ToastHub** keys (not ToastManager).

### B) Telemetry (truth size nudges)
**`MAIN/Modules/Continuum/Pipeline/06_Telemetry/Telemetry.ThresholdMonitor.cs`**

Emits “your conversation is getting large” nudges based on truth bytes/thresholds, typically:
- Soft / Medium / Critical levels
- Once-per-chat gating via ToastLedger
- Suppressed during launch-quiet

### C) Void (toggle on/off)
**`MAIN/MainWindow.xaml.cs`** (or Void host handler)
- Receives `void.command.set_enabled` from the Dock
- Emits “Void is now on/off” via ToastHub (usually once-per-state-change in that session)

---

## 5) Groups (replace behavior)

Common group keys used across the system (names may vary, but the pattern is stable):

- `pulse` — Pulse lifecycle toasts (replace within group)
- `chronicle` — Chronicle sticky + completion (replace within group)
- `continuum_guidance` — Prelude/Chronicle suggestion prompts (replace within group)
- `op.guard` — operation guard “busy/cancelled” toasts

If a toast “updates” another (e.g., Chronicle started → Chronicle completed), it should share a group key and use replace-group semantics.

---

## 6) Client-side emitters (Web → Host)

### `MAIN/Dock/Dock.main.js`
**Role:** UI control centre → sends commands to host.

Relevant web-messages:
- `void.command.set_enabled`
- `continuum.command.*` (Pulse, Chronicle rebuild, toggle logging, etc.)
- `continuum.ui.*` (new chat/composer interactions; guidance triggers)

### `MAIN/Modules/Continuum/Client/Continuum.Client.js`
**Role:** Watches ChatGPT DOM and emits key interaction events.

Toast-relevant events:
- **New chat composer interaction** → triggers Prelude guidance prompt
- **Existing chat composer interaction** → may trigger Chronicle suggestion prompt (if missing truth log)

The host decides whether to show the toast (policy stays host-side).

---

## 7) Quick “where do I change X?”

- **Toast text / duration / grouping / once-per-chat:** `ToastHub` (and its toast definitions)
- **Toast key list:** `ToastKey.cs`
- **Toast visuals / positioning:** `ToastManager.cs`
- **Continuum triggers:** `ContinuumHost.cs`
- **Telemetry thresholds:** `Telemetry.ThresholdMonitor.cs`
- **Once-per-chat persistence:** `ToastLedger.cs`

---

## 8) Recently removed

- **Continuum Archiving Enabled** toast:
  - Key removed: `ToastKey.ContinuumArchivingEnabled`
  - All emission/suppression/gating/lifecycle logic removed
  - No replacement behavior introduced
