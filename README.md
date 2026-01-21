# VAL (Virtual Assistant Layer)

## 🚀 Download

➡️ **[Download VAL (Latest Release)](https://github.com/Kryon411/VAL/releases/latest)**

VAL is a lightweight Windows desktop shell for running ChatGPT in a clean, controlled UI with **user-invoked, deterministic tools** layered on top.

VAL is designed around a simple philosophy:

- **Explicit**: features are activated by you (no background automation)
- **Deterministic**: outputs are reproducible and traceable
- **Transparent**: actions are visible, inspectable, and file-backed
- **Modular**: each module is self-contained and can evolve independently

---

## Download / Install

✅ Download the latest installer from the **Releases** page and run:

- `VAL_Setup.exe`

> Windows may warn that this is an unsigned installer (“Unknown publisher”). This is expected.

---

## Quick Start

1) Launch **VAL**
2) Open the **Control Centre** (the dock/pill UI)
3) Use modules as needed (each module is manual + user-invoked)

If something doesn’t look right after an update:
- Restart VAL
- Run the module action again
- Check the module’s output files (most modules write artifacts beside session data)

---

## Modules (Overview)

### Continuum (Continuity / Refresh)
Continuum is the continuity layer that helps you resume work cleanly across long sessions.

Typical actions:
- **Quick Refresh**: builds a compact “wake-up state” (Essence-M) from the most relevant recent Truth.log context
- **Deep Refresh**: uses a larger context window for tougher jumps

Outputs (per session):
- `Truth.log`
- `Seed.log`
- `RestructuredSeed.log`
- `Essence-M.Pulse.txt`

---

### Abyss (Recall / Search)
Abyss is VAL’s local recall and search module.

It searches across your archived session logs and returns **ranked results** with **provenance**, so you can rehydrate context without manually digging through folders.

Typical actions:
- **Search**: enter a recall question and retrieve ranked results
- **Inject**: inject a selected snippet into the composer
- **Open Source**: open the source session folder / log
- **Disregard**: dismiss a specific snippet result and try again

---

### Portal (Capture / Stage)
Portal is the capture workflow module (built to reduce friction when collecting screenshots or supporting material during a working session).

Typical actions:
- Capture → stage → send when ready

---

### Void (Clean View)
Void helps keep the UI fast and readable by suppressing high-cost content.

Typical actions:
- Hide or reduce heavy blocks (e.g. large pasted code or screenshots)

---

## Documentation

For “how-to” steps and module-specific guides, see:

- **Docs/Modules.md**

---

## Project Goals

VAL aims to be:
- A clean work environment for long AI-assisted sessions
- A modular foundation you can extend safely over time
- A tool that improves continuity without inventing “magic memory”

---

## License / Notes

This project is evolving quickly. Expect small behavioral changes between versions as modules are refined.
