// Void.main.js — Ship Fix v1 (default OFF + deterministic gating + placeholders)
// Hides code blocks + screenshots inside ChatGPT messages.
//
// Shipping contract:
//  1) Default OFF.
//  2) Remains OFF until user enables in Control Centre.
//  4) When hiding, insert inline placeholders per item:
//     - "Code block hidden by Void — click to expand"
//     - "Screenshot hidden by Void — click to expand"
//     Placeholders toggle expand/collapse for that specific item.
//  5) When OFF: no hiding, no placeholders, observers no-op.

(function () {
  let observer = null;
  let pendingScan = false;
  let idSeq = 0;

  const VOID_ENABLED_KEY = "VAL_VoidEnabled";

  const ATTR_MANAGED = "data-val-void-managed";
  const ATTR_HIDDEN = "data-val-void-hidden";
  const ATTR_PREV_DISPLAY = "data-val-void-prev-display";
  const ATTR_ID = "data-val-void-id";
  const ATTR_KIND = "data-val-void-kind";
  const ATTR_USER_EXPANDED = "data-val-void-user-expanded";

  const PH_ATTR = "data-val-void-placeholder";
  const PH_FOR_ATTR = "data-val-void-for";
  const PH_KIND_ATTR = "data-val-void-ph-kind";

  const PH_STATE_ATTR = "data-val-void-ph-state";

  const CODE_PLACEHOLDER_TEXT = "Code block hidden by Void — click to expand";
  const SCREENSHOT_PLACEHOLDER_TEXT = "Screenshot hidden by Void — click to expand";

  function readEnabledFromStorage() {
    try {
      const v = localStorage.getItem(VOID_ENABLED_KEY);
      if (v === "1" || v === "true") return true;
      if (v === "0" || v === "false") return false;
    } catch (_) {}
    return false;
  }

  function writeEnabledToStorage(enabled) {
    try {
      localStorage.setItem(VOID_ENABLED_KEY, enabled ? "1" : "0");
    } catch (_) {}
  }

  function getRoot() {
    return document.querySelector("main") || document.body;
  }

  function isEnabled() {
    return window.valVoidEnabled === true;
  }

  function isMessageNode(node) {
    if (!node) return false;
    return !!node.closest('[data-message-author-role]') || !!node.closest("article");
  }

  function ensureId(target) {
    if (!target) return null;
    const existing = target.getAttribute(ATTR_ID);
    if (existing) return existing;

    idSeq += 1;
    const id = "v" + String(idSeq);
    target.setAttribute(ATTR_ID, id);
    return id;
  }

  function findPlaceholderForTarget(target) {
    try {
      if (!target) return null;
      const id = target.getAttribute(ATTR_ID);
      if (!id) return null;
      const parent = target.parentElement;
      if (!parent) return null;
      return parent.querySelector(`[${PH_ATTR}="1"][${PH_FOR_ATTR}="${id}"]`);
    } catch (_) {
      return null;
    }
  }

  function setPlaceholderState(ph, state) {
    try {
      if (!ph) return;
      ph.setAttribute(PH_STATE_ATTR, state);
    } catch (_) {}
  }

  function movePlaceholder(ph, target, where) {
    try {
      if (!ph || !target) return;
      const parent = target.parentElement;
      if (!parent) return;

      if (where === "after") {
        // Place immediately after the target.
        if (ph.previousSibling === target) return;
        parent.insertBefore(ph, target.nextSibling);
      } else {
        // Place immediately before the target.
        if (ph.nextSibling === target) return;
        parent.insertBefore(ph, target);
      }
    } catch (_) {}
  }



  function toggleTargetVisibility(target) {
    if (!target) return;

    const ph = findPlaceholderForTarget(target);
    const hidden = target.getAttribute(ATTR_HIDDEN) === "1";

    if (hidden) {
      // Expand (restore normal layout).
      const prev = target.getAttribute(ATTR_PREV_DISPLAY);
      target.style.display = prev ?? "";
      target.removeAttribute(ATTR_HIDDEN);
      target.setAttribute(ATTR_USER_EXPANDED, "1");

      // When expanded, move placeholder after the target so the content reclaims
      // its normal position/margins.
      if (ph) {
        setPlaceholderState(ph, "expanded");
        movePlaceholder(ph, target, "after");
      }
      return;
    }

    // Collapse
    target.style.display = "none";
    target.setAttribute(ATTR_HIDDEN, "1");
    target.removeAttribute(ATTR_USER_EXPANDED);

    // When collapsed, restore placeholder to the hide-site position.
    if (ph) {
      setPlaceholderState(ph, "collapsed");
      movePlaceholder(ph, target, "before");
    }
  }

  function ensurePlaceholder(target, kind) {
    if (!target) return null;

    const parent = target.parentElement;
    if (!parent) return null;

    const id = ensureId(target);
    if (!id) return null;

    const existing = findPlaceholderForTarget(target);
    if (existing) return existing;

    const ph = document.createElement("div");
    ph.setAttribute(PH_ATTR, "1");
    ph.setAttribute(PH_FOR_ATTR, id);
    ph.setAttribute(PH_KIND_ATTR, kind);

    ph.setAttribute(PH_STATE_ATTR, "collapsed");
    ph.className = "val-void-placeholder";

    ph.textContent = (kind === "code")
      ? CODE_PLACEHOLDER_TEXT
      : SCREENSHOT_PLACEHOLDER_TEXT;

    ph.addEventListener("click", (e) => {
      try {
        e.preventDefault();
        e.stopPropagation();
      } catch (_) {}

      // Look up target by id to keep the click scoped to this item.
      let t = null;
      try {
        t = document.querySelector(`[${ATTR_ID}="${id}"]`);
      } catch (_) {}

      if (!t) {
        try { ph.remove(); } catch (_) {}
        return;
      }

      toggleTargetVisibility(t);
    }, true);

    // Insert directly before the target so it appears inline at the hide site.
    try {
      parent.insertBefore(ph, target);
    } catch (_) {
      try { parent.appendChild(ph); } catch (_) {}
    }

    return ph;
  }

  function markHidden(target, kind) {
    if (!target) return false;
    if (!isEnabled()) return false;

    // Respect per-item user expand.
    if (target.getAttribute(ATTR_USER_EXPANDED) === "1") return false;

    // Idempotency: only manage once.
    if (target.getAttribute(ATTR_MANAGED) !== "1") {
      target.setAttribute(ATTR_MANAGED, "1");
      target.setAttribute(ATTR_KIND, kind);
      if (!target.hasAttribute(ATTR_PREV_DISPLAY)) {
        target.setAttribute(ATTR_PREV_DISPLAY, target.style.display ?? "");
      }
      ensurePlaceholder(target, kind);
    }

    if (target.getAttribute(ATTR_HIDDEN) === "1") return false;

    target.setAttribute(ATTR_HIDDEN, "1");
    target.style.display = "none";
    target.removeAttribute(ATTR_USER_EXPANDED);

    return true;
  }

  function hideCodeBlock(el) {
    if (!el) return false;
    if (!isMessageNode(el)) return false;

    const target =
      el.closest('pre, div[data-testid*="code"], div[class*="code"], section[data-testid*="code"]') || el;

    if (!target) return false;
    return markHidden(target, "code");
  }

  function hideScreenshot(img) {
    if (!img) return false;
    if (!isMessageNode(img)) return false;

    // Ignore tiny icons; focus on "real content" images.
    try {
      const rect = img.getBoundingClientRect();
      if (rect.width < 40 && rect.height < 40) return false;
    } catch (_) {}

    const target =
      img.closest('[data-testid*="image"], figure, button, a, div[class*="image"], div[data-testid*="attachment"]') ||
      img.closest('div[role="button"]') ||
      img;

    if (!target) return false;
    return markHidden(target, "screenshot");
  }

  function removeAllPlaceholders(root) {
    try {
      root.querySelectorAll(`[${PH_ATTR}="1"]`).forEach((ph) => {
        try { ph.remove(); } catch (_) {}
      });
    } catch (_) {}
  }

  function restoreAll() {
    const root = getRoot();

    // Restore all managed targets.
    try {
      root.querySelectorAll(`[${ATTR_MANAGED}="1"]`).forEach((el) => {
        try {
          const prev = el.getAttribute(ATTR_PREV_DISPLAY);
          el.style.display = prev ?? "";

          el.removeAttribute(ATTR_HIDDEN);
          el.removeAttribute(ATTR_USER_EXPANDED);
          el.removeAttribute(ATTR_MANAGED);
          el.removeAttribute(ATTR_KIND);
          el.removeAttribute(ATTR_PREV_DISPLAY);
          el.removeAttribute(ATTR_ID);
        } catch (_) {}
      });
    } catch (_) {}

    // Remove placeholders.
    removeAllPlaceholders(root);
  }

  function scanOnce() {
    if (!isEnabled()) return;

    const root = getRoot();

    root
      .querySelectorAll('pre code, pre, div[data-testid*="code"], div[class*="code"], section[data-testid*="code"]')
      .forEach((el) => {
        try { hideCodeBlock(el); } catch (_) {}
      });

    root.querySelectorAll("img").forEach((img) => {
      try { hideScreenshot(img); } catch (_) {}
    });
  }

  function scheduleScan() {
    if (!isEnabled()) return;
    if (pendingScan) return;

    pendingScan = true;
    setTimeout(() => {
      pendingScan = false;
      try { scanOnce(); } catch (_) {}
    }, 250);
  }

  function startObserver() {
    if (!isEnabled()) return;
    if (observer) return;

    observer = new MutationObserver(() => {
      scheduleScan();
    });

    try {
      const root = getRoot();
      observer.observe(root, { childList: true, subtree: true, characterData: true });
    } catch (_) {
      try { observer.disconnect(); } catch (_) {}
      observer = null;
    }
  }

  function stopObserver() {
    if (!observer) return;
    try { observer.disconnect(); } catch (_) {}
    observer = null;
  }

  // Exposed hook used by the Dock to apply the current state.
  // - If enabled: start observer + scan now.
  // - If disabled: disconnect observer + restore + remove placeholders.
  window.applyVoidToAll = function () {
    try {
      // Canonicalize persisted state.
      writeEnabledToStorage(isEnabled());

      if (isEnabled()) {
        startObserver();
        scanOnce();
      } else {
        stopObserver();
        restoreAll();
      }
    } catch (_) {
      // No-op: Void must never break host UI.
    }
  };

  function boot() {
    // Deterministic startup: localStorage is the source of truth.
    try {
      window.valVoidEnabled = readEnabledFromStorage();
    } catch (_) {
      window.valVoidEnabled = false;
    }

    // Apply once after DOM is ready.
    try { window.applyVoidToAll(); } catch (_) {}
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", boot, { once: true });
  } else {
    boot();
  }
})();
