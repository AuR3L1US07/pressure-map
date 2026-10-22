/* 在线课程抓取回归：
 *
 * 手机上不能像桌面那样用 Playwright 开真浏览器，改成「在 App 内嵌窗口里登录，
 * 再往当前页注入提取脚本」。这里验证的是**提取脚本本身**：
 * 用合成页面（结构照搬桌面 reader.cjs / mooc_scanner.cjs 的选择器）跑一遍脚本，
 * 再看解析出的课程/作业数量、以及合并进待办时 sourceKey 是否能去重。
 *
 * 注意：合成页面只能证明「脚本对这套结构有效」，不能证明线上站点没改版。
 * 真实账号的端到端验证要在装好 App 后手动点一次。
 *
 * 用法： node scripts/qa-online.mjs [--base=http://127.0.0.1:8799]
 */
import { spawn } from "node:child_process";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const baseArg = process.argv.slice(2).find((a) => a.startsWith("--base="));
const BASE = baseArg ? baseArg.slice(7) : "http://127.0.0.1:8799";
const CHROME = "C:/Program Files/Google/Chrome/Application/chrome.exe";
const PORT = 9351;
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

let pass = 0;
const fails = [];
function ok(name, cond, extra) {
  if (cond) { pass++; return; }
  fails.push(name + (extra === undefined ? "" : "  ← " + JSON.stringify(extra)));
}

const CHAOXING = `<!doctype html><html><body>
<ul class="course-list">
  <li class="course">
    <h3><a href="https://mooc1.chaoxing.com/course/2001.html" class="course-name" title="高等数学A1">高等数学A1</a></h3>
    <span>已完成任务点 12/20</span>
  </li>
  <li class="course">
    <h3><a href="https://mooc1.chaoxing.com/course/2002.html" class="course-name" title="大学物理">大学物理</a></h3>
    <span>任务点：5 / 9</span>
  </li>
  <li class="course">
    <h3><a href="https://mooc1.chaoxing.com/course/2003.html" class="course-name" title="形势与政策">形势与政策</a></h3>
  </li>
</ul>
<ul class="workList">
  <li><a href="https://mooc1.chaoxing.com/work/1">第三章习题</a><span class="time">2026-11-05 23:59</span></li>
  <li><a href="https://mooc1.chaoxing.com/work/2">阶段测验二</a><span class="time">11月8日 12:00</span></li>
</ul>
</body></html>`;

const MOOC = `<!doctype html><html><body>
<div class="course-card"><a href="https://www.icourse163.org/learn/HDU-1001">数据结构</a>
  <h3 class="course-name">数据结构</h3><span>已学 40%</span></div>
<div class="course-card"><a href="https://www.icourse163.org/learn/HDU-1002">操作系统</a>
  <h3 class="course-name">操作系统</h3><span>已学 75%</span></div>
<ul class="task-list">
  <li><a href="https://www.icourse163.org/homework/9">第 4 单元作业</a><span class="deadline">2026-12-01 23:59</span></li>
</ul>
</body></html>`;

const chrome = spawn(CHROME, [
  "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
  "--remote-debugging-port=" + PORT, "--user-data-dir=" + join(ROOT, "build", "chrome-online"),
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
    setTimeout(() => pending.has(id) && (pending.delete(id), rej(new Error("超时 " + method))), 15000);
  });
}

