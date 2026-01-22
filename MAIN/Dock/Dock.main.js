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


  function post(msg){ try{ window.chrome?.webview?.postMessage(msg); }catch(e){} }

  // Persisted module flags (so Control Centre reflects real module state on load)
  const VOID_ENABLED_KEY = "VAL_VoidEnabled";

  const THEME_ENABLED_KEY = "VAL_ThemeEnabled";

  // Portal (Capture & Stage)
  // Portal should ALWAYS default to Off on load (armed state is explicit user action).
  const PORTAL_ENABLED_KEY = "VAL_PortalEnabled";
  const PORTAL_COUNT_KEY   = "VAL_PortalStageCount";

  function emitAbyssCommand(type, detail){
    try {
      window.dispatchEvent(new CustomEvent(type, { detail: detail || {} }));
    } catch(_) {}
  }

  function getPortalEnabled(){ return false; } // force Off at boot
  function setPortalEnabled(next){ try { localStorage.setItem(PORTAL_ENABLED_KEY, next ? "1" : "0"); } catch(_) {} }

  function getPortalCount(){
    try {
      const v = localStorage.getItem(PORTAL_COUNT_KEY);
      const n = Number(v);
      return Number.isFinite(n) ? Math.max(0, Math.min(10, n)) : 0;
    } catch(_) {}
    return 0;
  }
  function setPortalCount(n){
    try {
      const c = Math.max(0, Math.min(10, Number(n)||0));
      localStorage.setItem(PORTAL_COUNT_KEY, String(c));
    } catch(_) {}
  }



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
    try { post({ type: "void.command.set_enabled", enabled: v }); } catch(_) {}
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

