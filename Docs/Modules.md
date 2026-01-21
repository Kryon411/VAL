# VAL — Module Guide (How To Use)

This document explains how to use VAL’s modules in a practical, user-friendly way.

The goal of VAL is to keep everything:
- **manual**
- **transparent**
- **file-backed**
- **deterministic**

So you always know what happened and where to look.

---

## Control Centre Basics

The Control Centre is where modules live.

General rules:
- Modules are **user-invoked** (no background automation)
- If a module isn’t present or fails to load, VAL should **skip it gracefully**
- Most module actions create an artifact (text file, log, snapshot) you can inspect

---

## Continuum (Continuity / Refresh)

### What it is
Continuum helps you resume a working thread without losing your place.

It works by:
1) reading a canonical session transcript (`Truth.log`)
2) building a compact “wake-up state” file (`Essence-M.Pulse.txt`)
3) injecting that into the composer (so a new chat can continue correctly)

### When to use it
- Your chat is getting large / slow
- You need to switch chats but keep exact context
- You want a reliable “handoff packet” for a new thread

### Quick Refresh vs Deep Refresh
**Quick Refresh**
- Faster
- Uses a smaller working set
- Best for normal jumps

**Deep Refresh**
- Larger context window
- Better for complex work or when results feel too thin

### What files it produces (per session)
- `Truth.log` (canonical transcript)
- `Seed.log` (filtered excerpt set)
- `RestructuredSeed.log` (budget-packed ordering)
- `Essence-M.Pulse.txt` (final injection payload)

---

## Abyss (Recall / Search)

### What it is
Abyss is local recall and search across archived sessions.

It returns **ranked results** with provenance so you can retrieve the *right* snippet and inject it into the composer.

Abyss is intentionally **not** AI memory.
It’s deterministic retrieval.

### Basic workflow
1) Click **Abyss Search**
2) Type your recall question (example: “Where did we fix ProseMirror injection formatting?”)
3) Review the ranked result cards
4) Click:
   - **Inject** (to paste into the composer)
   - **Open Source** (to view the origin)
   - **Disregard** (if the snippet isn’t useful)

### Tips for better results
- Use specific phrases you remember seeing
- Include filenames, module names, or version tags
- Try shorter queries first, then refine

### “Disregard” vs “Exclude Chat”
- **Disregard**: removes a *snippet result* (so you don’t keep seeing the same useless excerpt)
- Excluding entire chats is usually not ideal because the correct answer might still be inside that session

---

## Portal (Capture / Stage)

### What it is
Portal is designed to reduce friction when you need to bring screenshots or references into a session.

### When to use it
- UI debugging
- sharing configs or visuals
- capturing multi-step states without dragging files manually

---

## Void (Clean View)

### What it is
Void keeps VAL responsive by removing or collapsing heavy UI content.

### When to use it
- Large pasted code blocks
- Large pasted screenshots
- Anything that slows scrolling or typing

---

## Where to look when something “doesn’t work”

Start here:
1) Restart VAL
2) Re-run the module action
3) Check DevTools Console for errors (if applicable)
4) Look in the session output folder for logs/artifacts

VAL is designed so that outputs are visible and inspectable.
When something fails, you should still be able to see what happened and where it stopped.
