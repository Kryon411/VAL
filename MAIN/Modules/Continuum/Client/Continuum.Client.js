try { console.log("[VAL Continuum] script loaded"); } catch (_) {}
/* Continuum.Client.js — v3.1.31 (fast seed inject on New Chat root)
 * Fix for 20s+ delays:
 *  - On openNewChat, ChatGPT often lands on "/" (no /c/<uuid> yet). Our previous injector waited for a chatId change,
 *    which cannot happen until after the first send. That forced long fallback timeouts.
 *
 * New behavior:
 *  - For openNewChat seeds, inject as soon as the composer is ready on:
 *      (a) a different chatId than origin, OR
 *      (b) the New Chat root page (no chatId, typically "/")
 *  - Truth logging remains chatId-gated (no Truth lines on root).
 *  - Emits: refresh.inject.fast_root when we inject on root without chatId.
 */
try {
(function () {
  "use strict";

  const CONTINUUM_VERSION = "3.1.32";

  function hasBridge() {
    return !!(window.chrome && window.chrome.webview && typeof window.chrome.webview.postMessage === "function");
  }

  function getNonce() {
    try { return window.__VAL_NONCE || null; } catch (_) { return null; }
  }

  function withNonce(envelope) {
    if (!envelope || typeof envelope !== "object") return envelope;
    if (envelope.nonce) return envelope;
    const nonce = getNonce();
    if (!nonce) return envelope;
    return { ...envelope, nonce };
  }

  function toEnvelope(message) {
    if (!message || typeof message !== "object") return message;
    const type = (message.type || "").toString();
    if (!type) return message;
    if (type === "command" || type === "event" || type === "log") return withNonce(message);
    return withNonce({
      type: "command",
      name: type,
      payload: message,
      chatId: message.chatId,
      source: "continuum"
    });
  }

  function unwrapEnvelope(message) {
    if (!message || typeof message !== "object") return message;
    if ((message.type === "command" || message.type === "event" || message.type === "log") && message.name) {
      const payload = (message.payload && typeof message.payload === "object" && !Array.isArray(message.payload))
        ? { ...message.payload }
        : {};
      if (!payload.chatId && message.chatId) payload.chatId = message.chatId;
      payload.type = message.name;
      return payload;
    }
    return message;
  }

  function post(message) {
    try { if (hasBridge()) window.chrome.webview.postMessage(toEnvelope(message)); } catch (_) { }
  }

  const state = {
    chatId: null,
    attachedChatId: null,
    lastHref: null,
    hostAttachedChatId: null,
    lastAttachPostAt: 0,
    attachWatchdogTimer: null,
    attachWatchdogEndsAt: 0,

    turnCounter: 0,
    seenTruthFp: new Set(),
    seenMsgEvent: new Set(),
    lastMutationAtById: new Map(),

    pendingSeed: null,
    injectorTimer: null,
    injectorLastStatusAt: 0,

    // Low-friction Truth capture: run a lightweight periodic scan so backfill
    // doesn't depend on focus/navigation quirks.
    pollTimer: null,

    // Chronicle: user-invoked Truth.log rebuild (recovery tool for deleted logs)
    chronicle: {
      active: false,
      requestId: null,
      freezeWrites: false,
      startedAt: 0,
      phase: "",
      cancelRequested: false,
      overlay: null,
      barFill: null,
      labelEl: null,
      lastProgressPostAt: 0,
      lastProgressPct: -1
    },
  };

  function getChatIdFromLocation() {
    try {
      const m = location.pathname.match(/\/c\/([a-f0-9\-]{36})/i);
      return m ? m[1] : null;
    } catch (_) { return null; }
  }

  function ensureAttachedIfReady() {
    const id = getChatIdFromLocation();
    state.chatId = id;

    if (!id) return false;

    const changedChat = state.attachedChatId !== id;

    if (changedChat) {
      state.attachedChatId = id;
      state.hostAttachedChatId = null;

      state.turnCounter = 0;
      state.seenTruthFp = new Set();
      state.seenMsgEvent = new Set();
      state.lastMutationAtById = new Map();
    }

    // If the host hasn't acked this chat attach yet, we may need to re-post session.attach.
    if (state.hostAttachedChatId !== id) {
      const now = Date.now();
      const minGapMs = 180;

      if (changedChat || (now - (state.lastAttachPostAt || 0)) >= minGapMs) {
        state.lastAttachPostAt = now;
        post({ type: "continuum.session.attach", chatId: id });

        // Only emit a boot event on a fresh chat switch (not on watchdog re-pings).
        if (changedChat) {
          post({ type: "continuum.event", chatId: id, evt: "client.boot:" + CONTINUUM_VERSION });
          try { console.log("[VAL Continuum] init", CONTINUUM_VERSION, "chatId=", id, "href=", location.href); } catch (_) {}
        }
      }
    }

    return true;
  }

  
  // Attach watchdog:
  // Some startup / navigation races can drop the initial session.attach message.
  // We run a short re-ping window on /c/<id> routes until the host acks.
  const ATTACH_WATCHDOG_WINDOW_MS = 2800;
  const ATTACH_WATCHDOG_INTERVAL_MS = 140;

  function stopAttachWatchdog() {
    try {
      if (state.attachWatchdogTimer) {
        clearInterval(state.attachWatchdogTimer);
        state.attachWatchdogTimer = null;
      }
      state.attachWatchdogEndsAt = 0;
    } catch (_) {}
  }

  function startAttachWatchdog() {
    try {
      const path = (location && location.pathname) ? location.pathname : "";
      if (!/\/c\//i.test(path)) return false;

      const id = getChatIdFromLocation();
      if (!id) return false;

      if (state.hostAttachedChatId === id) return true;

      // Reset any previous watchdog for the prior route.
      stopAttachWatchdog();

      state.attachWatchdogEndsAt = Date.now() + ATTACH_WATCHDOG_WINDOW_MS;

      // Prime an immediate attach attempt.
      ensureAttachedIfReady();

      state.attachWatchdogTimer = setInterval(() => {
        try {
          const now = Date.now();
          const currentPath = (location && location.pathname) ? location.pathname : "";
          const currentId = getChatIdFromLocation();

          if (!/\/c\//i.test(currentPath) || !currentId) { stopAttachWatchdog(); return; }
          if (currentId !== id) { stopAttachWatchdog(); return; }
          if (state.hostAttachedChatId === currentId) { stopAttachWatchdog(); return; }
          if (state.attachWatchdogEndsAt && now >= state.attachWatchdogEndsAt) { stopAttachWatchdog(); return; }

          ensureAttachedIfReady();
        } catch (_) {}
      }, ATTACH_WATCHDOG_INTERVAL_MS);

      return true;
    } catch (_) { return false; }
  }

  // Send-safety:
  // If the user sends a message in /c/<id> before the host has acked attach,
  // attempt an immediate attach before the send proceeds.
  function sendSafetyEnsureAttach() {
    try {
      const path = (location && location.pathname) ? location.pathname : "";
      if (!/\/c\//i.test(path)) return;

      const id = getChatIdFromLocation();
      if (!id) return;

      if (state.hostAttachedChatId === id) return;

      ensureAttachedIfReady();
      try { setTimeout(() => { try { ensureAttachedIfReady(); } catch (_) {} }, 0); } catch (_) {}

      // Keep a short watchdog window alive so attach completes deterministically.
      startAttachWatchdog();
    } catch (_) {}
  }

  function startSendSafetyHooks() {
    try {
      // Key-based send: Enter in the composer (without Shift).
      document.addEventListener("keydown", (e) => {
        try {
          if (!e || e.defaultPrevented) return;
          if (e.key !== "Enter") return;
          if (e.shiftKey) return;
          if (e.ctrlKey || e.altKey) return;
          if (e.isComposing) return;

          const t = e.target;
          if (!t) return;

          const isPrompt =
            (t.id === "prompt-textarea") ||
            (t.closest && (t.closest("#prompt-textarea") || t.closest("textarea")));

          if (!isPrompt) return;

          sendSafetyEnsureAttach();
        } catch (_) {}
      }, true);

      // Click-based send button.
      document.addEventListener("click", (e) => {
        try {
          const t = e && (e.target || e.srcElement);
          if (!t || !t.closest) return;

          const btn = t.closest("button[data-testid='send-button'], button[aria-label='Send prompt'], button[aria-label='Send message']");
          if (!btn) return;

          sendSafetyEnsureAttach();
        } catch (_) {}
      }, true);
    } catch (_) {}
  }

  function normalizeText(text) {
    return (text || "").toString()
      .replace(/\r\n/g, "\n")
      .replace(/\r/g, "\n")
      .replace(/\n/g, "\\n")
      .trim();
  }

  function truthFp(role, id, text) { return role + "|" + id + "|" + text; }

  function emitMessageEventOnce(role, id) {
    const k = role + "|" + id;
    if (state.seenMsgEvent.has(k)) return;
    state.seenMsgEvent.add(k);
    post({ type: "continuum.event", chatId: state.chatId, evt: `message:${role}:${id}` });
  }

  function logTruthLine(role, id, rawText) {
    if (!ensureAttachedIfReady()) return false;

    // During Chronicle preflight we freeze writes to prevent out-of-order reconstruction.
    if (state.chronicle && state.chronicle.active && state.chronicle.freezeWrites) return false;

    const safeText = normalizeText(rawText);
    if (!safeText) return false;

    const fp = truthFp(role, id, safeText);
    if (state.seenTruthFp.has(fp)) return false;
    state.seenTruthFp.add(fp);

    state.turnCounter++;
    const turn = String(state.turnCounter).padStart(5, "0");
    const roleTag = role === "assistant" ? "A" : role === "user" ? "U" : "S";

    post({ type: "continuum.truth", chatId: state.chatId, line: `[${turn}][${roleTag}][${id}] ${safeText}` });
    emitMessageEventOnce(role, id);
    return true;
  }

  const STOP_SELECTOR = "button[data-testid=\"stop-button\"][aria-label=\"Stop streaming\"]";

  const ACTIONS_SELECTOR = [
    "button[data-testid='copy-turn-action-button']",
    "button[data-testid='good-response-turn-action-button']",
    "button[data-testid='bad-response-turn-action-button']",
    "button[aria-label='Copy']",
    "button[aria-label='Good response']",
    "button[aria-label='Bad response']",
    "button[aria-label='More actions']",
    "button[aria-label='Share']"
  ].join(",");

  const TURN_SELECTOR = "[data-testid^='conversation-turn-'], [data-message-id]";
  const ASSISTANT_SETTLE_STABLE_MS = 450;

  function isStopPresent() {
    try { return !!document.querySelector(STOP_SELECTOR); } catch (_) { return false; }
  }

  function actionsPresentInOrNear(turnEl) {
    try {
      if (!turnEl) return false;
      if (turnEl.querySelector(ACTIONS_SELECTOR)) return true;

      const parent = turnEl.parentElement;
      if (parent && parent.querySelector(ACTIONS_SELECTOR)) return true;

      const next = turnEl.nextElementSibling;
      if (next && next.querySelector(ACTIONS_SELECTOR)) return true;

      const prev = turnEl.previousElementSibling;
      if (prev && prev.querySelector(ACTIONS_SELECTOR)) return true;

      return false;
    } catch (_) { return false; }
  }

  function getTurnId(turnEl) {
    if (!turnEl) return null;

    const msgIdEl = turnEl.matches("[data-message-id]") ? turnEl : turnEl.querySelector("[data-message-id]");
    if (msgIdEl) {
      const id = msgIdEl.getAttribute("data-message-id");
      if (id) return id;
    }

    const t = turnEl.getAttribute("data-testid");
    if (t) return t;

    return null;
  }

  function inferRoleFromTurn(turnEl) {
    try {
      const roleNode = turnEl.querySelector("[data-message-author-role]");
      if (roleNode) {
        const r = roleNode.getAttribute("data-message-author-role");
        if (r === "user" || r === "assistant") return r;
      }
    } catch (_) {}

    if (actionsPresentInOrNear(turnEl)) return "assistant";
    return null;
  }

  function extractTurnText(turnEl, role) {
    if (!turnEl) return "";

    try {
      const md = turnEl.querySelector(".markdown.prose");
      if (md) {
        const t = (md.innerText || "").trim();
        if (t) return t;
      }
    } catch (_) {}

    try {
      const roleNode = turnEl.querySelector(`[data-message-author-role='${role}']`);
      if (roleNode) {
        const t = (roleNode.innerText || "").trim();
        if (t) return t;
      }
    } catch (_) {}

    try {
      const clone = turnEl.cloneNode(true);
      const removeSelectors = [
        "button","svg","[role='menu']","[role='menuitem']",
        "[data-testid*='turn-action']","[data-testid*='response-turn-action']",
        "[aria-label='Copy']","[aria-label='Good response']","[aria-label='Bad response']",
        "[aria-label='Share']","[aria-label='More actions']",
        "[data-testid*='attachment']"
      ].join(",");
      clone.querySelectorAll(removeSelectors).forEach(n => n.remove());
      const t = (clone.innerText || clone.textContent || "").trim();
      if (t.length > 20000) return t.slice(0, 20000);
      return t;
    } catch (_) {}

    return "";
  }

  function isAssistantSettled(turnEl, turnId) {
    if (isStopPresent()) return false;
    if (!actionsPresentInOrNear(turnEl)) return false;

    const last = state.lastMutationAtById.get(turnId) || 0;
    const now = (performance && performance.now) ? performance.now() : Date.now();
    return (now - last) >= ASSISTANT_SETTLE_STABLE_MS;
  }

  function handleTurn(turnEl) {
    try {
      if (!ensureAttachedIfReady()) return false;

      const id = getTurnId(turnEl);
      if (!id) return false;

      const role = inferRoleFromTurn(turnEl);
      if (!role) return false;

      // Append-only invariant: never capture an assistant turn until it is settled.
      // (User turns are already final.)
      if (role === "assistant" && !isAssistantSettled(turnEl, id)) return false;

      const text = extractTurnText(turnEl, role);
      if (!text) return false;

      if (role === "assistant")
        post({ type: "continuum.event", chatId: state.chatId, evt: `assistant.settled:${id}` });

      return logTruthLine(role, id, text);
    } catch (_) { return false; }
  }

  function scanAllTurns() {
    if (!ensureAttachedIfReady()) return false;

    try {
      const all = Array.from(document.querySelectorAll(TURN_SELECTOR));
      const seen = new Set();
      const turns = [];

      for (const el of all) {
        const key = getTurnId(el) || "";
        if (!key) continue;
        if (seen.has(key)) continue;
        seen.add(key);
        turns.push(el);
      }

      for (const t of turns) handleTurn(t);

      return true;
    } catch (_) { return false; }
  }

  function startObserver() {
    let pending = false;

    function requestScan() {
      if (pending) return;
      pending = true;
      setTimeout(() => {
        pending = false;
        scanAllTurns();
      }, 250);
    }

    const obs = new MutationObserver((mutations) => {
      try {
        const now = (performance && performance.now) ? performance.now() : Date.now();

        for (const m of mutations || []) {
          const t = m && m.target;
          if (!t || !t.closest) continue;

          const turnEl = t.closest("[data-message-id]") || t.closest("[data-testid^='conversation-turn-']");
          if (!turnEl) continue;

          const id = getTurnId(turnEl);
          if (id) state.lastMutationAtById.set(id, now);
        }
      } catch (_) {}

      requestScan();
    });

    try {
      obs.observe(document.documentElement || document.body, { childList: true, subtree: true, characterData: true });
    } catch (_) {}

    try {
      const now = (performance && performance.now) ? performance.now() : Date.now();
      for (const el of document.querySelectorAll(TURN_SELECTOR)) {
        const id = getTurnId(el);
        if (id && !state.lastMutationAtById.has(id)) state.lastMutationAtById.set(id, now);
      }
    } catch (_) {}
  }

  function startNavigationWatch() {
    try { state.lastHref = location.href; } catch (_) { state.lastHref = null; }
    try { state.lastNavChatId = getChatIdFromLocation(); } catch (_) { state.lastNavChatId = null; }
    try { state.lastNavPath = location.pathname; } catch (_) { state.lastNavPath = null; }

    setInterval(() => {
      let href = null;
      try { href = location.href; } catch (_) { href = null; }
      if (!href || href === state.lastHref) return;

      // Only treat this as a "real" navigation if the chatId or pathname changed.
      // ChatGPT updates query/hash frequently (model pickers, UI panels, etc.), and
      // re-attaching on those creates spammy host-side effects (e.g., repeated toasts).
      let newChatId = null;
      let newPath = null;
      try { newChatId = getChatIdFromLocation(); } catch (_) { newChatId = null; }
      try { newPath = location.pathname; } catch (_) { newPath = null; }

      const oldChatId = state.lastNavChatId || null;
      const oldPath = state.lastNavPath || null;

      state.lastHref = href;
      state.lastNavChatId = newChatId;
      state.lastNavPath = newPath;

      if ((newChatId || null) === (oldChatId || null) && (newPath || null) === (oldPath || null)) {
        return;
      }
      state.attachedChatId = null;
      state.hostAttachedChatId = null;
      try { if (state.prelude) state.prelude.lastRootHrefSent = null; } catch (_) {}
      stopAttachWatchdog();
      ensureAttachedIfReady();
      startAttachWatchdog();
      scanAllTurns();
    }, 750);
  }

  // Boring backfill model: a small polling loop that repeatedly re-scans the currently
  // available DOM turns. Host-side de-dupe makes this safe across chat switching and restarts.
  function startPollingScan() {
    if (state.pollTimer) return;
    state.pollTimer = setInterval(() => {
      try {
        // Chronicle runs its own deterministic scan loop (top→bottom). Avoid interference.
        if (state.chronicle && state.chronicle.active) return;
        scanAllTurns();
      } catch (_) {}
    }, 1000);
  }

  function findNewChatButton() {
    const selectors = [
      "a[aria-label='New chat']",
      "button[aria-label='New chat']",
      "a[title='New chat']",
      "button[title='New chat']",
      "[data-testid*='new-chat']",
      "[data-testid*='new_chat']"
    ];

    for (const sel of selectors) {
      try {
        const el = document.querySelector(sel);
        if (el) return el;
      } catch (_) {}
    }

    try {
      const all = Array.from(document.querySelectorAll("a,button"));
      for (const el of all) {
        const t = (el.textContent || "").trim().toLowerCase();
        if (t === "new chat") return el;
      }
    } catch (_) {}

    return null;
  }

  function forceNewChatNavigation() {
    try {
      const origin = location.origin || "https://chatgpt.com";
      location.assign(origin + "/");
      return true;
    } catch (_) {}
    return false;
  }

  function requestNewChatNavigation() {
    try {
      post({ type: "continuum.event", chatId: state.chatId || "unknown", evt: "refresh.nav.requested" });

      const btn = findNewChatButton();
      if (btn) {
        try { btn.click(); } catch (_) {}
        post({ type: "continuum.event", chatId: state.chatId || "unknown", evt: "refresh.nav.clicked" });
        return true;
      }

      post({ type: "continuum.event", chatId: state.chatId || "unknown", evt: "refresh.nav.failed:no_button" });

      if (forceNewChatNavigation()) {
        post({ type: "continuum.event", chatId: state.chatId || "unknown", evt: "refresh.nav.forced:location_assign" });
        return true;
      }

      post({ type: "continuum.event", chatId: state.chatId || "unknown", evt: "refresh.nav.failed:force_failed" });
    } catch (_) {}

    return false;
  }

  // Dedicated injector loop
  function composerReady() {
    try {
      const prose = document.querySelector("#prompt-textarea.ProseMirror[contenteditable='true']");
      if (prose) return { kind: "prosemirror", el: prose };
      const textarea = document.querySelector("textarea");
      if (textarea) return { kind: "textarea", el: textarea };
    } catch (_) {}
    return null;
  }


  function _escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  // Convert plain text into ProseMirror-friendly HTML that preserves:
  // - paragraph breaks (blank lines => new <p>)
  // - single newlines inside a paragraph (=> <br>)
  // We do NOT trim lines to avoid jamming words together.
  function _plainTextToProseHtml(text) {
  // Convert plain text into ProseMirror-friendly HTML that preserves layout robustly.
  // IMPORTANT: We avoid <br> because ChatGPT's ProseMirror pipeline may drop/normalize it
  // when HTML is injected. Instead, we emit one <p> per line (including empty lines).
  // This guarantees that "Source:" / "USER:" etc never glue together.
  const t = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
  const lines = t.split("\n");

  let htmlOut = "";
  for (let i = 0; i < lines.length; i++) {
    const line = lines[i];
    if (line === "") {
      htmlOut += "<p></p>";
    } else {
      htmlOut += "<p>" + _escapeHtml(line) + "</p>";
    }
  }
  return htmlOut;
}


  function injectIntoComposer(text) {
    const c = composerReady();
    if (!c) return false;

    try {
      if (c.kind === "prosemirror") {
        // Preserve paragraphs/line-breaks so the injected Essence-M remains readable in the composer.
        const html = _plainTextToProseHtml(text);
        c.el.innerHTML = html;
        try { c.el.dispatchEvent(new InputEvent("input", { bubbles: true, cancelable: true })); } catch (_) {}
      } else {
        c.el.value = text;
        try { c.el.dispatchEvent(new Event("input", { bubbles: true })); } catch (_) {}
      }
      return true;
    } catch (_) {}

    return false;
  }


  // -----------------------------
  // Prelude prompt (new chat root) — non-blocking
  // -----------------------------
  // Goal: When the user interacts with the real composer on a blank new chat,
  // emit a single best-effort signal to the host so it can show a sticky Prelude toast
  // with actions (Prelude / Dismiss). This must never block other features.
  //
  // IMPORTANT: We reuse composerReady() as the single source of truth, because Prelude injection
  // depends on it and it is already proven stable on your current ChatGPT DOM.
  state.prelude = {
    lastRootHrefSent: null
  };

  function isComposerBlankNow() {
    try {
      const c = composerReady();
      if (!c || !c.el) return false;

      if (c.kind === "prosemirror") {
        const txt = (c.el.textContent || "").trim();
        return txt.length === 0;
      }
      const v = (c.el.value || "").trim();
      return v.length === 0;
    } catch (_) {}
    return false;
  }

  function getRootTurnCount() {
    try {
      const els = document.querySelectorAll(TURN_SELECTOR);
      return els ? els.length : 0;
    } catch (_) { return 0; }
  }

  function isBlankNewChatRoot() {
    try {
      if (!isLikelyNewChatRoot()) return false;

      // On root, chatId is not available yet. Use DOM turn count + blank composer.
      const turns = getRootTurnCount();
      if (turns > 1) return false;

      if (!composerReady()) return false;
      if (!isComposerBlankNow()) return false;

      return true;
    } catch (_) { return false; }
  }

  function isComposerInteractionTarget(target) {
    try {
      const c = composerReady();
      if (!c || !c.el) return false;

      if (target && c.el.contains && c.el.contains(target)) return true;

      const ae = document.activeElement;
      if (ae && c.el.contains && c.el.contains(ae)) return true;

      return false;
    } catch (_) { return false; }
  }

  function isComposerDirectTarget(target) {
    try {
      if (!target) return false;

      // Tight guard: require the event target (or its ancestors) to match the actual editor element.
      // This avoids false positives from focus/animation/overlay clicks near the composer.
      const hit = target.closest
        ? target.closest("#prompt-textarea.ProseMirror[contenteditable='true'], #prompt-textarea.ProseMirror")
        : null;

      if (!hit) return false;

      // Sanity: ensure the matched editor is the one composerReady() resolves (if available).
      const c = composerReady();
      if (c && c.el && hit !== c.el) {
        // If ChatGPT swapped nodes, accept only if it's still within the resolved composer.
        if (c.el.contains && c.el.contains(hit)) return true;
        return false;
      }

      return true;
    } catch (_) { return false; }
  }


  function emitPreludePromptSignal(reason) {
    try {
      const href = (location && location.href) ? location.href : "";
      if (!href) return;

      // Only once per root href (new chat instance). Leaving root resets this naturally.
      if (state.prelude && state.prelude.lastRootHrefSent === href) return;
      state.prelude.lastRootHrefSent = href;

      post({
        type: "continuum.ui.prelude_prompt",
        href,
        reason: reason || ""
      });
    } catch (_) {}
  }

  function startPreludePromptSignals() {
    try {
      // Capture-phase so nested ProseMirror nodes still trigger.
      document.addEventListener("pointerdown", (e) => {
        try {
          if (!e || e.defaultPrevented) return;
          if (e.isTrusted === false) return;
          if (!isComposerDirectTarget(e.target)) return;
          if (!isBlankNewChatRoot()) return;

          emitPreludePromptSignal("pointerdown");
        } catch (_) {}
      }, true);

      document.addEventListener("keydown", (e) => {
        try {
          if (!e || e.defaultPrevented) return;
          if (e.isTrusted === false) return;

          // If keyboard focus is inside composer, treat as interaction.
          if (!isComposerInteractionTarget(document.activeElement)) return; // keyboard-only path
          if (!isBlankNewChatRoot()) return;

          emitPreludePromptSignal("keydown");
        } catch (_) {}
      }, true);
    } catch (_) {}
  }

  // -----------------------------
  // Composer interaction signal (existing chats) — non-blocking
  // -----------------------------
  // Purpose: provide a clear user-intent signal for Chronicle guidance in existing chats.
  // Emits: continuum.ui.composer_interaction (host-side decides if/when to toast).
  function startComposerInteractionSignals() {
    try {
      let lastEmitAt = 0;
      const minGapMs = 650;

      function isExistingChatRoute() {
        try { return /\/c\//i.test(location.pathname || ""); } catch (_) { return false; }
      }

      function emit(reason) {
        try {
          const now = Date.now();
          if ((now - lastEmitAt) < minGapMs) return;
          lastEmitAt = now;

          const cid = getChatIdFromLocation();
          if (!cid) return;

          // Include a best-effort turn count so the host can distinguish
          // an older chat with history from a fresh/new chat shell.
          post({
            type: "continuum.ui.composer_interaction",
            chatId: cid,
            reason: reason || "",
            capturedTurns: state.turnCounter || 0
          });
        } catch (_) {}
      }

      document.addEventListener("pointerdown", (e) => {
        try {
          if (!isExistingChatRoute()) return;
          if (!e || e.defaultPrevented) return;
          if (e.isTrusted === false) return;
          if (!isComposerDirectTarget(e.target)) return;
          emit("pointerdown");
        } catch (_) {}
      }, true);

      document.addEventListener("keydown", (e) => {
        try {
          if (!isExistingChatRoute()) return;
          if (!e || e.defaultPrevented) return;
          if (e.isTrusted === false) return;
          if (!isComposerInteractionTarget(document.activeElement)) return;
          emit("keydown");
        } catch (_) {}
      }, true);
    } catch (_) {}
  }



  function startInjectorLoop() {
    if (state.injectorTimer) return;
    state.injectorTimer = setInterval(() => { tryInjectPendingSeed(); }, 120);
  }

  function stopInjectorLoopIfIdle() {
    if (!state.injectorTimer) return;
    if (state.pendingSeed) return;
    clearInterval(state.injectorTimer);
    state.injectorTimer = null;
  }

  function throttleInjectorStatus(evt) {
    const now = Date.now();
    if ((now - state.injectorLastStatusAt) < 900) return;
    state.injectorLastStatusAt = now;
    post({ type: "continuum.event", chatId: state.chatId || "unknown", evt });
  }

  function isLikelyNewChatRoot() {
    try {
      const p = location.pathname || "";
      return !/\/c\//i.test(p);
    } catch (_) {}
    return false;
  }

  function tryInjectPendingSeed() {
    try {
      if (!state.pendingSeed || !state.pendingSeed.essence) {
        stopInjectorLoopIfIdle();
        return false;
      }

      state.chatId = getChatIdFromLocation();

      const seed = state.pendingSeed;
      const now = Date.now();
      const started = seed.requestedAt || now;
      const mode = seed.mode || "Quick";

      if (seed.requireNewChat) {
        const currentId = state.chatId;
        const originId = seed.originChatId || null;

        const movedToNewChatId = currentId && originId && currentId !== originId;
        const onRoot = !currentId && isLikelyNewChatRoot();

        if (!movedToNewChatId && !onRoot) {
          throttleInjectorStatus("refresh.inject.waiting_for_chat");
          if ((now - started) >= 20000) {
            seed.requireNewChat = false;
            seed.fallback = true;
            post({ type: "continuum.event", chatId: currentId || originId || "unknown", evt: "refresh.inject.timeout_fallback:new_chat" });
          }
          return false;
        }
      }

      if (!composerReady()) {
        throttleInjectorStatus("refresh.inject.waiting_for_composer");
        return false;
      }

      if (!injectIntoComposer(seed.essence)) return false;

      const originId = seed.originChatId || null;
      const currentId = state.chatId;

      let label = "current_chat";
      if (seed.requireNewChat) {
        label = currentId ? "new_chat" : "new_chat_root";
        if (!currentId) post({ type: "continuum.event", chatId: originId || "unknown", evt: "refresh.inject.fast_root" });
      } else if (originId && currentId && currentId !== originId) {
        label = "new_chat";
      } else if (seed.fallback) {
        label = "current_chat_fallback";
      }

      post({ type: "continuum.event", chatId: currentId || originId || "unknown", evt: "rehydrate.seed_inserted:" + mode + ":" + label });
      post({ type: "continuum.event", chatId: currentId || originId || "unknown", evt: "refresh.inject.success:" + label });

      state.pendingSeed = null;
      stopInjectorLoopIfIdle();
      return true;
    } catch (_) { return false; }
  }

  

  // -----------------------------
  // Pulse preflight: capture flush
  // -----------------------------
  function flushCaptureAndAck(requestId, requestedChatId) {
    try {
      const rid = (requestId || "").toString().trim();
      if (!rid) return;

      // Best-effort: align chatId for outgoing truth lines during this flush.
      if (requestedChatId && typeof requestedChatId === "string" && requestedChatId.trim()) {
        state.chatId = requestedChatId.trim();
      }

      const before = state.turnCounter || 0;
      const start = Date.now();

      let done = false;
      function sendAck() {
        if (done) return;
        done = true;

        const after = state.turnCounter || 0;
        const appended = Math.max(0, after - before);

        // Include a tiny tail breadcrumb for debugging (ids only; text is already in Truth.log).
        let tailIds = [];
        try {
          const turns = Array.from(document.querySelectorAll(TURN_SELECTOR));
          const ids = [];
          for (let i = Math.max(0, turns.length - 6); i < turns.length; i++) {
            const id = getTurnId(turns[i]);
            if (id) ids.push(id);
          }
          tailIds = ids;
        } catch (_) {}

        post({
          type: "continuum.capture.flush_ack",
          chatId: state.chatId || getChatIdFromLocation() || requestedChatId || "unknown",
          requestId: rid,
          appended,
          ms: Date.now() - start,
          tailIds
        });
      }

      const timeoutAt = start + 1400; // hard stop: never hang Pulse
      (function step() {
        try { scanAllTurns(); } catch (_) {}

        // If assistant is still streaming, wait briefly (but cap hard).
        const streaming = (() => { try { return isStopPresent(); } catch (_) { return false; } })();

        if (!streaming) {
          // One small settle scan, then ACK.
          setTimeout(() => {
            try { scanAllTurns(); } catch (_) {}
            sendAck();
          }, 180);
          return;
        }

        if (Date.now() >= timeoutAt) {
          // Timed out waiting for stream to finish; ACK anyway (best-effort).
          sendAck();
          return;
        }

        setTimeout(step, 140);
      })();
    } catch (_) {}
  }



  // -----------------------------
  // Chronicle (Truth backfill/rebuild)
  // -----------------------------
  function sleep(ms){ return new Promise(r => setTimeout(r, ms||0)); }

  function chronicleEnsureOverlay(){
    try {
      const c = state.chronicle;
      if (c.overlay && document.body.contains(c.overlay)) return;

      // SoftGlass scaffold (match Control Centre Dock theme)
      // NOTE: We intentionally avoid the bright accent/teal here; Chronicle progress should feel
      // like part of the subdued system UI.
      const SG = {
        bgA: "rgba(255,255,255,0.06)",
        bgB: "rgba(255,255,255,0.02)",
        bgColor: "rgba(2,10,20,0.96)",
        border: "rgba(120, 180, 240, 0.16)",
        divider: "rgba(80, 120, 160, 0.15)",
        text: "rgba(245,251,255,0.75)",
        track: "rgba(255,255,255,0.08)",
        fillA: "rgba(245,251,255,0.18)",
        fillB: "rgba(245,251,255,0.42)",
        dotA: "rgba(255,255,255,0.34)",
        dotB: "rgba(255,255,255,0.12)",
        dotC: "rgba(2,10,20,0.95)",
      };

      const outer = document.createElement("div");
      outer.id = "val-chronicle-overlay";
      outer.style.position = "fixed";
      outer.style.left = "0";
      outer.style.right = "0";
      outer.style.top = "0";
      outer.style.zIndex = "2147483647";
      outer.style.pointerEvents = "none";
      outer.style.padding = "10px 12px";
      outer.style.display = "flex";
      outer.style.justifyContent = "center";

      const panel = document.createElement("div");
      panel.id = "val-chronicle-panel";
      panel.style.width = "100%";
      panel.style.maxWidth = "720px";
      panel.style.boxSizing = "border-box";
      panel.style.padding = "10px 14px";
      panel.style.borderRadius = "var(--val-radius-panel, 18px)";
      panel.style.border = `1px solid ${SG.border}`;
      panel.style.background = `linear-gradient(135deg, ${SG.bgA}, ${SG.bgB})`;
      panel.style.backgroundColor = SG.bgColor;
      panel.style.boxShadow = "none";
      panel.style.backdropFilter = "blur(18px)";
      panel.style.webkitBackdropFilter = "blur(18px)";
      panel.style.color = SG.text;
      panel.style.fontSize = "12px";
      panel.style.fontFamily = "system-ui, -apple-system, Segoe UI, Roboto, Arial, sans-serif";
      panel.style.display = "flex";
      panel.style.flexDirection = "column";
      panel.style.gap = "8px";

      const header = document.createElement("div");
      header.style.display = "flex";
      header.style.alignItems = "center";
      header.style.gap = "8px";

      const dot = document.createElement("div");
      dot.style.width = "10px";
      dot.style.height = "10px";
      dot.style.borderRadius = "999px";
      dot.style.background = `radial-gradient(circle at 30% 30%, ${SG.dotA} 0%, ${SG.dotB} 45%, ${SG.dotC} 100%)`;
      dot.style.border = `1px solid ${SG.border}`;
      dot.style.boxShadow = "none";

      const label = document.createElement("div");
      label.textContent = "Chronicle: preparing…";
      label.style.fontWeight = "600";
      label.style.letterSpacing = "0.02em";

      header.appendChild(dot);
      header.appendChild(label);

      const bar = document.createElement("div");
      bar.style.height = "5px";
      bar.style.background = SG.track;
      bar.style.border = `1px solid ${SG.divider}`;
      bar.style.borderRadius = "999px";
      bar.style.overflow = "hidden";

      const fill = document.createElement("div");
      fill.style.height = "100%";
      fill.style.width = "0%";
      fill.style.background = `linear-gradient(90deg, ${SG.fillA}, ${SG.fillB})`;
      fill.style.boxShadow = "none";
      fill.style.borderRadius = "999px";

      bar.appendChild(fill);
      panel.appendChild(header);
      panel.appendChild(bar);
      outer.appendChild(panel);

      document.body.appendChild(outer);

      c.overlay = outer;
      c.barFill = fill;
      c.labelEl = label;
    } catch (_) {}
  }

  function chronicleSetOverlay(text, pct){
    try {
      const c = state.chronicle;
      if (!c.overlay || !document.body.contains(c.overlay)) chronicleEnsureOverlay();
      if (c.labelEl) c.labelEl.textContent = text || "";
      const p = Math.max(0, Math.min(100, (pct==null ? 0 : pct)));
      if (c.barFill) c.barFill.style.width = p + "%";
    } catch (_) {}
  }

  function chronicleHideOverlay(){
    try {
      const c = state.chronicle;
      if (c.overlay && c.overlay.parentElement) c.overlay.parentElement.removeChild(c.overlay);
      c.overlay = null; c.barFill = null; c.labelEl = null;
    } catch (_) {}
  }

  function findScrollContainer(){
    try {
      const root = document.scrollingElement || document.documentElement || document.body;
      if (root && root.scrollHeight > (root.clientHeight + 200)) return root;

      const candidates = Array.from(document.querySelectorAll("main, [role='main'], #__next, body, html, div"));
      let best = null;
      let bestScore = 0;

      for (let i = 0; i < candidates.length; i++){
        const el = candidates[i];
        if (!el || !el.getBoundingClientRect) continue;
        const sh = el.scrollHeight || 0;
        const ch = el.clientHeight || 0;
        if (sh <= ch + 200) continue;

        let oy = "";
        try { oy = (getComputedStyle(el).overflowY || "").toLowerCase(); } catch(_) { oy = ""; }
        if (oy !== "auto" && oy !== "scroll") continue;

        if (sh > bestScore){
          best = el;
          bestScore = sh;
        }
      }

      return best || root;
    } catch (_) {}
    return document.scrollingElement || document.documentElement || document.body;
  }

  function getFirstAndLastTurnIds(){
    try {
      const turns = Array.from(document.querySelectorAll(TURN_SELECTOR));
      if (!turns || !turns.length) return { firstId: null, lastId: null, count: 0 };
      const firstId = getTurnId(turns[0]);
      const lastId = getTurnId(turns[turns.length - 1]);
      return { firstId, lastId, count: turns.length };
    } catch (_) {}
    return { firstId: null, lastId: null, count: 0 };
  }

  function postChronicleProgress(phase, pct){
    try {
      const c = state.chronicle;
      const now = Date.now();
      const p = Math.max(0, Math.min(100, pct|0));
      if ((now - (c.lastProgressPostAt||0)) < 2000 && p === (c.lastProgressPct|0)) return;
      c.lastProgressPostAt = now;
      c.lastProgressPct = p;

      post({
        type: "continuum.chronicle.progress",
        chatId: state.chatId || getChatIdFromLocation() || "unknown",
        requestId: c.requestId || "",
        phase: phase || "",
        percent: p,
        capturedTurns: state.turnCounter || 0
      });
    } catch (_) {}
  }

  async function startChronicle(requestId, requestedChatId){
    const c = state.chronicle;
    if (c.active) return;

    const resolveChatId = () => (state.chatId || getChatIdFromLocation() || requestedChatId || "unknown");

    c.active = true;
    c.requestId = (requestId || "").toString().trim() || ("chronicle-" + Date.now());
    c.freezeWrites = true;
    c.startedAt = Date.now();
    c.phase = "to_top";
    c.cancelRequested = false;
    c.lastProgressPostAt = 0;
    c.lastProgressPct = -1;

    // Align chatId so outgoing truth lines are attributed correctly.
    if (requestedChatId && typeof requestedChatId === "string" && requestedChatId.trim()){
      state.chatId = requestedChatId.trim();
    } else {
      state.chatId = getChatIdFromLocation();
    }

    // Reset local capture de-dupe so we can rebuild from scratch after host resets Truth.log.
    state.turnCounter = 0;
    state.seenTruthFp = new Set();
    state.seenMsgEvent = new Set();
    state.lastMutationAtById = new Map();

    chronicleEnsureOverlay();
    chronicleSetOverlay("Chronicle: loading to top…", 2);
    postChronicleProgress("to_top", 2);

    let donePosted = false;
    const cancelErr = () => {
      const e = new Error("Chronicle cancelled");
      e.__valChronicleCancelled = true;
      return e;
    };

    try {
      // Give the UI a moment so our freezeWrites gate is active before background scans run.
      await sleep(120);

      if (c.cancelRequested) throw cancelErr();

      const sc = findScrollContainer();

      // Phase 1: drive to top and wait until the oldest turn stops changing.
      let stable = 0;
      let lastFirst = null;

      for (let i = 0; i < 140; i++){
        if (c.cancelRequested) throw cancelErr();
        try { sc.scrollTop = 0; } catch(_) {}
        await sleep(420);

        if (c.cancelRequested) throw cancelErr();

        const ids = getFirstAndLastTurnIds();
        const firstId = ids.firstId;

        if (firstId && firstId === lastFirst) stable++;
        else stable = 0;

        lastFirst = firstId;

        const pct = Math.min(18, 2 + stable * 4);
        chronicleSetOverlay("Chronicle: loading to top…", pct);
        postChronicleProgress("to_top", pct);

        if (stable >= 4) break;
      }

      // Phase 2: enable writes and scan+scroll down to capture in order.
      if (c.cancelRequested) throw cancelErr();
      c.phase = "capture";
      c.freezeWrites = false;

      // Initial scan at top.
      try { scanAllTurns(); } catch(_) {}
      await sleep(200);
      try { scanAllTurns(); } catch(_) {}

      chronicleSetOverlay("Chronicle: capturing down…", 20);
      postChronicleProgress("capture", 20);

      let bottomStable = 0;
      let lastLastId = null;

      for (let i = 0; i < 2400; i++){
        if (c.cancelRequested) throw cancelErr();
        let max = 0;
        try { max = Math.max(0, (sc.scrollHeight || 0) - (sc.clientHeight || 0)); } catch(_) { max = 0; }
        let top = 0;
        try { top = sc.scrollTop || 0; } catch(_) { top = 0; }

        const pct = max > 0 ? Math.round((top / max) * 100) : 100;
        const uiPct = Math.max(20, Math.min(99, pct));
        chronicleSetOverlay(`Chronicle: capturing down… (turns: ${state.turnCounter || 0})`, uiPct);
        postChronicleProgress("capture", uiPct);

        // Step down
        const step = Math.max(180, Math.floor((sc.clientHeight || 800) * 0.85));
        const next = Math.min(max, top + step);

        if (next === top) bottomStable++;
        try { sc.scrollTop = next; } catch(_) {}

        await sleep(420);
        if (c.cancelRequested) throw cancelErr();
        try { scanAllTurns(); } catch(_) {}
        await sleep(140);
        if (c.cancelRequested) throw cancelErr();
        try { scanAllTurns(); } catch(_) {}

        const ids = getFirstAndLastTurnIds();
        const lastId = ids.lastId;

        if (next >= max && lastId && lastId === lastLastId) bottomStable++;
        else if (next < max) bottomStable = 0;

        lastLastId = lastId;

        if (max === 0 && i > 6) break; // nothing to scroll
        if (next >= max && bottomStable >= 5) break;
      }

      // Final settle scans
      try { scanAllTurns(); } catch(_) {}
      await sleep(250);
      try { scanAllTurns(); } catch(_) {}

      const elapsed = Date.now() - (c.startedAt || Date.now());
      chronicleSetOverlay(`Chronicle: complete — captured ${state.turnCounter || 0} turns`, 100);
      postChronicleProgress("done", 100);

      post({
        type: "continuum.chronicle.done",
        chatId: resolveChatId(),
        requestId: c.requestId,
        capturedTurns: state.turnCounter || 0,
        ms: elapsed
      });

      donePosted = true;
    } catch (err) {
      const elapsed = Date.now() - (c.startedAt || Date.now());

      const cancelled = !!(err && err.__valChronicleCancelled) || !!c.cancelRequested;
      if (cancelled) {
        try {
          post({
            type: "continuum.chronicle.done",
            chatId: resolveChatId(),
            requestId: c.requestId,
            capturedTurns: state.turnCounter || 0,
            ms: elapsed,
            error: "cancelled"
          });
          donePosted = true;
        } catch (_) {}
      } else {

        try { chronicleSetOverlay("Chronicle: error — stopping…", 100); } catch(_) {}
        try { postChronicleProgress("error", 100); } catch(_) {}

        try {
          post({
            type: "continuum.chronicle.done",
            chatId: resolveChatId(),
            requestId: c.requestId,
            capturedTurns: state.turnCounter || 0,
            ms: elapsed,
            error: (err && err.message) ? err.message : String(err)
          });
          donePosted = true;
        } catch (_) {}
      }
    } finally {
      // Never leave the system in a frozen state.
      c.freezeWrites = false;
      c.phase = "";
      c.active = false;

      if (!donePosted) {
        try {
          const elapsed = Date.now() - (c.startedAt || Date.now());
          post({
            type: "continuum.chronicle.done",
            chatId: resolveChatId(),
            requestId: c.requestId,
            capturedTurns: state.turnCounter || 0,
            ms: elapsed,
            error: "Chronicle aborted"
          });
        } catch (_) {}
      }

      try { await sleep(650); } catch (_) {}
      chronicleHideOverlay();
    }
  }


function handleHostMessage(event) {
    let msg = event && event.data;
    if (!msg) return;
    if (typeof msg === "string") {
      try { msg = JSON.parse(msg); } catch (_) { return; }
    }
    if (typeof msg !== "object") return;
    msg = unwrapEnvelope(msg);

    if (msg.type === "continuum.session.attached") {
      try {
        const cid = (msg.chatId || "").toString();
        if (cid) state.hostAttachedChatId = cid;
      } catch (_) {}
      stopAttachWatchdog();
      return;
    }


    if (msg.type === "continuum.capture.flush") {
      flushCaptureAndAck(msg.requestId, msg.chatId);
      return;
    }

    if (msg.type === "continuum.chronicle.cancel") {
      try {
        const c = state.chronicle;
        if (!c || !c.active) return;
        const rid = (msg.requestId || "").toString().trim();
        if (rid && c.requestId && rid !== c.requestId) return;
        c.cancelRequested = true;
      } catch (_) {}
      return;
    }

    if (msg.type === "continuum.chronicle.start") {
      startChronicle(msg.requestId, msg.chatId);
      return;
    }

    if (msg.type === "continuum.inject_text") {
      const essence = ((msg.text ?? msg.essence) || "").toString();
      if (!essence) return;

      const openNewChat = msg.openNewChat === true;
      const mode = (msg.capsuleMode || msg.mode || "Quick");

      state.pendingSeed = {
        essence,
        mode,
        requireNewChat: openNewChat,
        originChatId: getChatIdFromLocation(),
        requestedAt: Date.now(),
        fallback: false
      };

      startInjectorLoop();

      if (openNewChat) requestNewChatNavigation();
      else tryInjectPendingSeed();
    }
  }

  function attachHostListener() {
    try {
      if (window.chrome?.webview?.addEventListener) {
        window.chrome.webview.addEventListener("message", handleHostMessage);
      }
    } catch (_) {}
  }

  function init() {
    if (!hasBridge()) return;

    ensureAttachedIfReady();
    startAttachWatchdog();
    startSendSafetyHooks();
    attachHostListener();
    startPreludePromptSignals();
    startComposerInteractionSignals();
    startObserver();
    startNavigationWatch();
    scanAllTurns();
    startPollingScan();
    startInjectorLoop();
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", init, { once: true });
  } else {
    init();
  }
})();
} catch (e) {
  try { console.error("[VAL Continuum] load failed", e); } catch (_) {}
}


try {
  window.addEventListener("error", function(e){ try{ console.log("[VAL Continuum] error", e && (e.message||e)); }catch(_){} }, true);
} catch(_) {}
