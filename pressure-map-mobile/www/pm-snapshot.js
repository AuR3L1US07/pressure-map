/* 学期压力地图 · 与桌面版互通的数据层
   桌面版（PressureMap.Core/SnapshotData）的快照格式：
     { exported_at, schema_version, semesters[], desktop_todos[], course_calendars[] }
   手机端内部用 camelCase，只在「导入/导出/云同步」的边界做转换，
   因此界面代码不需要跟着改命名。哈希算法与桌面 SnapshotData.Hash 对齐，
   这样两端看到同一份数据时算出的指纹相同，不会产生假冲突。 */
(function () {
  "use strict";

  var PM = (window.PM = window.PM || {});
  PM.SCHEMA_VERSION = 1;
  var TYPE_HOURS = { "考试": 15, "大作业": 20, "报告": 8, "测验": 4, "其他": 4 };
  var TYPES = ["考试", "大作业", "报告", "测验", "其他"];

  // ---------------- SHA-256（同步实现，避免依赖 crypto.subtle 的异步/安全上下文限制） ----------------
  var K = [
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2
  ];
  function utf8Bytes(text) {
    if (window.TextEncoder) return new TextEncoder().encode(text);
    var out = [], i, c;
    for (i = 0; i < text.length; i++) {
      c = text.charCodeAt(i);
      if (c < 0x80) out.push(c);
      else if (c < 0x800) out.push(0xc0 | (c >> 6), 0x80 | (c & 63));
      else if (c >= 0xd800 && c <= 0xdbff) {
        var c2 = text.charCodeAt(++i);
        var cp = 0x10000 + ((c - 0xd800) << 10) + (c2 - 0xdc00);
        out.push(0xf0 | (cp >> 18), 0x80 | ((cp >> 12) & 63), 0x80 | ((cp >> 6) & 63), 0x80 | (cp & 63));
      } else out.push(0xe0 | (c >> 12), 0x80 | ((c >> 6) & 63), 0x80 | (c & 63));
    }
    return new Uint8Array(out);
  }
  function sha256Hex(text) {
    var bytes = utf8Bytes(text);
    var h = [0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19];
    var l = bytes.length, withPad = l + 9, blocks = Math.ceil(withPad / 64) * 64;
    var buf = new Uint8Array(blocks);
    buf.set(bytes);
    buf[l] = 0x80;
    var bits = l * 8;
    buf[blocks - 1] = bits & 255; buf[blocks - 2] = (bits >> 8) & 255;
    buf[blocks - 3] = (bits >> 16) & 255; buf[blocks - 4] = (bits >> 24) & 255;
    var w = new Int32Array(64);
    for (var off = 0; off < blocks; off += 64) {
      for (var t = 0; t < 16; t++) {
        w[t] = (buf[off + t * 4] << 24) | (buf[off + t * 4 + 1] << 16) | (buf[off + t * 4 + 2] << 8) | buf[off + t * 4 + 3];
      }
      for (t = 16; t < 64; t++) {
        var s0 = rotr(w[t - 15], 7) ^ rotr(w[t - 15], 18) ^ (w[t - 15] >>> 3);
        var s1 = rotr(w[t - 2], 17) ^ rotr(w[t - 2], 19) ^ (w[t - 2] >>> 10);
        w[t] = (w[t - 16] + s0 + w[t - 7] + s1) | 0;
      }
      var a = h[0], b = h[1], c = h[2], d = h[3], e = h[4], f = h[5], g = h[6], hh = h[7];
      for (t = 0; t < 64; t++) {
        var S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25);
        var ch = (e & f) ^ (~e & g);
        var t1 = (hh + S1 + ch + K[t] + w[t]) | 0;
        var S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22);
        var maj = (a & b) ^ (a & c) ^ (b & c);
        var t2 = (S0 + maj) | 0;
        hh = g; g = f; f = e; e = (d + t1) | 0; d = c; c = b; b = a; a = (t1 + t2) | 0;
      }
      h[0] = (h[0] + a) | 0; h[1] = (h[1] + b) | 0; h[2] = (h[2] + c) | 0; h[3] = (h[3] + d) | 0;
      h[4] = (h[4] + e) | 0; h[5] = (h[5] + f) | 0; h[6] = (h[6] + g) | 0; h[7] = (h[7] + hh) | 0;
    }
    return h.map(function (x) { return ("00000000" + (x >>> 0).toString(16)).slice(-8); }).join("").toUpperCase();
  }
  function rotr(x, n) { return (x >>> n) | (x << (32 - n)); }
  PM.sha256 = sha256Hex;

  // ---------------- 工具 ----------------
  function stamp() {
    var d = new Date();
    function p(n) { return n < 10 ? "0" + n : "" + n; }
    return d.getFullYear() + "-" + p(d.getMonth() + 1) + "-" + p(d.getDate()) + "T" + p(d.getHours()) + ":" + p(d.getMinutes()) + ":" + p(d.getSeconds());
  }
  PM.stamp = stamp;
  function num(v, fallback) { return typeof v === "number" && isFinite(v) ? v : fallback; }
  function str(v, fallback) { return typeof v === "string" ? v : fallback; }

  // ---------------- 内部模型 → 桌面快照 ----------------
  PM.snapshotOf = function (state) {
    var semesters = (state.semesters || []).map(function (s) {
      return {
        id: num(s.id, 1),
        name: str(s.name, "新学期"),
        total_weeks: num(s.totalWeeks, 18),
        week1_monday: s.week1Monday || null,
        threshold: num(s.threshold, 7),
        hours_per_week: num(s.hoursPerWeek, 20),
        type_hours: s.typeHours || TYPE_HOURS,
        current_step: num(s.currentStep, 1),
        created_at: s.createdAt || stamp(),
        updated_at: s.updatedAt || stamp(),
        courses: (s.courses || []).map(function (c) {
          return { key: c.key, name: c.name, raw_text: c.rawText || "" };
        }),
        tasks: (s.tasks || []).map(function (t) {
          return {
            course_key: t.courseKey || (s.courses[0] ? s.courses[0].key : ""),
            name: t.name,
            type: TYPES.indexOf(t.kind) >= 0 ? t.kind : "其他",
            week: t.week == null ? null : num(t.week, null),
            weight: num(t.weight, 2),
            est_hours: t.estHours == null ? null : num(t.estHours, null),
            source: t.source || ""
          };
        })
      };
    });
    var todos = (state.todos || []).map(function (t) {
      return {
        id: t.id,
        title: t.title,
        due_date: t.dueDate || null,
        important: !!t.important,
        completed: !!t.completed,
        source_key: t.sourceKey || null,
        source_label: t.sourceLabel || "",
        created_at: t.createdAt || stamp(),
        updated_at: t.updatedAt || stamp(),
        course_name: t.courseName || "",
        planned_date: t.plannedDate || null,
        task_kind: t.taskKind || "作业",
        estimated_hours: num(t.estimatedHours, 1),
        due_time: t.dueTime || null,
        requires_submission: !!t.requiresSubmission,
        submitted: !!t.submitted,
        reminders_enabled: t.remindersEnabled !== false,
        repeat_days: num(t.repeatDays, 0),
        steps: (t.steps || []).map(function (s) { return { id: s.id, title: s.title, completed: !!s.completed }; })
      };
    });
    var calendars = (state.calendars || []).map(function (c) {
      return { name: c.name, format: c.format, content: c.content, imported_at: c.importedAt || stamp() };
    });
    return { exported_at: stamp(), schema_version: PM.SCHEMA_VERSION, semesters: semesters, desktop_todos: todos, course_calendars: calendars };
  };

  // ---------------- 桌面快照 → 内部模型 ----------------
  PM.fromSnapshot = function (snap) {
    if (!snap || typeof snap !== "object") throw new Error("不是有效的备份数据");
    var version = snap.schema_version;
    if (version != null && num(version, 0) > PM.SCHEMA_VERSION) throw new Error("备份来自更新版本的应用（schema " + version + "）");
    if (!Array.isArray(snap.semesters)) throw new Error("备份里没有学期数据");
    var semesters = snap.semesters.map(function (s, index) {
      var courses = (Array.isArray(s.courses) ? s.courses : []).map(function (c) {
        return { key: str(c.key, "c" + index) || "c" + index, name: str(c.name, "未命名课程").slice(0, 40), rawText: str(c.raw_text, "") };
      });
      return {
        id: num(s.id, index + 1),
        name: str(s.name, "新学期").slice(0, 40),
        totalWeeks: Math.min(30, Math.max(1, num(s.total_weeks, 18))),
        week1Monday: /^\d{4}-\d{2}-\d{2}$/.test(s.week1_monday) ? s.week1_monday : null,
        threshold: Math.min(30, Math.max(1, num(s.threshold, 7))),
        hoursPerWeek: Math.min(100, Math.max(1, num(s.hours_per_week, 20))),
        typeHours: s.type_hours && typeof s.type_hours === "object" ? s.type_hours : Object.assign({}, TYPE_HOURS),
        currentStep: num(s.current_step, 1),
        createdAt: str(s.created_at, stamp()),
        updatedAt: str(s.updated_at, stamp()),
        courses: courses,
        tasks: (Array.isArray(s.tasks) ? s.tasks : []).filter(function (t) { return t && t.name; }).map(function (t) {
          return {
            courseKey: str(t.course_key, ""),
            name: String(t.name).slice(0, 60),
            kind: TYPES.indexOf(t.type) >= 0 ? t.type : "其他",
            week: t.week == null ? null : Math.max(1, Math.min(30, num(t.week, 1))),
            weight: num(t.weight, 2) > 0 ? num(t.weight, 2) : 2,
            estHours: t.est_hours == null ? null : Math.max(0, num(t.est_hours, 0)),
            source: str(t.source, "")
          };
        })
      };
    });
    var todos = (Array.isArray(snap.desktop_todos) ? snap.desktop_todos : []).filter(function (t) { return t && t.title; }).map(function (t) {
      return {
        id: str(t.id, "") || ("t" + Math.random().toString(36).slice(2, 10)),
        title: String(t.title).slice(0, 80),
        dueDate: /^\d{4}-\d{2}-\d{2}$/.test(t.due_date) ? t.due_date : null,
        dueTime: /^\d{2}:\d{2}$/.test(t.due_time) ? t.due_time : null,
        important: !!t.important,
        completed: !!t.completed,
        requiresSubmission: !!t.requires_submission,
        submitted: !!t.submitted,
        estimatedHours: Math.max(0, num(t.estimated_hours, 1)),
        courseName: str(t.course_name, "").slice(0, 40),
        plannedDate: /^\d{4}-\d{2}-\d{2}$/.test(t.planned_date) ? t.planned_date : null,
        taskKind: str(t.task_kind, "作业"),
        remindersEnabled: t.reminders_enabled !== false,
        repeatDays: Math.max(0, num(t.repeat_days, 0)),
        sourceKey: str(t.source_key, "") || null,
        sourceLabel: str(t.source_label, ""),
        createdAt: str(t.created_at, stamp()),
        updatedAt: str(t.updated_at, stamp()),
        steps: (Array.isArray(t.steps) ? t.steps : []).filter(function (s) { return s && s.title; }).map(function (s) {
          return { id: str(s.id, "") || ("s" + Math.random().toString(36).slice(2, 10)), title: String(s.title).slice(0, 60), completed: !!s.completed };
        })
      };
    });
    var calendars = (Array.isArray(snap.course_calendars) ? snap.course_calendars : []).filter(function (c) {
      return c && c.name && (c.format === "ics" || c.format === "csv") && typeof c.content === "string";
    }).map(function (c) {
      return { name: String(c.name).slice(0, 80), format: c.format, content: c.content, importedAt: str(c.imported_at, stamp()) };
    });
    return { semesters: semesters, todos: todos, calendars: calendars };
  };

  // ---------------- 与桌面 SnapshotData.Hash 对齐的指纹 ----------------
  /* 桌面做法：反序列化 → 重新序列化 → 去掉 exported_at/updated_at →
     把课程 key 按出现顺序重编号为 c0、c1…并同步改写任务的 course_key →
     按 id/name 排序三个列表 → 递归按键名排序 → 紧凑 JSON → SHA-256(hex, 大写)。 */
  PM.hashSnapshot = function (snap) {
    var root = JSON.parse(JSON.stringify(snap));
    delete root.exported_at;
    (root.semesters || []).forEach(function (sem) {
      delete sem.updated_at;
      var map = {};
      (sem.courses || []).forEach(function (course, index) {
        map[course.key] = "c" + index;
        course.key = "c" + index;
      });
      (sem.tasks || []).forEach(function (task) {
        task.course_key = map[task.course_key] != null ? map[task.course_key] : task.course_key;
      });
    });
    (root.desktop_todos || []).forEach(function (todo) { delete todo.updated_at; });
    function sortBy(list, key) {
      if (!Array.isArray(list)) return;
      var items = list.filter(function (x) { return x != null; });
      items.sort(function (a, b) { return String(a[key]) < String(b[key]) ? -1 : String(a[key]) > String(b[key]) ? 1 : 0; });
      return items;
    }
    if (Array.isArray(root.semesters)) root.semesters = sortBy(root.semesters, "id");
    if (Array.isArray(root.desktop_todos)) root.desktop_todos = sortBy(root.desktop_todos, "id");
    if (Array.isArray(root.course_calendars)) root.course_calendars = sortBy(root.course_calendars, "name");
    function canonical(node) {
      if (Array.isArray(node)) return node.map(canonical);
      if (node && typeof node === "object") {
        var out = {};
        Object.keys(node).sort().forEach(function (k) { out[k] = canonical(node[k]); });
        return out;
      }
      return node;
    }
    return sha256Hex(JSON.stringify(canonical(root)));
  };

  PM.isEmptySnapshot = function (snap) {
    return !snap || ((snap.semesters || []).length === 0 && (snap.desktop_todos || []).length === 0 && (snap.course_calendars || []).length === 0);
  };

  PM.summaryOf = function (snap) {
    var sems = snap.semesters || [], todos = snap.desktop_todos || [], cals = snap.course_calendars || [];
    var courses = 0;
    sems.forEach(function (s) { courses += (s.courses || []).length; });
    return sems.length + " 个学期 · " + courses + " 门课程 · " + todos.length + " 条待办 · " + cals.length + " 份课表";
  };
})();
