/* 中英切换验收：真跑一遍界面，把英文模式下残留的中文全揪出来。
 *
 * pm-i18n.js 是「渲染后按文本替换」，漏词不会报错、只会静默露出中文，
 * 所以必须用运行时检查而不是静态扫源码：模板拼出来的句子（「第 3 周 · 周一 · 10月21日」）
 * 只有渲染出来才看得见。
 *
 * 用户自己写的内容（待办标题、课程名、教室…）本来就该保持中文，会从扫描结果里排除。
 *
 * 用法： node scripts/qa-i18n.mjs [--base=http://127.0.0.1:8799]
 */
import { spawn } from "node:child_process";
import { writeFileSync, mkdirSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
mkdirSync(join(ROOT, "docs", "screenshots"), { recursive: true });
const baseArg = process.argv.slice(2).find((a) => a.startsWith("--base="));
const BASE = baseArg ? baseArg.slice(7) : "http://127.0.0.1:8799";
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9341;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const chrome = spawn(CHROME, [
  "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
  "--no-default-browser-check", "--remote-debugging-port=" + PORT,
  "--user-data-dir=" + join(ROOT, "build", "chrome-i18n"), "about:blank",
], { stdio: "ignore" });

let seq = 0;
const pending = new Map();
const problems = [];

function send(ws, method, params = {}) {
  const id = ++seq;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => {
    pending.set(id, { res, rej });
    setTimeout(() => pending.has(id) && (pending.delete(id), rej(new Error("超时 " + method))), 15000);
  });
}

