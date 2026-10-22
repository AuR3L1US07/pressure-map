/* 在线课程抓取（超星学习通 / 中国大学 MOOC）。
 *
 * 和桌面版的差异，先说清楚：
 *
 *   桌面版用 Playwright 开真 Chrome，可以同时开多个标签、遍历 iframe、
 *   一路点「章节 → 学习记录 → 作业」，所以能读到每门课的「任务点 x/y」。
 *   Android WebView 没有 iframe 枚举 API、也没有多标签，同一条路子搬不过来。
 *
 *   手机上改成「会话式只读抓取」：
 *     1. 在 App 内嵌的浏览窗口里打开课程页，你自己登录（不在本机存密码）；
 *     2. 点「读取这一页」，App 往当前页面注入下面的提取脚本；
 *     3. 脚本把课程名、进度、未完成作业的截止时间回传，合并进待办。
 *
 *   脚本本身是纯 DOM 读取，能被 scripts/qa-online.mjs 用合成页面覆盖。
 */
(function (PM) {
  "use strict";

  /** 每个平台：入口地址、页面识别、提取脚本（在目标页里执行，返回 JSON 字符串）。 */
  var adapters = {
    chaoxing: {
      id: "chaoxing",
      name: "超星学习通",
      entry: "https://i.mooc.chaoxing.com/space/index",
      loginHint: "登录后进入「我学的课」，再点「读取这一页」",
      source: "chaoxing",
      /** 判断当前页面是不是超星（用来提示用户先登录）。 */
      matches: function (url) {
        return /(^|\.)chaoxing\.com\//.test(String(url || ""));
      },
      script: function () {
        // 与桌面 reader.cjs 的 links() 用同一组选择器
        var cards = [].slice.call(document.querySelectorAll("li.course, .course-list li, div.course-list div"));
        var out = [];
        for (var i = 0; i < cards.length; i++) {
          var card = cards[i];
          var link = card.querySelector('h3 a[href], a[href*="stucoursemiddle"], a[href*="course/"]');
          if (!link) continue;
          var nameEl = card.querySelector(".course-name, .course-title, h3");
          var name = ((nameEl && (nameEl.getAttribute("title") || nameEl.textContent)) || link.textContent || "").trim();
          if (!name) continue;
          var progress = null;
          var text = card.textContent || "";
          var m = /任务点\s*[:：]?\s*(\d+)\s*\/\s*(\d+)/.exec(text);
          if (m && Number(m[1]) <= Number(m[2])) progress = [Number(m[1]), Number(m[2])];
          out.push({ name: name, url: link.href, progress: progress });
        }
        // 作业 / 考试：超星的待办列表在 work 页面，这里只取当前页能看到的
        var works = [];
        var rows = [].slice.call(document.querySelectorAll(".work-list li, ul.workList li, table.work-table tr"));
        for (var w = 0; w < rows.length; w++) {
          var row = rows[w];
          var titleEl = row.querySelector("a, .work-title, td");
          var timeEl = row.querySelector(".time, .work-time, td:nth-child(3)");
          var title = titleEl ? (titleEl.textContent || "").trim() : "";
          var when = timeEl ? (timeEl.textContent || "").trim() : "";
          if (!title) continue;
          works.push({ title: title, url: titleEl && titleEl.href ? titleEl.href : "", due: when, course: "" });
        }
        return JSON.stringify({ ok: true, url: location.href, courses: out, works: works });
      }
    },
    mooc: {
      id: "mooc",
      name: "中国大学 MOOC",
      entry: "https://www.icourse163.org/mycourse/myCourse.htm",
      loginHint: "登录后进入「我的课程」，再点「读取这一页」",
      source: "mooc",
      matches: function (url) {
        return /(^|\.)icourse163\.org\//.test(String(url || ""));
      },
      script: function () {
        // 与桌面 mooc_scanner.cjs 的选择器保持一致
        var out = [];
        var cards = [].slice.call(document.querySelectorAll(".course-card, .myCourseList li, .courseCard, [class*='course-card']"));
        for (var i = 0; i < cards.length; i++) {
          var card = cards[i];
          var link = card.querySelector('a[href*="course"], a[href*="learn"]');
          var nameEl = card.querySelector(".course-name, .name, h3, [class*='name']");
          var name = ((nameEl && nameEl.textContent) || (link && link.textContent) || "").trim();
          if (!name) continue;
          var text = card.textContent || "";
          var pct = /(\d+)\s*%/.exec(text);
          out.push({
            name: name,
            url: link ? link.href : "",
            progress: pct ? [Number(pct[1]), 100] : null
          });
        }
        var works = [];
        var rows = [].slice.call(document.querySelectorAll(".task-list li, .homeworkList li, [class*='homework'] li, [class*='exam'] li"));
        for (var w = 0; w < rows.length; w++) {
          var row = rows[w];
          var titleEl = row.querySelector("a, .title, [class*='title']");
          var timeEl = row.querySelector("[class*='time'], [class*='deadline'], [class*='date']");
          var title = titleEl ? (titleEl.textContent || "").trim() : "";
          if (!title) continue;
          works.push({
            title: title,
            url: titleEl && titleEl.href ? titleEl.href : "",
            due: timeEl ? (timeEl.textContent || "").trim() : "",
            course: ""
          });
        }
        return JSON.stringify({ ok: true, url: location.href, courses: out, works: works });
      }
    }
  };

  function adapter(id) {
    return adapters[id] || null;
  }

  /** 「2026-11-05 23:59」「11月5日 23:59」「2026/11/05」→ yyyy-MM-dd + HH:mm。 */
  function parseDue(text, now) {
    var raw = String(text || "").replace(/\s+/g, " ").trim();
    var date = null;
    var time = null;
    var m = /(\d{4})[-/.年](\d{1,2})[-/.月](\d{1,2})/.exec(raw);
    if (m) date = [Number(m[1]), Number(m[2]), Number(m[3])];
    else {
      m = /(\d{1,2})[-/.月](\d{1,2})日?/.exec(raw);
      if (m) date = [now.getFullYear(), Number(m[1]), Number(m[2])];
    }
    var t = /(\d{1,2}):(\d{2})/.exec(raw);
    if (t) time = [Number(t[1]), Number(t[2])];
    if (!date) return { dueDate: null, dueTime: null };
    var pad = function (n) { return (n < 10 ? "0" : "") + n; };
    return {
      dueDate: date[0] + "-" + pad(date[1]) + "-" + pad(date[2]),
      dueTime: time ? pad(time[0]) + ":" + pad(time[1]) : null
    };
  }

  /** 抓到的东西 → 待办。sourceKey 保证重复同步只更新不新增。 */
  function toTodos(result, id, now) {
    var ad = adapter(id);
    if (!ad || !result) return [];
    var stamp = now || new Date();
    var todos = [];
    var works = Array.isArray(result.works) ? result.works : [];
    for (var i = 0; i < works.length; i++) {
      var w = works[i];
      var title = String(w.title || "").trim();
      if (!title) continue;
      var due = parseDue(w.due, stamp);
      var key = ad.source + ":" + (w.url || title).slice(0, 120);
      todos.push({
        id: "ol-" + PM.sha256(key).slice(0, 12).toLowerCase(),
        title: title.slice(0, 80),
        courseName: String(w.course || "").slice(0, 40),
        dueDate: due.dueDate,
        dueTime: due.dueTime,
        plannedDate: null,
        taskKind: "作业",
        important: false,
        completed: false,
        requiresSubmission: true,
        submitted: false,
        remindersEnabled: true,
        repeatDays: 0,
        sourceKey: key,
        sourceLabel: ad.name,
        createdAt: PM.stamp(),
        updatedAt: PM.stamp(),
        estimatedHours: 1,
        steps: []
      });
    }
    return todos;
  }

  /** 把抓到的待办并进 state.todos：同 sourceKey 更新，其余保留。 */
  function merge(state, incoming) {
    var list = state.todos || [];
    var byKey = {};
    for (var i = 0; i < list.length; i++) {
      if (list[i].sourceKey) byKey[list[i].sourceKey] = list[i];
    }
    var added = 0;
    var updated = 0;
    for (var j = 0; j < incoming.length; j++) {
      var next = incoming[j];
      var old = byKey[next.sourceKey];
      if (old) {
        old.title = next.title;
        old.courseName = next.courseName || old.courseName;
        old.dueDate = next.dueDate || old.dueDate;
        old.dueTime = next.dueTime || old.dueTime;
        old.sourceLabel = next.sourceLabel;
        updated++;
      } else {
        list.push(next);
        added++;
      }
    }
    state.todos = list;
    return { added: added, updated: updated };
  }

  /** 概要统计：上次抓到多少门课、多少作业。 */
  function summary(result) {
    if (!result) return { courses: 0, works: 0 };
    return {
      courses: Array.isArray(result.courses) ? result.courses.length : 0,
      works: Array.isArray(result.works) ? result.works.length : 0
    };
  }

  /** 让原生壳打开内嵌浏览窗口；网页版返回 false。 */
  function open(id) {
    var ad = adapter(id);
    var host = typeof window !== "undefined" ? window.AndroidHost : null;
    if (!ad || !host || typeof host.openScrape !== "function") return false;
    try {
      host.openScrape(ad.id, ad.entry, "(" + ad.script.toString() + ")()");
      return true;
    } catch (e) {
      return false;
    }
  }

  PM.Online = {
    adapters: adapters,
    adapter: adapter,
    parseDue: parseDue,
    toTodos: toTodos,
    merge: merge,
    summary: summary,
    open: open
  };
})(window.PM = window.PM || {});
