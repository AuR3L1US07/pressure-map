/* 服务器线上验收：官网 Android 卡片、手机版 /app/、安装包下载
 * 用法： node deploy/check-mobile-live.mjs [http://121.43.32.85]
 */
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = dirname(dirname(fileURLToPath(import.meta.url)));
const OUT = join(ROOT, "verification");
mkdirSync(OUT, { recursive: true });

const BASE = (process.argv[2] || "http://121.43.32.85").replace(/\/$/, "");
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9351;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const chrome = spawn(CHROME, [
  "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
  "--remote-debugging-port=" + PORT, "--user-data-dir=" + join(ROOT, "..", "build", "chrome-live"),
  "about:blank",
], { stdio: "ignore" });

let id = 0;
const pending = new Map();
const problems = [];
const note = (m) => console.log("  · " + m);

const send = (ws, method, params = {}) => {
  const n = ++id;
  ws.send(JSON.stringify({ id: n, method, params }));
  return new Promise((res, rej) => {
    pending.set(n, { res, rej });
    setTimeout(() => pending.has(n) && (pending.delete(n), rej(new Error("timeout " + method))), 20000);
  });
};

async function evalJs(ws, expression) {
  const r = await send(ws, "Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) problems.push("evaluate: " + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result?.value;
}

async function shot(ws, name) {
  const r = await send(ws, "Page.captureScreenshot", { format: "png", fromSurface: true });
  writeFileSync(join(OUT, name + ".png"), Buffer.from(r.data, "base64"));
}

async function main() {
  for (let i = 0; i < 100; i++) {
    try { if ((await fetch(`http://127.0.0.1:${PORT}/json/version`)).ok) break; } catch { }
    await sleep(150);
  }
  const t = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: "PUT" })).json();
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });
  ws.onmessage = (e) => {
    const m = JSON.parse(e.data);
    if (m.id && pending.has(m.id)) {
      const p = pending.get(m.id); pending.delete(m.id);
      return m.error ? p.rej(new Error(m.error.message)) : p.res(m.result);
    }
    if (m.method === "Runtime.exceptionThrown") problems.push("异常: " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    if (m.method === "Log.entryAdded" && m.params.entry.level === "error") problems.push("log: " + m.params.entry.text + " " + (m.params.entry.url || ""));
  };
  await send(ws, "Page.enable");
  await send(ws, "Runtime.enable");
  await send(ws, "Log.enable");

  // ---------- 1. 手机版 /app/ ----------
  await send(ws, "Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  await send(ws, "Page.navigate", { url: BASE + "/app/" });
  await sleep(2200);
  const app = JSON.parse(await evalJs(ws, `(() => {
    const bar = document.querySelector('.tabbar');
    const link = document.getElementById('apkLink');
    return JSON.stringify({
      title: document.title,
      css: bar ? getComputedStyle(bar).position : 'missing',
      tabs: document.querySelectorAll('.tab').length,
      apk: link ? link.href : null,
      score: document.getElementById('ringScore')?.textContent
    });
  })()`));
  note("app: " + JSON.stringify(app));
  if (app.css !== "fixed") problems.push("/app/ 样式没生效（.tabbar position=" + app.css + "）");
  if (app.tabs !== 6) problems.push("/app/ tab 数量不是 6");
  if (!app.apk || !app.apk.endsWith("/download/PressureMap-android-2.3.0.apk")) problems.push("/app/ 安装包链接不对: " + app.apk);
  await shot(ws, "app-today-390");

  await evalJs(ws, `document.querySelector('[data-tab="settings"]').click(); window.scrollTo(0, document.documentElement.scrollHeight); true`);
  await sleep(600);
  const install = JSON.parse(await evalJs(ws, `(() => {
    const c = document.getElementById('webInstall');
    return JSON.stringify({ shown: c ? !c.hidden : null, href: document.getElementById('apkLink').href });
  })()`));
  note("install card: " + JSON.stringify(install));
  if (!install.shown) problems.push("/app/ 没有显示「装到手机上」卡片");
  await shot(ws, "app-settings-390");

  const apkStatus = await evalJs(ws, `fetch(document.getElementById('apkLink').href).then(r=>r.status+':'+r.headers.get('content-length')).catch(e=>'ERR')`);
  note("apk fetch: " + apkStatus);
  if (!String(apkStatus).startsWith("200")) problems.push("/app/ 安装包下载失败: " + apkStatus);

  // ---------- 2. 官网首页下载区 ----------
  await send(ws, "Emulation.setEmulatedMedia", {
    features: [{ name: "prefers-reduced-motion", value: "reduce" }],
  });
  await send(ws, "Emulation.setDeviceMetricsOverride", { width: 1440, height: 1000, deviceScaleFactor: 1, mobile: false });
  await send(ws, "Page.navigate", { url: BASE + "/" });
  await sleep(2200);
  // 语言会被记住，这里先固定回中文再做断言
  await evalJs(ws, `document.querySelector('[data-lang="zh"]').click(); true`);
  await sleep(400);

  const windows = JSON.parse(await evalJs(ws, `JSON.stringify((() => {
    const el = document.querySelector('[data-platform="windows"]');
    return {tag:el.tagName, href:el.getAttribute('href'), pending:el.classList.contains('pending'), text:el.textContent};
  })())`));
  note("windows card: " + JSON.stringify(windows));
  if (windows.tag !== "A" || windows.pending || windows.href !== "download/PressureMap-2.0.0-setup.exe" || !windows.text.includes("2.0.0")) problems.push("Windows download card invalid");
  const card = JSON.parse(await evalJs(ws, `(() => {
    const a = document.querySelector('[data-platform="android"]');
    const note = document.querySelector('.web-note');
    return JSON.stringify({
      tag: a ? a.tagName : null,
      href: a ? a.getAttribute('href') : null,
      download: a ? a.getAttribute('download') : null,
      pending: a ? a.classList.contains('pending') : null,
      label: a ? a.querySelector('span')?.textContent : null,
      meta: a ? (a.querySelector('.release-meta')?.textContent || '') : '',
      webNote: note ? note.textContent.trim() : null,
      webHref: note ? note.querySelector('a')?.getAttribute('href') : null
    });
  })()`));
  note("android card: " + JSON.stringify(card));
  if (card.tag !== "A") problems.push("Android 卡片没有变成下载链接（tag=" + card.tag + "）");
  if (!card.href || !card.href.endsWith("download/PressureMap-android-2.3.0.apk")) problems.push("Android 卡片 href 不对: " + card.href);
  if (card.pending) problems.push("Android 卡片仍带 pending 状态");
  if (!/2\.3\.0/.test(card.meta) || !/526 KB/.test(card.meta)) problems.push("Android 卡片没有显示版本信息: " + card.meta);
  if (card.webHref !== "app/") problems.push("网页版入口链接不对: " + card.webHref);
  if (!card.webNote || card.webNote.indexOf("手机版") < 0) problems.push("网页版入口文案不对: " + card.webNote);

  await evalJs(ws, `(() => { const el = document.getElementById('download'); window.scrollTo(0, el.getBoundingClientRect().top + window.scrollY - 80); return true; })()`);
  await sleep(1200);
  await shot(ws, "download-1440-zh");

  // 英文模式
  await evalJs(ws, `document.querySelector('[data-lang="en"]').click(); true`);
  await sleep(500);
  const en = JSON.parse(await evalJs(ws, `(() => {
    const a = document.querySelector('[data-platform="android"]');
    const note = document.querySelector('.web-note');
    return JSON.stringify({ label: a.querySelector('span')?.textContent, webNote: note.textContent.trim() });
  })()`));
  note("en: " + JSON.stringify(en));
  if (!/Download/i.test(en.label)) problems.push("英文模式下 Android 卡片文案没翻译: " + en.label);
  if (!/[A-Za-z]/.test(en.webNote)) problems.push("英文模式下网页版入口没翻译: " + en.webNote);
  await shot(ws, "download-1440-en");

  ws.close();
  chrome.kill();
  const report = {
    base: BASE,
    checkedAt: new Date().toISOString(),
    app: { title: app.title, cssApplied: app.css === "fixed", tabs: app.tabs, apkLink: app.apk, installCard: install.shown, apkFetch: apkStatus },
    site: { androidCard: card, english: en },
    problems,
    ok: problems.length === 0,
  };
  writeFileSync(join(OUT, "mobile-v1.json"), JSON.stringify(report, null, 2) + "\n");
  console.log(problems.length ? "\n❌ 问题：\n - " + problems.join("\n - ") : "\n✅ 线上验收通过");
  process.exitCode = problems.length ? 1 : 0;
}

main().catch((e) => { console.error("失败:", e.message, e.stack); try { chrome.kill(); } catch { } process.exit(1); });
