/* 线上链接冒烟检查：真实浏览器加载分享链接，确认样式/脚本加载成功、无 JS 异常 */
import { spawn } from "node:child_process";
import { writeFileSync } from "node:fs";

const URL_ = process.argv[2];
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9345;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const chrome = spawn(CHROME, [
  "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
  "--remote-debugging-port=" + PORT,
  "--user-data-dir=C:/Users/Sun Jinchen/AppData/Local/Temp/pm-live-profile",
  "about:blank",
], { stdio: "ignore" });

let id = 0;
const pending = new Map();
const problems = [];
const send = (ws, method, params = {}) => {
  const n = ++id;
  ws.send(JSON.stringify({ id: n, method, params }));
  return new Promise((res, rej) => {
    pending.set(n, { res, rej });
    setTimeout(() => pending.has(n) && (pending.delete(n), rej(new Error("timeout " + method))), 15000);
  });
};

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
    if (m.id && pending.has(m.id)) { const p = pending.get(m.id); pending.delete(m.id); return m.error ? p.rej(new Error(m.error.message)) : p.res(m.result); }
    if (m.method === "Runtime.exceptionThrown") problems.push("异常: " + (m.params.exceptionDetails.exception?.description || m.params.exceptionDetails.text));
    if (m.method === "Log.entryAdded" && m.params.entry.level === "error") {
      // 分享域名要求每个请求带 sharecode Cookie，而浏览器取 manifest 时不带 Cookie，
      // 必然 403 —— 只影响「安装到桌面」的图标清单，页面功能不受影响，这里忽略。
      if (/manifest\.webmanifest/.test(m.params.entry.text + m.params.entry.url)) return;
      problems.push("log: " + m.params.entry.text + " " + (m.params.entry.url || ""));
    }
  };
  await send(ws, "Page.enable");
  await send(ws, "Runtime.enable");
  await send(ws, "Log.enable");
  await send(ws, "Emulation.setDeviceMetricsOverride", { width: 390, height: 844, deviceScaleFactor: 2, mobile: true });
  await send(ws, "Page.navigate", { url: URL_ });
  await sleep(2500);

  const probe = await send(ws, "Runtime.evaluate", {
    expression: `(() => {
      const bar = document.querySelector('.tabbar');
      const ring = document.getElementById('ringScore');
      return JSON.stringify({
        title: document.title,
        cssOk: bar ? getComputedStyle(bar).position : 'missing',
        score: ring ? ring.textContent : null,
        tabs: document.querySelectorAll('.tab').length,
        apk: document.querySelector('#apkLink') ? document.querySelector('#apkLink').href : null,
        installVisible: document.getElementById('webInstall') ? !document.getElementById('webInstall').hidden : null,
        h: document.documentElement.scrollHeight
      });
    })()`, returnByValue: true,
  });
  console.log("probe:", probe.result.value);

  const shot = await send(ws, "Page.captureScreenshot", { format: "png", fromSurface: true });
  writeFileSync("E:/AIGC/pressure-map-mobile/docs/screenshots/live-today.png", Buffer.from(shot.data, "base64"));

  // 设置页底部：网页版应能看到「装到手机上」与 APK 下载
  await send(ws, "Runtime.evaluate", { expression: `document.querySelector('[data-tab="settings"]').click(); window.scrollTo(0, document.documentElement.scrollHeight); true` });
  await sleep(600);
  const shot2 = await send(ws, "Page.captureScreenshot", { format: "png", fromSurface: true });
  writeFileSync("E:/AIGC/pressure-map-mobile/docs/screenshots/live-settings.png", Buffer.from(shot2.data, "base64"));
  const shown = await send(ws, "Runtime.evaluate", {
    expression: `!document.getElementById('webInstall').hidden`, returnByValue: true,
  });
  if (!shown.result.value) problems.push("网页版没有显示「装到手机上」卡片");

  const apkRes = await send(ws, "Runtime.evaluate", {
    expression: `fetch(document.getElementById('apkLink').href, {method:'HEAD'}).then(r=>r.status+' '+r.headers.get('content-length')).catch(e=>'ERR '+e.message)`,
    awaitPromise: true, returnByValue: true,
  });
  console.log("apk:", apkRes.result.value);

  ws.close(); chrome.kill();
  console.log(problems.length ? "问题:\n - " + problems.join("\n - ") : "✅ 线上无异常");
  process.exitCode = problems.length ? 1 : 0;
}
main().catch((e) => { console.error("失败:", e.message); try { chrome.kill(); } catch { } process.exit(1); });
