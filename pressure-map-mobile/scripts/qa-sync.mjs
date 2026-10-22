/* 云同步协议校验：用本地模拟服务器跑完整场景（不碰生产数据）
   覆盖：登录、首次上传、第二台设备下载、双向改动冲突、冲突二选一、登录失效。
   用法：node scripts/qa-sync.mjs */
import fs from "node:fs";
import path from "node:path";
import vm from "node:vm";
import http from "node:http";
import { fileURLToPath } from "node:url";

const ROOT = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
globalThis.window = globalThis;
for (const file of ["www/pm-snapshot.js", "www/pm-cloud.js"]) {
  vm.runInThisContext(fs.readFileSync(path.join(ROOT, file), "utf8"), { filename: file });
}
const PM = globalThis.PM;

let failures = 0;
function check(name, ok, detail) {
  console.log(`${ok ? "PASS" : "FAIL"}  ${name}${detail ? "  — " + detail : ""}`);
  if (!ok) failures++;
}

// ---------------- 模拟云服务 ----------------
const users = new Map(); // account -> {id, name, password}
const tokens = new Map(); // token -> account
let remote = { revision: null, snapshot: { schema_version: 1, semesters: [], desktop_todos: [], course_calendars: [] } };
let revisionSeq = 0;
let forcedStatus = 0;

const server = http.createServer((req, res) => {
  let body = "";
  req.on("data", (c) => (body += c));
  req.on("end", () => {
    const send = (code, obj) => {
      res.writeHead(code, { "content-type": "application/json" });
      res.end(JSON.stringify(obj));
    };
    if (forcedStatus) { forcedStatus = 0; return send(503, { message: "boom" }); }
    const url = new URL(req.url, "http://localhost");
    const auth = req.headers.authorization || "";
    const token = auth.startsWith("Bearer ") ? auth.slice(7) : "";
    const user = tokens.get(token);

    if (req.method === "POST" && url.pathname === "/v1/auth/register") {
      const { account, name, password } = JSON.parse(body || "{}");
      if (users.has(account)) return send(400, { message: "账号已存在" });
      users.set(account, { id: "u" + (users.size + 1), name, password });
      return send(200, { ok: true });
    }
    if (req.method === "POST" && url.pathname === "/v1/auth/login") {
      const { account, password } = JSON.parse(body || "{}");
      const u = users.get(account);
      if (!u || u.password !== password) return send(401, { message: "账号或密码不正确" });
      const t = "tok-" + account + "-" + Math.random().toString(36).slice(2, 8);
      tokens.set(t, account);
      return send(200, { access_token: t, user: { id: u.id, name: u.name } });
    }
    if (req.method === "POST" && url.pathname === "/v1/auth/logout") {
      tokens.delete(token);
      return send(200, { ok: true });
    }
    if (url.pathname === "/v1/data") {
      if (!user) return send(401, { message: "未登录" });
      if (req.method === "GET") return send(200, { revision: remote.revision, snapshot: remote.snapshot });
      if (req.method === "PUT") {
        const { base_revision, snapshot } = JSON.parse(body || "{}");
        if ((base_revision || null) !== (remote.revision || null)) return send(409, { message: "版本冲突" });
        remote.revision = "r" + ++revisionSeq;
        remote.snapshot = snapshot;
        return send(200, { revision: remote.revision });
      }
    }
    send(404, { message: "not found" });
  });
});

await new Promise((r) => server.listen(8899, "127.0.0.1", r));
PM.Cloud.configure("http://127.0.0.1:8899");
PM.Cloud.clear();

// ---------------- 设备 A ----------------
function makeDevice(label) {
  const state = {
    semesters: [{ id: 1, name: label + "的学期", totalWeeks: 18, week1Monday: "2026-09-14", threshold: 7, hoursPerWeek: 20, courses: [{ key: "c1", name: "数据结构", rawText: "" }], tasks: [], typeHours: {}, currentStep: 1 }],
    todos: [],
    calendars: [],
    cloud: { revision: null, hash: null }
  };
  const ctx = {
    state,
    snapshot: () => PM.snapshotOf(state),
    apply: (snap) => {
      const converted = PM.fromSnapshot(snap);
      state.semesters = converted.semesters;
      state.todos = converted.todos;
      state.calendars = converted.calendars;
    },
    baseline: () => (state.cloud.revision ? { revision: state.cloud.revision, hash: state.cloud.hash } : null),
    saveBaseline: (revision, hash) => { state.cloud.revision = revision; state.cloud.hash = hash; },
    canApply: () => true
  };
  return ctx;
}

const deviceA = makeDevice("A");
const seedSemester = deviceA.state.semesters[0];
deviceA.state.semesters = [];
const account = "qa_user";
const password = "qa_pass_123";
try {
  await PM.Cloud.register(account, "QA", password);
  check("注册并登录", PM.Cloud.hasSession() && !!PM.Cloud.user.id, PM.Cloud.user && PM.Cloud.user.name);
} catch (e) {
  check("注册并登录", false, e.message);
}

let result = await PM.Sync.run(deviceA);
check("首次空对空同步判为无变化", result.outcome === "unchanged", result.outcome);

