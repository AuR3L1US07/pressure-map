/* 学期压力地图 · 手机版
   离线单页应用：数据保存在 localStorage，不联网。
   压力算法与桌面版 PressureMap.Core/Rules.cs 保持一致。 */
(function () {
  "use strict";

  // ---------------- 常量 ----------------
  var TYPES = ["考试", "大作业", "报告", "测验", "其他"];
  var WEIGHTS = { "考试": 5, "大作业": 4, "报告": 3, "测验": 2, "其他": 2 };
  var TYPE_HOURS = { "考试": 15, "大作业": 20, "报告": 8, "测验": 4, "其他": 4 };
  var COLORS = ["#7966d7", "#3f9c7a", "#df9430", "#d7584f", "#4d8bd1", "#9a6bd1", "#c2683f", "#4fa3a5"];
  var WEEK_NAMES = ["周一", "周二", "周三", "周四", "周五", "周六", "周日"];
  var STORE_KEY = "pressuremap.mobile.v2";
  var LEGACY_KEY = "pressuremap.mobile.v1";
  var VERSION = "2.3.0 (20261021)";

  // ---------------- 工具 ----------------
  function $(id) { return document.getElementById(id); }
  /** 是否运行在原生壳里（Android WebView / iOS WKWebView），用来决定要不要提示「装到手机上」。 */
  function isNativeShell() {
    if (window.AndroidHost) return true;
    return !!(window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.saveBackup);
  }
  function uid() { return Date.now().toString(36) + Math.random().toString(36).slice(2, 8); }
  function pad(n) { return n < 10 ? "0" + n : "" + n; }
  function ymd(d) { return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }
  function today() { var d = new Date(); d.setHours(0, 0, 0, 0); return d; }
  function parseYmd(s) {
    if (!s) return null;
    var p = String(s).split("-");
    if (p.length !== 3) return null;
    var d = new Date(+p[0], +p[1] - 1, +p[2]);
    return isNaN(d.getTime()) ? null : d;
  }
  function dayDiff(a, b) { return Math.round((a - b) / 86400000); }
  function round2(n) { return Math.round(n * 100) / 100; }
  function clamp(n, lo, hi) { return Math.min(hi, Math.max(lo, n)); }
  function esc(s) {
    return String(s == null ? "" : s).replace(/[&<>"']/g, function (c) {
      return { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c];
    });
  }
  function dueLabel(dateStr, timeStr) {
    var d = parseYmd(dateStr);
    if (!d) return "";
    var diff = dayDiff(d, today());
    var head = diff === 0 ? "今天" : diff === 1 ? "明天" : diff === 2 ? "后天" : diff < 0 ? "已逾期 " + Math.abs(diff) + " 天" : (d.getMonth() + 1) + "月" + d.getDate() + "日";
    return head + (timeStr ? " " + timeStr : "");
  }
  function courseColor(sem, key) {
    var i = sem.courses.findIndex(function (c) { return c.key === key; });
    return i < 0 ? COLORS[0] : COLORS[i % COLORS.length];
  }
  function courseName(sem, key) {
    var c = sem.courses.find(function (x) { return x.key === key; });
    return c ? c.name : "未分类课程";
  }

  // ---------------- 状态 ----------------
  var state = null;
  var ui = { view: "today", todoFilter: "open", dayIndex: 1, week: 1 };

  function defaultSettings() {
    return { theme: "auto", weekStart: 1, language: "zh", dailyCapacityHours: 6, apiUrl: "", remindersEnabled: true, onlineSync: "" };
  }

  function defaultSemester(id) {
    var base = today();
    var monday = new Date(base);
    monday.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    return {
      id: id || 1,
      name: "新学期",
      totalWeeks: 18,
      week1Monday: ymd(monday),
      threshold: 7,
      hoursPerWeek: 20,
      typeHours: Object.assign({}, TYPE_HOURS),
      currentStep: 1,
      createdAt: PM.stamp(),
      updatedAt: PM.stamp(),
      courses: [],
      tasks: []
    };
  }

  function blankState() {
    var s = {
      v: 2,
      settings: defaultSettings(),
      semesters: [defaultSemester(1)],
      current: 0,
      calendars: [],
      todos: [],
      slots: [],
      ai: { baseUrl: "", model: "", apiKey: "" },
      cloud: { baseUrl: PM.DEFAULT_API_BASE, accounts: [], revision: null, hash: null }
    };
    return defineSem(s);
  }

  /** 接受两种备份：桌面版快照（schema_version/desktop_todos/course_calendars）与 1.x 手机备份。 */
  function adoptSnapshot(parsed) {
    if (!parsed || typeof parsed !== "object") throw new Error("不是有效的备份文件");
    var isSnapshot = parsed.schema_version != null || Array.isArray(parsed.desktop_todos) || Array.isArray(parsed.course_calendars);
    if (isSnapshot) {
      var converted = PM.fromSnapshot(parsed);
      var next = normalize({
        settings: state.settings,
        semesters: converted.semesters,
        current: 0,
        calendars: converted.calendars,
        todos: converted.todos,
        slots: [],
        ai: state.ai,
        cloud: state.cloud
      });
      if (!next.semesters.length) throw new Error("没有可导入的数据");
      return next;
    }
    var legacy = normalize(parsed);
    if (!legacy.semester.courses.length && !legacy.todos.length && !legacy.calendars.length) throw new Error("没有可导入的数据");
    return legacy;
  }

  /** 云同步下载后替换全部数据，但保留本机设置与登录状态。 */
  function applyAdopted(snap) {
    var converted = PM.fromSnapshot(snap);
    state = normalize({
      settings: state.settings,
      semesters: converted.semesters,
      current: 0,
      calendars: converted.calendars,
      todos: converted.todos,
      slots: [],
      ai: state.ai,
      cloud: state.cloud
    });
    ui.dayIndex = ((new Date().getDay() + 6) % 7) + 1;
    ui.week = currentWeek();
    save();
    if (!sheetOpen) render();
  }
  /** `state.semester` 始终指向当前学期，历史界面代码因此不用改。
      定义为不可枚举的取值器，序列化时不会重复出现。 */
  function defineSem(s) {
    try {
      Object.defineProperty(s, "semester", {
        enumerable: false,
        configurable: true,
        get: function () { return s.semesters[s.current] || s.semesters[0]; },
        set: function (value) { s.semesters[s.current] = value; }
      });
    } catch (e) { }
    return s;
  }

  function load() {
    var raw = null;
    try { raw = localStorage.getItem(STORE_KEY); } catch (e) { raw = null; }
    if (!raw) {
      var legacy = null;
      try { legacy = localStorage.getItem(LEGACY_KEY); } catch (e) { legacy = null; }
      if (legacy) {
        try {
          state = normalize(JSON.parse(legacy));
          save();
          setTimeout(function () { toast("数据已升级，课表迁移为日历格式"); }, 700);
          return;
        } catch (e) { }
      }
      state = sampleState();
      save();
      return;
    }
    try {
      state = normalize(JSON.parse(raw));
    } catch (e) {
      state = sampleState();
    }
  }

  function normalizeSemester(src, index) {
    var base = defaultSemester(index + 1);
    if (!src || typeof src !== "object") return base;
    return {
      id: Number(src.id) || index + 1,
      name: typeof src.name === "string" && src.name ? src.name.slice(0, 40) : base.name,
      totalWeeks: clamp(Number(src.totalWeeks) || 18, 1, 30),
      week1Monday: parseYmd(src.week1Monday) ? src.week1Monday : base.week1Monday,
      threshold: clamp(Number(src.threshold) || 7, 1, 30),
      hoursPerWeek: clamp(Number(src.hoursPerWeek) || 20, 1, 100),
      typeHours: src.typeHours && typeof src.typeHours === "object" ? src.typeHours : Object.assign({}, TYPE_HOURS),
      currentStep: Number(src.currentStep) || 1,
      createdAt: src.createdAt || base.createdAt,
      updatedAt: src.updatedAt || base.updatedAt,
      courses: Array.isArray(src.courses) ? src.courses.filter(function (c) { return c && c.key && c.name; }).slice(0, 30).map(function (c) {
        return { key: String(c.key), name: String(c.name).slice(0, 40), rawText: String(c.rawText || "") };
      }) : [],
      tasks: Array.isArray(src.tasks) ? src.tasks.filter(function (t) { return t && t.name; }).slice(0, 300).map(function (t) {
        return {
          courseKey: t.courseKey || "",
          name: String(t.name).slice(0, 60),
          kind: TYPES.indexOf(t.kind) >= 0 ? t.kind : "其他",
          week: t.week == null ? null : clamp(Number(t.week) || 0, 1, 30),
          weight: Number(t.weight) > 0 ? Number(t.weight) : (WEIGHTS[t.kind] || 2),
          estHours: t.estHours == null || t.estHours === "" ? null : Math.max(0, Number(t.estHours) || 0),
          source: t.source || ""
        };
      }) : []
    };
  }

  function normalizeTodo(t) {
    if (!t || !t.title) return null;
    return {
      id: t.id || uid(),
      title: String(t.title).slice(0, 80),
      courseName: t.courseName ? String(t.courseName).slice(0, 40) : "",
      dueDate: parseYmd(t.dueDate) ? t.dueDate : null,
      dueTime: /^\d{2}:\d{2}$/.test(t.dueTime) ? t.dueTime : null,
      plannedDate: parseYmd(t.plannedDate) ? t.plannedDate : null,
      taskKind: t.taskKind ? String(t.taskKind).slice(0, 20) : "作业",
      important: !!t.important,
      completed: !!t.completed,
      requiresSubmission: !!t.requiresSubmission,
      submitted: !!t.submitted,
      remindersEnabled: t.remindersEnabled !== false,
      repeatDays: Math.max(0, Number(t.repeatDays) || 0),
      sourceKey: t.sourceKey || null,
      sourceLabel: t.sourceLabel ? String(t.sourceLabel).slice(0, 40) : "",
      createdAt: t.createdAt || PM.stamp(),
      updatedAt: t.updatedAt || PM.stamp(),
      estimatedHours: Math.max(0, Number(t.estimatedHours) || 1),
      steps: Array.isArray(t.steps) ? t.steps.filter(function (x) { return x && x.title; }).map(function (x) {
        return { id: x.id || uid(), title: String(x.title).slice(0, 60), completed: !!x.completed };
      }) : []
    };
  }

  function normalize(input) {
    var base = blankState();
    if (!input || typeof input !== "object") return base;
    var s = {
      v: 2,
      settings: {
        theme: ["auto", "light", "dark"].indexOf(input.settings && input.settings.theme) >= 0 ? input.settings.theme : "auto",
        weekStart: input.settings && input.settings.weekStart === 0 ? 0 : 1,
        language: input.settings && input.settings.language === "en" ? "en" : "zh",
        dailyCapacityHours: clamp(Number(input.settings && input.settings.dailyCapacityHours) || 6, 0.5, 24),
        apiUrl: (input.settings && input.settings.apiUrl) || "",
        remindersEnabled: !(input.settings && input.settings.remindersEnabled === false),
        onlineSync: (input.settings && input.settings.onlineSync) || ""
      },
      semesters: [],
      current: 0,
      calendars: [],
      todos: [],
      slots: [],
      ai: {
        baseUrl: (input.ai && input.ai.baseUrl) || "",
        model: (input.ai && input.ai.model) || "",
        apiKey: (input.ai && input.ai.apiKey) || ""
      },
      cloud: {
        baseUrl: (input.cloud && input.cloud.baseUrl) || PM.DEFAULT_API_BASE,
        accounts: Array.isArray(input.cloud && input.cloud.accounts) ? input.cloud.accounts.filter(function (a) { return a && a.key; }) : [],
        activeKey: (input.cloud && input.cloud.activeKey) || "",
        revision: (input.cloud && input.cloud.revision != null) ? String(input.cloud.revision) : null,
        hash: (input.cloud && input.cloud.hash) || null
      }
    };
    if (Array.isArray(input.semesters) && input.semesters.length) {
      s.semesters = input.semesters.map(normalizeSemester);
      s.current = clamp(Number(input.current) || 0, 0, s.semesters.length - 1);
    } else if (input.semester && typeof input.semester === "object") {
      s.semesters = [normalizeSemester(input.semester, 0)];
    } else {
      s.semesters = [defaultSemester(1)];
    }
    if (Array.isArray(input.calendars)) {
      s.calendars = input.calendars.filter(function (c) { return c && c.name && (c.format === "ics" || c.format === "csv") && typeof c.content === "string"; }).map(function (c) {
        return { name: String(c.name).slice(0, 80), format: c.format, content: c.content, importedAt: c.importedAt || PM.stamp() };
      });
    }
    // 1.x 的手排时段没有真实日期，迁移成一份 ICS 周重复日历，课表逻辑随之统一。
    if (!s.calendars.length && Array.isArray(input.slots) && input.slots.length) {
      var ics = PM.slotsToIcs(input.slots.filter(function (x) { return x && x.name; }).map(function (x) {
        return {
          name: String(x.name).slice(0, 40),
          weekday: clamp(Number(x.weekday) || 1, 1, 7),
          start: /^\d{2}:\d{2}$/.test(x.start) ? x.start : "08:00",
          end: /^\d{2}:\d{2}$/.test(x.end) ? x.end : "09:40",
          location: x.location ? String(x.location).slice(0, 40) : ""
        };
      }), s.semesters[0]);
      if (ics) s.calendars.push({ name: "手机版课表（迁移）.ics", format: "ics", content: ics, importedAt: PM.stamp() });
    }
    s.todos = Array.isArray(input.todos) ? input.todos.map(normalizeTodo).filter(Boolean) : [];
    return defineSem(s);
  }

  function save() {
    try {
      var sem = state.semester;
      if (sem) sem.updatedAt = PM.stamp();
      // 登录云账号后数据写在账号自己的键下，与「未登录」的本机数据互不干扰
      var key = (window.PMUI && PMUI.storeKey) ? PMUI.storeKey() : STORE_KEY;
      localStorage.setItem(key, JSON.stringify(state));
      if (window.PMUI && PMUI.onDataChanged) PMUI.onDataChanged();
      queueReminderSync();
    } catch (e) { toast("保存失败，存储空间可能已满"); }
  }

  /**
   * 待办一变就把提醒计划重排一遍（防抖 1.5 秒，避免连续勾选时反复排闹钟）。
   * 原生壳里 PM.Reminders.sync 会走 AndroidHost.syncReminders；网页版直接返回 false。
   */
  var reminderTimer = null;
  function queueReminderSync() {
    if (!window.PM || !PM.Reminders) return;
    clearTimeout(reminderTimer);
    reminderTimer = setTimeout(function () { PM.Reminders.sync(state); }, 1500);
  }

  // ---------------- 压力算法（对应 Rules.Compute / Rules.Plan） ----------------
  function compute(sem) {
    var weeks = sem.totalWeeks;
    var per = {};
    var total = [];
    for (var i = 0; i <= weeks; i++) total.push(0);
    sem.tasks.forEach(function (t) {
      if (!(t.week >= 1 && t.week <= weeks)) return;
      if (!per[t.courseKey]) {
        per[t.courseKey] = [];
        for (var i = 0; i <= weeks; i++) per[t.courseKey].push(0);
      }
      var w = t.weight || WEIGHTS[t.kind] || 2;
      [[0, 1], [1, 0.6], [2, 0.3]].forEach(function (pair) {
        var week = t.week - pair[0];
        if (week > 0) {
          per[t.courseKey][week] += w * pair[1];
          total[week] += w * pair[1];
        }
      });
    });
    Object.keys(per).forEach(function (k) { per[k] = per[k].map(round2); });
    return { per: per, total: total.map(round2) };
  }

  function level(value, threshold) {
    if (value >= threshold * 1.4) return "hot";
    if (value >= threshold) return "warn";
    return "ok";
  }

  function plan(sem) {
    var computed = compute(sem);
    var capacity = [];
    for (var w = 0; w <= sem.totalWeeks; w++) {
      capacity.push(w === 0 ? 0 : sem.hoursPerWeek * 0.4 * (computed.total[w] >= sem.threshold ? 0.5 : 1));
    }
    var result = sem.tasks.filter(function (t) { return t.week >= 1 && t.week <= sem.totalWeeks; })
      .slice()
      .sort(function (a, b) { return a.week - b.week; })
      .map(function (t) {
        var need = t.estHours == null ? (TYPE_HOURS[t.kind] || 4) : t.estHours;
        var start = t.week;
        var alloc = [];
        for (var week = t.week; week > 0 && need > 1e-8; week--) {
          var give = Math.min(capacity[week], need);
          if (give <= 1e-8) continue;
          capacity[week] -= give;
          need = round2(need - give);
          start = week;
          alloc.push({ week: week, hours: round2(give) });
        }
        if (need > 1e-8) start = 1;
        var overlap = [];
        for (var x = start; x <= t.week; x++) if (computed.total[x] >= sem.threshold) overlap.push(x);
        return { task: t, start: start, missing: Math.max(0, need), alloc: alloc, overlap: overlap };
      });
    return result.sort(function (a, b) { return a.start - b.start || a.task.week - b.task.week; });
  }

  function currentWeek() {
    var sem = state.semester;
    var monday = parseYmd(sem.week1Monday);
    if (!monday) return 1;
    var week = Math.floor(dayDiff(today(), monday) / 7) + 1;
    return clamp(week, 1, sem.totalWeeks);
  }

  function weekOf(dateStr) {
    var sem = state.semester;
    var monday = parseYmd(sem.week1Monday);
    var d = parseYmd(dateStr);
    if (!monday || !d) return null;
    var week = Math.floor(dayDiff(d, monday) / 7) + 1;
    return week >= 1 && week <= sem.totalWeeks ? week : null;
  }

  // ---------------- 示例数据 ----------------
  function sampleState() {
    var s = blankState();
    var base = today();
    var monday = new Date(base);
    monday.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    var sem = s.semester;
    sem.name = "2026 秋季学期";
    sem.totalWeeks = 18;
    sem.week1Monday = ymd(monday);
    sem.threshold = 7;
    sem.hoursPerWeek = 22;

    var names = ["高等数学", "数据结构", "大学英语", "计算机网络", "概率论", "体育"];
    sem.courses = names.map(function (n) { return { key: "c" + Math.random().toString(36).slice(2, 8), name: n }; });

    function task(ci, name, kind, week, est) {
      return { id: uid(), courseKey: sem.courses[ci].key, name: name, kind: kind, week: week, weight: WEIGHTS[kind], estHours: est };
    }
    sem.tasks = [
      task(0, "第三章习题", "其他", 3, 5),
      task(0, "期中考试", "考试", 8, 15),
      task(1, "链表实验报告", "报告", 4, 8),
      task(1, "课程设计开题", "大作业", 6, 10),
      task(1, "课程设计答辩", "大作业", 16, 22),
      task(2, "单元测验 2", "测验", 5, 4),
      task(2, "英语演讲稿", "报告", 9, 8),
      task(3, "网络实验一", "报告", 5, 6),
      task(3, "期末大作业", "大作业", 15, 24),
      task(4, "概率论作业 4", "其他", 7, 4),
      task(4, "期末考试", "考试", 18, 18)
    ];

    var times = [["08:00", "09:40"], ["10:00", "11:40"], ["14:00", "15:40"], ["16:00", "17:40"]];
    var plan = [
      [1, 0, "高等数学", "A201"], [1, 2, "数据结构", "B305"], [1, 3, "体育", "操场"],
      [2, 1, "大学英语", "C102"], [2, 2, "计算机网络", "B208"],
      [3, 0, "数据结构", "B305"], [3, 2, "概率论", "A104"],
      [4, 1, "高等数学", "A201"], [4, 3, "计算机网络", "B208"],
      [5, 0, "大学英语", "C102"], [5, 2, "体育", "操场"]
    ];
    s.slots = plan.map(function (p) {
      var ci = names.indexOf(p[2]);
      return {
        id: uid(),
        name: p[2],
        weekday: p[0],
        start: times[p[1]][0],
        end: times[p[1]][1],
        location: p[3],
        courseKey: sem.courses[ci].key
      };
    });

    var d1 = new Date(base); d1.setDate(base.getDate() + 1);
    var d2 = new Date(base); d2.setDate(base.getDate() + 2);
    var d3 = new Date(base); d3.setDate(base.getDate() + 5);
    var d0 = new Date(base); d0.setDate(base.getDate() - 1);
    s.todos = [
      { id: uid(), title: "数据结构实验报告 3", courseName: "数据结构", dueDate: ymd(d1), dueTime: "23:59", important: true, completed: false, requiresSubmission: true, submitted: false, estimatedHours: 3, steps: [{ id: uid(), title: "画测试用例图", completed: true }, { id: uid(), title: "补运行截图", completed: false }] },
      { id: uid(), title: "背 60 个单词", courseName: "大学英语", dueDate: ymd(base), dueTime: null, important: false, completed: false, requiresSubmission: false, submitted: false, estimatedHours: 1, steps: [] },
      { id: uid(), title: "网络实验一 抓包分析", courseName: "计算机网络", dueDate: ymd(d2), dueTime: "18:00", important: false, completed: false, requiresSubmission: true, submitted: false, estimatedHours: 2, steps: [] },
      { id: uid(), title: "高数第三章习题 1-12", courseName: "高等数学", dueDate: ymd(d0), dueTime: "23:59", important: false, completed: true, requiresSubmission: false, submitted: false, estimatedHours: 2, steps: [] },
      { id: uid(), title: "准备课程设计开题 PPT", courseName: "数据结构", dueDate: ymd(d3), dueTime: null, important: true, completed: false, requiresSubmission: false, submitted: false, estimatedHours: 4, steps: [] }
    ];
    return normalize(s);
  }

  // ---------------- 主题 ----------------
  var media = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;

  function resolvedTheme() {
    var t = state.settings.theme;
    if (t === "auto") return media && media.matches ? "dark" : "light";
    return t;
  }

  function applyTheme() {
    var t = resolvedTheme();
    document.documentElement.setAttribute("data-theme", t);
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", t === "dark" ? "#17151e" : "#f8f7fb");
    $("themeIcon").innerHTML = '<use href="#' + (t === "dark" ? "i-moon" : "i-sun") + '"></use>';
    if (window.AndroidHost && window.AndroidHost.setDark) {
      try { window.AndroidHost.setDark(t === "dark"); } catch (e) { }
    }
  }

  if (media && media.addEventListener) {
    media.addEventListener("change", function () { if (state.settings.theme === "auto") applyTheme(); });
  }

  // ---------------- toast ----------------
  var toastTimer = null;
  function toast(msg) {
    var el = $("toast");
    el.textContent = msg;
    el.classList.add("is-on");
    clearTimeout(toastTimer);
    toastTimer = setTimeout(function () { el.classList.remove("is-on"); }, 2200);
  }

  // ---------------- 导航 ----------------
  function goto(view) {
    if (view === ui.view) return;
    ui.view = view;
    ["today", "calendar", "todos", "pressure", "ai", "settings"].forEach(function (v) {
      var node = $("view-" + v);
      if (node) node.hidden = v !== view;
    });
    Array.prototype.forEach.call(document.querySelectorAll(".tab"), function (btn) {
      btn.classList.toggle("is-on", btn.getAttribute("data-tab") === view);
    });
    $("main").scrollTop = 0;
    window.scrollTo({ top: 0, behavior: "auto" });
    renderFab();
    render();
  }

  function renderFab() {
    var fab = $("fab");
    fab.hidden = ui.view === "settings" || ui.view === "ai";
    fab.setAttribute("aria-label", ui.view === "calendar" ? "导入课表" : ui.view === "pressure" ? "添加任务" : "新建待办");
  }

  // ---------------- 渲染：今日 ----------------
  function renderToday() {
    var sem = state.semester;
    var cw = currentWeek();
    var computed = compute(sem);
    var value = computed.total[cw] || 0;
    var lv = level(value, sem.threshold);

    var openTasks = sem.tasks.filter(function (t) { return t.week >= cw; }).length;
    var openTodos = state.todos.filter(function (t) { return !t.completed; }).length;
    var todaySlots = slotsOfDay(new Date().getDay() === 0 ? 7 : new Date().getDay());

    $("heroEyebrow").textContent = "第 " + cw + " 周 · 本周压力";
    $("heroTitle").textContent = lv === "hot" ? "红色预警" : lv === "warn" ? "压力偏高" : value > 0 ? "节奏正常" : "轻松";
    $("heroNote").textContent = openTasks > 0
      ? "还有 " + openTasks + " 个任务在这周之后截止"
      : "这学期暂时没有安排任务";
    $("ringScore").textContent = value.toFixed(1).replace(/\.0$/, "");
    $("ringUnit").textContent = "压力值";

    var ratio = clamp(value / (sem.threshold * 1.6), 0, 1);
    var ring = $("ringValue");
    setTimeout(function () { ring.style.strokeDashoffset = String(264 * (1 - ratio)); }, 40);

    $("heroStats").innerHTML =
      stat(todaySlots.length, "今日课程") +
      stat(openTodos, "待办未完成") +
      stat(fmtCount(sem.tasks.length), "任务总数");

    // 今日课程
    var box = $("todayClasses");
    if (!todaySlots.length) {
      box.innerHTML = emptyBox("今天没有课，可以补一补作业");
    } else {
      box.innerHTML = todaySlots.map(slotRow).join("");
    }

    // 今天要做
    var todays = state.todos.filter(function (t) { return !t.completed && t.dueDate === ymd(today()); });
    var overdue = state.todos.filter(function (t) { return !t.completed && t.dueDate && t.dueDate < ymd(today()); });
    var dueNow = overdue.concat(todays);
    var tbox = $("todayTodos");
    tbox.innerHTML = dueNow.length ? dueNow.map(todoRow).join("") : emptyBox("今天没有截止的事，做点想做的");

    // 最近截止
    var upcoming = state.todos
      .filter(function (t) { return !t.completed && t.dueDate && t.dueDate > ymd(today()); })
      .sort(function (a, b) { return (a.dueDate + (a.dueTime || "23:59")) < (b.dueDate + (b.dueTime || "23:59")) ? -1 : 1; })
      .slice(0, 3);
    $("upcomingTodos").innerHTML = upcoming.length ? upcoming.map(todoRow).join("") : emptyBox("后面几天还没有安排");
  }

  function fmtCount(n) { return n > 99 ? "99+" : String(n); }

  function stat(value, label) {
    return '<div class="hero-stat"><strong>' + esc(value) + "</strong><span>" + esc(label) + "</span></div>";
  }

  function emptyBox(text) {
    return '<div class="empty"><svg class="ico"><use href="#i-empty"></use></svg><span>' + esc(text) + "</span></div>";
  }

  function clockText(d) { return (d.getHours() < 10 ? "0" : "") + d.getHours() + ":" + (d.getMinutes() < 10 ? "0" : "") + d.getMinutes(); }

  function weekdayDate(weekday) {
    var base = today();
    var monday = new Date(base);
    monday.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    return new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + (weekday - 1));
  }

  /** 一天的课：优先用导入的课表（真实日期，能反映调课停课），没有课表时退回旧的手排时段。 */
  function classesOfDate(date) {
    var calendars = state.calendars || [];
    if (calendars.length) {
      return PM.dayOccurrences(calendars, date).map(function (c) {
        return {
          id: c.source + "@" + c.start.getTime(),
          title: c.title,
          start: clockText(c.start),
          end: clockText(c.end),
          location: c.location,
          source: c.source,
          imported: true
        };
      });
    }
    var weekday = ((date.getDay() + 6) % 7) + 1;
    return (state.slots || []).filter(function (s) { return Number(s.weekday) === weekday; })
      .sort(function (a, b) { return a.start < b.start ? -1 : a.start > b.start ? 1 : 0; })
      .map(function (s) {
        return { id: s.id, title: s.name, start: s.start, end: s.end, location: s.location, courseKey: s.courseKey, imported: false };
      });
  }

  function slotsOfDay(weekday) { return classesOfDate(weekdayDate(weekday)); }

  function slotRow(s) {
    var color = s.courseKey ? courseColor(state.semester, s.courseKey) : colorByName(s.title);
    var attr = s.imported ? ' data-cal="' + esc(s.id) + '"' : ' data-slot="' + esc(s.id) + '"';
    return '<div class="item"' + attr + ">" +
      '<div class="time-col">' + esc(s.start) + "<span>" + esc(s.end) + "</span></div>" +
      '<div class="course-dot" style="background:' + color + '"></div>' +
      '<div class="item-main"><div class="item-title">' + esc(s.title) + "</div>" +
      ((s.location || s.imported) ? '<div class="item-meta"><span>' + esc(s.location || "未填写教室") + "</span>" +
        (s.imported ? "<span>" + esc(s.source) + "</span>" : "") + "</div>" : "") +
      "</div></div>";
  }

  function todoRow(t) {
    var diff = t.dueDate ? dayDiff(parseYmd(t.dueDate), today()) : null;
    var cls = t.completed ? "item is-done" : "item";
    var dueCls = diff === null ? "" : diff < 0 ? "due-hot" : diff === 0 ? "due-hot" : diff <= 2 ? "due-warn" : "";
    var meta = [];
    if (t.courseName) meta.push('<span><i class="course-dot" style="display:inline-block;background:' + colorByName(t.courseName) + ';margin:0 5px 2px 0"></i>' + esc(t.courseName) + "</span>");
    if (t.dueDate) meta.push('<span class="' + dueCls + '">' + esc(dueLabel(t.dueDate, t.dueTime)) + "</span>");
    if (t.important) meta.push("<span>重要</span>");
    if (t.steps && t.steps.length) {
      meta.push("<span>子任务 " + t.steps.filter(function (s) { return s.completed; }).length + "/" + t.steps.length + "</span>");
    }
    if (t.requiresSubmission) meta.push("<span>" + (t.submitted ? "已提交" : t.completed ? "待提交" : "需提交") + "</span>");
    return '<div class="' + cls + '" data-todo="' + t.id + '">' +
      '<button class="tick' + (t.completed ? " is-on" : "") + '" type="button" data-tick="' + t.id + '" aria-label="完成">' +
      '<svg class="ico"><use href="#i-check"></use></svg></button>' +
      '<div class="item-main"><div class="item-title">' + esc(t.title) + "</div>" +
      (meta.length ? '<div class="item-meta">' + meta.join("") + "</div>" : "") +
      "</div></div>";
  }

  function colorByName(name) {
    var c = state.semester.courses.find(function (x) { return x.name === name; });
    return c ? courseColor(state.semester, c.key) : COLORS[4];
  }

  // ---------------- 渲染：课表 ----------------
  function renderCalendar() {
    var base = today();
    var monday = new Date(base);
    monday.setDate(base.getDate() - ((base.getDay() + 6) % 7));
    var todayIdx = ((base.getDay() + 6) % 7) + 1;

    var strip = "";
    for (var i = 0; i < 7; i++) {
      var d = new Date(monday);
      d.setDate(monday.getDate() + i);
      var on = ui.dayIndex === i + 1;
      strip += '<button class="day-btn' + (on ? " is-on" : "") + (i + 1 === todayIdx ? " is-today" : "") + '" type="button" data-day="' + (i + 1) + '">' +
        "<span>" + WEEK_NAMES[i].replace("周", "") + "</span><b>" + d.getDate() + "</b></button>";
    }
    $("weekStrip").innerHTML = strip;

    var classes = slotsOfDay(ui.dayIndex);
    $("dayTitle").textContent = "第 " + currentWeek() + " 周 · " + WEEK_NAMES[ui.dayIndex - 1] + " · " + classes.length + " 节";
    $("dayClasses").innerHTML = classes.length ? classes.map(slotRow).join("") : emptyBox("这天没有课；点右上角可导入课表");

    var sources = state.calendars || [];
    var box = $("calSources");
    if (box) {
      box.innerHTML = sources.length ? sources.map(function (c) {
        return '<div class="item"><div class="item-main"><div class="item-title">' + esc(c.name) + "</div>" +
          '<div class="item-meta"><span>' + (c.format === "ics" ? "ICS 日历" : "CSV 课表") + "</span><span>" + esc(c.importedAt || "") + "</span></div></div>" +
          '<button class="icon-btn" type="button" data-caldel="' + esc(c.name) + '" aria-label="删除课表"><svg class="ico"><use href="#i-trash"></use></svg></button></div>';
      }).join("") : '<p class="hint">还没有导入课表。支持教务导出的 .ics / .csv，导入后按真实日期显示，调课停课都能反映。</p>';
    }
  }

  // ---------------- 渲染：待办 ----------------
  function renderTodos() {
    var f = ui.todoFilter;
    var list = state.todos.slice();
    var todayStr = ymd(today());
    if (f === "open") list = list.filter(function (t) { return !t.completed; });
    else if (f === "done") list = list.filter(function (t) { return t.completed; });
    else if (f === "today") list = list.filter(function (t) { return t.dueDate === todayStr; });

    list.sort(function (a, b) {
      if (a.completed !== b.completed) return a.completed ? 1 : -1;
      if (!a.dueDate && !b.dueDate) return 0;
      if (!a.dueDate) return 1;
      if (!b.dueDate) return -1;
      var ka = a.dueDate + (a.dueTime || "23:59");
      var kb = b.dueDate + (b.dueTime || "23:59");
      return ka < kb ? -1 : ka > kb ? 1 : 0;
    });

    $("todoList").innerHTML = list.length ? list.map(todoRow).join("") : emptyBox(f === "done" ? "还没有完成的事" : "没有待办，点右下角新建");

    var open = state.todos.filter(function (t) { return !t.completed; }).length;
    var done = state.todos.length - open;
    $("todoHint").textContent = "共 " + state.todos.length + " 条 · 未完成 " + open + " 条 · 已完成 " + done + " 条";

    Array.prototype.forEach.call($("todoFilters").children, function (b) {
      b.classList.toggle("is-on", b.getAttribute("data-filter") === f);
    });
  }

  // ---------------- 渲染：压力 ----------------
  function renderPressure() {
    var sem = state.semester;
    var computed = compute(sem);
    var cw = ui.week && ui.week >= 1 && ui.week <= sem.totalWeeks ? ui.week : currentWeek();
    ui.week = cw;

    $("thresholdChip").textContent = "阈值 " + sem.threshold;
    $("weekChip").textContent = "第 " + cw + " 周";

    // 图表
    var max = Math.max(sem.threshold * 1.4, Math.max.apply(null, computed.total.slice(1)) || 1);
    var n = sem.totalWeeks;
    var W = 700, H = 220, padL = 6, padB = 26, padT = 12;
    var gap = 4;
    var bw = (W - padL * 2 - gap * (n - 1)) / n;
    var bars = "";
    var labelStep = n > 14 ? 2 : 1;
    for (var w = 1; w <= n; w++) {
      var v = computed.total[w] || 0;
      var h = Math.max(v > 0 ? 3 : 0, (v / max) * (H - padB - padT));
      var x = padL + (w - 1) * (bw + gap);
      var y = H - padB - h;
      var cls = v >= sem.threshold * 1.4 ? "bar-hot" : v >= sem.threshold ? "bar-warn" : "bar-ok";
      if (w === cw) cls += " bar-now";
      bars += '<rect class="bar-track" x="' + x.toFixed(1) + '" y="' + padT + '" width="' + bw.toFixed(1) + '" height="' + (H - padB - padT) + '" rx="3"/>';
      if (h > 0) bars += '<rect class="' + cls + '" x="' + x.toFixed(1) + '" y="' + y.toFixed(1) + '" width="' + bw.toFixed(1) + '" height="' + h.toFixed(1) + '" rx="3"/>';
      if ((w - 1) % labelStep === 0) {
        bars += '<text x="' + (x + bw / 2).toFixed(1) + '" y="' + (H - 8) + '" text-anchor="middle">' + w + "</text>";
      }
    }
    var ty = H - padB - (sem.threshold / max) * (H - padB - padT);
    var thresholdLine = '<line x1="' + padL + '" y1="' + ty.toFixed(1) + '" x2="' + (W - padL) + '" y2="' + ty.toFixed(1) + '" stroke="currentColor" stroke-opacity=".28" stroke-dasharray="5 5" stroke-width="1.2"/>';
    $("pressureChart").innerHTML = bars + thresholdLine;

    // 本周课程压力
    var rows = [];
    Object.keys(computed.per).forEach(function (key) {
      var v = computed.per[key][cw] || 0;
      if (v > 0) rows.push({ key: key, value: v });
    });
    rows.sort(function (a, b) { return b.value - a.value; });
    $("coursePressure").innerHTML = rows.length ? rows.map(function (r) {
      var pct = clamp(r.value / (sem.threshold * 1.4), 0.04, 1) * 100;
      var lv = level(r.value, sem.threshold);
      return '<div class="press-row">' +
        '<div class="course-dot" style="background:' + courseColor(sem, r.key) + ';margin-top:0"></div>' +
        '<div class="name">' + esc(courseName(sem, r.key)) + "</div>" +
        '<div class="press-bar"><i class="' + (lv === "ok" ? "" : lv) + '" style="width:' + pct.toFixed(1) + '%"></i></div>' +
        '<div class="press-val">' + r.value.toFixed(1) + "</div></div>";
    }).join("") : emptyBox("这一周还没有任务压力");

    // 学习计划
    var plans = plan(sem).filter(function (p) { return p.start === cw || p.task.week === cw; }).slice(0, 8);
    $("plans").innerHTML = plans.length ? plans.map(function (p) {
      var totalHours = p.alloc.reduce(function (a, b) { return a + b.hours; }, 0);
      var meta = "第 " + p.start + " 周开始 → 第 " + p.task.week + " 周截止" +
        (totalHours > 0 ? " · 共 " + round2(totalHours) + " 小时" : "");
      return '<div class="plan-item"><div class="plan-top"><span class="plan-title">' + esc(p.task.name) + "</span>" +
        '<span class="chip">' + esc(p.task.kind) + "</span></div>" +
        '<div class="plan-meta' + (p.missing > 0.01 ? " late" : "") + '">' + esc(meta) +
        (p.missing > 0.01 ? " · 缺少 " + round2(p.missing) + " 小时" : "") + "</div>" +
        (p.overlap.length ? '<div class="plan-meta">第 ' + p.overlap.join("、") + " 周有压力预警，可用时间减半</div>" : "") +
        "</div>";
    }).join("") : emptyBox("这一周没有需要提前开始的任务");
  }

  // ---------------- 渲染：设置 ----------------
  /** 提醒状态标签：网页版没有系统通知，装成 App 才有。 */
  function remindChipLabel() {
    if (!(window.PM && PM.Reminders && PM.Reminders.hasNative())) return "网页版不可用";
    if (state.settings.remindersEnabled === false) return "关闭";
    var host = window.AndroidHost;
    if (host && typeof host.canNotify === "function" && !host.canNotify()) return "未授权";
    return "已开启";
  }
  window.pmNotifyChanged = function () { renderSettings(); };

  function renderSettings() {
    var sem = state.semester;
    $("semName").value = sem.name;
    $("semWeeks").value = sem.totalWeeks;
    $("semThreshold").value = sem.threshold;
    $("semHours").value = sem.hoursPerWeek;
    $("semMonday").value = sem.week1Monday || "";
    $("semChip").textContent = "第 " + currentWeek() + " / " + sem.totalWeeks + " 周";
    var calCount = (state.calendars || []).length;
    $("semRule").textContent = "课程 " + sem.courses.length + " 门 · 任务 " + sem.tasks.length + " 个 · 课表 " +
      (calCount ? calCount + " 份" : "未导入");

    Array.prototype.forEach.call($("themeSeg").children, function (b) {
      b.classList.toggle("is-on", b.getAttribute("data-theme-opt") === state.settings.theme);
    });
    Array.prototype.forEach.call($("weekStartSeg").children, function (b) {
      b.classList.toggle("is-on", Number(b.getAttribute("data-week-start")) === state.settings.weekStart);
    });
    Array.prototype.forEach.call($("langSeg").children, function (b) {
      b.classList.toggle("is-on", b.getAttribute("data-lang-opt") === (state.settings.language || "zh"));
    });
    var remindOn = state.settings.remindersEnabled !== false;
    Array.prototype.forEach.call($("remindSeg").children, function (b) {
      b.classList.toggle("is-on", (b.getAttribute("data-remind-opt") === "on") === remindOn);
    });
    $("remindChip").textContent = remindChipLabel();
    $("onlineChip").textContent = state.settings.onlineSync ? "已同步" : "未同步";
    $("remindTest").disabled = !(window.PM && PM.Reminders && PM.Reminders.hasNative());
    $("webInstall").hidden = isNativeShell();
    $("aboutMeta").textContent = "版本 " + VERSION + (isNativeShell() ? " · Android / iOS" : " · 网页版");
  }

  function render() {
    if (ui.view === "today") renderToday();
    else if (ui.view === "calendar") renderCalendar();
    else if (ui.view === "todos") renderTodos();
    else if (ui.view === "pressure") renderPressure();
    else if (ui.view === "ai") { if (window.PMUI && PMUI.renderAi) PMUI.renderAi(); }
    else if (ui.view === "settings") renderSettings();
    var sem = state.semester;
    var cw = currentWeek();
    $("barTitle").textContent = sem.name;
    $("barSub").textContent = "第 " + cw + " 周 · " + WEEK_NAMES[((today().getDay() + 6) % 7)] + " · " + (today().getMonth() + 1) + "月" + today().getDate() + "日";
    document.title = sem.name + " · 学期压力地图";
    if (window.PMUI && PMUI.afterRender) PMUI.afterRender(ui.view);
  }


  // ---------------- 在线课程抓取 ----------------
  var scrapeAdapter = null;

  function startScrape(id) {
    if (!window.PM || !PM.Online) return;
    if (!isNativeShell()) { toast("网页版不能直接抓取，装成 App 后可用"); return; }
    if (!PM.Online.open(id)) { toast("打不开内嵌浏览窗口"); return; }
    scrapeAdapter = id;
    toast("在弹出的页面里登录，然后点顶部「读取这一页」");
  }

  /** 原生把内嵌窗口抓到的 JSON 交回来。 */
  window.pmScrapeResult = function (json) {
    var result;
    try {
      result = JSON.parse(String(json || "{}"));
    } catch (e) {
      toast("读取结果无法解析");
      return;
    }
    if (!result || result.ok === false) {
      toast("读取失败：" + ((result && result.error) || "页面结构可能变了"));
      return;
    }
    // 正常情况记着用户点的是哪个平台；万一丢了就按回传的地址判断
    var id = scrapeAdapter || (/icourse163\.org/.test(String(result.url)) ? "mooc" : "chaoxing");
    var todos = PM.Online.toTodos(result, id, new Date());
    var counts = PM.Online.merge(state, todos);
    state.settings.onlineSync = PM.stamp();
    save();
    renderSettings();
    render();
    var sum = PM.Online.summary(result);
    toast("读到 " + sum.courses + " 门课 · 新增 " + counts.added + " 个作业" + (counts.updated ? " · 更新 " + counts.updated + " 个" : ""));
    scrapeAdapter = null;
  };

  // ---------------- 弹层 ----------------
  var sheetOpen = false;

  function openSheet(title, html, onSubmit) {
    $("sheetTitle").textContent = title;
    var form = $("sheetForm");
    form.innerHTML = html;
    $("sheetWrap").hidden = false;
    sheetOpen = true;
    form.onsubmit = function (e) {
      e.preventDefault();
      if (onSubmit() !== false) closeSheet();
    };
    var first = form.querySelector("input,select,textarea");
    if (first) setTimeout(function () { try { first.focus({ preventScroll: true }); } catch (e) { } }, 260);
  }

  function closeSheet() {
    $("sheetWrap").hidden = true;
    sheetOpen = false;
    $("sheetForm").innerHTML = "";
    $("sheetForm").onsubmit = null;
  }

  function field(label, inner) {
    return '<div class="form-field"><label>' + esc(label) + "</label>" + inner + "</div>";
  }

  // 待办表单
  function openTodoSheet(todo) {
    var t = todo || {
      id: uid(), title: "", courseName: "", dueDate: ymd(today()), dueTime: "", plannedDate: "",
      taskKind: "作业", important: false, completed: false, requiresSubmission: false, submitted: false,
      remindersEnabled: true, repeatDays: 0, estimatedHours: 1, steps: []
    };
    var courseOptions = state.semester.courses.map(function (c) {
      return '<option value="' + esc(c.name) + '"' + (c.name === t.courseName ? " selected" : "") + ">" + esc(c.name) + "</option>";
    }).join("");
    var kindOptions = ["作业", "考试", "报告", "实验", "复习", "其他"].map(function (k) {
      return '<option value="' + k + '"' + (k === (t.taskKind || "作业") ? " selected" : "") + ">" + k + "</option>";
    }).join("");
    var stepsText = (t.steps || []).map(function (s) { return (s.completed ? "[x] " : "") + s.title; }).join("\n");

    var html =
      field("要做的事", '<input type="text" name="title" required maxlength="80" placeholder="例如：数据结构实验报告 3" value="' + esc(t.title) + '">') +
      field("课程", '<select name="courseName"><option value="">不选课程</option>' + courseOptions + "</select>") +
      '<div class="form-row">' +
      field("计划哪天做", '<input type="date" name="plannedDate" value="' + esc(t.plannedDate || "") + '">') +
      field("截止日期", '<input type="date" name="dueDate" value="' + esc(t.dueDate || "") + '">') +
      "</div>" +
      '<div class="form-row">' +
      field("截止时间", '<input type="time" name="dueTime" value="' + esc(t.dueTime || "") + '">') +
      field("预计要花几小时", '<input type="number" name="estimatedHours" min="0" max="200" step="0.5" inputmode="decimal" value="' + esc(t.estimatedHours) + '">') +
      "</div>" +
      '<div class="form-row">' +
      field("类型", '<select name="taskKind">' + kindOptions + "</select>") +
      field("每隔几天重复（0 不重复）", '<input type="number" name="repeatDays" min="0" max="365" inputmode="numeric" value="' + esc(t.repeatDays || 0) + '">') +
      "</div>" +
      field("子任务（每行一个，已完成的行首加 [x]）", '<textarea name="steps" rows="3" maxlength="1200" placeholder="画测试用例图&#10;[x] 补运行截图">' + esc(stepsText) + "</textarea>") +
      '<button class="switch-line" type="button" id="impSwitch">标记为重要<span class="switch' + (t.important ? " is-on" : "") + '"><i></i></span></button>' +
      '<button class="switch-line" type="button" id="subSwitch">需要提交<span class="switch' + (t.requiresSubmission ? " is-on" : "") + '"><i></i></span></button>' +
      '<button class="switch-line" type="button" id="remSwitch">到点提醒<span class="switch' + (t.remindersEnabled !== false ? " is-on" : "") + '"><i></i></span></button>' +
      '<div class="form-actions"><button class="btn primary" type="submit">保存</button>' +
      (todo ? '<button class="btn danger" type="button" id="delBtn">删除</button>' : "") +
      "</div>";

    openSheet(todo ? "编辑待办" : "新建待办", html, function () {
      var f = $("sheetForm");
      var title = f.title.value.trim();
      if (!title) { toast("先写点内容"); return false; }
      t.title = title;
      t.courseName = f.courseName.value;
      t.plannedDate = f.plannedDate.value || null;
      t.dueDate = f.dueDate.value || null;
      t.dueTime = f.dueTime.value || null;
      t.estimatedHours = Math.max(0, Number(f.estimatedHours.value) || 0);
      t.taskKind = f.taskKind.value;
      t.repeatDays = Math.max(0, Math.min(365, Number(f.repeatDays.value) || 0));
      t.important = $("impSwitch").querySelector(".switch").classList.contains("is-on");
      t.requiresSubmission = $("subSwitch").querySelector(".switch").classList.contains("is-on");
      t.remindersEnabled = $("remSwitch").querySelector(".switch").classList.contains("is-on");
      if (!t.requiresSubmission) t.submitted = false;
      var oldSteps = t.steps || [];
      t.steps = String(f.steps.value || "").split("\n").map(function (line) { return line.trim(); }).filter(Boolean).slice(0, 30).map(function (line) {
        var done = /^\[x\]\s*/i.test(line);
        var text = line.replace(/^\[(x| )\]\s*/i, "").slice(0, 60);
        var old = null;
        for (var k = 0; k < oldSteps.length; k++) if (oldSteps[k].title === text) { old = oldSteps[k]; break; }
        return { id: old ? old.id : uid(), title: text, completed: done };
      });
      t.updatedAt = PM.stamp();
      if (todo) {
        var i = state.todos.findIndex(function (x) { return x.id === t.id; });
        if (i >= 0) state.todos[i] = t;
      } else {
        t.createdAt = PM.stamp();
        state.todos.unshift(t);
      }
      save();
      render();
      toast(todo ? "已更新" : "已添加");
    });

    ["impSwitch", "subSwitch", "remSwitch"].forEach(function (id) {
      var btn = $(id);
      if (btn) btn.addEventListener("click", function () { this.querySelector(".switch").classList.toggle("is-on"); });
    });
    if (todo) {
      $("delBtn").addEventListener("click", function () {
        if (!confirm("删除这条待办？")) return;
        state.todos = state.todos.filter(function (x) { return x.id !== t.id; });
        save(); closeSheet(); render(); toast("已删除");
      });
    }
  }

  // 课程时段表单
  function openSlotSheet(slot) {
    var s = slot || { id: uid(), name: "", weekday: ui.dayIndex, start: "08:00", end: "09:40", location: "", courseKey: "" };
    var opts = state.semester.courses.map(function (c) {
      return '<option value="' + esc(c.key) + '"' + (c.key === s.courseKey ? " selected" : "") + ">" + esc(c.name) + "</option>";
    }).join("");
    var days = WEEK_NAMES.map(function (n, i) {
      return '<option value="' + (i + 1) + '"' + (i + 1 === Number(s.weekday) ? " selected" : "") + ">" + n + "</option>";
    }).join("");

    var html =
      field("课程名", '<input type="text" name="name" required maxlength="40" placeholder="例如：数据结构" value="' + esc(s.name) + '">') +
      field("关联课程（用于压力分析）", '<select name="courseKey"><option value="">不关联</option>' + opts + "</select>") +
      '<div class="form-row">' +
      field("星期", '<select name="weekday">' + days + "</select>") +
      field("地点", '<input type="text" name="location" maxlength="40" placeholder="教学楼 + 教室" value="' + esc(s.location) + '">') +
      "</div>" +
      '<div class="form-row">' +
      field("开始", '<input type="time" name="start" value="' + esc(s.start) + '">') +
      field("结束", '<input type="time" name="end" value="' + esc(s.end) + '">') +
      "</div>" +
      '<div class="form-actions"><button class="btn primary" type="submit">保存</button>' +
      (slot ? '<button class="btn danger" type="button" id="delBtn">删除</button>' : "") +
      "</div>";

    openSheet(slot ? "编辑课程" : "添加课程", html, function () {
      var f = $("sheetForm");
      var name = f.name.value.trim();
      if (!name) { toast("课程名不能为空"); return false; }
      s.name = name;
      s.courseKey = f.courseKey.value;
      s.weekday = Number(f.weekday.value);
      s.location = f.location.value.trim();
      s.start = f.start.value || "08:00";
      s.end = f.end.value || "09:40";
      if (s.end <= s.start) { toast("结束时间要晚于开始时间"); return false; }
      if (slot) {
        var i = state.slots.findIndex(function (x) { return x.id === s.id; });
        if (i >= 0) state.slots[i] = s;
      } else {
        state.slots.push(s);
      }
      ui.dayIndex = s.weekday;
      save(); render(); toast(slot ? "已更新" : "已添加");
    });

    if (slot) {
      $("delBtn").addEventListener("click", function () {
        if (!confirm("删除这节课？")) return;
        state.slots = state.slots.filter(function (x) { return x.id !== s.id; });
        save(); closeSheet(); render(); toast("已删除");
      });
    }
  }

  // 任务表单（压力分析用）
  function openTaskSheet() {
    var sem = state.semester;
    if (!sem.courses.length) {
      toast("先到设置里添加课程再建任务");
      return;
    }
    var opts = sem.courses.map(function (c) { return '<option value="' + esc(c.key) + '">' + esc(c.name) + "</option>"; }).join("");
    var kinds = TYPES.map(function (k) { return '<option value="' + k + '"' + (k === "作业" ? "" : "") + ">" + k + "</option>"; }).join("");

    var html =
      field("任务名", '<input type="text" name="name" required maxlength="60" placeholder="例如：期中考试">') +
      field("课程", '<select name="courseKey">' + opts + "</select>") +
      '<div class="form-row">' +
      field("类型", '<select name="kind">' + kinds + "</select>") +
      field("截止周", '<input type="number" name="week" min="1" max="' + sem.totalWeeks + '" inputmode="numeric" value="' + currentWeek() + '">') +
      "</div>" +
      field("预计小时（留空按类型默认）", '<input type="number" name="estHours" min="0" max="200" step="0.5" inputmode="decimal" placeholder="' + TYPE_HOURS["其他"] + '">') +
      '<div class="form-actions"><button class="btn primary" type="submit">保存</button></div>';

    openSheet("添加任务", html, function () {
      var f = $("sheetForm");
      var name = f.name.value.trim();
      if (!name) { toast("任务名不能为空"); return false; }
      var kind = f.kind.value;
      var week = clamp(Number(f.week.value) || 1, 1, sem.totalWeeks);
      state.semester.tasks.push({
        id: uid(), courseKey: f.courseKey.value, name: name, kind: kind,
        week: week, weight: WEIGHTS[kind], estHours: f.estHours.value === "" ? null : Number(f.estHours.value)
      });
      save(); render(); toast("已添加到第 " + week + " 周");
    });
  }

  // ---------------- 事件 ----------------
  function bind() {
    $("tabbar").addEventListener("click", function (e) {
      var btn = e.target.closest("[data-tab]");
      if (btn) goto(btn.getAttribute("data-tab"));
    });

    document.addEventListener("click", function (e) {
      var gotoBtn = e.target.closest("[data-goto]");
      if (gotoBtn) { goto(gotoBtn.getAttribute("data-goto")); return; }

      var tick = e.target.closest("[data-tick]");
      if (tick) {
        e.stopPropagation();
        var id = tick.getAttribute("data-tick");
        var t = state.todos.find(function (x) { return x.id === id; });
        if (t) {
          t.completed = !t.completed;
          if (t.completed && t.requiresSubmission && !t.submitted) t.submitted = false;
          save();
          if (t.completed) toast("完成了 · " + t.title.slice(0, 12));
          setTimeout(render, 60);
        }
        return;
      }

      var todoEl = e.target.closest("[data-todo]");
      if (todoEl) {
        var tid = todoEl.getAttribute("data-todo");
        var item = state.todos.find(function (x) { return x.id === tid; });
        if (item) openTodoSheet(item);
        return;
      }

      var calEl = e.target.closest("[data-cal]");
      if (calEl) {
        if (window.PMUI && PMUI.showClass) PMUI.showClass(calEl.getAttribute("data-cal"));
        return;
      }

      var slotEl = e.target.closest("[data-slot]");
      if (slotEl) {
        var sid = slotEl.getAttribute("data-slot");
        var slot = state.slots.find(function (x) { return x.id === sid; });
        if (slot) openSlotSheet(slot);
        return;
      }

      var dayBtn = e.target.closest("[data-day]");
      if (dayBtn) {
        ui.dayIndex = Number(dayBtn.getAttribute("data-day"));
        renderCalendar();
        return;
      }

      var filterBtn = e.target.closest("[data-filter]");
      if (filterBtn) {
        ui.todoFilter = filterBtn.getAttribute("data-filter");
        renderTodos();
        return;
      }

      var themeOpt = e.target.closest("[data-theme-opt]");
      if (themeOpt) {
        state.settings.theme = themeOpt.getAttribute("data-theme-opt");
        save(); applyTheme(); renderSettings();
        return;
      }

      var weekStartOpt = e.target.closest("[data-week-start]");
      if (weekStartOpt) {
        state.settings.weekStart = Number(weekStartOpt.getAttribute("data-week-start"));
        save(); renderSettings();
        return;
      }

      var langOpt = e.target.closest("[data-lang-opt]");
      if (langOpt) {
        state.settings.language = langOpt.getAttribute("data-lang-opt");
        save();
        if (window.PMI18N) PMI18N.apply();
        renderSettings();
        return;
      }

      var remindOpt = e.target.closest("[data-remind-opt]");
      if (remindOpt) {
        state.settings.remindersEnabled = remindOpt.getAttribute("data-remind-opt") === "on";
        save(); renderSettings();
        return;
      }
    });

    $("themeBtn").addEventListener("click", function () {
      var order = ["auto", "light", "dark"];
      var next = order[(order.indexOf(state.settings.theme) + 1) % 3];
      state.settings.theme = next;
      save(); applyTheme(); renderSettings();
      toast(next === "auto" ? "跟随系统" : next === "light" ? "浅色主题" : "深色主题");
    });

    $("fab").addEventListener("click", function () {
      if (ui.view === "calendar") { if (window.PMUI) PMUI.importCalendar(); return; }
      else if (ui.view === "pressure") openTaskSheet();
      else openTodoSheet(null);
    });

    $("addSlotBtn").addEventListener("click", function () { if (window.PMUI) PMUI.importCalendar(); });
    $("sheetClose").addEventListener("click", closeSheet);
    $("sheetBackdrop").addEventListener("click", closeSheet);

    // 设置项
    var sem = function () { return state.semester; };
    $("semName").addEventListener("change", function () { sem().name = this.value.trim().slice(0, 40) || "新学期"; save(); render(); });
    $("semWeeks").addEventListener("change", function () {
      sem().totalWeeks = clamp(Number(this.value) || 18, 1, 30);
      this.value = sem().totalWeeks; save(); render();
    });
    $("semThreshold").addEventListener("change", function () {
      sem().threshold = clamp(Number(this.value) || 7, 1, 30);
      this.value = sem().threshold; save(); render();
    });
    $("semHours").addEventListener("change", function () {
      sem().hoursPerWeek = clamp(Number(this.value) || 20, 1, 100);
      this.value = sem().hoursPerWeek; save(); render();
    });
    $("semMonday").addEventListener("change", function () {
      var d = parseYmd(this.value);
      if (d && d.getDay() !== 1) {
        toast("第 1 周需要从周一开始");
        var m = new Date(d);
        m.setDate(d.getDate() - ((d.getDay() + 6) % 7));
        this.value = ymd(m);
      }
      sem().week1Monday = this.value; save(); render();
    });

    $("sampleBtn").addEventListener("click", function () {
      if (!confirm("载入示例学期会覆盖现在的课程、任务和课表，继续？")) return;
      var theme = state.settings.theme;
      state = sampleState();
      state.settings.theme = theme;
      save(); applyTheme(); render(); toast("已载入示例学期");
    });

    $("clearBtn").addEventListener("click", function () {
      if (!confirm("清空所有课程、任务、待办和课表？此操作不可撤销。")) return;
      var theme = state.settings.theme;
      state = blankState();
      state.settings.theme = theme;
      save(); applyTheme(); render(); toast("已清空");
    });

    $("exportBtn").addEventListener("click", function () {
      // 导出为桌面版同构快照，电脑上「导入备份」可以直接读
      var json = JSON.stringify(PM.snapshotOf(state), null, 2);
      var name = "pressuremap-backup-" + ymd(new Date()) + ".json";
      if (window.AndroidHost && window.AndroidHost.saveBackup) {
        try {
          var path = window.AndroidHost.saveBackup(name, json);
          toast(path && path.indexOf("ERR") !== 0 ? "已保存到 " + path : "保存失败");
          return;
        } catch (e) { }
      }
      if (window.webkit && window.webkit.messageHandlers && window.webkit.messageHandlers.saveBackup) {
        try {
          window.webkit.messageHandlers.saveBackup.postMessage({ name: name, content: json });
          toast("已保存到「文件」App / 我的 iPhone / PressureMap");
          return;
        } catch (e) { }
      }
      try {
        var blob = new Blob([json], { type: "application/json" });
        var url = URL.createObjectURL(blob);
        var a = document.createElement("a");
        a.href = url; a.download = name;
        document.body.appendChild(a); a.click();
        setTimeout(function () { URL.revokeObjectURL(url); a.remove(); }, 800);
        toast("已导出备份");
      } catch (e) {
        openSheet("备份内容", '<div class="form-field"><label>复制下面全部内容保存</label><textarea rows="10" readonly>' + esc(json) + "</textarea></div>", function () { });
      }
    });

    $("importBtn").addEventListener("click", function () {
      openSheet("导入备份", '<div class="form-field"><label>把备份 JSON 粘贴到这里（电脑版导出的也能直接导入）</label><textarea name="payload" rows="8" placeholder=\'{"schema_version":1,...}\'></textarea></div>' +
        '<div class="form-actions"><button class="btn primary" type="submit">导入</button></div>', function () {
          var text = $("sheetForm").payload.value.trim();
          if (!text) { toast("先粘贴备份内容"); return false; }
          try {
            var next = adoptSnapshot(JSON.parse(text));
            state = next;
            save(); applyTheme(); render(); toast("导入成功");
          } catch (err) {
            toast("导入失败：" + (err && err.message ? err.message : "不是有效的备份文件"));
            return false;
          }
        });
    });

    $("onlineChaoxing").addEventListener("click", function () { startScrape("chaoxing"); });
    $("onlineMooc").addEventListener("click", function () { startScrape("mooc"); });

    $("remindTest").addEventListener("click", function () {
      if (!window.PM || !PM.Reminders) return;
      if (!PM.Reminders.hasNative()) { toast("网页版没有系统通知，装成 App 才能提醒"); return; }
      if (PM.Reminders.test()) toast("5 秒后发一条测试提醒");
      else toast("没发出去，检查系统的通知权限");
    });

    $("fileInput").addEventListener("change", function () {
      var file = this.files && this.files[0];
      if (!file) return;
      var reader = new FileReader();
      reader.onload = function () {
        try {
          var next = adoptSnapshot(JSON.parse(String(reader.result)));
          state = next;
          save(); applyTheme(); render(); toast("导入成功");
        } catch (e) { toast("导入失败：" + (e && e.message ? e.message : "文件格式不对")); }
      };
      reader.readAsText(file);
      this.value = "";
    });
  }

  // 供 Android 返回键调用
  window.pmHandleBack = function () {
    if (sheetOpen) { closeSheet(); return true; }
    if (ui.view !== "today") { goto("today"); return true; }
    return false;
  };

  // 供 iOS/Android 原生保存备份回执
  window.pmBackupResult = function (path) {
    toast(path && path.indexOf("ERR") !== 0 ? "已保存到 " + path : "保存失败");
  };

  // ---------------- 对外接口（pm-ui.js 用） ----------------
  window.PMUI = {
    state: function () { return state; },
    setState: function (next) { state = defineSem(next); },
    resetState: function (next) { state = next; },
    snapshot: function () { return PM.snapshotOf(state); },
    applySnapshot: applyAdopted,
    normalize: normalize,
    save: save,
    render: render,
    renderSettings: renderSettings,
    renderCalendar: renderCalendar,
    openSheet: openSheet,
    closeSheet: closeSheet,
    toast: toast,
    esc: esc,
    field: field,
    applyTheme: applyTheme,
    goto: goto,
    currentWeek: currentWeek,
    ymd: ymd,
    parseYmd: parseYmd,
    today: today,
    uid: uid,
    ui: function () { return ui; },
    sampleState: sampleState,
    version: VERSION
  };

  // ---------------- 启动 ----------------
  function init() {
    load();
    applyTheme();
    ui.dayIndex = ((new Date().getDay() + 6) % 7) + 1;
    ui.week = currentWeek();
    bind();
    renderFab();
    render();
    if (window.PMUI && PMUI.boot) { try { PMUI.boot(); } catch (e) { } }
    // 回到前台时重排一次提醒：可能关机过、也可能改了系统通知权限
    if (window.PM && PM.Reminders) {
      PM.Reminders.sync(state);
      document.addEventListener("visibilitychange", function () {
        if (document.visibilityState === "visible") queueReminderSync();
      });
    }

    // 首次启动：没有数据时给出示例
    if (!state.semester.tasks.length && !state.todos.length && !(state.calendars || []).length) {
      setTimeout(function () { toast("载入示例数据，可在设置里清空"); }, 600);
    }
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", init);
  else init();
})();
