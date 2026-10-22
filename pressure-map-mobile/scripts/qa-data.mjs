/* 数据层校验：手机端快照转换 / 哈希 与桌面版真实备份、真实课表对照。
   用法：node scripts/qa-data.mjs [备份.json] [课表.ics] */
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import os from "node:os";

const ROOT = path.resolve(path.dirname(new URL(import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, "$1")), "..");
globalThis.window = globalThis;
if (!globalThis.TextEncoder) throw new Error("需要 Node 18+");

for (const file of ["www/pm-snapshot.js", "www/pm-calendar.js"]) {
  vm.runInThisContext(fs.readFileSync(path.join(ROOT, file), "utf8"), { filename: file });
}
const PM = globalThis.PM;

let failures = 0;
function check(name, ok, detail) {
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`);
  if (!ok) failures++;
}

// ---------- 1. 真实桌面备份：解析 → 回写 → 指纹必须一致 ----------
const backupDir = path.join(process.env.APPDATA || path.join(os.homedir(), "AppData", "Roaming"), "学期压力地图", "backups");
let backupFile = process.argv[2];
if (!backupFile) {
  try {
    const files = fs.readdirSync(backupDir).filter((f) => f.endsWith(".json")).sort().reverse();
    if (files.length) backupFile = path.join(backupDir, files[0]);
  } catch { }
}
if (backupFile && fs.existsSync(backupFile)) {
  const original = JSON.parse(fs.readFileSync(backupFile, "utf8"));
  const originalHash = PM.hashSnapshot(original);
  const internal = PM.fromSnapshot(original);
  const roundTrip = PM.snapshotOf({ semesters: internal.semesters, todos: internal.todos, calendars: internal.calendars });
  const roundHash = PM.hashSnapshot(roundTrip);
  check("桌面备份可解析",
    internal.semesters.length + internal.todos.length + internal.calendars.length > 0,
    path.basename(backupFile));
  check("学期/课程/任务/待办/课表数量保留",
    internal.semesters.reduce((n, s) => n + s.courses.length, 0) === (original.semesters || []).reduce((n, s) => n + (s.courses || []).length, 0) &&
    internal.todos.length === (original.desktop_todos || []).length &&
    internal.calendars.length === (original.course_calendars || []).length,
    `${internal.semesters.length} 学期 / ${internal.todos.length} 待办 / ${internal.calendars.length} 课表`);
  check("往返后指纹与桌面一致", originalHash === roundHash, originalHash.slice(0, 16) + " vs " + roundHash.slice(0, 16));
  if (originalHash !== roundHash) {
    // 找出差异字段，便于定位
    const a = JSON.stringify(original, Object.keys(original).sort());
    console.log("  原始摘要:", a.length, "字节；往返摘要:", JSON.stringify(roundTrip).length, "字节");
  }
  const todo = internal.todos[0];
  if (todo) check("待办字段完整（计划日期/类型/提醒/重复/子任务）",
    "plannedDate" in todo && "taskKind" in todo && "remindersEnabled" in todo && "repeatDays" in todo && Array.isArray(todo.steps));
} else {
  console.log("SKIP  没有找到桌面备份，跳过往返校验");
}

// ---------- 2. 真实课表 ICS ----------
const icsFile = process.argv[3] || path.join(ROOT, "..", "pressure-map", "output", "孙金辰(2026-2027-1).ics");
if (fs.existsSync(icsFile)) {
  const content = fs.readFileSync(icsFile, "utf8");
  const events = PM.parseIcs(content);
  check("ICS 可解析", events.length > 0, events.length + " 个 VEVENT");
  const source = { name: path.basename(icsFile), format: "ics", content, importedAt: PM.stamp() };
  const day = new Date(2026, 9, 20); // 2026-10-20 周二
  const dayList = PM.dayOccurrences([source], day);
  check("当天有课", dayList.length > 0, `2026-10-20 共 ${dayList.length} 节：` + dayList.map((c) => `${c.start.getHours()}:${String(c.start.getMinutes()).padStart(2, "0")} ${c.title}`).join(" / "));
  const span = PM.occurrences([source], new Date(2026, 8, 14), new Date(2027, 0, 21));
  check("整学期展开有事件", span.length > 100, span.length + " 次课");
  const bad = span.filter((c) => !(c.end > c.start));
  check("开始结束时间合法", bad.length === 0, bad.length ? bad[0].title : "");
  // 停课日不应有课（国庆 10-01 ~ 10-07 在 PDF 转换阶段已被剔除）
  const holiday = PM.dayOccurrences([source], new Date(2026, 9, 2));
  check("国庆停课日为 0 节", holiday.length === 0, holiday.length + " 节");
} else {
  console.log("SKIP  没有找到课表 ICS，跳过课表校验");
}

// ---------- 3. CSV ----------
const csv = "日期,课程,开始时间,结束时间,教室,教师\n2026-10-20,高等数学,08:05,08:50,A201,张老师\n2026-10-20,数据结构,10:00,10:45,B305,李老师\n";
const csvSource = { name: "test.csv", format: "csv", content: csv, importedAt: PM.stamp() };
const csvDay = PM.dayOccurrences([csvSource], new Date(2026, 9, 20));
check("CSV 解析", csvDay.length === 2, csvDay.map((c) => c.title).join(" / "));

// ---------- 4. RRULE 与 EXDATE ----------
const rrule = [
  "BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//test//CN",
  "BEGIN:VEVENT", "UID:a", "DTSTART:20260914T080000", "DTEND:20260914T094000",
  "RRULE:FREQ=WEEKLY;COUNT=6", "SUMMARY:周一的课", "END:VEVENT",
  "BEGIN:VEVENT", "UID:b", "DTSTART:20260915T100000", "DTEND:20260915T114000",
  "RRULE:FREQ=WEEKLY;COUNT=6", "EXDATE:20260922T100000", "SUMMARY:被停掉一次", "END:VEVENT",
  "END:VCALENDAR"
].join("\r\n");
const rrSource = { name: "r.ics", format: "ics", content: rrule, importedAt: PM.stamp() };
const mondays = [14, 21, 28, 5, 12, 19].map((d, i) => {
  const date = i < 3 ? new Date(2026, 8, d) : new Date(2026, 9, d);
  return PM.dayOccurrences([rrSource], date).filter((c) => c.title === "周一的课").length;
});
check("RRULE COUNT 展开 6 次", mondays.reduce((a, b) => a + b, 0) === 6, mondays.join(","));
const skipped = PM.dayOccurrences([rrSource], new Date(2026, 8, 22)).filter((c) => c.title === "被停掉一次").length;
const kept = PM.dayOccurrences([rrSource], new Date(2026, 8, 29)).filter((c) => c.title === "被停掉一次").length;
check("EXDATE 停课生效", skipped === 0 && kept === 1, `9/22=${skipped} 9/29=${kept}`);

// ---------- 5. 构造完整快照（学期/课程/任务/待办的每个字段）往返 ----------
const rich = {
  exported_at: "2026-10-20T22:00:00",
  schema_version: 1,
  semesters: [
    {
      id: 7, name: "2026 秋", total_weeks: 18, week1_monday: "2026-09-14", threshold: 7.5,
      hours_per_week: 22, type_hours: { 考试: 15, 大作业: 20, 报告: 8, 测验: 4, 其他: 4 },
      current_step: 3, created_at: "2026-09-01T10:00:00", updated_at: "2026-10-01T10:00:00",
      courses: [{ key: "c11", name: "数据结构", raw_text: "原始描述" }, { key: "c12", name: "高等数学", raw_text: "" }],
      tasks: [
        { course_key: "c11", name: "课程设计", type: "大作业", week: 16, weight: 4, est_hours: 22, source: "手动" },
        { course_key: "c12", name: "期中考试", type: "考试", week: 8, weight: 5, est_hours: null, source: "" }
      ]
    },
    {
      id: 8, name: "2027 春", total_weeks: 16, week1_monday: "2027-02-22", threshold: 6,
      hours_per_week: 18, type_hours: { 考试: 15, 大作业: 20, 报告: 8, 测验: 4, 其他: 4 },
      current_step: 1, created_at: "2027-01-01T10:00:00", updated_at: "2027-01-01T10:00:00",
      courses: [], tasks: []
    }
  ],
  desktop_todos: [
    {
      id: "a1b2", title: "实验报告", due_date: "2026-10-22", important: true, completed: false,
      source_key: null, source_label: "", created_at: "2026-10-20T09:00:00", updated_at: "2026-10-20T09:30:00",
      course_name: "数据结构", planned_date: "2026-10-21", task_kind: "报告", estimated_hours: 3,
      due_time: "23:59", requires_submission: true, submitted: false, reminders_enabled: false, repeat_days: 7,
      steps: [{ id: "s1", title: "画图", completed: true }, { id: "s2", title: "补截图", completed: false }]
    }
  ],
  course_calendars: [{ name: "课表.ics", format: "ics", content: "BEGIN:VCALENDAR\r\nEND:VCALENDAR", imported_at: "2026-09-14T08:00:00" }]
};
const richInternal = PM.fromSnapshot(rich);
const richBack = PM.snapshotOf({ semesters: richInternal.semesters, todos: richInternal.todos, calendars: richInternal.calendars });
check("多学期往返保留", richBack.semesters.length === 2 && richBack.semesters[0].courses.length === 2 && richBack.semesters[0].tasks.length === 2);
check("学期参数往返保留（阈值/可用小时/类型工时/步骤）",
  richBack.semesters[0].threshold === 7.5 && richBack.semesters[0].hours_per_week === 22 &&
  richBack.semesters[0].current_step === 3 && richBack.semesters[0].type_hours["大作业"] === 20);
check("待办全字段往返保留",
  richBack.desktop_todos[0].planned_date === "2026-10-21" && richBack.desktop_todos[0].task_kind === "报告" &&
  richBack.desktop_todos[0].reminders_enabled === false && richBack.desktop_todos[0].repeat_days === 7 &&
  richBack.desktop_todos[0].steps.length === 2 && richBack.desktop_todos[0].due_time === "23:59");
check("完整快照往返指纹一致", PM.hashSnapshot(rich) === PM.hashSnapshot(richBack),
  PM.hashSnapshot(rich).slice(0, 12) + " vs " + PM.hashSnapshot(richBack).slice(0, 12));

// ---------- 6. 空快照判定与指纹稳定 ----------
const empty = { schema_version: 1, semesters: [], desktop_todos: [], course_calendars: [] };
check("空快照判定", PM.isEmptySnapshot(empty) === true);
check("指纹只随内容变化", PM.hashSnapshot(empty) === PM.hashSnapshot({ ...empty, exported_at: "2026-01-01T00:00:00" }));

console.log(failures ? `\n${failures} 项失败` : "\n全部通过");
process.exitCode = failures ? 1 : 0;