deviceA.state.semesters.push(seedSemester);
deviceA.state.semesters[0].tasks.push({ courseKey: "c1", name: "课程设计", kind: "大作业", week: 16, weight: 4, estHours: 20 });
deviceA.state.calendars.push({ name: "课表.ics", format: "ics", content: "BEGIN:VCALENDAR\r\nEND:VCALENDAR", importedAt: "2026-09-14T08:00:00" });
result = await PM.Sync.run(deviceA);
check("本机有改动时上传", result.outcome === "uploaded", result.outcome);
check("云端收到学期与课表",
  remote.snapshot.semesters.length === 1 && remote.snapshot.course_calendars.length === 1 && remote.revision === "r1",
  remote.revision + " / " + remote.snapshot.semesters.length + " 学期");
deviceA.state.todos.push({ id: "t1", title: "实验报告", dueDate: "2026-10-22", estimatedHours: 3, steps: [] });
result = await PM.Sync.run(deviceA);
check("追加待办后再次上传", result.outcome === "uploaded", result.outcome);
check("云端待办已更新", remote.snapshot.desktop_todos.length === 1, remote.snapshot.desktop_todos.length + " 条");

// ---------------- 设备 B：首次同步应下载 ----------------
const deviceB = makeDevice("B");
deviceB.state.semesters = [];
deviceB.state.todos = [];
result = await PM.Sync.run(deviceB);
check("第二台设备首次同步下载", result.outcome === "downloaded", result.outcome);
check("第二台设备拿到 A 的数据", deviceB.state.todos.length === 1 && deviceB.state.todos[0].title === "实验报告");
check("下载后指纹一致", PM.hashSnapshot(PM.snapshotOf(deviceB.state)) === PM.hashSnapshot(remote.snapshot));

const revBefore = remote.revision;
result = await PM.Sync.run(deviceB);
check("无改动时不重复上传", result.outcome === "unchanged" && remote.revision === revBefore, result.outcome + " " + remote.revision);

// ---------------- 双向改动 → 冲突 ----------------
deviceA.state.todos.push({ id: "t2", title: "A 的新待办", dueDate: "2026-10-23", estimatedHours: 1, steps: [] });
deviceB.state.todos.push({ id: "t3", title: "B 的新待办", dueDate: "2026-10-24", estimatedHours: 1, steps: [] });
result = await PM.Sync.run(deviceA);
check("A 再次上传", result.outcome === "uploaded", result.outcome);
result = await PM.Sync.run(deviceB);
check("B 检测到冲突", result.outcome === "conflict", result.outcome);
const conflictRevision = PM.Sync.conflict && PM.Sync.conflict.revision;
check("冲突里带云端版本", !!conflictRevision, conflictRevision);

// 用本机覆盖云端
result = await PM.Sync.resolve(true, deviceB);
check("冲突选「用本机覆盖」", result.outcome === "uploaded" && !PM.Sync.conflict, result.outcome);
check("云端被 B 的数据覆盖", remote.snapshot.desktop_todos.some((t) => t.title === "B 的新待办") && !remote.snapshot.desktop_todos.some((t) => t.title === "A 的新待办"),
  remote.snapshot.desktop_todos.map((t) => t.title).join(","));

// A 拉取
result = await PM.Sync.run(deviceA);
check("A 同步后接受云端版本", result.outcome === "downloaded" || result.outcome === "unchanged", result.outcome);
check("A 本地变成 B 的数据", deviceA.state.todos.some((t) => t.title === "B 的新待办"), deviceA.state.todos.map((t) => t.title).join(","));

// ---------------- 冲突选「用云端」 ----------------
deviceA.state.todos.push({ id: "t4", title: "A2", dueDate: "2026-10-25", estimatedHours: 1, steps: [] });
deviceB.state.todos.push({ id: "t5", title: "B2", dueDate: "2026-10-26", estimatedHours: 1, steps: [] });
await PM.Sync.run(deviceA);
result = await PM.Sync.run(deviceB);
check("第二次冲突", result.outcome === "conflict", result.outcome);
result = await PM.Sync.resolve(false, deviceB);
check("冲突选「用云端覆盖本机」", result.outcome === "downloaded" && !PM.Sync.conflict, result.outcome);
check("B 本地被云端覆盖", deviceB.state.todos.some((t) => t.title === "A2") && !deviceB.state.todos.some((t) => t.title === "B2"),
  deviceB.state.todos.map((t) => t.title).join(","));

// ---------------- 登录失效与网络错误 ----------------
PM.Cloud.token = "invalid-token";
try {
  await PM.Cloud.download();
  check("无效令牌被拒", false);
} catch (e) {
  check("无效令牌被拒", e.code === 401, e.message);
}
PM.Cloud.restore(Array.from(tokens.keys())[0], PM.Cloud.user);
forcedStatus = 1;
try {
  await PM.Cloud.download();
  check("服务端错误有可读提示", false);
} catch (e) {
  check("服务端错误有可读提示", /暂时不可用/.test(e.message), e.message);
}

await PM.Cloud.logout();
check("退出登录清空会话", !PM.Cloud.hasSession());

server.close();
console.log(failures ? `\n${failures} 项失败` : "\n全部通过");
process.exitCode = failures ? 1 : 0;
