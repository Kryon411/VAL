"use strict";

const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");
const vm = require("node:vm");

const root = path.resolve(__dirname, "..");
let checks = 0;
function check(name, action) {
  action();
  checks++;
  console.log(`PASS ${name}`);
}
function scripts(directory) {
  return fs.readdirSync(directory, { withFileTypes: true }).flatMap(entry => {
    const file = path.join(directory, entry.name);
    return entry.isDirectory() ? scripts(file) : file.endsWith(".js") ? [file] : [];
  });
}
for (const file of [...scripts(path.join(root, "MAIN", "Dock")), ...scripts(path.join(root, "MAIN", "Modules"))]) {
  check(`syntax ${path.relative(root, file)}`, () => new vm.Script(fs.readFileSync(file, "utf8"), { filename: file }));
}

const policySource = fs.readFileSync(path.join(root, "MAIN", "VAL.Host", "Security", "WebViewScriptPolicy.cs"), "utf8");
const originPolicySource = fs.readFileSync(path.join(root, "MAIN", "VAL.Host", "Security", "WebOriginPolicy.cs"), "utf8");
const guardTemplate = /internal const string GuardTemplate = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(policySource)?.[1];
const authPathsSource = /AuthenticationPathPrefixes =\s*\{([\s\S]*?)\};/.exec(originPolicySource)?.[1];
assert.ok(guardTemplate && authPathsSource, "Document-created guards must be covered by client validation.");
const authPaths = [...authPathsSource.matchAll(/"([^"]+)"/g)].map(match => match[1]);
assert.ok(authPaths.length > 0);
const guardedFixture = guardTemplate.replace("__VAL_AUTH_PATHS__", JSON.stringify(authPaths))
  .replace("__VAL_SCRIPT__", "window.fixtureModuleLoaded = true; window.__VAL_NONCE = 'synthetic-nonce';");
function guardedPage(source, topLevel = true) {
  const url = new URL(source);
  const window = { location: { origin: url.origin, pathname: url.pathname } };
  window.top = topLevel ? window : {};
  vm.runInNewContext(guardedFixture, { window });
  return window;
}
function assertNoModuleOrNonce(source, topLevel = true) {
  const window = guardedPage(source, topLevel);
  assert.equal(window.fixtureModuleLoaded, undefined);
  assert.equal(window.__VAL_NONCE, undefined);
}
const stylesheetSource = fs.readFileSync(path.join(root, "MAIN", "VAL.Host", "ModuleStylesheetScript.cs"), "utf8");
const stylesheetTemplate = /internal const string InjectionTemplate = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(stylesheetSource)?.[1];
assert.ok(stylesheetTemplate, "Production stylesheet injection must be covered by client validation.");
function stylesheetFixture(source = "https://chatgpt.com/", ready = true) {
  const nodes = new Map();
  const callbacks = [];
  const container = { appendChild(node) { nodes.set(node.id, node); } };
  const document = {
    head: ready ? container : null, documentElement: null, body: null, readyState: ready ? "complete" : "loading",
    getElementById(id) { return nodes.get(id); }, createElement(tag) { return { tag }; },
    addEventListener(event, callback) { assert.equal(event, "DOMContentLoaded"); callbacks.push(callback); }
  };
  const url = new URL(source);
  const window = { location: { origin: url.origin, pathname: url.pathname } };
  window.top = window;
  return { nodes, ready() { document.head = container; callbacks.forEach(callback => callback()); },
    inject(id, css) {
      const injection = stylesheetTemplate.replace("__VAL_STYLE_ID__", JSON.stringify(id)).replace("__VAL_STYLE_CSS__", JSON.stringify(css));
      const guarded = guardTemplate.replace("__VAL_AUTH_PATHS__", JSON.stringify(authPaths)).replace("__VAL_SCRIPT__", injection);
      vm.runInNewContext(guarded, { window, document, setTimeout(callback) { callbacks.push(callback); } });
    }
  };
}
check("multiple module styles inject independently and repeated registration is idempotent", () => {
  const page = stylesheetFixture();
  for (let repeat = 0; repeat < 2; repeat++) {
    page.inject("val-style-dock-base", ".valdock-panel { position: fixed; }");
    page.inject("val-style-dock-controls", "[data-val-ui='1'] { --val-rim: 4px; }");
  }
  assert.equal(page.nodes.size, 2);
  assert.match(page.nodes.get("val-style-dock-controls").textContent, /--val-rim: 4px/);
});
check("multiple module styles survive document-created head timing", () => {
  const page = stylesheetFixture("https://chatgpt.com/", false);
  page.inject("base", "base-css"); page.inject("controls", "controls-css");
  assert.equal(page.nodes.size, 0);
  page.ready();
  assert.equal(page.nodes.size, 2);
});
check("shared control stylesheet stays absent on authentication pages", () => {
  for (const source of ["https://accounts.google.com/", "https://chatgpt.com/auth/login"]) {
    const page = stylesheetFixture(source);
    page.inject("controls", "controls-css");
    assert.equal(page.nodes.size, 0);
  }
});
check("document guard loads modules and nonce on normal chat pages", () => {
  for (const source of ["https://chatgpt.com/", "https://chat.openai.com/c/fixture", "https://chatgpt.com/g/fixture"]) {
    const window = guardedPage(source);
    assert.equal(window.fixtureModuleLoaded, true);
    assert.equal(window.__VAL_NONCE, "synthetic-nonce");
  }
});
check("document guard omits modules and nonce from external authentication providers", () => {
  for (const host of ["auth.openai.com", "accounts.google.com", "appleid.apple.com", "login.microsoftonline.com"]) {
    assertNoModuleOrNonce(`https://${host}/login`);
  }
});
check("document guard omits modules and nonce from every same-origin authentication route", () => {
  for (const host of ["chatgpt.com", "chat.openai.com"]) {
    for (const prefix of authPaths) {
      assertNoModuleOrNonce(`https://${host}${prefix}`);
      assertNoModuleOrNonce(`https://${host}${prefix}/callback?token=synthetic`);
    }
  }
});
check("document guard blocks encoded and case-varied authentication paths", () => {
  for (const route of ["/AUTH/login", "/%61uth/login", "/auth%2Flogin", "/auth%5Clogin", "/%2561uth/login", "/%E0%A4%A"]) {
    assertNoModuleOrNonce(`https://chatgpt.com${route}`);
  }
});
check("document guard matches whole path segments, not queries or conversation names", () => {
  for (const route of ["/authentication", "/c/auth", "/g/project-login", "/?next=/auth/login"]) {
    assert.equal(guardedPage(`https://chatgpt.com${route}`).fixtureModuleLoaded, true);
  }
});
check("document guard never installs modules or nonce in a child frame", () => {
  assertNoModuleOrNonce("https://chatgpt.com/c/fixture", false);
});
check("document guard rejects unsafe origins and non-default ports", () => {
  for (const source of ["http://chatgpt.com/", "https://chatgpt.com:8443/", "https://chatgpt.com.example.test/"]) {
    assertNoModuleOrNonce(source);
  }
});

