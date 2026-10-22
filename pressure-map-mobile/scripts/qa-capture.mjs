/* 手机端 UI 截图与运行时检查（Chrome DevTools Protocol，无第三方依赖）
 *
 * 用法： node scripts/qa-capture.mjs [--base http://127.0.0.1:8799]
 * 产物： docs/screenshots/<theme>-<view>.png
 * 退出码： 有 JS 异常时返回 1
 */
import { spawn } from "node:child_process";
import { mkdirSync, writeFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const OUT = join(ROOT, "docs", "screenshots");
mkdirSync(OUT, { recursive: true });

const args = process.argv.slice(2);
const baseArg = args.find((a) => a.startsWith("--base="));
const BASE = baseArg ? baseArg.slice(7) : "http://127.0.0.1:8799";

const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9333;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const chrome = spawn(CHROME, [
  "--headless=new",
  "--disable-gpu",
  "--hide-scrollbars",
  "--no-first-run",
  "--no-default-browser-check",
  "--remote-debugging-port=" + PORT,
  "--user-data-dir=" + join(ROOT, "build", "chrome-profile"),
  "about:blank",
], { stdio: "ignore" });

async function waitForEndpoint() {
  for (let i = 0; i < 100; i++) {
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/version`);
      if (res.ok) return;
    } catch { }
    await sleep(150);
  }
  throw new Error("Chrome 调试端口没有就绪");
}

let msgId = 0;
const pending = new Map();
const problems = [];

function send(ws, method, params = {}) {
  const id = ++msgId;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((resolve, reject) => {
    pending.set(id, { resolve, reject });
    setTimeout(() => {
      if (pending.has(id)) {
        pending.delete(id);
        reject(new Error("CDP 超时: " + method));
      }
    }, 15000);
  });
}

async function evaluate(ws, expression) {
  const res = await send(ws, "Runtime.evaluate", {
    expression,
    awaitPromise: true,
    returnByValue: true,
  });
  if (res.exceptionDetails) {
    problems.push("evaluate: " + (res.exceptionDetails.exception?.description || res.exceptionDetails.text));
  }
  return res.result?.value;
}

async function shot(ws, name) {
  const res = await send(ws, "Page.captureScreenshot", { format: "png", fromSurface: true });
  const file = join(OUT, name + ".png");
  writeFileSync(file, Buffer.from(res.data, "base64"));
  console.log("  截图 " + name + ".png");
}

async function main() {
  await waitForEndpoint();
  const target = await (await fetch(`http://127.0.0.1:${PORT}/json/new?about:blank`, { method: "PUT" })).json();
  const ws = new WebSocket(target.webSocketDebuggerUrl);
  await new Promise((r, j) => { ws.onopen = r; ws.onerror = j; });

  ws.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(new Error(msg.error.message));
      else resolve(msg.result);
      return;
    }
    if (msg.method === "Runtime.exceptionThrown") {
      const d = msg.params.exceptionDetails;
      problems.push("未捕获异常: " + (d.exception?.description || d.text) + " @" + (d.url || "") + ":" + d.lineNumber);
    }
    if (msg.method === "Runtime.consoleAPICalled" && msg.params.type === "error") {
      problems.push("console.error: " + msg.params.args.map((a) => a.value || a.description || "").join(" "));
    }
    if (msg.method === "Log.entryAdded" && msg.params.entry.level === "error") {
      problems.push("log: " + msg.params.entry.text + " " + (msg.params.entry.url || ""));
    }
  };

  await send(ws, "Page.enable");
  await send(ws, "Runtime.enable");
  await send(ws, "Log.enable");
  await send(ws, "Network.enable");
  await send(ws, "Network.setCacheDisabled", { cacheDisabled: true });
  await send(ws, "Emulation.setDeviceMetricsOverride", {
    width: 390, height: 844, deviceScaleFactor: 2, mobile: true,
    screenOrientation: { type: "portraitPrimary", angle: 0 },
  });

  const themes = ["light", "dark"];
  const views = ["today", "calendar", "todos", "pressure", "ai", "settings"];

  for (const theme of themes) {
    await send(ws, "Emulation.setEmulatedMedia", {
      features: [{ name: "prefers-color-scheme", value: theme }],
    });
    const loaded = new Promise((r) => {
      const handler = (ev) => {
        const msg = JSON.parse(ev.data);
        if (msg.method === "Page.loadEventFired") { ws.removeEventListener("message", handler); r(); }
      };
      ws.addEventListener("message", handler);
    });
    await send(ws, "Page.navigate", { url: BASE + "/index.html" });
    await loaded;
    await sleep(700);
    await evaluate(ws, "localStorage.clear(); location.reload(); true");
    await sleep(900);

    for (const view of views) {
      if (view !== "today") {
        await evaluate(ws, `document.querySelector('[data-tab="${view}"]').click(); true`);
        await sleep(450);
      }
      await shot(ws, `${theme}-${view}`);
    }

    if (theme === "light") {
      // 设置页底部：网页版应出现「装到手机上」卡片 + 安装包可下载
      await evaluate(ws, `document.querySelector('[data-tab="settings"]').click(); true`);
      await sleep(350);
      await evaluate(ws, `window.scrollTo(0, document.documentElement.scrollHeight); true`);
      await sleep(350);
      await shot(ws, "light-settings-bottom");
      const shown = await evaluate(ws, "!document.getElementById('webInstall').hidden");
      if (!shown) problems.push("网页版没有显示安装卡片");
      const href = await evaluate(ws, "document.getElementById('apkLink').getAttribute('href')");
      const head = await fetch(BASE + "/" + href, { method: "HEAD" }).catch(() => null);
      if (!head || !head.ok) problems.push("安装包链接不可用: " + href);

      // 交互检查：打开新建待办面板
      await evaluate(ws, `document.querySelector('[data-tab="todos"]').click(); document.getElementById('fab').click(); true`);
      await sleep(500);
      await shot(ws, "light-sheet");
      const title = await evaluate(ws, "document.getElementById('sheetTitle').textContent");
      if (title !== "新建待办") problems.push("弹层标题不正确: " + title);
      await evaluate(ws, "window.pmHandleBack()");

      // 勾选态：方框由 .tick 画、勾由图标画，换成桌面几何后最容易出「框中框」
      await evaluate(ws, `document.querySelector('[data-tab="todos"]').click(); document.querySelector('[data-filter="done"]').click(); true`);
      await sleep(350);
      await shot(ws, "light-todos-done");
      const tickBox = await evaluate(ws, `(function(){
        var on = document.querySelector('.tick.is-on');
        if (!on) return null;
        var r = on.getBoundingClientRect(), ico = on.querySelector('.ico').getBoundingClientRect();
        return { box: Math.round(r.width), ico: Math.round(ico.width), paths: on.querySelectorAll('path').length };
      })()`);
      if (!tickBox) problems.push("找不到已完成待办的勾选框");
      else if (tickBox.ico >= tickBox.box) problems.push("勾选框里的图标没有留出内边距，会出现框中框");
      await evaluate(ws, `document.querySelector('[data-filter="open"]').click(); true`);
      await sleep(250);

      // 底部导航图标必须与桌面端导航同一个几何
      const tabIcons = await evaluate(ws, `JSON.stringify(Array.prototype.map.call(document.querySelectorAll('.tab use'), function(u){ return u.getAttribute('href'); }))`);
      const wantTabs = ["#i-today", "#i-calendar", "#i-todo", "#i-chart", "#i-spark", "#i-settings"];
      const gotTabs = JSON.parse(tabIcons || "[]");
      if (JSON.stringify(gotTabs) !== JSON.stringify(wantTabs)) problems.push("底部导航图标不是桌面几何: " + tabIcons);

      // 数据写入与结构检查（v2 = 桌面快照同构）
      const shape = await evaluate(ws, `(function(){
        var st = JSON.parse(localStorage.getItem('pressuremap.mobile.v2') || 'null');
        if (!st) return null;
        var snap = PMUI.snapshot();
        return {
          tabs: document.querySelectorAll('.tab').length,
          sems: st.semesters.length,
          tasks: st.semesters[0].tasks.length,
          todos: st.todos.length,
          keys: Object.keys(snap),
          schema: snap.schema_version,
          todoKeys: snap.desktop_todos[0] ? Object.keys(snap.desktop_todos[0]).length : 0,
          cals: st.calendars.length
        };
      })()`);
      if (!shape) problems.push("示例数据没有写入 localStorage（v2 键）");
      else {
        if (shape.tabs !== 6) problems.push("底部导航不是 6 个 tab: " + shape.tabs);
        if (!shape.tasks) problems.push("示例数据缺少压力任务");
        if (!shape.sems) problems.push("缺少学期数据");
        if (shape.schema !== 1) problems.push("快照 schema_version 不对: " + shape.schema);
        for (const key of ["exported_at", "schema_version", "semesters", "desktop_todos", "course_calendars"]) {
          if (!shape.keys.includes(key)) problems.push("快照缺少字段 " + key);
        }
        if (shape.todoKeys < 18) problems.push("待办字段过少（" + shape.todoKeys + "），可能与桌面版不兼容");
        console.log("  结构 " + JSON.stringify(shape));
      }

      // 云账号与 AI 卡片必须出现在设置页
      const cards = await evaluate(ws, `(function(){
        return {
          cloud: !!document.getElementById('cloudBody'),
          ai: !!document.getElementById('aiBase'),
          sem: !!document.getElementById('semSelect'),
          cache: !!document.getElementById('cacheBtn')
        };
      })()`);
      for (const [name, ok] of Object.entries(cards)) if (!ok) problems.push("设置页缺少 " + name + " 模块");
    }
  }

  ws.close();
  chrome.kill();
  await sleep(300);

  if (problems.length) {
    console.log("\n发现 " + problems.length + " 个问题：");
    for (const p of problems) console.log("  - " + p);
    process.exitCode = 1;
  } else {
    console.log("\n✅ 没有 JS 异常，截图已生成。");
  }
}

main().catch((err) => {
  console.error("QA 失败:", err.message);
  try { chrome.kill(); } catch { }
  process.exit(1);
});
