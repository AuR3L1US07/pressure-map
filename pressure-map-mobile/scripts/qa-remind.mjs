/* 提醒规则引擎单测：逐条对照桌面版 PressureMap.Core/Store.TaskDetails.cs 里的
 * ReminderRules.Due 与 Models.cs 的 Deadline 推导。
 *
 * 用法： node scripts/qa-remind.mjs
 */
import { readFileSync } from "node:fs";
import { join, dirname } from "node:path";
import { fileURLToPath } from "node:url";
import vm from "node:vm";

const ROOT = join(dirname(fileURLToPath(import.meta.url)), "..");
const sandbox = { window: {} };
vm.createContext(sandbox);
vm.runInContext(readFileSync(join(ROOT, "www", "pm-remind.js"), "utf8"), sandbox);
const R = sandbox.window.PM.Reminders;

let pass = 0;
const fails = [];
function ok(name, cond, extra) {
  if (cond) { pass++; return; }
  fails.push(name + (extra === undefined ? "" : "  ← " + JSON.stringify(extra)));
}
function eq(name, actual, expected) {
  ok(name, JSON.stringify(actual) === JSON.stringify(expected), { actual, expected });
}

const local = (y, m, d, hh = 23, mm = 59) => new Date(y, m - 1, d, hh, mm, 0, 0);
const todo = (over) => Object.assign({
  id: "t1", title: "数据结构实验报告 3", dueDate: "2026-10-22", dueTime: null,
  completed: false, requiresSubmission: true, remindersEnabled: true
}, over || {});

// ---------- 1. 截止时间推导（桌面：没填时间按 23:59） ----------
eq("没填时间 → 当天 23:59", R.deadline(todo()).getTime(), local(2026, 10, 22).getTime());
eq("填了 18:00 → 18:00", R.deadline(todo({ dueTime: "18:00" })).getTime(), local(2026, 10, 22, 18, 0).getTime());
eq("没有截止日期 → null", R.deadline(todo({ dueDate: null })), null);
eq("脏日期 → null", R.deadline(todo({ dueDate: "2026/10/22" })), null);

// ---------- 2. 阶段判定 ----------
{
  const due = local(2026, 10, 22);
  eq("早于 24 小时 → 还不到提醒窗口", R.stageOf(due, local(2026, 10, 20, 23, 0).getTime()), null);
  eq("距截止 23 小时 → 1440", R.stageOf(due, local(2026, 10, 22, 0, 59).getTime()), 1440);
  eq("距截止 30 分钟 → 60", R.stageOf(due, local(2026, 10, 22, 23, 29).getTime()), 60);
  eq("刚好到期 → 0", R.stageOf(due, due.getTime()), 0);
  eq("已过期 → 0", R.stageOf(due, local(2026, 10, 25).getTime()), 0);
}

// ---------- 3. key 与文案 ----------
{
  const due = local(2026, 10, 22, 18, 0);
  eq("key 格式（未完成）", R.keyOf(todo(), due, 1440), "2026-10-22T18:00|1440|work");
  eq("key 格式（已完成待提交）", R.keyOf(todo({ completed: true }), due, 0), "2026-10-22T18:00|0|submit");
  eq("文案：到期", R.labelOf(todo(), 0), "已到截止时间");
  eq("文案：一小时内", R.labelOf(todo(), 60), "将在 1 小时内截止");
  eq("文案：一天内", R.labelOf(todo(), 1440), "将在 24 小时内截止");
  eq("文案：已完成待提交优先", R.labelOf(todo({ completed: true }), 0), "已完成，记得提交");
}

// ---------- 4. 当前该不该弹（等价于桌面 Due） ----------
{
  const due = local(2026, 10, 22);
  const now = local(2026, 10, 22, 23, 30).getTime();
  const hit = R.due(todo(), now, []);
  ok("一小时内 → 有提醒", !!hit);
  eq("提醒带 key", hit && hit.key, "2026-10-22T23:59|60|work");
  eq("提醒正文带日期", hit && hit.body, "将在 1 小时内截止 · 10月22日 23:59");
  eq("已投递过 → 不重复", R.due(todo(), now, [hit.key]), null);
  eq("已完成 → 不提醒", R.due(todo({ completed: true }), now, []), null);
  eq("关掉提醒 → 不提醒", R.due(todo({ remindersEnabled: false }), now, []), null);
  eq("还早 → 不提醒", R.due(todo(), local(2026, 10, 21, 12, 0).getTime(), []), null);
  eq("刚好 24 小时前 → 进入一天内大关", R.due(todo(), local(2026, 10, 21, 23, 59).getTime(), []).stage, 1440);
}

// ---------- 5. 排给系统的闹钟计划 ----------
{
  const now = local(2026, 10, 21, 12, 0).getTime();
  const soon = todo({ id: "soon", dueDate: "2026-10-21", dueTime: "12:30" });
  const later = todo({ id: "later", dueDate: "2026-10-24" });
  const plan = R.plan({ todos: [soon, later] }, now);
  const ids = plan.map((x) => x.id);
  ok("临近截止只排「到期」这一个点", ids.indexOf("soon#0") >= 0 && ids.indexOf("soon#1440") < 0, ids);
  ok("三天后截止排三个点", ["later#1440", "later#60", "later#0"].every((k) => ids.indexOf(k) >= 0), ids);
  ok("按时间升序", plan.every((x, i) => i === 0 || plan[i - 1].at <= x.at));
  ok("全部在将来", plan.every((x) => x.at > now));
  eq("计划里没有已完成任务", R.plan({ todos: [todo({ id: "done", completed: true })] }, now), []);
  eq("总开关关闭 → 空计划", R.plan({ settings: { remindersEnabled: false }, todos: [later] }, now), []);
  eq("默认开启", R.plan({ settings: {}, todos: [later] }, now).length, 3);
}

// ---------- 6. 上限与脏数据 ----------
{
  const now = Date.now();
  const many = [];
  for (let i = 0; i < 60; i++) many.push(todo({ id: "t" + i, dueDate: "2026-12-01" }));
  ok("超过 100 条要截断", R.plan({ todos: many }, new Date(2026, 11, 15).getTime() - 1e9).length <= 100);
  eq("空 state 不炸", R.plan({}, now), []);
  eq("null state 不炸", R.plan(null, now), []);
  eq("没有原生宿主时 sync 返回 false", R.sync({ todos: [todo()] }, now), false);
  eq("hasNative 在浏览器里是 false", R.hasNative(), false);
}

console.log("通过 " + pass + " 项");
if (fails.length) {
  console.log("\n❌ 失败 " + fails.length + " 项：\n - " + fails.join("\n - "));
  process.exitCode = 1;
} else {
  console.log("✅ 提醒规则与桌面版一致");
}
