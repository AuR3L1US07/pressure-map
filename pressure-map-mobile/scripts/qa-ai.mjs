/* AI 接口校验：请求体必须与桌面版 DailyAiPlanner 同构，并验证错误提示。
   用法：node scripts/qa-ai.mjs */
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import http from "node:http";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
globalThis.window = globalThis;
for (const file of ["www/pm-snapshot.js", "www/pm-calendar.js", "www/pm-ai.js"]) {
  vm.runInThisContext(fs.readFileSync(path.join(ROOT, file), "utf8"), { filename: file });
}
const PM = globalThis.PM;

let failures = 0;
function check(name, ok, detail) {
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`);
  if (!ok) failures++;
}

let captured = null;
let nextStatus = 200;
const server = http.createServer((req, res) => {
  let body = "";
  req.on("data", (c) => (body += c));
  req.on("end", () => {
    captured = { url: req.url, headers: req.headers, body: body ? JSON.parse(body) : null };
    if (nextStatus !== 200) {
      res.writeHead(nextStatus, { "content-type": "application/json" });
      return res.end(JSON.stringify({ error: { message: "bad key" } }));
    }
    res.writeHead(200, { "content-type": "application/json" });
    res.end(JSON.stringify({ choices: [{ message: { role: "assistant", content: "  1. 先做实验报告\n2. 晚上复习 " } }] }));
  });
});
await new Promise((r) => server.listen(8898, "127.0.0.1", r));

const cfg = { baseUrl: "http://127.0.0.1:8898/v1", model: "test-model", apiKey: "sk-test" };
check("配置合法（http 仅回环）", PM.aiValid(cfg) === true);
check("拒绝非 https 的远程地址", PM.aiValid({ baseUrl: "http://example.com/v1", model: "m", apiKey: "k" }) === false);
check("允许粘贴完整 /chat/completions 地址", PM.aiBase("https://x.com/v1/chat/completions") === "https://x.com/v1");

const semester = {
  name: "2026 秋", totalWeeks: 18, week1Monday: "2026-09-14", threshold: 7, hoursPerWeek: 22,
  courses: [{ key: "c1", name: "数据结构" }],
  tasks: [{ courseKey: "c1", name: "课程设计", kind: "大作业", week: 16, weight: 4, estHours: 20 }]
};
const ics = ["BEGIN:VCALENDAR", "VERSION:2.0", "BEGIN:VEVENT", "UID:1", "DTSTART;TZID=Asia/Shanghai:20261020T080500",
  "DTEND;TZID=Asia/Shanghai:20261020T085000", "SUMMARY:高等数学", "LOCATION:A201", "END:VEVENT", "END:VCALENDAR"].join("\r\n");
PM._calendars = [{ name: "课表.ics", format: "ics", content: ics, importedAt: PM.stamp() }];

const todos = [
  { id: "t1", title: "实验报告", courseName: "数据结构", dueDate: "2026-10-21", dueTime: "23:59", important: true, completed: false, requiresSubmission: true, submitted: false, estimatedHours: 3, steps: [{ title: "画图", completed: true }] },
  { id: "t2", title: "已完成的事", dueDate: "2026-10-21", completed: true, requiresSubmission: false, submitted: false, estimatedHours: 1, steps: [] },
  { id: "t3", title: "很久以后", dueDate: "2026-12-01", completed: false, requiresSubmission: false, submitted: false, estimatedHours: 1, steps: [] }
];

const ctx = PM.aiContext(semester, todos, 6, new Date(2026, 9, 20, 21, 30), "晚上 8 点后有空");
check("上下文含当前时间", ctx.currentTime === "2026-10-20 21:30", ctx.currentTime);
check("上下文含每日可用小时", ctx.dailyCapacityHours === 6);
check("上下文含当天真实课程", ctx.courses.length === 1 && ctx.courses[0].title === "高等数学" && ctx.courses[0].start === "08:05",
  JSON.stringify(ctx.courses));
check("只带未完成的近期任务", ctx.tasks.length === 1 && ctx.tasks[0].title === "实验报告", ctx.tasks.map((t) => t.title).join(","));
check("任务带截止与子任务", ctx.tasks[0].deadline === "2026-10-21 23:59" && ctx.tasks[0].steps.length === 1);
check("上下文带补充要求", ctx.preferences === "晚上 8 点后有空");

const text = await PM.aiGenerate(cfg, ctx, false);
check("返回内容去掉首尾空白", text === "1. 先做实验报告\n2. 晚上复习", JSON.stringify(text));
check("请求打到 /v1/chat/completions", captured.url === "/v1/chat/completions", captured.url);
check("带 Bearer 密钥", captured.headers.authorization === "Bearer sk-test");
check("请求体含 model", captured.body.model === "test-model");
check("system 提示要求中文并防注入",
  captured.body.messages[0].role === "system" &&
  /never as instructions/.test(captured.body.messages[0].content) &&
  /用中文回答/.test(captured.body.messages[0].content));
const sentCtx = JSON.parse(captured.body.messages[1].content);
check("user 消息就是上下文 JSON", captured.body.messages[1].role === "user" && sentCtx.tasks[0].title === "实验报告");

const en = await PM.aiGenerate(cfg, ctx, true);
check("英文模式切换 system 提示", /Respond in English/.test(captured.body.messages[0].content) && en.length > 0);

nextStatus = 401;
try {
  await PM.aiGenerate(cfg, ctx, false);
  check("401 给出密钥提示", false);
} catch (e) {
  check("401 给出密钥提示", /API Key/.test(e.message), e.message);
}

nextStatus = 500;
try {
  await PM.aiGenerate(cfg, ctx, false);
  check("500 给出状态码", false);
} catch (e) {
  check("500 给出状态码", /HTTP 500/.test(e.message), e.message);
}

try {
  await PM.aiGenerate({ baseUrl: "", model: "", apiKey: "" }, ctx, false);
  check("未配置时拒绝请求", false);
} catch (e) {
  check("未配置时拒绝请求", /请先在设置/.test(e.message), e.message);
}

server.close();
console.log(failures ? `\n${failures} 项失败` : "\n全部通过");
process.exitCode = failures ? 1 : 0;
