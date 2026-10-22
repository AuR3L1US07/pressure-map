/* 教务 PDF 课表导入回归：合成 PDF → pm-hdu.js 解析 → 与真实 ICS 逐条比对。
 *
 * 手边没有原始教务 PDF（只有它导出的 156 条 .ics），所以先用
 * scripts/make-hdu-pdf.py 从真实 ICS 反推一份同版式的 PDF，再让解析器
 * 把它读回来。生成的 ICS 必须和原始 ICS 的课程集合完全一致，否则说明
 * 版式识别或周次/节次/调课处理有偏差。
 *
 * 用法： node scripts/qa-hdu.mjs [--base=http://127.0.0.1:8799]
 */
import { spawn } from "node:child_process";
import { readFileSync, existsSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const baseArg = process.argv.slice(2).find((a) => a.startsWith("--base="));
const BASE = baseArg ? baseArg.slice(7) : "http://127.0.0.1:8799";
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9347;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

const PDF = join(ROOT, "build", "hdu-sample.pdf");
const ICS = process.env.PM_ICS || join(dirname(ROOT), "pressure-map", "output", "孙金辰(2026-2027-1).ics");

let pass = 0;
const fails = [];
function ok(name, cond, extra) {
  if (cond) { pass++; return; }
  fails.push(name + (extra === undefined ? "" : "  ← " + JSON.stringify(extra)));
}

/** 极简 ICS 解析：只取回归需要的那几个字段。 */
function parseIcs(text) {
  const raw = text.replace(/\r\n/g, "\n").replace(/\n[ \t]/g, "");
  const out = [];
  for (const block of raw.match(/BEGIN:VEVENT[\s\S]*?END:VEVENT/g) || []) {
    const fields = {};
    for (const line of block.split("\n")) {
      const i = line.indexOf(":");
      if (i > 0) fields[line.slice(0, i)] = line.slice(i + 1);
    }
    const stamp = (v) => v; // 已经是 yyyyMMddTHHmmssZ
    out.push({
      title: (fields.SUMMARY || "").replace(/\\,/g, ","),
      location: (fields.LOCATION || "").replace(/\\,/g, ","),
      start: stamp(fields.DTSTART),
      end: stamp(fields.DTEND),
    });
  }
  return out;
}

function keyOf(e) {
  return e.title + "|" + e.start + "|" + e.end + "|" + e.location;
}

const chrome = spawn(CHROME, [
  "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
  "--remote-debugging-port=" + PORT, "--user-data-dir=" + join(ROOT, "build", "chrome-hdu"),
  "about:blank",
], { stdio: "ignore" });

let seq = 0;
const pending = new Map();
const problems = [];

function send(ws, method, params = {}) {
  const id = ++seq;
  ws.send(JSON.stringify({ id, method, params }));
  return new Promise((res, rej) => {
    pending.set(id, { res, rej });
    setTimeout(() => pending.has(id) && (pending.delete(id), rej(new Error("超时 " + method))), 30000);
  });
}

async function evaluate(ws, expression) {
  const r = await send(ws, "Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) problems.push("evaluate: " + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result?.value;
}

async function main() {
  if (!existsSync(PDF)) {
    console.log("跳过：先跑 python scripts/make-hdu-pdf.py 生成合成 PDF");
    process.exitCode = 1;
    chrome.kill();
    return;
  }
  if (!existsSync(ICS)) {
    console.log("跳过：找不到真实 ICS " + ICS);
    process.exitCode = 1;
    chrome.kill();
    return;
  }

  const b64 = readFileSync(PDF).toString("base64");
  const truth = parseIcs(readFileSync(ICS, "utf8"));

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

  const hasPdf = await evaluate(ws, "typeof window.pdfjsLib === 'object'");
  const hasHdu = await evaluate(ws, "!!(window.PM && PM.Hdu)");
  console.log("  探针 pdf.js=" + hasPdf + " PM.Hdu=" + hasHdu + " 问题=" + problems.join("; "));
  ok("pdf.js 已加载", hasPdf);
  ok("PM.Hdu 已注册", hasHdu);

  // ---- 纯函数：周次表达式与校历调整 ----
  const weeksRaw = await evaluate(ws, `JSON.stringify([
    PM.Hdu.parseWeeks("1-16周"),
    PM.Hdu.parseWeeks("1-16周(单)"),
    PM.Hdu.parseWeeks("3周,5周,9-11周"),
    PM.Hdu.parseWeeks("第2-4周(双)")
  ])`);
  if (typeof weeksRaw !== "string") throw new Error("周次表达式求值失败: " + weeksRaw);
  const weeks = JSON.parse(weeksRaw);
  ok("1-16 周", weeks[0].length === 16 && weeks[0][0] === 1 && weeks[0][15] === 16, weeks[0]);
  ok("单周只留奇数", JSON.stringify(weeks[1]) === JSON.stringify([1, 3, 5, 7, 9, 11, 13, 15]), weeks[1]);
  ok("逗号列表", JSON.stringify(weeks[2]) === JSON.stringify([3, 5, 9, 10, 11]), weeks[2]);
  ok("双周只留偶数", JSON.stringify(weeks[3]) === JSON.stringify([2, 4]), weeks[3]);

  const adjustedRaw = await evaluate(ws, `JSON.stringify({
    moved: (function(){ var d = PM.Hdu.adjustDate(new Date(2026, 9, 6)); return d ? [d.getFullYear(), d.getMonth()+1, d.getDate()] : null; })(),
    holiday: PM.Hdu.adjustDate(new Date(2026, 9, 3)),
    normal: (function(){ var d = PM.Hdu.adjustDate(new Date(2026, 10, 3)); return d ? [d.getMonth()+1, d.getDate()] : null; })()
  })`);
  if (typeof adjustedRaw !== "string") throw new Error("校历调整求值失败: " + adjustedRaw);
  const adjusted = JSON.parse(adjustedRaw);
  ok("10 月 6 日补到 9 月 20 日", JSON.stringify(adjusted.moved) === JSON.stringify([2026, 9, 20]), adjusted.moved);
  ok("国庆停课", adjusted.holiday === null);
  ok("平常日照常", JSON.stringify(adjusted.normal) === JSON.stringify([11, 3]), adjusted.normal);

  // ---- 真正解析 PDF ----
  const resultRaw = await evaluate(ws, `(async function(){
    try {
      var bin = atob("${b64}");
      var bytes = new Uint8Array(bin.length);
      for (var i = 0; i < bin.length; i++) bytes[i] = bin.charCodeAt(i);
      var probe = await PM.Hdu.probe(bytes);
      var out = await PM.Hdu.convert("hdu-sample.pdf", bytes, "2026-2027-1");
      return JSON.stringify({ ok: true, probe: probe, name: out.name, lessons: out.lessons, ics: out.ics });
    } catch (e) {
      return JSON.stringify({ ok: false, error: (e && e.message) || String(e) });
    }
  })()`);
  if (typeof resultRaw !== "string") throw new Error("PDF 解析求值失败: " + resultRaw + " | " + problems.join(";"));
  const result = JSON.parse(resultRaw);

  if (!result.ok) {
    problems.push("PDF 解析失败：" + result.error);
  } else {
    ok("识别为杭电教务课表", result.probe.isSchedule === true, result.probe);
    ok("自动匹配学期预设", result.probe.semester === "2026-2027-1", result.probe);
    ok("输出文件名带 .ics", /^hdu-sample\.ics$/.test(result.name), result.name);
    ok("课程格数量为 15", result.lessons === 15, result.lessons);

    const got = parseIcs(result.ics);
    const gotKeys = new Set(got.map(keyOf));
    const truthKeys = new Set(truth.map(keyOf));
    const missing = [...truthKeys].filter((k) => !gotKeys.has(k));
    const extra = [...gotKeys].filter((k) => !truthKeys.has(k));

    ok("事件总数一致（" + truthKeys.size + "）", gotKeys.size === truthKeys.size, { got: gotKeys.size, want: truthKeys.size });
    ok("没有漏掉的课", missing.length === 0, missing.slice(0, 3));
    ok("没有多出来的课", extra.length === 0, extra.slice(0, 3));
    console.log("  真实 ICS " + truth.length + " 条 / 解析得到 " + got.length + " 条 / 课程格 " + result.lessons + " 个");
    if (missing.length || extra.length) {
      console.log("  漏：" + missing.slice(0, 3).join(" ; "));
      console.log("  多：" + extra.slice(0, 3).join(" ; "));
    }
    // 调课事件必须被搬到补课日
    const moved = got.filter((e) => e.start === "20260920T000500Z");
    ok("调课事件落在补课日", moved.length === 1, moved.length);
  }

  // ---- 走一遍真实 UI：把 PDF 塞进 <input type=file>，看预览弹窗 ----
  await send(ws, "DOM.enable");
  await evaluate(ws, "PMUI.calendarInput(); true");
  const doc = await send(ws, "DOM.getDocument", { depth: -1 });
  const node = await send(ws, "DOM.querySelector", { nodeId: doc.root.nodeId, selector: "#calFileInput" });
  if (!node.nodeId) {
    problems.push("找不到课表文件输入框");
  } else {
    await send(ws, "DOM.setFileInputFiles", { nodeId: node.nodeId, files: [PDF] });
    await sleep(2500);
    const sheet = JSON.parse(await evaluate(ws, `(function(){
      var body = document.getElementById('sheetForm');
      return JSON.stringify({
        open: !document.getElementById('sheetWrap').hidden,
        title: document.getElementById('sheetTitle').textContent,
        text: body ? body.textContent : ''
      });
    })()`));
    ok("PDF 导入打开了预览弹窗", sheet.open && sheet.title === "导入课表", sheet.title);
    ok("预览统计到 156 个事件", /156 个事件/.test(sheet.text), sheet.text.slice(0, 120));
    ok("标注了课程格数量", /教务 PDF · 15 个课程格/.test(sheet.text), sheet.text.slice(0, 160));
    ok("预览列出了具体课程", /高等数学|数据结构|大学英语/.test(sheet.text), sheet.text.slice(0, 200));

    // 确认导入：课表来源里应出现这份 PDF 生成的日历
    await evaluate(ws, `document.querySelector('#sheet form button[type=submit]').click(); true`);
    await sleep(600);
    const imported = JSON.parse(await evaluate(ws, `JSON.stringify((PMUI.state().calendars || []).map(function(c){ return {name: c.name, bytes: (c.content || '').length}; }))`));
    const hdu = imported.find((c) => /hdu-sample\.ics$/.test(c.name));
    ok("课表来源里出现导入的日历", !!hdu, imported);
    ok("日历内容非空", !!hdu && hdu.bytes > 1000, hdu);
  }

  ws.close();
  chrome.kill();
  await sleep(200);

  if (fails.length || problems.length) {
    if (fails.length) console.log("\n❌ 失败 " + fails.length + " 项：\n - " + fails.join("\n - "));
    if (problems.length) console.log("\n运行期问题：\n - " + problems.join("\n - "));
    process.exitCode = 1;
  } else {
    console.log("通过 " + pass + " 项\n✅ PDF 课表导入与真实 ICS 完全一致");
  }
}

main().catch((e) => {
  console.error("失败:", e.message);
  try { chrome.kill(); } catch { }
  process.exit(1);
});
