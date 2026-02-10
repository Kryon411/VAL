// Dock.main.js — Working Beta Dock (Continuum + Void)
// Self-contained dock UI injected into chatgpt.com via ModuleLoader.
// Provides:
//  - Minimized "Control Centre" pill (top-center by default)
//  - Expandable "Control Centre" panel
//  - Continuum toggle (best-effort) + Pulse + Open Folder buttons
//  - Void toggle (best-effort) for hide-code/screenshots modules
//  - Per-session position + collapsed state via localStorage

(function () {
  if (window.__VAL_DOCK_BOOTED__) return;
  window.__VAL_DOCK_BOOTED__ = true;

  console.log("[VAL Dock] script loaded");


  function getNonce(){
    try { return window.__VAL_NONCE || null; } catch(_) { return null; }
  }

  function withNonce(envelope){
    if (!envelope || typeof envelope !== "object") return envelope;
    if (envelope.nonce) return envelope;
    const nonce = getNonce();
    if (!nonce) return envelope;
    return { ...envelope, nonce };
  }

  function toEnvelope(message){
    if (!message || typeof message !== "object") return message;
    const type = (message.type || "").toString();
    if (!type) return message;
    if (type === "command" || type === "event" || type === "log") return withNonce(message);
    return withNonce({
      type: "command",
      name: type,
      payload: message,
      chatId: message.chatId,
      source: "dock"
    });
  }

  function unwrapEnvelope(message){
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

  function post(msg){ try{ window.chrome?.webview?.postMessage(toEnvelope(msg)); }catch(e){} }

  const COMMAND_NAME_BOOTSTRAP_TYPE = "val.contracts.bootstrap";
  const DOCK_MODEL_EVENT = "val.dock.model";
  const DEFAULT_COMMAND_NAMES = Object.freeze({
    VoidCommandSetEnabled: "void.command.set_enabled",
    ContinuumCommandPulse: "continuum.command.pulse",
    ContinuumCommandInjectPreamble: "continuum.command.inject_preamble",
    ContinuumCommandChronicleCancel: "continuum.command.chronicle_cancel",
    ContinuumCommandChronicleRebuildTruth: "continuum.command.chronicle_rebuild_truth",
    ContinuumCommandOpenSessionFolder: "continuum.command.open_session_folder",
    AbyssCommandLast: "abyss.command.last",
    PortalCommandSetEnabled: "portal.command.set_enabled",
    PortalCommandSendStaged: "portal.command.send_staged",
    PrivacyCommandSetContinuumLogging: "privacy.command.set_continuum_logging",
    PrivacyCommandSetPortalCapture: "privacy.command.set_portal_capture",
    PrivacyCommandOpenDataFolder: "privacy.command.open_data_folder",
    PrivacyCommandWipeData: "privacy.command.wipe_data",
    ToolsOpenTruthHealth: "tools.open_truth_health",
    ToolsOpenDiagnostics: "tools.open_diagnostics",
    NavCommandGoChat: "nav.command.go_chat",
    NavCommandGoBack: "nav.command.go_back",
    DockCommandRequestModel: "dock.command.request_model"
  });

  let commandNames = { ...DEFAULT_COMMAND_NAMES };

  function applyCommandNames(update){
    if (!update || typeof update !== "object") return;
    Object.keys(update).forEach((key)=>{
      const value = update[key];
      if (typeof value === "string" && value.trim()) {
        commandNames[key] = value;
      }
    });
  }

  function getCommandName(key){
    return commandNames[key] || DEFAULT_COMMAND_NAMES[key] || key;
  }

  // Persisted module flags (so Control Centre reflects real module state on load)
  const VOID_ENABLED_KEY = "VAL_VoidEnabled";

  const THEME_ENABLED_KEY = "VAL_ThemeEnabled";

  // Portal (Capture & Stage)
  // Portal should ALWAYS default to Off on load (armed state is explicit user action).
  const PORTAL_ENABLED_KEY = "VAL_PortalEnabled";
  const VOID_NO_CODE_BLOCKS_TEXT = "Please avoid code blocks in your responses; they are hidden on my side.";

  function emitAbyssCommand(type, detail){
    try {
      window.dispatchEvent(new CustomEvent(type, { detail: detail || {} }));
    } catch(_) {}
  }

  function getPortalEnabled(){ return false; } // force Off at boot
  function setPortalEnabled(next){ try { localStorage.setItem(PORTAL_ENABLED_KEY, next ? "1" : "0"); } catch(_) {} }


  function readBoolLS(key, fallback){
    try{
      const v = localStorage.getItem(key);
      if (v === "1" || v === "true") return true;
      if (v === "0" || v === "false") return false;
    }catch(_){}
    return !!fallback;
  }

  function writeBoolLS(key, value){
    try { localStorage.setItem(key, value ? "1" : "0"); } catch(_) {}
  }


  function getVoidEnabled(){
    // Source of truth: persisted state. Default OFF when unset.
    try { return readBoolLS(VOID_ENABLED_KEY, false); } catch(_) {}
    return (window.valVoidEnabled === true);
  }

  function setVoidEnabled(next){
    const v = !!next;
    writeBoolLS(VOID_ENABLED_KEY, v);
    window.valVoidEnabled = v;
    // Host toast (activation only is handled host-side).
    try { post({ type: getCommandName("VoidCommandSetEnabled"), enabled: v, reason: "dock_click" }); } catch(_) {}
    try { if (typeof window.applyVoidToAll === "function") window.applyVoidToAll(); } catch(_) {}
  }

  // Establish a boolean early so Void state is deterministic and persists across restarts.
  try { window.valVoidEnabled = getVoidEnabled(); } catch(_) { window.valVoidEnabled = false; }


function getThemeEnabled(){
  // Source of truth: persisted state.
  // First-run default: if the enabled key does not exist yet, default the theme ON once.
  // Existing users are never overridden (stored value wins).
  try {
    const raw = localStorage.getItem(THEME_ENABLED_KEY);
    if (raw === null) return true;
    return readBoolLS(THEME_ENABLED_KEY, false);
  } catch(_) {}
  return (window.valThemeEnabled === true);
}

function setThemeEnabled(next){
  const v = !!next;
  writeBoolLS(THEME_ENABLED_KEY, v);
  window.valThemeEnabled = v;

  // Preferred: call the theme API if present
  try {
    if (window.VAL_Theme && typeof window.VAL_Theme.setEnabled === "function") {
      window.VAL_Theme.setEnabled(v);
      return;
    }
  } catch(_) {}

  // Fallback: broadcast (VALTheme listens)
  try { window.postMessage({ type: "val.theme.set_enabled", enabled: v }, "*"); } catch(_) {}
}

// Establish a boolean early so Theme state is deterministic and persists across restarts.
try { window.valThemeEnabled = getThemeEnabled(); } catch(_) { window.valThemeEnabled = false; }


// Prelude nudge suppression: used to avoid showing the "New chat detected" toast
// during a Pulse cycle (Pulse opens a new chat automatically).
const PRELUDE_NUDGE_SUPPRESS_KEY = "VAL_PreludeNudgeSuppressUntil";

function suppressPreludeNudge(ms){
  try { localStorage.setItem(PRELUDE_NUDGE_SUPPRESS_KEY, String(Date.now() + (ms||0))); } catch(_) {}
}


  function getChatId(){
    try {
      if (typeof window.VAL_Continuum_getChatId === "function") {
        const id = window.VAL_Continuum_getChatId();
        if (id) return id;
      }
    } catch(e){}
    try{
      const m = (location.pathname||"").match(/\/c\/([a-f0-9\-]{36})/i);
      if (m) return m[1];
      const tail = (location.pathname||"").split("/").pop() || "";
      if (/^[a-f0-9\-]{36}$/i.test(tail)) return tail;
    }catch(e){}
    return "session-"+Date.now().toString(36);
  }

  const CHAT_ID = getChatId();
  const LS_KEY  = "VAL_Dock_"+CHAT_ID;

  let dock, pill, panel;
  let dockBody;
  let portalBadge, portalPillIndicator;
  let pulseStatusHint;
  let pulseBtn;
  let chronicleBtn;
  let portalSendHint;
  let currentModel = null;
  let chronicleBusy = false;
  let refreshLocked = false;
  let refreshTimer = null;
  let lastFocusedElement = null;
  let isDragging = false;
  let dragOffset = [0,0];
  let dragFromPill = false;
  let dragHasMoved = false;

  let state = { x: null, y: null, collapsed: true };
  try {
    const raw = localStorage.getItem(LS_KEY);
    if (raw) state = Object.assign(state, JSON.parse(raw));
  } catch(e) {}

  function saveState(){
    try { localStorage.setItem(LS_KEY, JSON.stringify(state)); } catch(e) {}
  }

  function el(tag, cls, text){
    const n = document.createElement(tag);
    if (cls) n.className = cls;
    if (text != null) n.textContent = text;
    return n;
  }

  function btn(label, kind){
    return el("button", "valdock-btn " + (kind||"primary"), label);
  }

  // --- Tooltips ------------------------------------------------------------
  // SoftGlass tooltip shown when hovering Control Centre buttons.
  let tooltipEl = null;
  let tooltipTarget = null;
  let tooltipRaf = 0;

  function ensureTooltip(){
    if (tooltipEl) return;
    tooltipEl = el("div", "valdock-tooltip");
    tooltipEl.style.display = "none";
    document.body.appendChild(tooltipEl);

    // Keep position stable if viewport changes.
    window.addEventListener("resize", ()=>{
      if (tooltipTarget) positionTooltip(tooltipTarget);
    }, { passive:true });
  }

  function positionTooltip(target){
    if (!tooltipEl || !target) return;
    if (tooltipRaf) { try { cancelAnimationFrame(tooltipRaf); } catch(_) {} }
    tooltipRaf = requestAnimationFrame(()=>{
      tooltipRaf = 0;
      try {
        const r = target.getBoundingClientRect();
        const pad = 10;

        // Ensure it's measurable.
        tooltipEl.style.display = "block";
        const tr = tooltipEl.getBoundingClientRect();

        let x = r.left + (r.width/2) - (tr.width/2);
        x = Math.max(pad, Math.min(window.innerWidth - pad - tr.width, x));

        let y = r.top - tr.height - 12;
        if (y < pad) y = r.bottom + 12;
        y = Math.max(pad, Math.min(window.innerHeight - pad - tr.height, y));

        tooltipEl.style.left = Math.round(x) + "px";
        tooltipEl.style.top  = Math.round(y) + "px";
      } catch(_) {}
    });
  }

  function showTooltip(target, text){
    if (!target || !text) return;
    ensureTooltip();
    tooltipTarget = target;
    tooltipEl.textContent = text;
    tooltipEl.style.display = "block";
    tooltipEl.classList.add("visible");
    positionTooltip(target);
  }

  function hideTooltip(){
    tooltipTarget = null;
    if (!tooltipEl) return;
    tooltipEl.classList.remove("visible");
    tooltipEl.style.display = "none";
  }

  function attachTooltip(buttonEl, text){
    if (!buttonEl || !text) return;
    try { buttonEl.setAttribute("data-val-tooltip", text); } catch(_) {}
    buttonEl.addEventListener("mouseenter", ()=> showTooltip(buttonEl, text), true);
    buttonEl.addEventListener("mouseleave", hideTooltip, true);
    buttonEl.addEventListener("focus", ()=> showTooltip(buttonEl, text), true);
    buttonEl.addEventListener("blur", hideTooltip, true);
  }


  // Ensure the ChatGPT composer is focused so host-side Ctrl+V paste lands correctly.
  // We keep selectors broad because ChatGPT's composer can vary (textarea vs contenteditable).
  function focusComposerForPaste(){
    try {
      const selectors = [
        "#prompt-textarea",
        "textarea#prompt-textarea",
        "textarea[data-testid='prompt-textarea']",
        "textarea[placeholder*='Message']",
        "textarea[placeholder*='Send']",
        "div.ProseMirror[contenteditable='true']",
        "[role='textbox'][contenteditable='true']",
        "div[contenteditable='true'][role='textbox']",
      ];

      for (const sel of selectors) {
        const el = document.querySelector(sel);
        if (!el) continue;
        try { el.focus({ preventScroll: true }); } catch(_) { try { el.focus(); } catch(__) {} }
        try { el.click(); } catch(_) {}
        return true;
      }
    } catch(_) {}
    return false;
  }
  try { window.VAL_focusComposerForPaste = focusComposerForPaste; } catch(_) {}

  function showDockToast(message){
    try {
      if (!message) return;
      let toast = document.getElementById("val-dock-toast");
      if (!toast) {
        toast = document.createElement("div");
        toast.id = "val-dock-toast";
        toast.className = "valdock-toast";
        document.body.appendChild(toast);
      }

      toast.textContent = message;
      toast.classList.add("visible");

      if (showDockToast._timer) {
        try { clearTimeout(showDockToast._timer); } catch(_) {}
      }

      showDockToast._timer = setTimeout(()=>{
        try { toast.classList.remove("visible"); } catch(_) {}
      }, 1700);
    } catch(_) {}
  }

  function getComposerElement(){
    const selectors = [
      "#prompt-textarea",
      "textarea#prompt-textarea",
      "textarea[data-testid='prompt-textarea']",
      "textarea[placeholder*='Message']",
      "textarea[placeholder*='Send']",
      "div.ProseMirror[contenteditable='true']",
      "[role='textbox'][contenteditable='true']",
      "div[contenteditable='true'][role='textbox']"
    ];

    for (const selector of selectors) {
      const node = document.querySelector(selector);
      if (node) return node;
    }

    return null;
  }

  function appendTextToTextarea(composer, text){
    if (!composer || !(composer instanceof HTMLTextAreaElement)) return false;

    const current = composer.value || "";
    const prefix = current.trim().length > 0 ? "\n" : "";
    const insertion = prefix + text;
    const start = current.length;

    const prevSelStart = Number.isFinite(composer.selectionStart) ? composer.selectionStart : null;
    const prevSelEnd = Number.isFinite(composer.selectionEnd) ? composer.selectionEnd : null;
    const prevScrollTop = composer.scrollTop;

    composer.focus();
    composer.setSelectionRange(start, start);
    composer.setRangeText(insertion, start, start, "end");
    composer.dispatchEvent(new Event("input", { bubbles: true }));

    if (prevSelStart !== null && prevSelEnd !== null) {
      try {
        composer.setSelectionRange(prevSelStart, prevSelEnd);
        composer.scrollTop = prevScrollTop;
      } catch(_) {}
    }

    return true;
  }

  function appendTextToContentEditable(composer, text){
    if (!composer || !(composer instanceof HTMLElement) || !composer.isContentEditable) return false;

    let previousRange = null;
    try {
      const sel = window.getSelection();
      if (sel && sel.rangeCount > 0) {
        const candidate = sel.getRangeAt(0);
        if (composer.contains(candidate.startContainer)) {
          previousRange = candidate.cloneRange();
        }
      }
    } catch(_) {}

    const current = (composer.textContent || "").trim();
    const prefix = current.length > 0 ? "\n" : "";
    composer.appendChild(document.createTextNode(prefix + text));
    composer.dispatchEvent(new Event("input", { bubbles: true }));

    if (previousRange) {
      try {
        const sel = window.getSelection();
        sel.removeAllRanges();
        sel.addRange(previousRange);
      } catch(_) {}
    }

    return true;
  }

  function insertVoidNoCodeBlocksHelper(){
    const composer = getComposerElement();
    if (!composer) {
      showDockToast("Composer not found.");
      return;
    }

    let inserted = false;
    try {
      inserted = appendTextToTextarea(composer, VOID_NO_CODE_BLOCKS_TEXT);
      if (!inserted) inserted = appendTextToContentEditable(composer, VOID_NO_CODE_BLOCKS_TEXT);
    } catch(_) {
      inserted = false;
    }

    showDockToast(inserted ? "Inserted: No code blocks note." : "Unable to insert into composer.");
  }

  function createToggle(label, initialPressed, tooltipText, onToggle){
    const wrap = el("div","valdock-toggle");
    const lab  = el("div","valdock-tg-label",label);

    const init = (typeof initialPressed === "boolean") ? initialPressed : true;
    const sw   = el("button","valdock-tg-switch", init ? "On" : "Off");
    sw.setAttribute("aria-pressed", String(init));

    function setState(next){
      sw.setAttribute("aria-pressed", String(next));
      sw.textContent = next ? "On" : "Off";
    }

    function setDisabled(disabled){
      sw.disabled = !!disabled;
      sw.classList.toggle("valdock-tg-disabled", !!disabled);
    }

    sw.addEventListener("click",(e)=>{
      e.preventDefault();
      if (sw.disabled) return;
      const pressed = sw.getAttribute("aria-pressed")==="true";
      const next = !pressed;
      setState(next);

      if (typeof onToggle === "function") {
        try { onToggle(next); } catch(_) {}
      }
    }, true);

    if (tooltipText) attachTooltip(sw, tooltipText);

    wrap.setState = setState;
    wrap.setDisabled = setDisabled;
    wrap.getState = () => sw.getAttribute("aria-pressed")==="true";
    wrap.append(lab, sw);
    return wrap;
  }

  function getLocalToggleState(key){
    if (key === "Void") return getVoidEnabled();
    if (key === "Theme") return getThemeEnabled();
    return false;
  }

  function applyLocalToggle(key, next){
    if (key === "Void") {
      try { setVoidEnabled(next); } catch(_) {}
      return;
    }
    if (key === "Theme") {
      try { setThemeEnabled(next); } catch(_) {}
      return;
    }
  }

  function updatePortalBadge(badge){
    if (!portalBadge || !badge || typeof badge !== "object") return;
    const count = Number(badge.count);
    const countText = Number.isFinite(count) && count > 0 ? ` • ${count}` : "";
    const label = badge.label || "Portal";
    portalBadge.textContent = label + countText;
    const active = !!badge.active;
    try {
      if (portalBadge) portalBadge.classList.toggle("active", active);
      if (portalPillIndicator) portalPillIndicator.classList.toggle("active", active);
    } catch(_) {}
  }

  function setChronicleBusy(next){
    chronicleBusy = !!next;
    try { if (chronicleBtn) chronicleBtn.textContent = chronicleBusy ? "Cancel" : "Chronicle"; } catch(_) {}
  }

  function syncChronicleBusyFromDom(){
    try {
      const hasOverlay = !!document.getElementById("val-chronicle-overlay");
      if (hasOverlay !== chronicleBusy) setChronicleBusy(hasOverlay);
    } catch(_) {}
  }

  function setRefreshLocked(locked){
    refreshLocked = !!locked;
    try {
      if (pulseBtn) {
        pulseBtn.disabled = refreshLocked;
        pulseBtn.classList.toggle("valdock-btn-disabled", refreshLocked);
      }
      if (pulseStatusHint) {
        pulseStatusHint.textContent = refreshLocked
          ? "Pulse is cooling down. Try again in a moment."
          : "Pulse opens a fresh chat with a summarized handoff.";
        pulseStatusHint.classList.toggle("is-muted", !refreshLocked);
      }
    } catch(_) {}
  }

  function startRefreshCooldown(ms){
    setRefreshLocked(true);
    if (refreshTimer) { try{ clearTimeout(refreshTimer); }catch(_){} }
    refreshTimer = setTimeout(function(){ setRefreshLocked(false); }, ms);
  }

  function requestDockModel(){
    try {
      post({ type: getCommandName("DockCommandRequestModel"), chatId: getChatId() });
    } catch(_) {}
  }

  function sendCommand(commandName, payload, requiresChatId, reason){
    if (!commandName) return;
    const msg = { type: commandName };
    if (payload && typeof payload === "object" && !Array.isArray(payload)) {
      Object.assign(msg, payload);
    }
    if (reason) msg.reason = reason;
    if (requiresChatId) {
      msg.chatId = getChatId();
    }
    try { post(msg); } catch(_) {}
  }

  function renderItem(item){
    if (!item || typeof item !== "object") return null;
    const type = (item.type || "").toString();
    if (type === "button") {
      const button = btn(item.label || "", item.kind || "primary");
      if (item.disabled) {
        button.disabled = true;
        button.classList.toggle("valdock-btn-disabled", true);
      }
      const tooltip = item.disabled && item.disabledReason ? item.disabledReason : item.tooltip;
      if (tooltip) attachTooltip(button, tooltip);

      if (item.id === "pulse") pulseBtn = button;
      if (item.id === "chronicle") chronicleBtn = button;

      button.addEventListener("click",(e)=>{
        e.preventDefault();
        if (button.disabled) return;

        if (item.id === "abyssSearch") {
          try { emitAbyssCommand("abyss.command.open_query_ui", { source: "dock" }); } catch(_) {}
          return;
        }

        if (item.id === "pulse") {
          if (refreshLocked) return;
          try {
            suppressPreludeNudge(25000);
            sendCommand(item.command?.name, item.command?.payload, true, "dock_click");
            collapse(true);
            startRefreshCooldown(8000);
          } catch(_) {
            setRefreshLocked(false);
          }
          return;
        }

        if (item.id === "chronicle" && chronicleBusy) {
          sendCommand(getCommandName("ContinuumCommandChronicleCancel"), {}, true, "dock_click");
          return;
        }

        if (item.id === "portalSend") {
          try { focusComposerForPaste(); } catch(_) {}
          setTimeout(()=>{
            sendCommand(item.command?.name, item.command?.payload, item.command?.requiresChatId, "dock_click");
          }, 80);
          return;
        }

        if (item.id === "voidInsertNoCodeBlocks") {
          insertVoidNoCodeBlocksHelper();
          return;
        }

        if (item.id === "wipeData") {
          const msg = [
            "Wipe local VAL data?",
            "",
            "This will delete:",
            "• Logs (VAL.log)",
            "• WebView profile/cache",
            "• Continuum session memory (Truth.log + snapshots)",
            "• Portal staging",
            "",
            "Privacy settings are preserved. The app itself will not be removed."
          ].join("\\n");
          try {
            if (!window.confirm(msg)) return;
          } catch(_) { return; }
        }

        sendCommand(item.command?.name, item.command?.payload, item.command?.requiresChatId, "dock_click");
      }, true);
      return button;
    }

    if (type === "toggle") {
      const state = typeof item.state === "boolean"
        ? item.state
        : (item.localStateKey ? getLocalToggleState(item.localStateKey) : false);
      const toggleEl = createToggle(item.label || "", state, item.tooltip, (next)=>{
        if (item.localStateKey) {
          applyLocalToggle(item.localStateKey, next);
        }

        if (item.id === "portalToggle") {
          try { setPortalEnabled(next); } catch(_) {}
        }

        const commandName = item.command?.name;
        if (!commandName) return;
        if (commandName.startsWith("local.")) return;
        const payload = (item.command?.payload && typeof item.command.payload === "object" && !Array.isArray(item.command.payload))
          ? { ...item.command.payload }
          : {};
        payload.enabled = next;
        sendCommand(commandName, payload, item.command?.requiresChatId, "dock_click");
      });

      if (item.disabled) {
        toggleEl.setDisabled(true);
        if (item.disabledReason) {
          const sw = toggleEl.querySelector(".valdock-tg-switch");
          attachTooltip(sw, item.disabledReason);
        }
      }

      return toggleEl;
    }

    if (type === "count") {
      const max = Number.isFinite(Number(item.max)) ? Number(item.max) : 10;
      const count = Number.isFinite(Number(item.count)) ? Number(item.count) : 0;
      return el("div", "valdock-count", `${count}/${max}`);
    }

    return null;
  }

  function renderBlock(block, container){
    if (!block || typeof block !== "object" || !container) return;
    const type = (block.type || "").toString();
    if (type === "row") {
      const row = el("div", block.className || "valdock-row");
      const items = Array.isArray(block.items) ? block.items : [];
      items.forEach((item)=>{
        const node = renderItem(item);
        if (node) row.append(node);
      });
      container.append(row);
      return;
    }

    if (type === "hint") {
      const hint = el("div", block.className || "valdock-section-hint", block.text || "");
      if (block.id === "pulseStatusHint") pulseStatusHint = hint;
      if (block.id === "portalSendHint") portalSendHint = hint;
      container.append(hint);
    }
  }

  function renderSection(section){
    const sectionEl = el("div","valdock-section");
    const header = el("div","valdock-section-header");
    const heading = el("div","valdock-section-heading");
    const title = el("div","valdock-section-title", section.title || "");
    heading.append(title);

    let subtitleEl = null;
    if (section.subtitle) {
      subtitleEl = el("div","valdock-section-subtitle", section.subtitle);
      heading.append(subtitleEl);
    }

    header.append(heading);
    if (section.headerControl) {
      const control = renderItem(section.headerControl);
      if (control) header.append(control);
    }

    const divider = el("div","valdock-section-divider");
    const content = el("div","valdock-section-body");
    const blocks = Array.isArray(section.blocks) ? section.blocks : [];
    blocks.forEach((block)=> renderBlock(block, content));

    sectionEl.append(header, divider, content);
    return { sectionEl, subtitleEl };
  }

  function renderDockModel(model){
    if (!dockBody || !model || typeof model !== "object") return;
    currentModel = model;
    pulseBtn = null;
    chronicleBtn = null;
    pulseStatusHint = null;
    portalSendHint = null;

    dockBody.textContent = "";

    const sections = Array.isArray(model.sections) ? model.sections : [];
    sections.forEach((sectionData)=>{
      const rendered = renderSection(sectionData);
      dockBody.append(rendered.sectionEl);
    });

    const advancedSections = Array.isArray(model.advancedSections) ? model.advancedSections : [];
    if (advancedSections.length > 0) {
      const advancedSection = el("details", "valdock-advanced");
      const advancedSummary = el("summary", "valdock-advanced-summary", "Advanced");
      const advancedBody = el("div", "valdock-advanced-body");
      advancedSections.forEach((sectionData)=>{
        const rendered = renderSection(sectionData);
        advancedBody.append(rendered.sectionEl);
      });
      advancedSection.append(advancedSummary, advancedBody);
      dockBody.append(advancedSection);
    }

    const status = el("div","valdock-status", (model.status && model.status.text) ? model.status.text : "");
    dockBody.append(status);

    updatePortalBadge(model.portalBadge);
    syncChronicleBusyFromDom();
    if (chronicleBusy && chronicleBtn) {
      try { chronicleBtn.textContent = "Cancel"; } catch(_) {}
    }
    setRefreshLocked(refreshLocked);
  }



  function rectW(){ return dock.getBoundingClientRect().width; }
  function rectH(){ return dock.getBoundingClientRect().height; }

  function applyPos(){
    if (!dock) return;
    if (state.x == null || state.y == null){
      dock.style.top   = "14px";
      dock.style.right = "14px";
      dock.style.left  = "auto";
      dock.style.transform = "";
    } else {
      dock.style.left  = state.x+"px";
      dock.style.top   = state.y+"px";
      dock.style.right = "auto";
      dock.style.transform = "";
    }
  }

  function onDown(e){
    const headerHit = e.target.closest(".valdock-header");
    const pillHit   = e.target.closest(".valdock-pill");
    if (!headerHit && !pillHit) return;

    const rect = dock.getBoundingClientRect();
    isDragging   = true;
    dragFromPill = !!pillHit;
    dragHasMoved = false;
    dragOffset   = [e.clientX-rect.left, e.clientY-rect.top];

    document.addEventListener("mousemove", onMove, true);
    document.addEventListener("mouseup", onUp, true);
  }

  function onMove(e){
    if (!isDragging) return;
    dragHasMoved = true;
    state.x = Math.max(8, Math.min(window.innerWidth-8-rectW(), e.clientX - dragOffset[0]));
    state.y = Math.max(8, Math.min(window.innerHeight-8-rectH(), e.clientY - dragOffset[1]));
    applyPos();

    // Keep Void behavior in sync with its current enabled flag at startup.
    try { if (typeof window.applyVoidToAll === "function") window.applyVoidToAll(); } catch(_) {}

  }

  function onUp(){
    document.removeEventListener("mousemove", onMove, true);
    document.removeEventListener("mouseup", onUp, true);

    if (dragFromPill && !dragHasMoved) {
      collapse(false);
    }

    dragFromPill = false;
    isDragging   = false;
    saveState();
  }

  function getDockFocusableElements(){
    if (!dock) return [];
    const selectors = [
      "button",
      "summary",
      "[href]",
      "input",
      "select",
      "textarea",
      "[tabindex]:not([tabindex='-1'])"
    ];
    const nodes = Array.from(dock.querySelectorAll(selectors.join(",")));
    return nodes.filter((node) => {
      if (!(node instanceof HTMLElement)) return false;
      if (node.hasAttribute("disabled")) return false;
      if (node.getAttribute("aria-disabled") === "true") return false;
      return node.offsetParent !== null;
    });
  }

  function focusFirstDockControl(){
    const focusables = getDockFocusableElements();
    if (focusables.length > 0) {
      try { focusables[0].focus({ preventScroll: true }); } catch(_) { try { focusables[0].focus(); } catch(__) {} }
    }
  }

  function collapse(toCollapsed){
    state.collapsed = !!toCollapsed;
    if (!pill || !panel) return;
    try { hideTooltip(); } catch(_) {}
    pill.style.display  = state.collapsed ? "block" : "none";
    panel.style.display = state.collapsed ? "none"  : "flex";
    pill.setAttribute("aria-expanded", state.collapsed ? "false" : "true");
    saveState();

    if (state.collapsed) {
      if (lastFocusedElement && dock && dock.contains(lastFocusedElement)) {
        try { pill.focus({ preventScroll: true }); } catch(_) { try { pill.focus(); } catch(__) {} }
      }
    } else {
      lastFocusedElement = document.activeElement;
      focusFirstDockControl();
      requestDockModel();
    }
  }

  function buildDock(){
    dock  = el("div","valdock");
    pill  = el("button","valdock-pill","Control Centre");
    portalPillIndicator = el("span","valdock-pill-indicator","●");
    pill.append(portalPillIndicator);
    pill.setAttribute("aria-controls", "valdock-panel");
    pill.setAttribute("aria-expanded", "false");
    panel = el("div","valdock-panel");
    panel.id = "valdock-panel";
    panel.setAttribute("role", "dialog");
    panel.setAttribute("aria-label", "Control Centre");

    // Drag handlers
    dock.addEventListener("mousedown", onDown, true);

    // Header
    const header = el("div","valdock-header");
    const title  = el("div","valdock-title","CONTROL CENTRE");
    title.id = "valdock-title";
    panel.setAttribute("aria-labelledby", "valdock-title");
    const close  = el("button","valdock-close","×");
    portalBadge = el("div", "valdock-portal-badge", "Portal");
    close.addEventListener("click",(e)=>{ e.preventDefault(); collapse(true); }, true);
    header.append(title, portalBadge, close);

    const body = el("div","valdock-body");
    dockBody = body;
    panel.append(header, body);
    dock.append(pill, panel);

    document.body.appendChild(dock);
    requestDockModel();

    // Best-effort: reflect Chronicle run state in the button label.
    syncChronicleBusyFromDom();
    try {
      const mo = new MutationObserver(()=>{ syncChronicleBusyFromDom(); });
      mo.observe(document.body, { childList:true, subtree:true });
    } catch(_) {}

    try {
      if (window.chrome?.webview?.addEventListener) {
        window.chrome.webview.addEventListener("message", (ev)=>{
          try {
            let msg = ev && ev.data;
            if (!msg) return;
            if (typeof msg === "string") {
              try { msg = JSON.parse(msg); } catch(_) { return; }
            }
            if (!msg || typeof msg !== "object") return;

            if ((msg.type || "") === COMMAND_NAME_BOOTSTRAP_TYPE) {
              try {
                const payload = (msg.payload && typeof msg.payload === "object" && !Array.isArray(msg.payload))
                  ? msg.payload
                  : (msg.commandNames && typeof msg.commandNames === "object" ? msg.commandNames : (msg.commands && typeof msg.commands === "object" ? msg.commands : null));
                applyCommandNames(payload);
              } catch(_) {}
              return;
            }

            const unwrapped = unwrapEnvelope(msg);
            const msgType = (unwrapped && unwrapped.type) ? unwrapped.type : msg.type;
            if (msgType === DOCK_MODEL_EVENT) {
              renderDockModel(unwrapped);
              return;
            }
            if (msgType === "continuum.chronicle.start") setChronicleBusy(true);
            if (msgType === "continuum.chronicle.done" || msgType === "continuum.chronicle.cancel") setChronicleBusy(false);
            if (msgType === "continuum.session.attached") requestDockModel();
          } catch(_) {}
        });
      }
    } catch(_) {}

    // Clicking minimized pill expands
    pill.addEventListener("click",(e)=>{ e.preventDefault(); collapse(false); }, true);

    // Keyboard support: ESC closes, Tab wraps within open dock
    dock.addEventListener("keydown", (e)=>{
      if (state.collapsed) return;
      if (e.key === "Escape") {
        e.preventDefault();
        collapse(true);
        return;
      }
      if (e.key === "Tab") {
        const focusables = getDockFocusableElements();
        if (focusables.length === 0) return;
        const currentIndex = focusables.indexOf(document.activeElement);
        const lastIndex = focusables.length - 1;
        if (e.shiftKey) {
          if (currentIndex <= 0 || document.activeElement === dock) {
            e.preventDefault();
            focusables[lastIndex].focus();
          }
        } else if (currentIndex === lastIndex) {
          e.preventDefault();
          focusables[0].focus();
        }
      }
    }, true);

    document.addEventListener("keydown", (e)=>{
      if (state.collapsed) return;
      if (e.key === "Escape") {
        e.preventDefault();
        collapse(true);
      }
    }, true);

    // Initial state
    if (state.collapsed) collapse(true); else collapse(false);
    applyPos();

    // Keep Void behavior in sync with its current enabled flag at startup.
    try { if (typeof window.applyVoidToAll === "function") window.applyVoidToAll(); } catch(_) {}



    // Emit console signal
    try { console.log("[VAL Dock] Loaded for", CHAT_ID); } catch(_) {}
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", buildDock, { once: true });
  } else {
    buildDock();
  }
})();
