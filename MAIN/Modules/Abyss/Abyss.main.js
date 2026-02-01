// Abyss.main.js — VAL-native recall UI
(() => {
  if (window.__VAL_ABYSS_BOOTED__) return;
  window.__VAL_ABYSS_BOOTED__ = true;

  console.log("[VAL Abyss] booted");

  const MAX_RESULTS = 4;

  const state = {
    lastQuery: "",
    lastQueryOriginal: "",
    generatedUtc: "",
    results: [],
    disregarded: new Set(),
    queryOpen: false,
    panelOpen: false
  };

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
      source: "abyss"
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

  function post(msg){
    try { window.chrome?.webview?.postMessage(toEnvelope(msg)); } catch(_) {}
  }

  function getChatId(){
    try {
      if (typeof window.VAL_Continuum_getChatId === "function") {
        const id = window.VAL_Continuum_getChatId();
        if (id) return id;
      }
    } catch(_) {}

    try {
      const m = (location.pathname || "").match(/\/c\/([a-f0-9\-]{36})/i);
      if (m) return m[1];
      const tail = (location.pathname || "").split("/").pop() || "";
      if (/^[a-f0-9\-]{36}$/i.test(tail)) return tail;
    } catch(_) {}

    return "session-" + Date.now().toString(36);
  }

  function focusComposerForPaste(){
    try {
      if (typeof window.VAL_focusComposerForPaste === "function") {
        return window.VAL_focusComposerForPaste();
      }
    } catch(_) {}

    try {
      const el = document.querySelector("#prompt-textarea, textarea[data-testid='prompt-textarea'], div.ProseMirror[contenteditable='true']");
      if (!el) return false;
      try { el.focus({ preventScroll: true }); } catch(_) { try { el.focus(); } catch(__) {} }
      try { el.click(); } catch(_) {}
      return true;
    } catch(_) {}

    return false;
  }

  function resetDisregarded(){
    state.disregarded = new Set();
  }

  function el(tag, cls, text){
    const node = document.createElement(tag);
    if (cls) node.className = cls;
    if (text != null) node.textContent = text;
    return node;
  }

  let queryWrap = null;
  let queryInput = null;
  let panel = null;
  let panelMeta = null;
  let resultsWrap = null;

  function ensureQueryUI(){
    if (queryWrap) return;

    queryWrap = el("div", "valabyss-query");
    const panelEl = el("div", "valabyss-query-panel");
    const title = el("div", "valabyss-query-title", "Abyss Recall");
    queryInput = document.createElement("input");
    queryInput.className = "valabyss-query-input";
    queryInput.type = "text";
    queryInput.placeholder = "[ Type your recall question… ]";

    const actions = el("div", "valabyss-query-actions");
    const goBtn = el("button", "valabyss-btn primary", "Go");
    const closeBtn = el("button", "valabyss-btn ghost", "Close");

    function runSearch(){
      const query = (queryInput?.value || "").trim();
      if (!query) return;
      if (state.lastQueryOriginal && state.lastQueryOriginal !== query) {
        resetDisregarded();
      }
      state.lastQueryOriginal = query;
      state.lastQuery = query;
      console.log("[VAL Abyss] search", query);
      post({
        type: "abyss.command.search",
        chatId: getChatId(),
        query,
        queryOriginal: query,
        excludeFingerprints: Array.from(state.disregarded),
        maxResults: MAX_RESULTS
      });
      hideQueryUI();
    }

    goBtn.addEventListener("click", (e)=>{ e.preventDefault(); runSearch(); }, true);
    closeBtn.addEventListener("click", (e)=>{ e.preventDefault(); hideQueryUI(); }, true);

    queryInput.addEventListener("keydown", (e)=>{
      if (e.key === "Enter") {
        e.preventDefault();
        runSearch();
      }
      if (e.key === "Escape") {
        e.preventDefault();
        hideQueryUI();
      }
    }, true);

    actions.append(goBtn, closeBtn);
    panelEl.append(title, queryInput, actions);
    queryWrap.appendChild(panelEl);
    document.body.appendChild(queryWrap);
  }

  function showQueryUI(prefill){
    ensureQueryUI();
    if (typeof prefill === "string") {
      queryInput.value = prefill;
    } else if (!queryInput.value && state.lastQueryOriginal) {
      queryInput.value = state.lastQueryOriginal;
    }

    queryWrap.style.display = "block";
    state.queryOpen = true;
    try { queryInput.focus(); queryInput.select(); } catch(_) {}
  }

  function hideQueryUI(){
    if (!queryWrap) return;
    queryWrap.style.display = "none";
    state.queryOpen = false;
  }

  function ensureResultsPanel(){
    if (panel) return;

    panel = el("div", "valabyss-panel");
    const header = el("div", "valabyss-panel-header");
    const title = el("div", "valabyss-panel-title", "Abyss Results");
    panelMeta = el("div", "valabyss-panel-meta", "");
    const actions = el("div", "valabyss-panel-actions");

    const retryBtn = el("button", "valabyss-btn", "Search again");
    const refineBtn = el("button", "valabyss-btn ghost", "Refine search");
    const clearBtn = el("button", "valabyss-btn ghost", "Clear results");

    retryBtn.addEventListener("click", (e)=>{
      e.preventDefault();
      if (!state.lastQuery) return;
      console.log("[VAL Abyss] retry", state.lastQuery);
      post({
        type: "abyss.command.retry_last",
        chatId: getChatId(),
        excludeFingerprints: Array.from(state.disregarded),
        maxResults: MAX_RESULTS
      });
    }, true);

    refineBtn.addEventListener("click", (e)=>{
      e.preventDefault();
      console.log("[VAL Abyss] refine");
      showQueryUI(state.lastQueryOriginal || state.lastQuery);
    }, true);

    clearBtn.addEventListener("click", (e)=>{
      e.preventDefault();
      console.log("[VAL Abyss] clear");
      clearResults();
      post({ type: "abyss.command.clear_results", chatId: getChatId() });
    }, true);

    actions.append(retryBtn, refineBtn, clearBtn);
    header.append(title, panelMeta, actions);

    resultsWrap = el("div", "valabyss-results");
    panel.append(header, resultsWrap);
    document.body.appendChild(panel);
  }

  function showResultsPanel(){
    ensureResultsPanel();
    panel.style.display = "flex";
    state.panelOpen = true;
  }

  function hideResultsPanel(){
    if (!panel) return;
    panel.style.display = "none";
    state.panelOpen = false;
  }

  function clearResults(){
    state.results = [];
    if (resultsWrap) resultsWrap.textContent = "";
    hideResultsPanel();
  }

  function formatLineRange(start, end){
    if (!start || !end) return "L?";
    return start === end ? `L${start}` : `L${start}–L${end}`;
  }

  function renderResults(){
    if (!resultsWrap || !panelMeta) return;

    const count = Array.isArray(state.results) ? state.results.length : 0;
    panelMeta.textContent = `Query: ${state.lastQueryOriginal || state.lastQuery || "—"}\nMatches: ${count}`;

    resultsWrap.textContent = "";

    if (count === 0) {
      resultsWrap.appendChild(el("div", "valabyss-empty", "No results to display."));
      return;
    }

    state.results.slice(0, MAX_RESULTS).forEach((result)=>{
      const card = el("div", "valabyss-card");
      card.dataset.resultId = result.id || result.fingerprint || "";

      const title = el("div", "valabyss-card-title", result.title || "Abyss Match");
      const preview = el("div", "valabyss-card-preview", result.preview || result.snippet || "");
      const snippet = el("div", "valabyss-card-snippet", result.snippet || "");
      const meta = el(
        "div",
        "valabyss-card-meta",
        `Source: ${result.chatId || "unknown"} • Truth.log ${formatLineRange(result.startLine, result.endLine)}`
      );

      const actions = el("div", "valabyss-card-actions");
      const injectBtn = el("button", "valabyss-btn primary", "Inject");
      const openBtn = el("button", "valabyss-btn", "Open Source");
      const disregardBtn = el("button", "valabyss-btn ghost", "Disregard");

      injectBtn.addEventListener("click", (e)=>{
        e.preventDefault();
        e.stopPropagation();
        focusComposerForPaste();
        console.log("[VAL Abyss] inject", result.id || result.fingerprint || result.index);
        post({
          type: "abyss.command.inject_result",
          chatId: getChatId(),
          id: result.id || result.fingerprint || "",
          index: result.index
        });
        clearResults();
      }, true);

      openBtn.addEventListener("click", (e)=>{
        e.preventDefault();
        e.stopPropagation();
        post({
          type: "abyss.command.open_source",
          chatId: result.chatId,
          startLine: result.startLine,
          endLine: result.endLine
        });
      }, true);

      disregardBtn.addEventListener("click", (e)=>{
        e.preventDefault();
        e.stopPropagation();
        const fingerprint = result.fingerprint || result.id;
        if (!fingerprint) return;
        console.log("[VAL Abyss] disregard", fingerprint);
        state.disregarded.add(fingerprint);
        state.results = state.results.filter((r)=> (r.fingerprint || r.id) !== fingerprint);
        post({ type: "abyss.command.disregard", chatId: getChatId(), fingerprint });
        renderResults();
      }, true);

      actions.append(injectBtn, openBtn, disregardBtn);
      card.append(title, preview, snippet, meta, actions);

      card.addEventListener("click", ()=>{
        card.classList.toggle("expanded");
      }, true);

      resultsWrap.appendChild(card);
    });
  }

  function handleHostMessage(msg){
    if (!msg) return;
    if (typeof msg === "string") {
      try { msg = JSON.parse(msg); } catch(_) { return; }
    }
    if (typeof msg !== "object") return;
    msg = unwrapEnvelope(msg);
    if ((msg.type || "") !== "abyss.results") return;

    const nextQuery = (msg.queryUsed || msg.lastQuery || "").toString();
    const nextQueryOriginal = (msg.queryOriginal || nextQuery || "").toString();
    if (state.lastQueryOriginal && nextQueryOriginal && state.lastQueryOriginal !== nextQueryOriginal) {
      resetDisregarded();
    }
    state.lastQuery = nextQuery;
    state.lastQueryOriginal = nextQueryOriginal;
    state.generatedUtc = (msg.generatedUtc || "").toString();
    const incoming = Array.isArray(msg.results) ? msg.results : [];
    state.results = incoming.filter((result)=> {
      const fingerprint = result?.fingerprint || result?.id;
      return !fingerprint || !state.disregarded.has(fingerprint);
    });

    try {
      window.__VAL_ABYSS_LAST_RESULTS__ = msg;
      window.__VAL_ABYSS_LAST_QUERY__ = state.lastQueryOriginal || state.lastQuery || "";
    } catch(_) {}

    showResultsPanel();
    renderResults();
  }

  function attachHostListener(){
    try {
      if (window.chrome?.webview?.addEventListener) {
        window.chrome.webview.addEventListener("message", (ev)=>{
          try { handleHostMessage(ev?.data); } catch(_) {}
        });
      }
    } catch(_) {}
  }

  function attachCommandListeners(){
    window.addEventListener("abyss.command.open_query_ui", ()=> showQueryUI(), true);
    window.addEventListener("abyss.command.clear_results", ()=> clearResults(), true);
  }

  attachHostListener();
  attachCommandListeners();
})();
