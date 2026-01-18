// Dock.main.js — Working Beta Dock (Continuum + Void)
// Self-contained dock UI injected into chatgpt.com via ModuleLoader.
// Provides:
//  - Minimized "Control Centre" pill (top-center by default)
//  - Expandable "Control Centre" panel
//  - Continuum toggle (best-effort) + Pulse + Open Folder buttons
//  - Void toggle (best-effort) for hide-code/screenshots modules
//  - Per-session position + collapsed state via localStorage

(function () {
  console.log("[VAL Dock] script loaded");


  function post(msg){ try{ window.chrome?.webview?.postMessage(msg); }catch(e){} }

  // Persisted module flags (so Control Centre reflects real module state on load)
  const VOID_ENABLED_KEY = "VAL_VoidEnabled";

  const THEME_ENABLED_KEY = "VAL_ThemeEnabled";

  // Portal (Capture & Stage)
  // Portal should ALWAYS default to Off on load (armed state is explicit user action).
  const PORTAL_ENABLED_KEY = "VAL_PortalEnabled";
  const PORTAL_COUNT_KEY   = "VAL_PortalStageCount";

  const ABYSS_QUERY_PENDING_KEY = "VAL_AbyssQueryPending";

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

  function getAbyssQueryPending(){
    try { return readBoolLS(ABYSS_QUERY_PENDING_KEY, false); } catch(_) {}
    return false;
  }

  function setAbyssQueryPending(next){
    try { writeBoolLS(ABYSS_QUERY_PENDING_KEY, !!next); } catch(_) {}
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

  // --- Abyss UI ------------------------------------------------------------
  let abyssSearchOverlay = null;
  let abyssResultsOverlay = null;
  let abyssResultsContainer = null;
  let abyssState = {
    queryOriginal: "",
    queryUsed: "",
    generatedUtc: "",
    totalMatches: 0,
    memoryRoot: "",
    results: []
  };
  let abyssExcludeChatIds = loadAbyssExclusions();

  function ensureAbyssSearchOverlay(){
    if (abyssSearchOverlay) return;

    const overlay = el("div", "valabyss-overlay");
    const panel = el("div", "valabyss-panel");
    const header = el("div", "valabyss-header");
    const title = el("div", "valabyss-title", "Abyss Search");
    const closeBtn = el("button", "valabyss-close", "×");
    closeBtn.addEventListener("click", ()=> hideAbyssSearchOverlay(), true);
    header.append(title, closeBtn);

    const input = document.createElement("input");
    input.className = "valabyss-input";
    input.type = "text";
    input.placeholder = "Ask a question or type keywords…";

    const textarea = document.createElement("textarea");
    textarea.className = "valabyss-textarea";
    textarea.placeholder = "Optional extra context (Shift+Enter for newline)…";

    const actions = el("div", "valabyss-actions");
    const runBtn = el("button", "valabyss-btn primary", "Run");
    const cancelBtn = el("button", "valabyss-btn ghost", "Cancel");
    cancelBtn.addEventListener("click", ()=> hideAbyssSearchOverlay(), true);

    function runSearch(){
      const primary = (input.value || "").trim();
      const extra = (textarea.value || "").trim();
      const queryOriginal = extra ? `${primary}\n${extra}`.trim() : primary;
      if (!queryOriginal) return;

      abyssState.queryOriginal = queryOriginal;
      try {
        post({
          type: "abyss.command.search",
          chatId: getChatId(),
          queryOriginal,
          excludeChatIds: abyssExcludeChatIds,
          maxResults: 10
        });
      } catch(_) {}
      hideAbyssSearchOverlay();
    }

    runBtn.addEventListener("click", (e)=>{ e.preventDefault(); runSearch(); }, true);
    input.addEventListener("keydown", (e)=>{
      if (e.key === "Enter") { e.preventDefault(); runSearch(); }
    }, true);
    textarea.addEventListener("keydown", (e)=>{
      if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); runSearch(); }
    }, true);

    actions.append(runBtn, cancelBtn);
    panel.append(header, input, textarea, actions);
    overlay.append(panel);
    overlay.addEventListener("click", (e)=>{ if (e.target === overlay) hideAbyssSearchOverlay(); }, true);

    abyssSearchOverlay = overlay;
    document.body.appendChild(overlay);
  }

  function showAbyssSearchOverlay(){
    ensureAbyssSearchOverlay();
    abyssSearchOverlay.style.display = "flex";
    try {
      const input = abyssSearchOverlay.querySelector(".valabyss-input");
      if (input) input.focus();
    } catch(_) {}
  }

  function hideAbyssSearchOverlay(){
    if (!abyssSearchOverlay) return;
    abyssSearchOverlay.style.display = "none";
  }

  function ensureAbyssResultsOverlay(){
    if (abyssResultsOverlay) return;

    const overlay = el("div", "valabyss-overlay");
    const panel = el("div", "valabyss-panel wide");
    const header = el("div", "valabyss-header");
    const title = el("div", "valabyss-title", "Abyss Results");
    const closeBtn = el("button", "valabyss-close", "×");
    closeBtn.addEventListener("click", ()=> hideAbyssResultsOverlay(), true);
    header.append(title, closeBtn);

    const summary = el("div", "valabyss-summary");
    const container = el("div", "valabyss-results");

    panel.append(header, summary, container);
    overlay.append(panel);
    overlay.addEventListener("click", (e)=>{ if (e.target === overlay) hideAbyssResultsOverlay(); }, true);

    abyssResultsOverlay = overlay;
    abyssResultsContainer = container;
    document.body.appendChild(overlay);
  }

  function hideAbyssResultsOverlay(){
    if (!abyssResultsOverlay) return;
    abyssResultsOverlay.style.display = "none";
  }

  function showAbyssResultsOverlay(){
    ensureAbyssResultsOverlay();
    abyssResultsOverlay.style.display = "flex";
    renderAbyssResults();
  }

  function shortId(id){
    if (!id) return "unknown";
    return id.length > 8 ? id.slice(0, 8) : id;
  }

  function renderAbyssResults(){
    if (!abyssResultsOverlay || !abyssResultsContainer) return;
    const summary = abyssResultsOverlay.querySelector(".valabyss-summary");
    if (summary) {
      summary.textContent = `QueryOriginal: ${abyssState.queryOriginal || "—"}\nQueryUsed: ${abyssState.queryUsed || "—"}\nScope: All Chats\nMatches: ${abyssState.totalMatches || 0}`;
    }

    abyssResultsContainer.textContent = "";

    const results = Array.isArray(abyssState.results) ? abyssState.results : [];
    if (results.length === 0) {
      abyssResultsContainer.append(el("div", "valabyss-empty", "No results to display."));
      return;
    }

    const grouped = {};
    for (const r of results) {
      const cid = (r.chatId || "").toString();
      if (!grouped[cid]) grouped[cid] = [];
      grouped[cid].push(r);
    }

    Object.keys(grouped).forEach((cid)=>{
      const list = grouped[cid];
      list.sort((a,b)=> (b.score||0) - (a.score||0));
      const best = list[0];
      const groupEl = el("div", "valabyss-group");
      const groupHeader = el("div", "valabyss-group-header");
      const groupTitle = el("div", "valabyss-group-title", `Chat ${shortId(cid)} • ${list.length} match(es) • Best score ${best.score || 0}`);
      const groupPreview = el("div", "valabyss-preview", (best.userText || "").trim());

      const groupActions = el("div", "valabyss-actions");
      const showBtn = el("button", "valabyss-btn secondary", "Show Matches");
      const openBtn = el("button", "valabyss-btn ghost", "Open Source");
      const excludeBtn = el("button", "valabyss-btn ghost", "Exclude");

      const matchesEl = el("div", "valabyss-matches");
      matchesEl.style.display = "none";

      showBtn.addEventListener("click", ()=>{
        const next = matchesEl.style.display === "none";
        matchesEl.style.display = next ? "block" : "none";
        showBtn.textContent = next ? "Hide Matches" : "Show Matches";
      }, true);

      openBtn.addEventListener("click", ()=>{
        try { post({ type: "abyss.command.open_source", chatId: cid, truthPath: best.truthPath || "" }); } catch(_) {}
      }, true);

      excludeBtn.addEventListener("click", ()=>{
        if (!cid) return;
        if (!abyssExcludeChatIds.includes(cid)) abyssExcludeChatIds.push(cid);
        saveAbyssExclusions(abyssExcludeChatIds);
        if (abyssState.queryOriginal) {
          try {
            post({
              type: "abyss.command.search",
              chatId: getChatId(),
              queryOriginal: abyssState.queryOriginal,
              excludeChatIds: abyssExcludeChatIds,
              maxResults: 10
            });
          } catch(_) {}
        }
      }, true);

      groupActions.append(showBtn, openBtn, excludeBtn);
      groupHeader.append(groupTitle, groupActions);
      groupEl.append(groupHeader, groupPreview, matchesEl);

      list.forEach((match)=>{
        const item = el("div", "valabyss-match");
        const meta = el("div", "valabyss-meta", `#${match.index} • score ${match.score || 0} • U@${match.approxUserLine || "n/a"} A@${match.approxAssistantLineStart || "n/a"}-${match.approxAssistantLineEnd || "n/a"}`);
        const preview = el("div", "valabyss-preview", `${match.userText || ""}\n${match.assistantText || ""}`.trim());

        const actions = el("div", "valabyss-actions");
        const injectBtn = el("button", "valabyss-btn primary", "Inject");
        const openBtnItem = el("button", "valabyss-btn ghost", "Open Source");

        injectBtn.addEventListener("click", ()=>{
          try { post({ type: "abyss.command.inject_results", chatId: getChatId(), indices: String(match.index) }); } catch(_) {}
        }, true);

        openBtnItem.addEventListener("click", ()=>{
          try { post({ type: "abyss.command.open_source", chatId: match.chatId, truthPath: match.truthPath || "" }); } catch(_) {}
        }, true);

        actions.append(injectBtn, openBtnItem);
        item.append(meta, preview, actions);
        matchesEl.appendChild(item);
      });

      abyssResultsContainer.appendChild(groupEl);
    });
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

  function readComposerText(){
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

        if (el instanceof HTMLTextAreaElement) return el.value || "";
        if (el.isContentEditable) return (el.textContent || "");
      }
    } catch(_) {}

    return "";
  }

  function extractAbyssQuery(text){
    if (!text) return "";
    const trimmed = text.trim();
    if (!trimmed) return "";

    const lower = trimmed.toLowerCase();
    const prefixes = ["abyss query:", "abyss:", "find:"];

    for (const prefix of prefixes) {
      if (lower.startsWith(prefix)) {
        return trimmed.slice(prefix.length).trim();
      }
    }

    return trimmed;
  }

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

            if ((msg.type || "") === "abyss.results") {
              try {
                abyssState = {
                  queryOriginal: (msg.queryOriginal || "").toString(),
                  queryUsed: (msg.queryUsed || "").toString(),
                  generatedUtc: (msg.generatedUtc || "").toString(),
                  totalMatches: Number(msg.totalMatches || 0),
                  memoryRoot: (msg.memoryRoot || "").toString(),
                  results: Array.isArray(msg.results) ? msg.results : []
                };
                if (abyssResultsOverlay && abyssResultsOverlay.style.display !== "none") {
                  renderAbyssResults();
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
    const abyssInjectBtn = btn("Inject", "secondary");
    const abyssLastBtn = btn("Last", "ghost");
    const abyssFolderBtn = btn("Session Folder", "ghost");

    attachTooltip(abyssSearchBtn, "Inject an Abyss query prompt, then click again to search.");
    attachTooltip(abyssInjectBtn, "Inject selected results (e.g., 1 or 1,2).");
    attachTooltip(abyssLastBtn, "Recall the most recent exchange from the latest Truth.log.");
    attachTooltip(abyssFolderBtn, "Open the folder where this session’s memory is stored.");

    abyssSearchBtn.addEventListener("click",(e)=>{
      e.preventDefault();

      if (!getAbyssQueryPending()) {
        setAbyssQueryPending(true);
        try { post({ type:"abyss.command.inject_prompt", chatId: getChatId() }); } catch(_) {}
        return;
      }

      const raw = readComposerText();
      const query = extractAbyssQuery(raw);
      setAbyssQueryPending(false);

      try { post({ type:"abyss.command.search", chatId: getChatId(), query }); } catch(_) {}
    }, true);

    abyssInjectBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      let raw = "";
      try { raw = prompt("Inject Abyss result # (e.g., 1 or 1,2)", "1") || ""; } catch(_) { raw = ""; }
      if (!raw) return;

      const indices = raw
        .split(/[\\s,]+/)
        .map(v => parseInt(v, 10))
        .filter(v => Number.isFinite(v) && v > 0)
        .slice(0, 3);

      if (indices.length === 0) return;
      try { post({ type:"abyss.command.inject", chatId: getChatId(), indices }); } catch(_) {}
    }, true);

    abyssLastBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"abyss.command.last", chatId: getChatId(), count: 2, inject: true }); } catch(_) {}
    }, true);

    abyssFolderBtn.addEventListener("click",(e)=>{
      e.preventDefault();
      try { post({ type:"continuum.command.open_session_folder", chatId: getChatId() }); } catch(_) {}
    }, true);

    rowABtns.append(abyssSearchBtn, abyssInjectBtn, abyssLastBtn, abyssFolderBtn);


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