const portalSource = fs.readFileSync(path.join(root, "MAIN", "VAL.Host", "Services", "PortalRuntimeService.cs"), "utf8");
const focusScript = /internal const string FocusScript = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(portalSource)?.[1];
assert.ok(focusScript, "Portal focus script must be covered by client validation.");
function editable(overrides = {}) {
  return {
    isConnected: true, value: "", textContent: "", disabled: false, readOnly: false,
    getAttribute() { return null; }, contains() { return false; }, focus() {}, dispatchEvent() {},
    getBoundingClientRect() { return { width: 400, height: 60, top: 700, bottom: 760 }; },
    ...overrides
  };
}
function portalFocus(element, acceptsFocus = true) {
  const document = { activeElement: null, querySelectorAll: selector => selector === 'textarea#mobile-composer-prompt' ? [element] : [] };
  element.focus = () => { if (acceptsFocus) document.activeElement = element; };
  return vm.runInNewContext(focusScript, { document, window: { innerHeight: 900 }, getComputedStyle: () => ({ display: 'block', visibility: 'visible' }) });
}
check("Portal embedded focus script parses and focuses a visible mobile composer", () => {
  assert.equal(portalFocus(editable()), true);
});
check("Portal does not report success when the composer refuses focus", () => {
  assert.equal(portalFocus(editable(), false), false);
});
check("Portal focusing preserves an existing draft without submitting it", () => {
  const element = editable({ value: "VAL synthetic unsent Portal draft" });
  assert.equal(portalFocus(element), true);
  assert.equal(element.value, "VAL synthetic unsent Portal draft");
});
check("Portal rejects hidden, disconnected, disabled and read-only composers", () => {
  for (const overrides of [{ isConnected: false }, { disabled: true }, { readOnly: true }, { getBoundingClientRect: () => ({ width: 0, height: 0 }) }]) {
    assert.equal(portalFocus(editable(overrides)), false);
  }
});
check("Portal does not focus an unrelated contenteditable field", () => {
  let focused = false;
  const element = editable({ focus() { focused = true; } });
  const result = vm.runInNewContext(focusScript, {
    document: { querySelectorAll: selector => selector === 'div[contenteditable="true"]' ? [element] : [] },
    window: { innerHeight: 900 }, getComputedStyle: () => ({ display: 'block', visibility: 'visible' })
  });
  assert.equal(result, false);
  assert.equal(focused, false);
});

const continuumPath = path.join(root, "MAIN", "Modules", "Continuum", "Client", "Continuum.Client.js");
const continuumSource = fs.readFileSync(continuumPath, "utf8");
const hook = "  function init() {";
assert.equal(continuumSource.split(hook).length, 2, "Test hook must identify exactly one module initialization boundary.");
const instrumented = continuumSource.replace(hook,
  "globalThis.clientTest = { state, handleHostMessage, injectIntoComposer, tryAutoSendSeed, tryInjectPendingSeed, composerReady, getNewChatInjectionTarget, _plainTextToProseHtml, handleTurn, initializeSafely, findSendButton, startSendSafetyHooks, findScrollContainer, getChronicleScrollMetrics, setChronicleScrollTop, chronicleScrollIsStalled, startChronicle };\n" + hook);
