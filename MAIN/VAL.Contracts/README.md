# Web contracts

## Commands vs. events

- **Commands**: Action requests sent to the host (or a module) that should trigger a handler. These are explicit, allow-listed, and registered in host command registries.
- **Events**: Notifications about something that already happened. Consumers may subscribe, but unhandled events are safe to ignore.
- **Logs**: Diagnostic-only messages intended for debugging/telemetry.

`WebCommandNames` centralizes command name constants. `WebMessageTypes` centralizes envelope `type` values.