async function evaluate(ws, expression) {
  const r = await send(ws, "Runtime.evaluate", { expression, awaitPromise: true, returnByValue: true });
  if (r.exceptionDetails) problems.push("evaluate: " + (r.exceptionDetails.exception?.description || r.exceptionDetails.text));
  return r.result?.value;
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

  ok("PM.Online 已注册", await evaluate(ws, "!!(window.PM && PM.Online)"));
  ok("两个平台适配器都在", await evaluate(ws, "Object.keys(PM.Online.adapters).join(',') === 'chaoxing,mooc'"));
  ok("网页版打不开抓取窗口", await evaluate(ws, "PM.Online.open('chaoxing') === false"));

  // ---- 超星 ----
  const cx = JSON.parse(await evaluate(ws, `(function(){
    document.open(); document.write(${JSON.stringify(CHAOXING)}); document.close();
    return PM.Online.adapters.chaoxing.script();
  })()`));
  ok("超星：读到 3 门课", cx.courses.length === 3, cx.courses.map((c) => c.name));
  ok("超星：课程名来自 title 属性", cx.courses[0].name === "高等数学A1", cx.courses[0]);
  ok("超星：解析出任务点进度", JSON.stringify(cx.courses[0].progress) === "[12,20]", cx.courses[0].progress);
  ok("超星：中文冒号写法也能认", JSON.stringify(cx.courses[1].progress) === "[5,9]", cx.courses[1].progress);
  ok("超星：没有进度也不报错", cx.courses[2].progress === null, cx.courses[2]);
  ok("超星：读到 2 个作业", cx.works.length === 2, cx.works);
  ok("超星：作业带截止时间文本", /23:59/.test(cx.works[0].due), cx.works[0]);

  // ---- MOOC ----
  const mo = JSON.parse(await evaluate(ws, `(function(){
    document.open(); document.write(${JSON.stringify(MOOC)}); document.close();
    return PM.Online.adapters.mooc.script();
  })()`));
  ok("MOOC：读到 2 门课", mo.courses.length === 2, mo.courses.map((c) => c.name));
  ok("MOOC：百分比换算成进度", JSON.stringify(mo.courses[0].progress) === "[40,100]", mo.courses[0].progress);
  ok("MOOC：读到 1 个作业", mo.works.length === 1, mo.works);

  // ---- 时间解析 ----
  const due = JSON.parse(await evaluate(ws, `JSON.stringify([
    PM.Online.parseDue("2026-11-05 23:59", new Date(2026, 9, 21)),
    PM.Online.parseDue("11月8日 12:00", new Date(2026, 9, 21)),
    PM.Online.parseDue("2026/12/01", new Date(2026, 9, 21)),
    PM.Online.parseDue("随时", new Date(2026, 9, 21))
  ])`));
  ok("完整日期时间", JSON.stringify(due[0]) === JSON.stringify({ dueDate: "2026-11-05", dueTime: "23:59" }), due[0]);
  ok("只有月日按今年补", JSON.stringify(due[1]) === JSON.stringify({ dueDate: "2026-11-08", dueTime: "12:00" }), due[1]);
  ok("斜杠日期", JSON.stringify(due[2]) === JSON.stringify({ dueDate: "2026-12-01", dueTime: null }), due[2]);
  ok("认不出就留空", due[3].dueDate === null, due[3]);

  // ---- 合并进待办 ----
  const merged = JSON.parse(await evaluate(ws, `(function(){
    var state = { todos: [] };
    var todos = PM.Online.toTodos(${JSON.stringify(cx)}, "chaoxing", new Date(2026, 9, 21));
    var first = PM.Online.merge(state, todos);
    var again = PM.Online.merge(state, PM.Online.toTodos(${JSON.stringify(cx)}, "chaoxing", new Date(2026, 9, 21)));
    var one = state.todos[0];
    return JSON.stringify({
      added: first.added, updated: first.updated,
      secondAdded: again.added, secondUpdated: again.updated,
      total: state.todos.length,
      keys: Object.keys(one).length,
      sourceKey: one.sourceKey,
      sourceLabel: one.sourceLabel,
      dueDate: one.dueDate, dueTime: one.dueTime,
      requiresSubmission: one.requiresSubmission
    });
  })()`));
  ok("首次合并新增 2 条", merged.added === 2 && merged.updated === 0, merged);
  ok("重复同步只更新不新增", merged.secondAdded === 0 && merged.secondUpdated === 2, merged);
  ok("待办总数没有翻倍", merged.total === 2, merged.total);
  ok("带 sourceKey 便于去重", /^chaoxing:/.test(merged.sourceKey), merged.sourceKey);
  ok("带来源标签", merged.sourceLabel === "超星学习通", merged.sourceLabel);
  ok("截止时间落进字段", merged.dueDate === "2026-11-05" && merged.dueTime === "23:59", merged);
  ok("作业默认需要提交", merged.requiresSubmission === true);
  ok("字段数量和新建待办一致（19）", merged.keys === 19, merged.keys);

  ws.close();
  chrome.kill();
  await sleep(200);

  if (fails.length || problems.length) {
    if (fails.length) console.log("\n❌ 失败 " + fails.length + " 项：\n - " + fails.join("\n - "));
    if (problems.length) console.log("\n运行期问题：\n - " + problems.join("\n - "));
    process.exitCode = 1;
  } else {
    console.log("通过 " + pass + " 项");
    console.log("✅ 提取脚本与待办合并逻辑正常（线上站点结构仍需真实账号验证）");
  }
}

main().catch((e) => {
  console.error("失败:", e.message);
  try { chrome.kill(); } catch { }
  process.exit(1);
});