const currentId = "11111111-1111-1111-1111-111111111111";
const staleId = "22222222-2222-2222-2222-222222222222";
const posted = [];
const context = {
  console: { log() {}, error(message) { throw new Error(message); } },
  location: { pathname: `/c/${currentId}`, href: `https://chatgpt.com/c/${currentId}` },
  document: { readyState: "loading", addEventListener() {}, contains: () => true, querySelectorAll: () => [] },
  window: { innerHeight: 900, __VAL_NONCE: "test", addEventListener() {}, chrome: { webview: { postMessage: message => posted.push(message) } } },
  getComputedStyle: () => ({ display: "block", visibility: "visible" }),
};
vm.createContext(context);
vm.runInContext(instrumented, context, { filename: continuumPath });
function send(type, payload) {
  posted.length = 0;
  context.clientTest.handleHostMessage({ data: { type, ...payload } });
  return posted;
}
check("stale capture flush does not relabel another conversation", () => {
  const messages = send("continuum.capture.flush", { chatId: staleId, requestId: "request" });
  assert.equal(messages[0].payload.error, "chat_changed");
  assert.equal(context.clientTest.state.chatId, null);
});
check("stale Chronicle start fails without rebuilding another conversation", () => {
  const messages = send("continuum.chronicle.start", { chatId: staleId, requestId: "request" });
  assert.equal(messages[0].payload.error, "chat_changed");
  assert.equal(context.clientTest.state.chronicle.active, false);
});
check("stale injection does not change the current composer", () => {
  const messages = send("continuum.inject_text", { chatId: staleId, text: "private archive" });
  assert.equal(messages[0].payload.evt, "refresh.inject.failed:chat_changed");
  assert.equal(context.clientTest.state.pendingSeed, null);
});
check("existing user drafts are not overwritten", () => {
  const element = editable({ value: "unsent draft" });
  assert.equal(context.clientTest.injectIntoComposer("archive", { kind: "textarea", el: element }), false);
  assert.equal(element.value, "unsent draft");
});
check("injection expires rather than waiting indefinitely on a draft", () => {
  posted.length = 0;
  context.clientTest.state.pendingSeed = {
    essence: "archive", targetChatId: currentId, originChatId: currentId,
    createdAt: Date.now() - 121000, requestedAt: Date.now(), requestId: "expired"
  };
  assert.equal(context.clientTest.tryInjectPendingSeed(), false);
  assert.equal(context.clientTest.state.pendingSeed, null);
  assert.equal(posted[0].payload.evt, "refresh.inject.failed:timeout");
});
check("plain-text injection escapes HTML", () => {
  const html = context.clientTest._plainTextToProseHtml('<img src=x onerror="alert(1)">\nhello');
  assert.ok(!html.includes("<img"));
  assert.ok(html.includes("&lt;img"));
  assert.ok(html.includes("<p>hello</p>"));
});
check("successful injection echoes the exact host request ID", () => {
  const textarea = editable();
  context.document.querySelectorAll = selector => selector === "textarea#mobile-composer-prompt" ? [textarea] : [];
  posted.length = 0;
  context.clientTest.state.pendingSeed = {
    essence: "packet", mode: "Signal", requestId: "exact-request", targetChatId: currentId,
    originChatId: currentId, createdAt: Date.now(), requestedAt: Date.now(), requireNewChat: false
  };
  assert.equal(context.clientTest.tryInjectPendingSeed(), true);
  const success = posted.find(message => message.payload?.evt === "refresh.inject.success:Signal:current_chat");
  assert.equal(success.payload.requestId, "exact-request");
  assert.equal(textarea.value, "packet");
  context.document.querySelectorAll = () => [];
});
check("injection rejects hidden, disconnected, disabled and read-only fields", () => {
  for (const overrides of [{ isConnected: false }, { disabled: true }, { readOnly: true }, { getBoundingClientRect: () => ({ width: 0, height: 0 }) }]) {
    const element = editable(overrides);
    assert.equal(context.clientTest.injectIntoComposer("packet", { kind: "textarea", el: element }), false);
    assert.equal(element.value, "");
  }
});
check("ProseMirror drafts are preserved and blank fields retain paragraph layout", () => {
  const draft = editable({ textContent: "Unsent draft", innerHTML: "<p>Unsent draft</p>" });
  assert.equal(context.clientTest.injectIntoComposer("packet", { kind: "prosemirror", el: draft }), false);
  assert.equal(draft.innerHTML, "<p>Unsent draft</p>");
  const blank = editable();
  assert.equal(context.clientTest.injectIntoComposer("first\n\nlast", { kind: "prosemirror", el: blank }), true);
  assert.equal(blank.innerHTML, "<p>first</p><p></p><p>last</p>");
});
check("composer discovery skips hidden matches and unrelated editable fields", () => {
  const hidden = editable({ disabled: true });
  const visible = editable();
  context.document.querySelectorAll = selector => selector === "textarea#prompt-textarea" ? [hidden, visible] : [];
  assert.equal(context.clientTest.composerReady().el, visible);
  context.document.querySelectorAll = selector => selector === "div[contenteditable='true']" ? [visible] : [];
  assert.equal(context.clientTest.composerReady(), null);
  context.document.querySelectorAll = selector => selector === "form textarea" ? [visible] : [];
  assert.equal(context.clientTest.composerReady(), null);
  context.document.querySelectorAll = () => [];
});
check("new-chat injection waits for the host to attach the new conversation", () => {
  const element = editable();
  context.document.querySelectorAll = selector => selector === "textarea#prompt-textarea" ? [element] : [];
  context.clientTest.state.hostAttachedChatId = staleId;
  const seed = { originChatId: staleId };
  assert.equal(context.clientTest.getNewChatInjectionTarget(seed).waitEvt, "refresh.inject.waiting_for_attach");
  context.clientTest.state.hostAttachedChatId = currentId;
  assert.equal(context.clientTest.getNewChatInjectionTarget(seed).ready, true);
  context.document.querySelectorAll = () => [];
});
check("automatic send refuses a changed conversation", () => {
  assert.equal(context.clientTest.tryAutoSendSeed({ autoSend: true, requestId: "request" }, staleId), false);
});
check("cancelled injection removes its pending seed and suppresses delayed auto-send", () => {
  context.clientTest.state.pendingSeed = { essence: "private", requestId: "cancelled" };
  send("continuum.inject.cancel", { requestId: "cancelled" });
  assert.equal(context.clientTest.state.pendingSeed, null);
  assert.equal(context.clientTest.tryAutoSendSeed({ autoSend: true, requestId: "cancelled" }, currentId), false);
  send("continuum.inject_text", { chatId: currentId, requestId: "cancelled", text: "private" });
  assert.equal(context.clientTest.state.pendingSeed, null);
});
check("cancelling an older request preserves the current injection", () => {
  context.clientTest.state.pendingSeed = { essence: "current", requestId: "current" };
  send("continuum.inject.cancel", { requestId: "older" });
  assert.equal(context.clientTest.state.pendingSeed.requestId, "current");
  context.clientTest.state.pendingSeed = null;
});
check("current ChatGPT composer is narrowly discovered and drafts are preserved", () => {
  const element = editable({ textContent: "synthetic unsent draft", innerHTML: "<p>synthetic unsent draft</p>" });
  context.document.querySelectorAll = selector => selector === "form[data-chatgpt-composer] [data-composer-markdown][contenteditable='true'][role='textbox']" ? [element] : [];
  assert.equal(context.clientTest.composerReady().el, element);
  assert.equal(context.clientTest.injectIntoComposer("packet"), false);
  assert.equal(element.textContent, "synthetic unsent draft");
  context.document.querySelectorAll = () => [];
});
check("Portal focuses the current ChatGPT composer without a prompt-textarea ID", () => {
  const document = { activeElement: null };
  const element = editable({ focus() { document.activeElement = element; } });
  document.querySelectorAll = selector => selector === 'form[data-chatgpt-composer] [data-composer-markdown][contenteditable="true"][role="textbox"]' ? [element] : [];
  assert.equal(vm.runInNewContext(focusScript, { document, window: { innerHeight: 900 }, getComputedStyle: () => ({ display: 'block', visibility: 'visible' }) }), true);
});
function currentTurn(role, id, text, actionsInTurn = false) {
  const body = { innerText: text };
  return {
    isConnected: true,
    getBoundingClientRect: () => ({ width: 600, height: 100 }),
    matches: selector => selector === "[data-chatgpt-search-unit-key]",
    closest: selector => actionsInTurn && selector === "[data-turn-key]" ? {
      querySelector: candidate => candidate.includes("copy-turn-action-button") ? {} : null
    } : null,
    getAttribute: name => name === "data-chatgpt-search-unit-key" ? id : null,
    querySelector: selector => {
      if (role === "user" && selector === "[data-user-message-bubble]") return body;
      if (role === "assistant" && selector === "[data-markdown-text-style]") return body;
      if (role === "assistant" && selector === "[data-conversation-role='assistant']") return {};
      if (role === "assistant" && !actionsInTurn && selector.includes("copy-turn-action-button")) return {};
      return null;
    },
    parentElement: null, nextElementSibling: null, previousElementSibling: null
  };
}
check("current ChatGPT units capture user and settled assistant once without UI labels", () => {
  context.performance = { now: () => 1000 };
  context.document.querySelector = () => null;
  context.clientTest.state.seenTruthFp.clear();
  posted.length = 0;
  const user = currentTurn("user", "synthetic-user", "Synthetic prompt");
  const assistant = currentTurn("assistant", "synthetic-assistant", "Synthetic reply");
  assert.equal(context.clientTest.handleTurn(user), true);
  assert.equal(context.clientTest.handleTurn(assistant), true);
  assert.equal(context.clientTest.handleTurn(user), false);
  assert.equal(context.clientTest.handleTurn(assistant), false);
  const captures = posted.filter(message => message.name === "continuum.truth");
  assert.equal(captures.length, 2);
  assert.match(captures[0].payload.line, /\[U\].*Synthetic prompt$/);
  assert.match(captures[1].payload.line, /\[A\].*Synthetic reply$/);
});
check("current Stop button prevents partial assistant capture", () => {
  context.document.querySelector = selector => selector.includes("form[data-chatgpt-composer] button[aria-label='Stop']") ? {} : null;
  assert.equal(context.clientTest.handleTurn(currentTurn("assistant", "streaming", "partial")), false);
  context.document.querySelector = () => null;
});
check("hidden or wrong-conversation retained units are never captured", () => {
  for (const hidden of [
    { isConnected: false },
    { getBoundingClientRect: () => ({ width: 0, height: 0 }) },
    { closest: selector => selector.includes("[hidden]") ? {} : null }
  ]) {
    assert.equal(context.clientTest.handleTurn(Object.assign(currentTurn("user", "hidden-unit", "Do not capture"), hidden)), false);
  }
  const otherChat = currentTurn("user", "other-chat-unit", "Other chat");
  otherChat.closest = selector => selector === "[data-turn-key]" ? {
    querySelector: () => ({ getAttribute: () => staleId })
  } : null;
  assert.equal(context.clientTest.handleTurn(otherChat), false);
});
check("new-chat blank-shell detection ignores hidden retained history, not rendered offscreen turns", () => {
  const element = editable();
  const hidden = Object.assign(currentTurn("user", "hidden-history", "Old history"), {
    getBoundingClientRect: () => ({ width: 0, height: 0 })
  });
  context.location.pathname = "/";
  context.document.querySelectorAll = selector => selector === "textarea#prompt-textarea" ? [element] :
    selector.includes("data-chatgpt-search-unit-key") ? [hidden, hidden, hidden] : [];
  assert.equal(context.clientTest.getNewChatInjectionTarget({ originChatId: currentId }).ready, true);
  const rendered = currentTurn("user", "visible-history", "Rendered history");
  rendered.getBoundingClientRect = () => ({ width: 600, height: 100, top: -2000, bottom: -1900 });
  context.document.querySelectorAll = selector => selector === "textarea#prompt-textarea" ? [element] :
    selector.includes("data-chatgpt-search-unit-key") ? [rendered, rendered] : [];
  assert.equal(context.clientTest.getNewChatInjectionTarget({ originChatId: currentId }).waitEvt, "refresh.inject.waiting_for_blank_shell");
  context.location.pathname = `/c/${currentId}`;
  context.document.querySelectorAll = () => [];
});
check("current assistant actions may be outside the unit but must stay inside its turn", () => {
  assert.equal(context.clientTest.handleTurn(currentTurn("assistant", "turn-actions", "Completed reply", true)), true);
  const unfinished = currentTurn("assistant", "no-actions", "Unfinished reply", true);
  unfinished.closest = () => null;
  assert.equal(context.clientTest.handleTurn(unfinished), false);
});
check("current Send discovery stays inside the known composer form", () => {
  const button = { disabled: false, getAttribute: () => null };
  context.document.querySelector = selector => selector === "form[data-chatgpt-composer] button[aria-label='Send']" ? button : null;
  assert.equal(context.clientTest.findSendButton(), button);
  context.document.querySelector = () => null;
});
check("Chronicle scrolls the current conversation ancestor, not the longer sidebar or clipped document", () => {
  const root = { scrollHeight: 20000, clientHeight: 800, isConnected: true };
  const sidebar = { scrollHeight: 30000, clientHeight: 800, isConnected: true };
  const thread = { scrollHeight: 2000, clientHeight: 800, isConnected: true, parentElement: root, overflowY: "auto" };
  const turn = currentTurn("user", "scroll-turn", "Synthetic");
  turn.parentElement = thread;
  context.document.scrollingElement = root;
  context.getComputedStyle = element => ({ display: "block", visibility: "visible", overflowY: element.overflowY || "hidden" });
  context.document.querySelectorAll = selector => selector.includes("data-chatgpt-search-unit-key") ? [turn] : [sidebar];
  assert.equal(context.clientTest.findScrollContainer(), thread);
  context.document.querySelectorAll = () => [];
  context.getComputedStyle = () => ({ display: "block", visibility: "visible" });
  delete context.document.scrollingElement;
});
check("Chronicle recognizes stalled scrolling without rejecting bottom or real advancement", () => {
  const geometry = { scrollHeight: 1500, clientHeight: 500 };
  assert.equal(context.clientTest.chronicleScrollIsStalled({ ...geometry, scrollTop: 0 }, 0, 500), true);
  assert.equal(context.clientTest.chronicleScrollIsStalled({ ...geometry, scrollTop: 500 }, 0, 500), false);
  assert.equal(context.clientTest.chronicleScrollIsStalled({ ...geometry, scrollTop: 500 }, 500, 500), false);
});
check("Chronicle normalizes reversed flex scrolling from oldest to newest", () => {
  const scroller = { scrollHeight: 1928, clientHeight: 650, scrollTop: 0 };
  context.getComputedStyle = () => ({ flexDirection: "column-reverse" });
  assert.equal(context.clientTest.getChronicleScrollMetrics(scroller).top, 1278);
  context.clientTest.setChronicleScrollTop(scroller, 0);
  assert.equal(scroller.scrollTop, -1278);
  assert.equal(context.clientTest.getChronicleScrollMetrics(scroller).top, 0);
  context.clientTest.setChronicleScrollTop(scroller, 500);
  assert.equal(scroller.scrollTop, -778);
  assert.equal(context.clientTest.chronicleScrollIsStalled(scroller, 0, 500), false);
  context.clientTest.setChronicleScrollTop(scroller, 1278);
  assert.equal(scroller.scrollTop, 0);
  assert.equal(context.clientTest.chronicleScrollIsStalled(scroller, 1278, 1278), false);
  context.clientTest.setChronicleScrollTop(scroller, -100);
  assert.equal(scroller.scrollTop, -1278);
  context.clientTest.setChronicleScrollTop(scroller, 2000);
  assert.equal(scroller.scrollTop, 0);
  context.getComputedStyle = () => ({ display: "block", visibility: "visible" });
});
check("module initialization reaches the observer and polling lifecycle", () => {
  const timers = [];
  context.setInterval = (callback, interval) => { timers.push(interval); return timers.length; };
  context.clearInterval = () => {};
  context.setTimeout = () => 1;
  context.MutationObserver = class { observe() {} };
  context.document.documentElement = {};
  context.clientTest.initializeSafely();
  assert.equal(context.window.__VAL_CONTINUUM_HEALTH.initialized, true);
  assert.equal(context.window.__VAL_CONTINUUM_HEALTH.failed, false);
  assert.ok(timers.includes(1000), "capture polling must start");
});
function functionSlice(source, name, nextName) {
  const start = source.indexOf(`  function ${name}(`);
  const end = source.indexOf(`  function ${nextName}(`, start);
  assert.ok(start >= 0 && end > start, `Missing function boundary for ${name}`);
  return source.slice(start, end);
}
const dockSource = fs.readFileSync(path.join(root, "MAIN", "Dock", "Dock.main.js"), "utf8");
const layoutWindow = {};
vm.runInNewContext(fs.readFileSync(path.join(root, "MAIN", "Dock", "Dock.Layout.js"), "utf8"), { window:layoutWindow });
const dockLayout = layoutWindow.VAL_DockLayout;
function headerRect(left, top, width, height = 36) {
  return { left, top, width, height, right:left + width, bottom:top + height };
}
check("signed-out header anchors before the full account action cluster", () => {
  const anchor = dockLayout.selectHeaderAnchor([headerRect(50, 10, 100), headerRect(748, 10, 70), headerRect(830, 10, 154)], 1000);
  assert.equal(anchor.left, 748);
  const orb = dockLayout.resolveLauncher(anchor, 1000, 700);
  assert.equal(orb.x, 698);
  assert.ok(orb.x + orb.w < anchor.left);
});
check("signed-in Share, Summary and menu actions form one stable anchor", () => {
  const anchor = dockLayout.selectHeaderAnchor([headerRect(790, 8, 90), headerRect(888, 8, 36), headerRect(936, 8, 48)], 1000);
  assert.equal(anchor.left, 790);
  const panel = dockLayout.resolvePanel({}, false, anchor, 1000, 700);
  assert.equal(panel.w, 560);
  assert.equal(panel.x + panel.w, anchor.right);
  assert.ok(panel.y > anchor.bottom);
});
check("header selection ignores sidebar, hidden and below-header controls", () => {
  assert.equal(dockLayout.selectHeaderAnchor([headerRect(30, 12, 100), headerRect(900, 200, 80), headerRect(970, 10, 0)], 1000), null);
});
check("compact header retains its leftmost action across the viewport midpoint", () => {
  const actions = [headerRect(104, 8, 42), headerRect(154, 8, 76), headerRect(238, 8, 28)];
  const anchor = dockLayout.selectHeaderAnchor(actions, 320);
  assert.equal(anchor.left, 104);
  const orb = dockLayout.resolveLauncher(anchor, 320, 800);
  for (const action of actions) {
    assert.ok(orb.x + orb.w <= action.left || orb.x >= action.right ||
      orb.y + orb.h <= action.top || orb.y >= action.bottom);
  }
});
check("fallback and crowded header layouts do not overlap header actions", () => {
  assert.equal(dockLayout.resolveLauncher(null, 1000, 700).x, 948);
  const anchor = { left:30, top:8, right:288, bottom:44 };
  assert.ok(dockLayout.resolveLauncher(anchor, 300, 500).y > anchor.bottom);
});
check("narrow panels remain within viewport without destroying preferred size", () => {
  const preferred = { x:700, y:400, w:700, h:600 };
  const narrow = dockLayout.resolvePanel(preferred, true, null, 320, 400);
  assert.equal(narrow.w, 296);
  assert.ok(narrow.x >= 12 && narrow.x + narrow.w <= 308);
  assert.ok(narrow.y >= 12 && narrow.y + narrow.h <= 388);
  const wide = dockLayout.resolvePanel(preferred, true, null, 1600, 1000);
  assert.equal(wide.w, 700);
  assert.equal(preferred.w, 700);
});
check("direct drag and resize expose pointer cancellation and keyboard alternatives", () => {
  assert.ok(dockSource.includes('moveBar.addEventListener("pointerdown"'));
  assert.ok(dockSource.includes('resizeHandle.addEventListener("keydown"'));
  assert.ok(dockSource.includes('document.addEventListener("pointercancel"'));
  assert.ok(dockSource.includes('resetPlacement: true, isCustom: false'));
  assert.ok(!dockSource.includes('if (!layoutMode) return;'));
});
function dockFocusFixture() {
  const document = { activeElement:null };
  class Control {
    constructor(inDock = false) {
      this.inDock = inDock;
      this.isConnected = true;
      this.attributes = {};
      this.style = { display:"block", visibility:"visible" };
      this.value = "Unsent synthetic draft";
    }
    hasAttribute(name) { return name in this.attributes; }
    getAttribute(name) { return this.attributes[name] ?? null; }
    getBoundingClientRect() { return { width:80, height:32 }; }
    focus() { document.activeElement = this; }
    blur() { if (document.activeElement === this) document.activeElement = null; }
  }
  const previous = new Control();
  const active = new Control(true);
  document.activeElement = active;
  const fixture = {
    document, HTMLElement:Control,
    dock:{ contains:node => !!node?.inDock },
    lastFocusedElement:previous, launcher:null,
    window:{ getComputedStyle:node => node.style }
  };
  return {
    fixture, previous, active, Control,
    restore() {
      vm.runInNewContext(functionSlice(dockSource, "restoreDockFocus", "focusFirstDockControl") + "restoreDockFocus();", fixture);
    }
  };
}
check("dock dismissal restores the previous page control without changing its draft", () => {
  const test = dockFocusFixture();
  test.restore();
  assert.equal(test.fixture.document.activeElement, test.previous);
  assert.equal(test.previous.value, "Unsent synthetic draft");
  assert.ok(dockSource.indexOf("if (state.collapsed) restoreDockFocus();") <
    dockSource.indexOf('panel.style.display = state.collapsed ? "none" : "flex";'), "Restore focus before hiding its source control.");
});
check("dock dismissal skips stale, hidden and disabled origins and uses a valid DOM launcher", () => {
  for (const invalidate of [
    node => { node.isConnected = false; },
    node => { node.style.display = "none"; },
    node => { node.style.visibility = "hidden"; },
    node => { node.attributes.disabled = ""; },
    node => { node.attributes["aria-disabled"] = "true"; },
    node => { node.inDock = true; }
  ]) {
    const test = dockFocusFixture();
    invalidate(test.previous);
    test.fixture.launcher = new test.Control();
    test.restore();
    assert.equal(test.fixture.document.activeElement, test.fixture.launcher);
  }
});
check("dock dismissal clears hidden-panel focus when no safe origin remains", () => {
  const test = dockFocusFixture();
  test.previous.isConnected = false;
  test.restore();
  assert.equal(test.fixture.document.activeElement, null);
});
check("dock dismissal proves focus restoration and tries a safe fallback if the origin refuses", () => {
  const test = dockFocusFixture();
  test.previous.focus = () => {};
  test.fixture.launcher = new test.Control();
  test.restore();
  assert.equal(test.fixture.document.activeElement, test.fixture.launcher);
});
check("dock dismissal clears panel focus if all restoration targets refuse or throw", () => {
  const test = dockFocusFixture();
  test.previous.focus = () => { throw new Error("Synthetic focus failure"); };
  test.fixture.launcher = new test.Control();
  test.fixture.launcher.focus = () => {};
  test.restore();
  assert.equal(test.fixture.document.activeElement, null);
});
check("dock dismissal does not steal focus already moved outside the panel", () => {
  const test = dockFocusFixture();
  const outside = new test.Control();
  test.fixture.document.activeElement = outside;
  test.restore();
  assert.equal(test.fixture.document.activeElement, outside);
});
const abyssSource = fs.readFileSync(path.join(root, "MAIN", "Modules", "Abyss", "Abyss.main.js"), "utf8");
check("Control Centre typography is readable without double-fading secondary text", () => {
  const css = fs.readFileSync(path.join(root, "MAIN", "Dock", "Dock.styles.css"), "utf8");
  const rule = selector => {
    const escaped = selector.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
    return new RegExp(escaped + "\\s*\\{([^}]+)\\}").exec(css)?.[1] || "";
  };
  assert.match(rule(".valdock-layout-movebar"), /font-size:\s*16px/);
  assert.match(rule(".valdock-layout-movebar"), /color:\s*var\(--val-text/);
  assert.match(rule(".valdock-btn"), /font-size:\s*14px/);
  assert.match(rule(".valdock-section-title"), /font-size:\s*15px/);
  for (const selector of [".valdock-section-subtitle", ".valdock-section-hint", ".valdock-status", ".valdock-row-note"]) {
    assert.match(rule(selector), /font-size:\s*13px/);
    assert.match(rule(selector), /color:\s*var\(--val-muted/);
    assert.doesNotMatch(rule(selector), /opacity/);
  }
  assert.match(rule(".valdock-btn-disabled"), /opacity:\s*0\.45/);
  assert.match(css, /container-name:\s*val-control-centre/);
  assert.match(css, /@container val-control-centre \(max-width:\s*350px\)/);
  assert.match(css, /\.valdock-panel \[data-val-ui="1"\]\.valdock-section-header\s*\{\s*grid-template-columns:\s*minmax\(0, 1fr\)/);
});
check("approved control skin is VAL-scoped and retains frozen colours and noninteractive faces", () => {
  const skin = fs.readFileSync(path.join(root, "MAIN", "Dock", "VAL.Controls.css"), "utf8");
  for (const token of ["--val-highlight: #66768b", "--val-reflection: #1a314a", "--val-blue: #193f6b",
    "--val-blue-soft: rgba(23,77,142,0.35)", "--val-sheen: rgba(117,148,182,0.10510510510510511)",
    "--val-face-top: #152130", "--val-face-bottom: #0a111b", "--val-local-rim: 3.2px", "--val-local-rim: 2.6px"]) {
    assert.ok(skin.includes(token), `Approved token changed: ${token}`);
  }
  assert.ok(skin.includes("pointer-events: none"));
  assert.ok(skin.includes(":focus-visible"));
  assert.ok(skin.includes("prefers-reduced-motion:reduce"));
  assert.ok(!/button\[data-val-ui="1"\]:is\([^)]*\.valdock-close[^)]*\)\s*\{\s*position:\s*relative/.test(skin), "The close control must retain its existing absolute anchor.");
  assert.ok(!/(^|\n)\s*(?:body|html|:root|button(?!\[data-val-ui))\b/.test(skin));
  const manifest = JSON.parse(fs.readFileSync(path.join(root, "MAIN", "Dock", "Dock.module.json"), "utf8"));
  assert.ok(manifest.styles.includes("VAL.Controls.css"));
});
const dockFocus = functionSlice(dockSource, "focusComposerForPaste", "showDockToast") +
  functionSlice(dockSource, "getComposerElement", "appendTextToTextarea") + "focusComposerForPaste();";
const abyssFocus = functionSlice(abyssSource, "focusComposerForPaste", "resetDisregarded") + "focusComposerForPaste();";
function abyssFixture() {
  const nodes = [];
  const posted = [];
  const commands = {};
  let receive;
  function node(tag) {
    const element = {
      tag, style: {}, dataset: {}, children: [], handlers: {}, value: "", textContent: "",
      attributes: {},
      setAttribute(name, value) { this.attributes[name] = String(value); },
      getAttribute(name) { return this.attributes[name] ?? null; },
      classList: { toggle() {} },
      append(...children) { this.children.push(...children); },
      appendChild(child) { this.children.push(child); },
      addEventListener(name, callback) { this.handlers[name] = callback; },
      focus() {}, select() {}
    };
    nodes.push(element);
    return element;
  }
  const fixture = {
    console: { log() {} }, location: { pathname: `/c/${currentId}` },
    document: { createElement: node, body: node("body") },
    window: {
      __VAL_NONCE: "fixture",
      addEventListener(name, callback) { commands[name] = callback; },
      chrome: { webview: {
        postMessage(message) { posted.push(message); },
        addEventListener(name, callback) { if (name === "message") receive = callback; }
      } }
    }
  };
  vm.runInNewContext(abyssSource, fixture);
  const click = text => {
    const button = nodes.find(element => element.tag === "button" && element.textContent === text);
    assert.ok(button, `Missing Abyss button ${text}`);
    button.handlers.click({ preventDefault() {}, stopPropagation() {} });
  };
  return {
    fixture, posted, click, nodes, command(name) { commands[name](); },
    search(query) {
      commands["abyss.command.open_query_ui"]();
      nodes.find(element => element.className === "valabyss-query-input").value = query;
      click("Go");
      return posted.at(-1).payload;
    },
    results(request, query = request.query, overrides = {}) {
      receive({ data: { type: "event", name: "abyss.results", chatId: currentId, payload: {
        requestId: request.requestId, queryUsed: query, queryOriginal: query,
        results: [{ id: "synthetic", chatId: currentId, snippet: "Synthetic recall", startLine: 1, endLine: 2 }],
        ...overrides
      } } });
    },
    panel() { return nodes.find(element => element.className === "valabyss-panel"); }
  };
}
check("Abyss ignores an older response after a newer search intent", () => {
  const abyss = abyssFixture();
  const older = abyss.search("older synthetic query");
  const newer = abyss.search("newer synthetic query");
  abyss.results(newer);
  abyss.results(older);
  assert.equal(abyss.fixture.window.__VAL_ABYSS_LAST_QUERY__, "newer synthetic query");
});
check("Abyss framing preserves the real input and keyboard search handler", () => {
  const abyss = abyssFixture();
  abyss.command("abyss.command.open_query_ui");
  const input = abyss.nodes.find(node => node.className === "valabyss-query-input");
  const frame = abyss.nodes.find(node => node.className === "val-ui-input-frame");
  assert.equal(frame.getAttribute("data-val-ui"), "1");
  assert.ok(frame.children.includes(input));
  input.value = "synthetic framed input";
  input.handlers.keydown({ key: "Enter", preventDefault() {} });
  assert.equal(abyss.posted.at(-1).payload.query, input.value);
});
check("Abyss Clear acknowledgement and delayed results cannot reopen its panel", () => {
  const abyss = abyssFixture();
  const search = abyss.search("synthetic query");
  abyss.results(search);
  abyss.click("Clear results");
  const clear = abyss.posted.at(-1).payload;
  abyss.results(clear, "", { results: [], cleared: true });
  abyss.results(search);
  assert.equal(abyss.panel().style.display, "none");
});
check("Abyss gives repeated identical queries different identities and rejects uncorrelated responses", () => {
  const abyss = abyssFixture();
  const first = abyss.search("same query");
  const second = abyss.search("same query");
  assert.notEqual(first.requestId, second.requestId);
  abyss.results(first);
  abyss.results(second, second.query, { requestId: null });
  assert.equal(abyss.panel(), undefined);
  abyss.results(second);
  assert.equal(abyss.panel().style.display, "flex");
});
check("Abyss drops results after navigation and invalidates them on local panel dismissal", () => {
  const abyss = abyssFixture();
  const search = abyss.search("synthetic query");
  abyss.fixture.location.pathname = `/c/${staleId}`;
  abyss.results(search);
  assert.equal(abyss.panel(), undefined);
  abyss.fixture.location.pathname = `/c/${currentId}`;
  abyss.results(search);
  abyss.command("abyss.command.clear_results");
  abyss.results(search);
  assert.equal(abyss.panel().style.display, "none");
});
check("Abyss recent recall and retry use the same request correlation path as search", () => {
  const abyss = abyssFixture();
  assert.equal(abyss.fixture.window.VAL_Abyss_requestResults("portal.command.send", {}), false);
  assert.equal(abyss.posted.length, 0);
  abyss.fixture.window.VAL_Abyss_requestResults("abyss.command.last", { chatId: currentId, count: 2 });
  const recent = abyss.posted.at(-1).payload;
  abyss.results(recent, "(Last)");
  abyss.click("Search again");
  const retry = abyss.posted.at(-1).payload;
  assert.notEqual(recent.requestId, retry.requestId);
  assert.equal(retry.type, "abyss.command.retry_last");
  assert.equal(retry.chatId, currentId);
  abyss.results(retry, "(Last)", { results: [] });
  assert.equal(abyss.panel().style.display, "flex");
});
check("Dock routes recall requests through Abyss correlation but preserves other commands", () => {
  const abyss = abyssFixture();
  const direct = [];
  vm.runInNewContext(functionSlice(dockSource, "sendCommand", "renderItem") +
    'sendCommand("abyss.command.last", { count: 2 }, true, "fixture");' +
    'sendCommand("portal.command.send", {}, true, "fixture");', {
    window: abyss.fixture.window, getChatId: () => currentId, post: message => direct.push(message)
  });
  assert.equal(abyss.posted.length, 1);
  assert.ok(abyss.posted[0].payload.requestId);
  assert.equal(abyss.posted[0].chatId, currentId);
  assert.equal(direct.length, 1);
  assert.equal(direct[0].type, "portal.command.send");
});
for (const [name, script] of [["Dock", dockFocus], ["Abyss", abyssFocus]]) {
  check(`${name} focus accepts only visible known composers and proves focus`, () => {
    const document = { activeElement: null };
    const element = editable({ focus() { document.activeElement = element; } });
    const run = () => vm.runInNewContext(script, { document, window: { innerHeight: 900 }, getComputedStyle: () => ({ display: "block", visibility: "visible" }) });
    document.querySelectorAll = selector => selector === "form[data-chatgpt-composer] [data-composer-markdown][contenteditable='true'][role='textbox']" ? [element] : [];
    assert.equal(run(), true);
    document.activeElement = null;
    element.focus = () => {};
    assert.equal(run(), false);
    for (const overrides of [{ isConnected: false }, { disabled: true }, { readOnly: true }, { getBoundingClientRect: () => ({ width: 0, height: 0 }) }]) {
      const rejected = editable(overrides);
      document.querySelectorAll = selector => selector === "textarea#prompt-textarea" ? [rejected] : [];
      assert.equal(run(), false);
    }
    for (const selector of ["form textarea", "div.ProseMirror[contenteditable='true']", "[role='textbox'][contenteditable='true']", "textarea[placeholder*='Message']"]) {
      document.querySelectorAll = candidate => candidate === selector ? [element] : [];
      assert.equal(run(), false);
    }
  });
}
check("send safety observes current composer sends but ignores unrelated editors", () => {
  const handlers = {};
  context.document.addEventListener = (name, handler) => { handlers[name] = handler; };
  context.clientTest.startSendSafetyHooks();
  context.clientTest.state.hostAttachedChatId = null;
  context.clientTest.state.lastAttachPostAt = 0;
  const target = { closest: selector => selector.includes("form[data-chatgpt-composer]") ? {} : null };
  posted.length = 0;
  handlers.keydown({ key: "Enter", target });
  assert.ok(posted.some(message => message.name === "continuum.session.attach"));
  context.clientTest.state.lastAttachPostAt = 0;
  posted.length = 0;
  handlers.click({ target });
  assert.ok(posted.some(message => message.name === "continuum.session.attach"));
  posted.length = 0;
  handlers.keydown({ key: "Enter", target: { closest: () => null } });
  assert.equal(posted.length, 0);
});
const observerSource = fs.readFileSync(path.join(root, "MAIN", "VAL.App", "Host", "Services", "ValidationBrowserObserver.cs"), "utf8");
const snapshotScript = /internal const string SnapshotScript = """\r?\n([\s\S]*?)\r?\n\s*""";/.exec(observerSource)?.[1];
assert.ok(snapshotScript, "Interactive diagnostics script must be validated.");
check("browser validation snapshots exclude content and refuse other origins and child frames", () => {
  const document = { readyState: "complete", querySelectorAll: () => [] };
  const window = { chrome: { webview: { postMessage() {} } }, __VAL_CONTINUUM_HEALTH: { initialized: true } };
  window.top = window;
  const metrics = vm.runInNewContext(snapshotScript, { window, document, location: { origin: "https://chatgpt.com" } });
  assert.equal(metrics.clientInitialized, true);
  assert.deepEqual(Object.keys(metrics), ['ready', 'bridge', 'clientInitialized', 'clientFailed', 'pendingInjection', 'awaitingNewChat', 'awaitingSignal', 'newChatRoot', 'valControls', 'turns', 'messages', 'userRoles', 'assistantRoles', 'copyActions', 'promptFields', 'stopButtons', 'distinctUnitKeys', 'renderedUnits', 'scrollAncestors', 'captureUnits', 'structure']);
  assert.equal(metrics.valControls.dockStylesheets, 0);
  assert.equal(metrics.valControls.rim, null);
  assert.doesNotMatch(snapshotScript, /innerText|textContent|innerHTML|cookie|localStorage|sessionStorage|\.value\b/);
  assert.equal(vm.runInNewContext(snapshotScript, { window, document, location: { origin: "https://auth.openai.com" } }), null);
  window.top = {};
  assert.equal(vm.runInNewContext(snapshotScript, { window, document, location: { origin: "https://chatgpt.com" } }), null);
});
async function chronicleTraversal({ reversed = false, stuck = false, growing = false, stallAfterTop = false, cancelDuringCapture = false, navigateDuringCapture = false, gradual = false } = {}) {
  const messages = [];
  let height = 2000;
  let rawTop = reversed ? 0 : 1200;
  const scroller = { isConnected: true, clientHeight: 800, parentElement: null };
  Object.defineProperties(scroller, {
    scrollHeight: { get: () => height },
    scrollTop: {
      get: () => rawTop,
      set: value => {
        if (!stuck && !(stallAfterTop && value > 0)) {
          const target = Math.max(reversed ? 800 - height : 0, Math.min(reversed ? 0 : height - 800, value));
          rawTop = gradual && value > 0 ? rawTop + (target - rawTop) * 0.3 : target;
        }
        if (growing && value > 0) height += 1000;
        if (cancelDuringCapture && value > 0) fixture.clientTest.state.chronicle.cancelRequested = true;
        if (navigateDuringCapture && value > 0) fixture.location.pathname = `/c/${staleId}`;
      }
    }
  });
  const turns = [currentTurn("user", "chronicle-first", "First synthetic turn"), currentTurn("user", "chronicle-last", "Last synthetic turn")];
  for (const turn of turns) turn.parentElement = scroller;
  const fixture = {
    console: { log() {}, error(message) { throw new Error(message); } },
    location: { pathname: `/c/${currentId}` },
    document: {
      readyState: "loading", addEventListener() {}, contains: () => true, querySelector: () => null,
      querySelectorAll: selector => selector.includes("data-chatgpt-search-unit-key") ? turns : [],
      scrollingElement: scroller
    },
    window: { __VAL_NONCE: "fixture", addEventListener() {}, chrome: { webview: { postMessage: message => messages.push(message) } } },
    getComputedStyle: element => ({ display: "flex", visibility: "visible", overflowY: "auto", flexDirection: element === scroller && reversed ? "column-reverse" : "column" }),
    performance: { now: () => 1000 }, setTimeout: callback => { callback(); return 1; }, clearTimeout() {}
  };
  vm.createContext(fixture);
  vm.runInContext(instrumented, fixture, { filename: continuumPath });
  await fixture.clientTest.startChronicle("chronicle-fixture", currentId);
  const completions = messages.filter(message => message.name === "continuum.chronicle.done");
  assert.equal(completions.length, 1);
  assert.equal(completions[0].payload.requestId, "chronicle-fixture");
  assert.equal(completions[0].chatId, currentId);
  assert.equal(fixture.clientTest.state.chronicle.active, false);
  assert.equal(fixture.clientTest.state.chronicle.freezeWrites, false);
  if (!completions[0].payload.error) {
    const metrics = fixture.clientTest.getChronicleScrollMetrics(scroller);
    assert.ok(metrics.max - metrics.top <= 1, "Successful capture must reach the actual bottom, not merely request it");
  }
  return completions[0].payload;
}
async function finishAsyncChecks() {
  for (const reversed of [false, true]) {
    const result = await chronicleTraversal({ reversed });
    assert.equal(result.error, undefined);
    assert.equal(result.capturedTurns, 2);
    checks++;
    console.log(`PASS Chronicle complete traversal and request cleanup (${reversed ? "reversed" : "normal"} flex)`);
  }
  const stuck = await chronicleTraversal({ stuck: true });
  assert.equal(stuck.error, "top_not_reached");
  checks++;
  console.log("PASS Chronicle refuses completion when the oldest scroll boundary is unreachable");
  for (const [options, error] of [
    [{ stallAfterTop: true }, "scroll_stalled"],
    [{ cancelDuringCapture: true }, "cancelled"],
    [{ navigateDuringCapture: true }, "chat_changed"]
  ]) {
    const result = await chronicleTraversal(options);
    assert.equal(result.error, error);
    checks++;
    console.log(`PASS Chronicle aborts and clears request state on ${error}`);
  }
  const growing = await chronicleTraversal({ growing: true });
  assert.equal(growing.error, "capture_incomplete");
  checks++;
  console.log("PASS Chronicle refuses completion after exhausting traversal without reaching the bottom");
  const gradual = await chronicleTraversal({ gradual: true });
  assert.equal(gradual.error, undefined);
  checks++;
  console.log("PASS Chronicle waits for actual bottom arrival during gradual scrolling");
  console.log(`${checks} client checks passed.`);
}
finishAsyncChecks().catch(error => { console.error(error); process.exitCode = 1; });