function isPreludeNudgeSuppressed(){
  try {
    const until = parseInt(localStorage.getItem(PRELUDE_NUDGE_SUPPRESS_KEY) || "0", 10);
    return Date.now() < until;
  } catch(_) { return false; }
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

  function toggle(label, module, initialPressed, tooltipText){
    const wrap = el("div","valdock-toggle");
    const lab  = el("div","valdock-tg-label",label);

    const init = (typeof initialPressed === "boolean") ? initialPressed : true;
    const sw   = el("button","valdock-tg-switch", init ? "On" : "Off");
    sw.setAttribute("aria-pressed", String(init));

    sw.addEventListener("click",(e)=>{
      e.preventDefault();
      const pressed = sw.getAttribute("aria-pressed")==="true";
      const next = !pressed;
      sw.setAttribute("aria-pressed", String(next));
      sw.textContent = next ? "On" : "Off";

      if (module==="Continuum") {
        try {
          if (typeof window.VAL_Continuum_toggleLogging === "function") {
            window.VAL_Continuum_toggleLogging(next);
          } else {
            post({ type:"continuum.command.toggle_logging", chatId: getChatId(), enabled: next });
          }
        } catch(_) {}
      }

      if (module==="Void") {
        try { setVoidEnabled(next); } catch(_) {}
      }

      
      if (module==="Portal") {
        try {
          setPortalEnabled(next);
          post({ type:"portal.command.set_enabled", enabled: next });
        } catch(_) {}
      }

if (module==="Theme") {
        try { setThemeEnabled(next); } catch(_) {}
      }
    }, true);

    if (tooltipText) attachTooltip(sw, tooltipText);

    wrap.append(lab, sw);
    return wrap;
  }



  function rectW(){ return dock.getBoundingClientRect().width; }
  function rectH(){ return dock.getBoundingClientRect().height; }

  function applyPos(){
    if (!dock) return;
    if (state.x == null || state.y == null){
      dock.style.top   = "12px";
      dock.style.left  = "50%";
      dock.style.right = "auto";
      dock.style.transform = "translateX(-50%)";
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

  function collapse(toCollapsed){
    state.collapsed = !!toCollapsed;
    if (!pill || !panel) return;
    try { hideTooltip(); } catch(_) {}
    pill.style.display  = state.collapsed ? "block" : "none";
    panel.style.display = state.collapsed ? "none"  : "flex";
    saveState();
  }

  function buildDock(){
    dock  = el("div","valdock");
    pill  = el("button","valdock-pill","Control Centre");
    panel = el("div","valdock-panel");

    // Drag handlers
    dock.addEventListener("mousedown", onDown, true);

    // Header
    const header = el("div","valdock-header");
    const title  = el("div","valdock-title","Control Centre");
    const close  = el("button","valdock-close","×");
    close.addEventListener("click",(e)=>{ e.preventDefault(); collapse(true); }, true);
    header.append(title, close);



    // Continuum row
    const rowC = el("div","valdock-row");
    rowC.append(
      el("div","valdock-row-title","Continuum:"),
      toggle(
        "Chat monitoring",
        "Continuum",
        undefined
      )
    );

    // Buttons row
    const rowBtns = el("div","valdock-row valdock-actions");

    let refreshLocked = false;
    let refreshTimer = null;

    function setRefreshLocked(locked){
      refreshLocked = !!locked;
      try {
        pulseBtn.disabled = refreshLocked;
        pulseBtn.classList.toggle("valdock-btn-disabled", refreshLocked);
      } catch(_) {}
    }

    function startRefreshCooldown(ms){
      setRefreshLocked(true);
      if (refreshTimer) { try{ clearTimeout(refreshTimer); }catch(_){} }
      refreshTimer = setTimeout(function(){ setRefreshLocked(false); }, ms);
    }

    const pulseBtn = btn("Pulse", "primary");
    attachTooltip(pulseBtn, "Open a new chat with a summary of your current conversation and guidelines for a smooth transition.");
    pulseBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      if (refreshLocked) return;

      try {
        suppressPreludeNudge(25000);
        post({ type:"continuum.command.pulse", chatId: getChatId() });
        collapse(true);
        startRefreshCooldown(8000);
      } catch(_) {
        setRefreshLocked(false);
      }
    }, true);

    const preludeBtn = btn("Prelude", "secondary");
    attachTooltip(preludeBtn, "Add the session setup and instructions to the current chat.");
    preludeBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"continuum.command.inject_preamble", chatId: getChatId() }); } catch(_) {}
    }, true);



    const chronicleBtn = btn("Chronicle", "ghost");
    attachTooltip(chronicleBtn, "Scan the current chat and rebuild VAL’s memory for this session.");

    let chronicleBusy = false;
    function setChronicleBusy(next){
      chronicleBusy = !!next;
      try { chronicleBtn.textContent = chronicleBusy ? "Cancel" : "Chronicle"; } catch(_) {}
    }

    function syncChronicleBusyFromDom(){
      try {
        const hasOverlay = !!document.getElementById("val-chronicle-overlay");
        if (hasOverlay !== chronicleBusy) setChronicleBusy(hasOverlay);
      } catch(_) {}
    }

    // Best-effort: reflect Chronicle run state in the button label.
    syncChronicleBusyFromDom();
    try {
      const mo = new MutationObserver(()=>{ syncChronicleBusyFromDom(); });
      mo.observe(document.body, { childList:true, subtree:true });
    } catch(_) {}

    // Pre-emptively flip to "Cancel" as soon as the host starts Chronicle.
    try {
      if (window.chrome?.webview?.addEventListener) {
        window.chrome.webview.addEventListener("message", (ev)=>{
          try {
            const msg = ev && ev.data;
            if (!msg || typeof msg !== "object") return;
            if ((msg.type || "") === "continuum.chronicle.start") setChronicleBusy(true);
            if ((msg.type || "") === "portal.stage.count") {
              try {
                const c = Number(msg.count);
                if (Number.isFinite(c)) {
                  setPortalCount(c);
                  if (portalCount) portalCount.textContent = `${Math.max(0, Math.min(10, c))}/10`;
                }
              } catch(_) {}
            }

          } catch(_) {}
        });
      }
    } catch(_) {}
    chronicleBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try {
        if (chronicleBusy) post({ type:"continuum.command.chronicle_cancel", chatId: getChatId() });
        else post({ type:"continuum.command.chronicle_rebuild_truth", chatId: getChatId() });
      } catch(_) {}
    }, true);

    const openBtn = btn("Session Folder", "ghost");
    attachTooltip(openBtn, "Open the folder on your computer where this session’s files are stored.");
    openBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"continuum.command.open_session_folder", chatId: getChatId() }); } catch(_) {}
    }, true);

    rowBtns.append(pulseBtn, preludeBtn, chronicleBtn, openBtn);

    // Portal row
    const rowP = el("div","valdock-row");
    const portalCount = el("div","valdock-count", `0/10`);
    const portalSendBtn = btn("Send", "secondary");
    attachTooltip(portalSendBtn, "Paste all staged clipboard images into the composer (max 10).");

    portalSendBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { focusComposerForPaste(); } catch(_) {}
      setTimeout(()=>{ try { post({ type:"portal.command.send_staged", max: 10 }); } catch(_) {} }, 80);
    }, true);

    rowP.append(
      el("div","valdock-row-title","Portal:"),
      toggle(
        "Capture & Stage",
        "Portal",
        false,
        "Arm Portal. Press 1 to open Screen Snip. Any clipboard images will stage (max 10)."
      ),
      portalCount,
      portalSendBtn
    );

    // Abyss row
    const rowA = el("div","valdock-row");
    const abyssHint = el("div","valdock-row-note","Recall/Search");
    rowA.append(
      el("div","valdock-row-title","Abyss:"),
      abyssHint
    );

    const rowABtns = el("div","valdock-row valdock-actions");
    const abyssSearchBtn = btn("Search", "primary");
    const abyssLastBtn = btn("Last", "ghost");
    const abyssFolderBtn = btn("Session Folder", "ghost");

    attachTooltip(abyssSearchBtn, "Open Abyss search and enter a recall query.");
    attachTooltip(abyssLastBtn, "Recall the most recent exchange from the latest Truth.log.");
    attachTooltip(abyssFolderBtn, "Open the folder where this session’s memory is stored.");

    abyssSearchBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      emitAbyssCommand("abyss.command.open_query_ui", { source: "dock" });
    }, true);

    abyssLastBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"abyss.command.last", chatId: getChatId(), count: 2, inject: false }); } catch(_) {}
    }, true);

    abyssFolderBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"continuum.command.open_session_folder", chatId: getChatId() }); } catch(_) {}
    }, true);

    rowABtns.append(abyssSearchBtn, abyssLastBtn, abyssFolderBtn);


    // Void row
    const rowV = el("div","valdock-row");
    rowV.append(
      el("div","valdock-row-title","Void:"),
      toggle(
        "Hide Code & Screenshots",
        "Void",
        getVoidEnabled()
      )
    );

    // Theme row
    const rowT = el("div","valdock-row");
    rowT.append(
      el("div","valdock-row-title","Theme:"),
      toggle(
        "VAL Theme",
        "Theme",
        getThemeEnabled()
      )
    );

    // Status
    const status = el("div","valdock-status","");
    function refreshStatus(){
      try {
        const id = getChatId() || "unknown";
        status.textContent = "Current Session Id: " + id;
      } catch(_) {
        status.textContent = "Current Session Id: unknown";
      }
    }
    refreshStatus();
    setInterval(refreshStatus, 2000);

    // Compose

    const divider0 = el("div","valdock-divider");
    const divider1 = el("div","valdock-divider");
    const divider2 = el("div","valdock-divider");
    const divider3 = el("div","valdock-divider");

    panel.append(
      header,
      rowC,
      rowBtns,
      divider0,
      rowP,
      divider1,
      rowA,
      rowABtns,
      divider2,
      rowV,
      divider3,
      rowT,
      status
    );
    dock.append(pill, panel);

    // Portal safety: always start disarmed, and reset count display.
    try {
      setPortalEnabled(false);
      setPortalCount(0);
      if (portalCount) portalCount.textContent = "0/10";
      post({ type:"portal.command.set_enabled", enabled: false });
    } catch(_) {}
    document.body.appendChild(dock);

    // Clicking minimized pill expands
    pill.addEventListener("click",(e)=>{ e.preventDefault(); collapse(false); }, true);

    // Initial state
    if (state.collapsed) collapse(true); else collapse(false);
    applyPos();

    // Keep Void behavior in sync with its current enabled flag at startup.
    try { if (typeof window.applyVoidToAll === "function") window.applyVoidToAll(); } catch(_) {}



    // New Chat helper toast: when we are on the ChatGPT "home" route (no /c/<id> yet),
    // nudge the user to click Prelude so assistant tagging guardrails are active.
    (function setupPreludeNudge(){
      // Initialize lastPath to the current route so we don't emit on app load.
      let lastPath = (location && location.pathname) ? location.pathname : "";
      function tick(){
        try {
          const p = (location && location.pathname) ? location.pathname : "";
          if (p === lastPath) return;
          lastPath = p;

          // New chat / home page is typically "/"
          if (p === "/") {
            // Avoid nudging during Pulse (Pulse opens a new chat automatically).
            if (isPreludeNudgeSuppressed()) return;

            // Host toast (preferred)
            try { post({ type: "continuum.ui.new_chat" }); } catch(_) {}
          }
        } catch(_) {}
      }
      tick();
      try { setInterval(tick, 800); } catch(_) {}
    })();

    // Emit console signal
    try { console.log("[VAL Dock] Loaded for", CHAT_ID); } catch(_) {}
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", buildDock, { once: true });
  } else {
    buildDock();
  }
})();
