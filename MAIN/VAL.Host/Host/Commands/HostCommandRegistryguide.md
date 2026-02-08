# Host Command Registry (WebView → Host)

This document explains how VAL routes **WebView (JS modules)** messages into the **Host (C#)**, and how to safely add new commands.

## Goals

- **One entry-point** for all WebView → Host messages (no scattered `if (type == ...)` in `MainWindow`).
- A **single discoverable list** of supported commands (the registry).
- A **schema‑lite** approach: validate only what protects correctness; keep the host resilient.
- A **single authoritative session context** (chatId tracking lives in one place).

---

## Architecture at a glance

**Flow**

1. WebView module posts JSON: `window.chrome.webview.postMessage(...)`
2. Host receives it via WebView2 `WebMessageReceived`
3. `HostCommandRouter` parses the envelope and updates `SessionContext`
4. `CommandRegistry` selects a handler
5. Handler performs the action (and may call `ToastHub`)

**Core files (Host)**

- `Host/Commands/HostCommandRouter.cs`
  - Single entry-point: parses JSON, extracts `type` and optional `chatId`, updates `SessionContext`, calls registry.

- `Host/Commands/CommandRegistry.cs`
  - The **single catalog** of command types and their handlers.
  - Provides **forward‑compat** routing for unknown `continuum.*` commands.

- `Host/Commands/CommandSpec.cs`
  - A small record describing a command:
    - `Type`, `Module`, `RequiredFields`, `Handler`

- `Host/Commands/HostCommand.cs`
  - A lightweight wrapper around the inbound message.
  - Contains helpers like `TryGetBool`, `TryGetString`, `TryGetInt`.
  - ⚠️ **Important:** `Root` (`JsonElement`) is only valid during dispatch. Do not store it.

- `Host/SessionContext.cs`
  - The single authoritative place for “what chatId are we in”.

Related (often used by handlers):

- `Host/ToastHub.cs` + `Host/ToastKey.cs`
  - Centralized toast routing/policy (use this instead of calling `ToastManager` from random places).

---

## WebView message envelope

Every message must be JSON with:

- **Required**
  - `type` (string)

- **Optional (recommended)**
  - `chatId` (string)

Example:

```json
{
  "type": "void.command.set_enabled",
  "chatId": "c_abc123",
  "enabled": true
}
```

### chatId guidance

- If a module knows the active chatId, include it.
- If it does not, omit it; the host will fall back to `SessionContext.ActiveChatId` when appropriate.
- Use `SessionContext.IsValidChatId(...)` if a handler requires a real conversation id.

---

## Registry philosophy (schema‑lite)

`CommandRegistry` supports a **RequiredFields** list for each command.

This is intentionally minimal:
- It prevents “silent no-ops” due to missing fields.
- It does **not** enforce types or complex structure.
- Handlers still do their own defensive parsing.

If you need stronger validation, do it inside the handler using `HostCommand.TryGet...`.

---

## Adding a new command (step-by-step)

### Step 1 — Define the command type string

Pick a consistent naming pattern:

- `module.command.action`
- `module.event.something`
- `module.ui.something`

Examples:
- `void.command.set_enabled`
- `continuum.command.pulse`
- `continuum.ui.prelude_prompt`

### Step 2 — Create (or choose) a handler class

Create a new handler class under:

`Host/Commands/<YourModule>CommandHandlers.cs`

Example skeleton:

```csharp
namespace VAL.Host.Commands
{
    internal static class FooCommandHandlers
    {
        public static void HandleDoThing(HostCommand cmd)
        {
            // Defensive parsing
            if (!cmd.TryGetString("mode", out var mode))
                return;

            // Optional: session-aware behavior
            var chatId = SessionContext.ResolveChatId(cmd.ChatId);
            if (!SessionContext.IsValidChatId(chatId))
                return;

            // Do the work...

            // Optional: toast feedback
            // ToastHub.TryShow(ToastKey.ActionUnavailable);
        }
    }
}
```

### Step 3 — Register it in `CommandRegistry`

Add a `Register(new CommandSpec(...))` entry, usually in the static constructor:

```csharp
Register(new CommandSpec(
    "foo.command.do_thing",
    "Foo",
    new[] { "mode" },               // RequiredFields (presence only)
    FooCommandHandlers.HandleDoThing
));
```

### Step 4 — Emit the message from WebView JS

Example:

```js
window.chrome.webview.postMessage(JSON.stringify({
  type: "foo.command.do_thing",
  chatId,
  mode: "safe"
}));
```

### Step 5 — Smoke test

- Run VAL
- Trigger the UI action
- Confirm the host responds without exceptions
- Confirm the command does nothing (safely) if fields are missing or malformed

---

## Forward compatibility rules

`CommandRegistry` deliberately does two tiers of routing:

1. **Exact match**
   - Uses the catalog for known commands.

2. **Prefix fallback for Continuum**
   - Unknown `continuum.*` commands are forwarded to `ContinuumHost` (which remains defensive).
   - This reduces breakage when Continuum evolves faster than the registry.

If you add a new *non‑Continuum* module prefix and want the same behavior, implement a similar prefix fallback intentionally (don’t do it automatically for everything).

---

## Handler guidelines (keep the host resilient)

- Handlers should be **idempotent** where possible (safe to receive duplicates).
- Handlers should **never throw**; swallow/return on bad data.
- Do not store `cmd.Root` for later use.
- Prefer using `TryGetBool`, `TryGetString`, `TryGetInt` helpers.
- If a handler triggers a toast, call `ToastHub` (not `ToastManager`) so policy remains centralized.

---

## Troubleshooting

### “Nothing happens”
- Confirm the message JSON includes a valid `type` string.
- Confirm the `type` is registered in `CommandRegistry` (unless it’s `continuum.*` and you expect fallback).
- Add a temporary toast in the handler via `ToastHub.TryShow(...)` to confirm execution path.

### “Sometimes it works, sometimes it doesn’t”
- If the command depends on chatId:
  - Ensure WebView includes `chatId`, or
  - Use `SessionContext.ResolveChatId(...)` and verify with `IsValidChatId(...)`.

---

## Recommended place to add this doc

Place this file at:

`MAIN/VAL.Host/Host/Commands/README.md`

so the documentation lives next to the command system it describes.
