/* 学期压力地图 · 新增功能的界面层
   云账号/同步、AI 助手、多学期管理、课表导入、本地缓存清理。
   与 app.js 通过 window.PMUI 协作，互不侵入。 */
(function () {
  "use strict";
  var UI = (window.PMUI = window.PMUI || {});
  var PM = window.PM;
  if (!PM) return;

  function $(id) { return document.getElementById(id); }
  function esc(s) { return UI.esc ? UI.esc(s) : String(s == null ? "" : s); }
  function st() { return UI.state(); }
  function toast(msg) { if (UI.toast) UI.toast(msg); }
  function saveRender() { UI.save(); UI.render(); }
  function pad(n) { return n < 10 ? "0" + n : "" + n; }
  function hhmm(d) { return pad(d.getHours()) + ":" + pad(d.getMinutes()); }

  var ACCOUNT_PREFIX = "pressuremap.account.";
  var CAL_FILE_ID = "calFileInput";
  var lastAdvice = "";
  var lastAdviceAt = "";
  var syncTimer = null;
  var syncStatus = "本地模式";
  var busy = false;

  // ---------------- 数据源隔离（每个云账号一份） ----------------
  function activeStorageKey() {
    var s = st();
    return s.cloud && s.cloud.activeKey ? ACCOUNT_PREFIX + s.cloud.activeKey : "pressuremap.mobile.v2";
  }
  function readDataset(key) {
    try {
      var raw = localStorage.getItem(key);
      if (!raw) return null;
      return JSON.parse(raw);
    } catch (e) { return null; }
  }
  function writeDataset(key, snapshotState) {
    try { localStorage.setItem(key, JSON.stringify(snapshotState)); } catch (e) { }
  }
  /** 切换账号 = 换一份数据集：先把当前数据写回它自己的键，再读目标账号的键。
      顺序不能反，否则会把旧账号的数据写到新账号名下。 */
  function switchDataset(nextKey) {
    var current = st();
    var prevKey = current.cloud && current.cloud.activeKey ? ACCOUNT_PREFIX + current.cloud.activeKey : "pressuremap.mobile.v2";
    if (prevKey !== nextKey) writeDataset(prevKey, current);
    current.cloud.activeKey = nextKey === "pressuremap.mobile.v2" ? "" : nextKey.replace(ACCOUNT_PREFIX, "");
    current.cloud.revision = null;
    current.cloud.hash = null;
    var raw = prevKey === nextKey ? current : readDataset(nextKey);
    var next = raw ? UI.normalize(raw) : UI.normalize({});
    next.cloud = current.cloud;
    next.settings = current.settings;
    next.ai = current.ai;
    UI.setState(next);
    UI.save();
    UI.render();
  }

  // ---------------- 同步上下文 ----------------
  function syncContext() {
    return {
      snapshot: function () { return PM.snapshotOf(st()); },
      apply: function (snap) { UI.applySnapshot(snap); },
      baseline: function () {
        var s = st();
        return s.cloud && s.cloud.revision ? { revision: s.cloud.revision, hash: s.cloud.hash } : null;
      },
      saveBaseline: function (revision, hash) {
        var s = st();
        s.cloud.revision = revision;
        s.cloud.hash = hash;
        UI.save();
      },
      canApply: function () {
        var active = document.activeElement;
        if (!active) return true;
        var tag = (active.tagName || "").toLowerCase();
        return tag !== "input" && tag !== "textarea" && tag !== "select";
      }
    };
  }

  function describeSync(result) {
    if (!result) return "无需同步";
    return { unchanged: "已是最新", uploaded: "已上传到云端", downloaded: "已从云端更新", conflict: "发现冲突，需要你选择保留哪份" }[result.outcome] || "同步完成";
  }

  function doSync(quiet) {
    if (!PM.Cloud.hasSession()) {
      if (!quiet) toast("请先登录云账号");
      return Promise.resolve();
    }
    if (busy) return Promise.resolve();
    busy = true;
    syncStatus = "正在同步…";
    renderCloud();
    return PM.Sync.run(syncContext()).then(function (result) {
      busy = false;
      syncStatus = describeSync(result);
      if (result.outcome === "downloaded") { UI.render(); }
      if (result.outcome === "conflict") renderCloud();
      if (!quiet) toast(describeSync(result));
    }).catch(function (err) {
      busy = false;
      syncStatus = err.message || "同步失败";
      if (!quiet) toast(syncStatus);
    }).then(function () {
      renderCloud();
    });
  }

  // ---------------- 云账号界面 ----------------
  function renderCloud() {
    var chip = $("cloudChip");
    var body = $("cloudBody");
    if (!chip || !body) return;
    var s = st();
    var logged = PM.Cloud.hasSession();
    chip.textContent = logged ? "已登录" : "未登录";
    if (!logged) {
      var accounts = (s.cloud.accounts || []);
      body.innerHTML =
        '<div class="field"><label for="cloudBase">云服务地址</label><input id="cloudBase" type="url" inputmode="url" value="' + esc(s.cloud.baseUrl || PM.DEFAULT_API_BASE) + '"></div>' +
        '<div class="field"><label for="cloudAccount">账号</label><input id="cloudAccount" type="text" autocomplete="username" placeholder="手机号或邮箱"></div>' +
        '<div class="field"><label for="cloudPass">密码</label><input id="cloudPass" type="password" autocomplete="current-password"></div>' +
        '<div class="field"><label for="cloudName">昵称（注册时用）</label><input id="cloudName" type="text" maxlength="20"></div>' +
        '<div class="list-btns">' +
        '<button class="row-btn" type="button" data-cloud="login">登录</button>' +
        '<button class="row-btn" type="button" data-cloud="register">注册新账号</button>' +
        "</div>" +
        (accounts.length ? '<p class="hint">本机登录过的账号：' + accounts.map(function (a) {
          return '<button class="text-btn" type="button" data-cloud-switch="' + esc(a.key) + '">' + esc(a.name) + "</button>";
        }).join("、") + "</p>" : "") +
        '<p class="hint">登录后手机与电脑共用同一份数据；不登录则一切只存本机。</p>';
      return;
    }
    var conflict = PM.Sync.conflict;
    body.innerHTML =
      '<div class="setting-line"><span>账号</span><b>' + esc(PM.Cloud.user.name) + "</b></div>" +
      '<div class="setting-line"><span>服务器</span><span class="mono">' + esc(PM.Cloud.baseUrl) + "</span></div>" +
      '<div class="setting-line"><span>状态</span><span>' + esc(syncStatus) + "</span></div>" +
      (conflict
        ? '<div class="conflict-box"><p>云端和本机都改过数据，且改动时间重叠。请选择保留哪一份（另一份会被覆盖）。</p>' +
          '<div class="list-btns"><button class="row-btn" type="button" data-cloud="use-local">用本机数据覆盖云端</button>' +
          '<button class="row-btn" type="button" data-cloud="use-remote">用云端数据覆盖本机</button></div></div>'
        : "") +
      '<div class="list-btns">' +
      '<button class="row-btn" type="button" data-cloud="sync">立即同步</button>' +
      '<button class="row-btn" type="button" data-cloud="switch">切换 / 添加账号</button>' +
      '<button class="row-btn danger" type="button" data-cloud="logout">退出登录</button>' +
      "</div>";
  }

  function bindCloud() {
    document.addEventListener("click", function (e) {
      var btn = e.target.closest("[data-cloud]");
      if (btn) {
        var action = btn.getAttribute("data-cloud");
        if (action === "login" || action === "register") {
          var base = $("cloudBase").value.trim();
          var account = $("cloudAccount").value.trim();
          var pass = $("cloudPass").value;
          var name = $("cloudName").value.trim();
          if (!PM.isValidApiUrl(base)) { toast("云服务地址必须是有效的 HTTPS 地址"); return; }
          if (!account || !pass) { toast("请填写账号和密码"); return; }
          if (action === "register" && !name) { toast("注册需要一个昵称"); return; }
          PM.Cloud.configure(base);
          st().cloud.baseUrl = base;
          syncStatus = action === "register" ? "正在注册…" : "正在登录…";
          renderCloud();
          var work = action === "register" ? PM.Cloud.register(account, name, pass) : PM.Cloud.login(account, pass);
          work.then(function (user) {
            var key = PM.Cloud.accountKey();
            var s = st();
            s.cloud.accounts = (s.cloud.accounts || []).filter(function (a) { return a.key !== key; });
            s.cloud.accounts.unshift({ key: key, name: user.name, baseUrl: PM.Cloud.baseUrl, token: PM.Cloud.token, userId: user.id });
            s.cloud.activeKey = key;
            s.cloud.revision = null;
            s.cloud.hash = null;
            UI.save();
            syncStatus = "已登录，准备同步";
            switchDataset(ACCOUNT_PREFIX + key);
            return doSync(true);
          }).then(function () {
            toast("已登录并同步");
            renderCloud();
          }).catch(function (err) {
            PM.Cloud.clear();
            syncStatus = err.message || "登录失败";
            toast(syncStatus);
            renderCloud();
          });
          return;
        }
        if (action === "sync") { doSync(false); return; }
        if (action === "switch") {
          PM.Cloud.logout().then(function () {
            switchDataset("pressuremap.mobile.v2");
            syncStatus = "未登录";
            renderCloud();
          });
          return;
        }
        if (action === "logout") {
          if (!confirm("退出登录？本机数据会保留，但不再与云端同步。")) return;
          PM.Cloud.logout().then(function () {
            var s = st();
            (s.cloud.accounts || []).forEach(function (a) {
              if (a.key === s.cloud.activeKey) { a.token = null; }
            });
            s.cloud.activeKey = "";
            s.cloud.revision = null;
            s.cloud.hash = null;
            syncStatus = "已退出登录";
            UI.save();
            renderCloud();
            toast("已退出登录");
          });
          return;
        }
        if (action === "use-local" || action === "use-remote") {
          PM.Sync.resolve(action === "use-local", syncContext()).then(function (result) {
            syncStatus = describeSync(result);
            UI.render();
            renderCloud();
            toast(syncStatus);
          }).catch(function (err) {
            syncStatus = err.message;
            toast(syncStatus);
            renderCloud();
          });
          return;
        }
        return;
      }
      var switchBtn = e.target.closest("[data-cloud-switch]");
      if (switchBtn) {
        var key = switchBtn.getAttribute("data-cloud-switch");
        var s = st();
        var account = (s.cloud.accounts || []).filter(function (a) { return a.key === key; })[0];
        if (!account || !account.token) { toast("这个账号需要重新登录"); return; }
        PM.Cloud.configure(account.baseUrl || PM.DEFAULT_API_BASE);
        PM.Cloud.restore(account.token, { id: account.userId, name: account.name });
        s.cloud.baseUrl = account.baseUrl || PM.DEFAULT_API_BASE;
        s.cloud.activeKey = key;
        s.cloud.revision = null;
        s.cloud.hash = null;
        UI.save();
        switchDataset(ACCOUNT_PREFIX + key);
        doSync(true);
      }
    });
  }

  // ---------------- 学期管理 ----------------
  function renderSemesters() {
    var s = st();
    var sel = $("semSelect");
    var chip = $("semCount");
    if (!sel) return;
    sel.innerHTML = s.semesters.map(function (sem, i) {
      return '<option value="' + i + '"' + (i === s.current ? " selected" : "") + ">" + esc(sem.name) + "（" + sem.totalWeeks + " 周）</option>";
    }).join("");
    if (chip) chip.textContent = s.semesters.length + " 个学期";
  }

  function bindSemesters() {
    var sel = $("semSelect");
    if (sel) {
      sel.addEventListener("change", function () {
        var s = st();
        s.current = Math.max(0, Math.min(s.semesters.length - 1, Number(this.value) || 0));
        UI.save();
        UI.render();
        toast("已切换到 " + s.semester.name);
      });
    }
    var add = $("semNew");
    if (add) {
      add.addEventListener("click", function () {
        UI.openSheet("新建学期",
          UI.field("学期名称", '<input type="text" name="name" required maxlength="40" placeholder="例如：2027 春季学期">') +
          UI.field("第 1 周周一", '<input type="date" name="monday" value="' + PM.ymd(new Date()) + '">') +
          UI.field("总周数", '<input type="number" name="weeks" min="1" max="30" value="18" inputmode="numeric">') +
          '<div class="form-actions"><button class="btn primary" type="submit">创建</button></div>',
          function () {
            var f = $("sheetForm");
            var name = f.name.value.trim();
            if (!name) { toast("学期名称不能为空"); return false; }
            var s = st();
            var id = s.semesters.reduce(function (max, x) { return Math.max(max, Number(x.id) || 0); }, 0) + 1;
            var next = UI.normalize({ semesters: [{ id: id, name: name, totalWeeks: Number(f.weeks.value) || 18, week1Monday: f.monday.value }], current: 0 });
            var sem = next.semester;
            s.semesters.push(sem);
            s.current = s.semesters.length - 1;
            UI.save();
            UI.render();
            toast("已创建 " + sem.name);
          });
      });
    }
    var del = $("semDel");
    if (del) {
      del.addEventListener("click", function () {
        var s = st();
        if (s.semesters.length <= 1) { toast("至少保留一个学期"); return; }
        if (!confirm("删除「" + s.semester.name + "」及其课程、任务？")) return;
        s.semesters.splice(s.current, 1);
        s.current = Math.max(0, s.current - 1);
        UI.save();
        UI.render();
        toast("已删除");
      });
    }
  }

  // ---------------- 课表导入 ----------------
  function ensureCalInput() {
    var input = $(CAL_FILE_ID);
    if (input) return input;
    input = document.createElement("input");
    input.type = "file";
    input.id = CAL_FILE_ID;
    input.accept = ".ics,.csv,.pdf,text/calendar,text/csv,application/pdf";
    input.hidden = true;
    document.body.appendChild(input);
    input.addEventListener("change", function () {
      var file = this.files && this.files[0];
      this.value = "";
      if (!file) return;
      handlePicked(file);
    });
    return input;
  }

  /** 教务 PDF 走单独一条路：先识别，再转成 ICS，最后复用同一套预览确认。 */
  function handlePicked(file) {
    var isPdf = /\.pdf$/i.test(file.name) || file.type === "application/pdf";
    if (isPdf) {
      if (!window.PM || !PM.Hdu) { toast("PDF 导入组件没有加载"); return; }
      if (file.size > PM.Hdu.MAX_BYTES) { toast("PDF 不能超过 10 MB"); return; }
      var buffer = new FileReader();
      buffer.onload = function () {
        var bytes = new Uint8Array(buffer.result);
        PM.Hdu.probe(bytes).then(function (probe) {
          if (!probe.isSchedule) { toast("这不是杭电教务课表 PDF"); return; }
          if (probe.semester) return convertHdu(file.name, bytes, probe.semester);
          // 认不出学期就让用户选（和桌面版的下拉一致）
          var options = PM.Hdu.SEMESTERS.map(function (s) {
            return '<option value="' + esc(s.id) + '">' + esc(s.name) + "</option>";
          }).join("");
          UI.openSheet("选择 PDF 课表学期",
            '<div class="form-field"><label>学期预设</label><select name="semesterId">' + options + "</select></div>" +
            '<p class="hint">PDF 首页没有匹配到已知学期，请手动选一个；选错会导致周次与日期错位。</p>' +
            '<div class="form-actions"><button class="btn primary" type="submit">解析</button></div>',
            function () {
              convertHdu(file.name, bytes, UI.field("semesterId"));
              return false;
            });
        }).catch(function (err) {
          toast("导入失败：" + (err.message || "无法解析这个 PDF"));
        });
      };
      buffer.readAsArrayBuffer(file);
      return;
    }
    if (file.size > 2 * 1024 * 1024) { toast("课表文件不能超过 2 MB"); return; }
    var reader = new FileReader();
    reader.onload = function () {
      try {
        var preview = PM.preview(file.name, String(reader.result), new Date());
        showPreview(preview);
      } catch (err) {
        toast("导入失败：" + (err.message || "无法解析这个文件"));
      }
    };
    reader.readAsText(file);
  }

  function convertHdu(name, bytes, semesterId) {
    PM.Hdu.convert(name, bytes, semesterId).then(function (result) {
      UI.closeSheet();
      var preview = PM.preview(result.name, result.ics, new Date());
      preview.note = "教务 PDF · " + result.lessons + " 个课程格";
      showPreview(preview);
    }).catch(function (err) {
      toast("导入失败：" + (err.message || "无法解析这个 PDF"));
    });
  }

  UI.importCalendar = function () { ensureCalInput().click(); };
  // 供自动化测试与原生壳直接取用（不会触发系统文件选择框）
  UI.calendarInput = function () { return ensureCalInput(); };

  function showPreview(preview) {
    var first = preview.upcoming.slice(0, 5).map(function (c) {
      return '<div class="item"><div class="time-col">' + hhmm(c.start) + "<span>" + hhmm(c.end) + "</span></div>" +
        '<div class="item-main"><div class="item-title">' + esc(c.title) + "</div>" +
        '<div class="item-meta"><span>' + esc(PM.ymd(c.start)) + "</span>" + (c.location ? "<span>" + esc(c.location) + "</span>" : "") + "</div></div></div>";
    }).join("");
    UI.openSheet("导入课表",
      '<p class="hint">' + esc(preview.source.name) + " · " + preview.count + " 个事件" + (preview.allDay ? " · 含 " + preview.allDay + " 个全天事件（已忽略）" : "") + "</p>" +
      (preview.note ? '<p class="hint">' + esc(preview.note) + "</p>" : "") +
      (first ? '<div class="stack">' + first + "</div>" : '<p class="hint">接下来一年没有可显示的课程。</p>') +
      '<div class="form-actions"><button class="btn primary" type="submit">导入</button></div>',
      function () {
        var s = st();
        s.calendars = (s.calendars || []).filter(function (c) { return c.name !== preview.source.name; });
        s.calendars.push(preview.source);
        UI.save();
        UI.render();
        toast("已导入 " + preview.count + " 个事件");
      });
  }

  UI.showClass = function (id) {
    var s = st();
    var parts = String(id).split("@");
    var sourceName = parts[0];
    var startMs = Number(parts[1]);
    var sources = (s.calendars || []).filter(function (c) { return c.name === sourceName; });
    if (!sources.length) return;
    var from = new Date(startMs - 86400000);
    var list = PM.occurrences(sources, from, new Date(startMs + 2 * 86400000));
    var hit = list.filter(function (c) { return c.start.getTime() === startMs; })[0];
    if (!hit) { toast("这条课程来自课表，删除课表可移除"); return; }
    UI.openSheet(hit.title,
      '<div class="setting-line"><span>时间</span><b>' + PM.ymd(hit.start) + " " + hhmm(hit.start) + "–" + hhmm(hit.end) + "</b></div>" +
      '<div class="setting-line"><span>地点</span><b>' + esc(hit.location || "未填写") + "</b></div>" +
      (hit.description ? '<div class="setting-line"><span>备注</span><b>' + esc(hit.description) + "</b></div>" : "") +
      '<div class="setting-line"><span>来源</span><b>' + esc(hit.source) + "</b></div>" +
      '<div class="form-actions"><button class="btn" type="button" id="delCalSrc">删除这份课表</button></div>',
      function () { return false; });
    var delBtn = $("delCalSrc");
    if (delBtn) {
      delBtn.addEventListener("click", function () {
        if (!confirm("删除「" + hit.source + "」？该课表里的所有课程都会移除。")) return;
        var current = st();
        current.calendars = (current.calendars || []).filter(function (c) { return c.name !== hit.source; });
        UI.save();
        UI.closeSheet();
        UI.render();
        toast("已删除课表");
      });
    }
  };

  document.addEventListener("click", function (e) {
    var del = e.target.closest("[data-caldel]");
    if (!del) return;
    var name = del.getAttribute("data-caldel");
    if (!confirm("删除「" + name + "」这份课表？")) return;
    var s = st();
    s.calendars = (s.calendars || []).filter(function (c) { return c.name !== name; });
    UI.save();
    UI.render();
    toast("已删除");
  });

  // ---------------- AI ----------------
  UI.renderAi = function () {
    var s = st();
    var sem = s.semester;
    var now = new Date();
    var day = UI.today();
    var classes = PM.dayOccurrences(s.calendars || [], day);
    var capacity = s.settings.dailyCapacityHours || 6;
    var dayEnd = new Date(day.getTime() + 8 * 86400000);
    var tasks = s.todos.filter(function (t) {
      if (t.completed && (!t.requiresSubmission || t.submitted)) return false;
      if (t.plannedDate === PM.ymd(day)) return true;
      if (!t.dueDate) return false;
      var p = String(t.dueDate).split("-");
      return new Date(+p[0], +p[1] - 1, +p[2]) < dayEnd;
    }).sort(function (a, b) {
      if (!!b.important !== !!a.important) return b.important ? 1 : -1;
      return (a.dueDate || "9999") < (b.dueDate || "9999") ? -1 : 1;
    });
    var summary = $("aiSummary");
    if (summary) summary.textContent = (now.getMonth() + 1) + "月" + now.getDate() + "日";
    var box = $("aiToday");
    if (box) {
      var rows = "";
      rows += '<p class="hint">今日课程 ' + classes.length + " 节 · 相关待办 " + tasks.length + " 项 · 每天可用 " + capacity + " 小时</p>";
      rows += classes.length ? classes.map(function (c) {
        return '<div class="item"><div class="time-col">' + hhmm(c.start) + "<span>" + hhmm(c.end) + "</span></div>" +
          '<div class="item-main"><div class="item-title">' + esc(c.title) + "</div>" +
          (c.location ? '<div class="item-meta"><span>' + esc(c.location) + "</span></div>" : "") + "</div></div>";
      }).join("") : '<p class="hint">今天没有课。</p>';
      rows += tasks.length ? tasks.map(function (t) {
        return '<div class="item"><div class="item-main"><div class="item-title">' + esc(t.title) + "</div>" +
          '<div class="item-meta"><span>' + esc(t.courseName || "未分类") + "</span><span>" + esc(t.dueDate || "无截止") + "</span></div></div></div>";
      }).join("") : '<p class="hint">今天没有相关待办。</p>';
      box.innerHTML = rows;
    }
    var chip = $("aiChip");
    var cfg = { baseUrl: s.ai.baseUrl, model: s.ai.model, apiKey: s.ai.apiKey };
    if (chip) chip.textContent = PM.aiValid(cfg) ? "已配置" : "未配置";
    var advice = $("aiAdvice");
    if (advice) advice.textContent = lastAdvice || "生成后，这里会显示今天的安排建议。";
    var status = $("aiStatus");
    if (status) {
      status.textContent = lastAdviceAt
        ? "更新于 " + lastAdviceAt + " · 建议仅供参考，任务不会被自动修改。"
        : "生成时会把今天的课程时间、相关待办和补充要求发送到你配置的 AI 接口。";
    }
    PM._calendars = s.calendars || [];
  };

  function bindAi() {
    var gen = $("aiGenerate");
    if (gen) {
      gen.addEventListener("click", function () {
        var s = st();
        var cfg = { baseUrl: s.ai.baseUrl, model: s.ai.model, apiKey: s.ai.apiKey };
        if (!PM.aiValid(cfg)) { toast("先到设置里填写 AI 接口地址、模型和 Key"); return; }
        var notes = ($("aiNotes") && $("aiNotes").value) || "";
        var context = PM.aiContext(s.semester, s.todos, s.settings.dailyCapacityHours || 6, new Date(), notes);
        gen.disabled = true;
        var status = $("aiStatus");
        if (status) status.textContent = "正在生成建议…";
        PM.aiGenerate(cfg, context, s.settings.language === "en").then(function (text) {
          lastAdvice = text;
          lastAdviceAt = hhmm(new Date());
          UI.render();
          toast("已生成建议");
        }).catch(function (err) {
          if (status) status.textContent = err.message || "生成失败";
          toast(err.message || "生成失败");
        }).then(function () { gen.disabled = false; });
      });
    }
    var saveBtn = $("aiSave");
    if (saveBtn) {
      saveBtn.addEventListener("click", function () {
        var s = st();
        var base = $("aiBase").value.trim();
        if (base && !PM.aiValid({ baseUrl: base, model: "x", apiKey: "x" })) {
          toast("接口地址必须是 https:// 开头");
          return;
        }
        s.ai = { baseUrl: base, model: $("aiModel").value.trim(), apiKey: $("aiKey").value.trim() };
        s.settings.dailyCapacityHours = Math.max(0.5, Math.min(24, Number($("aiCapacity").value) || 6));
        UI.save();
        UI.render();
        toast(PM.aiValid(s.ai) ? "已保存" : "已保存，但配置还不完整");
      });
    }
    var clearBtn = $("aiClear");
    if (clearBtn) {
      clearBtn.addEventListener("click", function () {
        if (!confirm("清除 AI 接口配置（密钥）？")) return;
        var s = st();
        s.ai = { baseUrl: "", model: "", apiKey: "" };
        UI.save();
        UI.render();
        toast("已清除");
      });
    }
  }

  // ---------------- 本地缓存清理 ----------------
  function bindCache() {
    var btn = $("cacheBtn");
    if (!btn) return;
    btn.addEventListener("click", function () {
      if (!confirm("清理本地缓存？会删掉 1.x 旧版数据残留、课表/备份的临时副本，不影响学期、待办和登录状态。")) return;
      var removed = [];
      try {
        if (localStorage.getItem("pressuremap.mobile.v1")) { localStorage.removeItem("pressuremap.mobile.v1"); removed.push("旧版数据"); }
        Object.keys(localStorage).forEach(function (key) {
          if (/^pressuremap\.(tmp|conflict)\./.test(key)) { localStorage.removeItem(key); removed.push(key); }
        });
      } catch (e) { }
      toast(removed.length ? "已清理 " + removed.length + " 项" : "没有需要清理的缓存");
    });
  }

  // ---------------- 渲染钩子 ----------------
  UI.afterRender = function (view) {
    if (view === "settings") {
      renderSemesters();
      renderCloud();
      var s = st();
      var base = $("aiBase"), model = $("aiModel"), key = $("aiKey"), cap = $("aiCapacity");
      if (base && document.activeElement !== base) base.value = s.ai.baseUrl || "";
      if (model && document.activeElement !== model) model.value = s.ai.model || "";
      if (key && document.activeElement !== key) key.value = s.ai.apiKey || "";
      if (cap && document.activeElement !== cap) cap.value = s.settings.dailyCapacityHours || 6;
      var chip = $("aiChip");
      if (chip) chip.textContent = PM.aiValid(s.ai) ? "已配置" : "未配置";
      var dataChip = $("dataChip");
      if (dataChip) dataChip.textContent = PM.Cloud.hasSession() ? "云同步" : "本地";
    }
    if (view === "ai") UI.renderAi();
  };

  UI.onDataChanged = function () {
    if (!PM.Cloud.hasSession() || busy) return;
    clearTimeout(syncTimer);
    syncTimer = setTimeout(function () { doSync(true); }, 8000);
  };

  UI.storeKey = function () { return activeStorageKey(); };

  // ---------------- 启动 ----------------
  UI.boot = function () {
    bindCloud();
    bindSemesters();
    bindAi();
    bindCache();
    var s = st();
    PM.Cloud.configure(s.cloud.baseUrl || PM.DEFAULT_API_BASE);
    // 上次登录的是哪个账号，就先切到它自己的数据集
    if (s.cloud.activeKey) {
      var key = ACCOUNT_PREFIX + s.cloud.activeKey;
      if (readDataset(key)) switchDataset(key);
    }
    // 恢复上次的登录状态
    var account = (st().cloud.accounts || []).filter(function (a) { return a.key === st().cloud.activeKey && a.token; })[0];
    if (account) {
      PM.Cloud.restore(account.token, { id: account.userId, name: account.name });
      syncStatus = "已恢复登录";
      doSync(true);
    }
    // 打开应用时同步一次
    document.addEventListener("visibilitychange", function () {
      if (!document.hidden && PM.Cloud.hasSession()) doSync(true);
    });
    setInterval(function () {
      if (!document.hidden && PM.Cloud.hasSession()) doSync(true);
    }, 5 * 60 * 1000);
  };
})();