async function evaluate(ws, expression) {
  const r = await send(ws, "Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) problems.push("evaluate: " + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result?.value;
}

async function shot(ws, name) {
  const r = await send(ws, "Page.captureScreenshot", { format: "png", fromSurface: true });
  writeFileSync(join(ROOT, "docs", "screenshots", name + ".png"), Buffer.from(r.data, "base64"));
}

async function main() {
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`http://127.0.0.1:${PORT}/json/version`)).ok) break; } catch { }
    await sleep(150);
  }
  const target = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: "PUT" })).json();
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      return m.error ? p.rej(new Error(m.error.message)) : p.res(m.result);
    }
    if (m.method === "Runtime.exceptionThrown") {
      problems.push("未捕获异常: " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    }
  };

  await send(ws, "Page.enable");
  await send(ws, "Runtime.enable");
  await send(ws, "Network.enable");
  await send(ws, "Network.setCacheDisabled", { cacheDisabled: true });
  await send(ws, "Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });

  const loaded = new Promise((r) => {
    const h = (ev) => {
      const m = JSON.parse(ev.data);
      if (m.method === "Page.loadEventFired") { ws.removeEventListener("message", h); r(); }
    };
    ws.addEventListener("message", h);
  });
  await send(ws, "Page.navigate", { url: BASE + "/index.html" });
  await loaded;
  await sleep(600);
  await evaluate(ws, "localStorage.clear(); location.reload(); true");
  await sleep(1200);

  // 切到英文（走真实 UI，顺带验证设置页的接线）
  await evaluate(ws, `document.querySelector('[data-tab="settings"]').click(); true`);
  await sleep(350);
  const switched = await evaluate(ws, `(function(){
    var b = document.querySelector('[data-lang-opt="en"]');
    if (!b) return 'no-button';
    b.click();
    return PMUI.state().settings.language;
  })()`);
  if (switched !== "en") problems.push("语言开关没生效: " + switched);
  await sleep(500);

  // 用户自己写的内容不该被翻译，扫描时排除掉
  const userStrings = JSON.parse(await evaluate(ws, `(function(){
    var st = PMUI.state(), out = [];
    out.push(st.semester.name);
    (st.semester.courses || []).forEach(function(c){ out.push(c.name); if (c.location) out.push(c.location); if (c.teacher) out.push(c.teacher); });
    (st.semester.tasks || []).forEach(function(t){ if (t.title) out.push(t.title); });
    (st.todos || []).forEach(function(t){
      out.push(t.title); if (t.courseName) out.push(t.courseName);
      (t.steps || []).forEach(function(s){ out.push(s.title); });
    });
    (st.calendars || []).forEach(function(c){ if (c.name) out.push(c.name); });
    return JSON.stringify(out.filter(Boolean));
  })()`));

  const SCAN = `(function(user){
    var out = [];
    var w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null), n;
    function skip(el){
      if (!el) return true;
      if (/^(SCRIPT|STYLE|TEXTAREA)$/.test(el.nodeName)) return true;
      return !!(el.closest && el.closest('[data-i18n-skip]'));
    }
    function isUser(t){
      // 节点整串是用户内容，或者用户内容被拼进了界面模板（「2026 秋季学期（18 周）」）都算
      for (var i = 0; i < user.length; i++) {
        if (user[i].indexOf(t) >= 0) return true;
        if (t.length >= 2 && t.indexOf(user[i]) >= 0) return true;
      }
      return false;
    }
    while ((n = w.nextNode())) {
      if (skip(n.parentNode)) continue;
      var t = n.nodeValue.trim();
      if (!/[\u4e00-\u9fa5]/.test(t)) continue;
      if (isUser(t)) continue;
      out.push(t);
    }
    var els = document.querySelectorAll('[placeholder],[title]');
    for (var i = 0; i < els.length; i++) {
      ['placeholder','title'].forEach(function(a){
        var v = els[i].getAttribute(a);
        if (!v || !/[\u4e00-\u9fa5]/.test(v) || isUser(v.trim())) return;
        out.push('[' + a + '] ' + v.trim());
      });
    }
    return JSON.stringify(out);
  })(${JSON.stringify(userStrings)})`;

  const views = ["today", "calendar", "todos", "pressure", "ai", "settings"];
  const leftovers = new Map();
  for (const view of views) {
    await evaluate(ws, `document.querySelector('[data-tab="${view}"]').click(); true`);
    await sleep(450);
    const found = JSON.parse(await evaluate(ws, SCAN) || "[]");
    for (const text of found) {
      if (!leftovers.has(text)) leftovers.set(text, []);
      leftovers.get(text).push(view);
    }
  }

  // 弹层也扫一遍（新建待办表单里文案最多）
  await evaluate(ws, `document.querySelector('[data-tab="todos"]').click(); document.getElementById('fab').click(); true`);
  await sleep(500);
  for (const text of JSON.parse(await evaluate(ws, SCAN) || "[]")) {
    if (!leftovers.has(text)) leftovers.set(text, []);
    leftovers.get(text).push("sheet");
  }
  await evaluate(ws, "window.pmHandleBack()");

  // 切回中文必须完全复原（静态标签不在 render 的重画范围内，最容易漏）
  await evaluate(ws, `document.querySelector('[data-tab="settings"]').click(); document.querySelector('[data-lang-opt="zh"]').click(); true`);
  await sleep(600);
  const back = JSON.parse(await evaluate(ws, `(function(){
    var zh = 0, en = 0;
    var w = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT, null), n;
    while ((n = w.nextNode())) {
      var t = n.nodeValue.trim();
      if (!t) continue;
      if (/[\u4e00-\u9fa5]/.test(t)) zh++;
      else if (/^[A-Za-z][A-Za-z \u00b7\/&,.()-]{2,}$/.test(t) && /\b(Week|Tasks|Settings|Today|Courses|Semester|Load)\b/.test(t)) en++;
    }
    return JSON.stringify({ zh: zh, en: en, head: document.querySelector('#view-settings h2').textContent });
  })()`));
  if (back.head !== "学期管理") problems.push("切回中文后设置页标题没复原: " + back.head);
  if (back.en > 0) problems.push("切回中文后仍有 " + back.en + " 处英文残留");

  // 顺带留下英文界面的截图，方便人工比对排版
  await evaluate(ws, `document.querySelector('[data-tab="today"]').click(); true`);
  await sleep(400);
  await shot(ws, "en-today");
  await evaluate(ws, `document.querySelector('[data-tab="settings"]').click(); window.scrollTo(0, 0); true`);
  await sleep(400);
  await shot(ws, "en-settings");

  const list = [...leftovers.keys()];
  ws.close();
  chrome.kill();
  await sleep(200);

  if (list.length) {
    console.log("❌ 英文模式下还有 " + list.length + " 处中文：");
    for (const t of list) console.log("  - " + JSON.stringify(t) + "  (" + [...new Set(leftovers.get(t))].join(",") + ")");
    if (problems.length) console.log("\n运行期问题：\n - " + problems.join("\n - "));
    process.exitCode = 1;
  } else if (problems.length) {
    console.log("❌ 没有残留中文，但运行期有问题：\n - " + problems.join("\n - "));
    process.exitCode = 1;
  } else {
    console.log("✅ 英文模式没有残留中文（6 个页面 + 弹层）");
  }
}

main().catch((e) => {
  console.error("失败:", e.message);
  try { chrome.kill(); } catch { }
  process.exit(1);
});
