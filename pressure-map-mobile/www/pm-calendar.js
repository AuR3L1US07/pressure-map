/* 学期压力地图 · 课表解析（对应桌面版 PressureMap.Core/CourseCalendar.cs）
   支持 .ics（含 RRULE 周重复 / EXDATE 停课）与 .csv（日期、课程、开始、结束、教室、教师），
   按真实日期取课，因此调课、停课、单双周都能正确显示。 */
(function () {
  "use strict";
  var PM = (window.PM = window.PM || {});
  var MAX_BYTES = 2 * 1024 * 1024;
  var MAX_EVENTS = 5000;
  var MAX_SCAN = 25000;
  var DAY = 86400000;

  function pad(n) { return n < 10 ? "0" + n : "" + n; }
  function ymd(d) { return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate()); }
  function hhmm(d) { return pad(d.getHours()) + ":" + pad(d.getMinutes()); }
  PM.ymd = PM.ymd || ymd;

  // ---------------- 解析 ----------------
  function unfold(text) {
    return String(text).replace(/\r\n/g, "\n").replace(/\r/g, "\n").replace(/\n[ \t]/g, "");
  }
  function unescapeIcs(v) {
    return String(v).replace(/\\n/gi, "\n").replace(/\\,/g, ",").replace(/\\;/g, ";").replace(/\\\\/g, "\\");
  }
  function parseDate(value) {
    // 20260914T080500Z / 20260914T080500 / 20260914
    var m = /^(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2})(\d{2})(Z)?)?$/.exec(String(value).trim());
    if (!m) return null;
    var y = +m[1], mo = +m[2] - 1, d = +m[3];
    if (!m[4]) return { date: new Date(y, mo, d), allDay: true };
    var h = +m[4], mi = +m[5], s = +m[6];
    if (m[7]) return { date: new Date(Date.UTC(y, mo, d, h, mi, s)), allDay: false };
    return { date: new Date(y, mo, d, h, mi, s), allDay: false };
  }
  function parseRule(text) {
    var rule = {};
    String(text).split(";").forEach(function (part) {
      var i = part.indexOf("=");
      if (i < 0) return;
      rule[part.slice(0, i).toUpperCase()] = part.slice(i + 1);
    });
    return rule;
  }
  var BYDAY = { SU: 0, MO: 1, TU: 2, WE: 3, TH: 4, FR: 5, SA: 6 };

  PM.parseIcs = function (content) {
    var text = unfold(content);
    if (text.trim().slice(0, 15).toUpperCase() !== "BEGIN:VCALENDAR") throw new Error("不是完整的 ICS 日历文件");
    if (text.toUpperCase().indexOf("END:VCALENDAR") < 0) throw new Error("不是完整的 ICS 日历文件");
    var lines = text.split("\n");
    var events = [];
    var current = null;
    var key = null;
    lines.forEach(function (line) {
      var upper = line.toUpperCase();
      if (upper === "BEGIN:VEVENT") { current = { exdates: [] }; key = null; return; }
      if (upper === "END:VEVENT") {
        if (current) events.push(current);
        current = null; key = null; return;
      }
      if (!current) return;
      var colon = line.indexOf(":");
      if (colon < 0) return;
      var name = line.slice(0, colon).split(";")[0].toUpperCase();
      var params = line.slice(0, colon).toUpperCase();
      var value = line.slice(colon + 1);
      if (params.indexOf(";VALUE=DATE") >= 0 && name === "DTSTART") key = null;
      switch (name) {
        case "SUMMARY": current.summary = unescapeIcs(value).trim(); break;
        case "LOCATION": current.location = unescapeIcs(value).trim(); break;
        case "DESCRIPTION": current.description = unescapeIcs(value).trim(); break;
        case "STATUS": current.status = value.trim().toUpperCase(); break;
        case "DTSTART": current.start = parseDate(value); break;
        case "DTEND": current.end = parseDate(value); break;
        case "DURATION": current.duration = value.trim(); break;
        case "RRULE": current.rule = parseRule(value); break;
        case "EXDATE":
          String(value).split(",").forEach(function (one) {
            var d = parseDate(one);
            if (d) current.exdates.push(d.date.getTime());
          });
          break;
        default: break;
      }
      key = name;
    });
    if (events.length > MAX_EVENTS) throw new Error("单个课表最多包含 " + MAX_EVENTS + " 个事件");
    return events.map(function (ev) {
      if (!ev.start) throw new Error("课程“" + (ev.summary || "未命名") + "”缺少开始时间");
      var start = ev.start.date;
      var end;
      if (ev.end) end = ev.end.date;
      else if (ev.duration) end = new Date(start.getTime() + durationMs(ev.duration));
      else end = new Date(start.getTime() + (ev.start.allDay ? DAY : 0));
      return {
        summary: (ev.summary || "未命名课程").trim(),
        location: ev.location || "",
        description: ev.description || "",
        start: start,
        end: end,
        allDay: !!ev.start.allDay,
        status: ev.status || "",
        rule: ev.rule || null,
        exdates: ev.exdates || []
      };
    });
  };
  function durationMs(text) {
    var m = /^P(?:(\d+)W)?(?:(\d+)D)?(?:T(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?)?$/.exec(String(text).trim().toUpperCase());
    if (!m) return 0;
    return ((+m[1] || 0) * 7 + (+m[2] || 0)) * DAY + ((+m[3] || 0) * 3600 + (+m[4] || 0) * 60 + (+m[5] || 0)) * 1000;
  }
  PM.parseCsv = function (content) {
    var rows = csvRows(String(content).replace(/^\uFEFF/, ""));
    if (rows.length < 2) throw new Error("CSV 课表没有课程数据");
    var headers = rows[0].map(function (x) { return x.trim().toLowerCase(); });
    function col() {
      for (var i = 0; i < arguments.length; i++) {
        var idx = headers.indexOf(arguments[i]);
        if (idx >= 0) return idx;
      }
      return -1;
    }
    var dateCol = col("日期", "date"), titleCol = col("课程", "课程名称", "title");
    var startCol = col("开始时间", "start"), endCol = col("结束时间", "end");
    var locationCol = col("教室", "地点", "location"), teacherCol = col("教师", "teacher");
    if (dateCol < 0 || titleCol < 0 || startCol < 0 || endCol < 0) throw new Error("CSV 需要「日期、课程、开始时间、结束时间」列，可选「教室、教师」");
    if (rows.length > 5001) throw new Error("CSV 最多导入 5000 条课程");
    var out = [];
    for (var i = 1; i < rows.length; i++) {
      var row = rows[i];
      function read(c) { return c >= 0 && c < row.length ? row[c].trim() : ""; }
      var dm = /^(\d{4})-(\d{2})-(\d{2})$/.exec(read(dateCol));
      var sm = /^(\d{1,2}):(\d{2})$/.exec(read(startCol));
      var em = /^(\d{1,2}):(\d{2})$/.exec(read(endCol));
      if (!dm || !sm || !em || !read(titleCol)) throw new Error("CSV 第 " + (i + 1) + " 行无效：日期用 yyyy-MM-dd，时间用 HH:mm，结束时间必须晚于开始时间");
      var start = new Date(+dm[1], +dm[2] - 1, +dm[3], +sm[1], +sm[2]);
      var end = new Date(+dm[1], +dm[2] - 1, +dm[3], +em[1], +em[2]);
      if (!(end > start)) throw new Error("CSV 第 " + (i + 1) + " 行无效：结束时间必须晚于开始时间");
      out.push({ summary: read(titleCol), location: read(locationCol), description: read(teacherCol), start: start, end: end, allDay: false, status: "", rule: null, exdates: [] });
    }
    return out;
  };
  function csvRows(content) {
    var rows = [], cells = [], word = "", quoted = false, i;
    for (i = 0; i < content.length; i++) {
      var c = content[i];
      if (c === '"') {
        if (quoted && content[i + 1] === '"') { word += '"'; i++; }
        else quoted = !quoted;
      } else if (c === "," && !quoted) { cells.push(word); word = ""; }
      else if ((c === "\r" || c === "\n") && !quoted) {
        if (c === "\r" && content[i + 1] === "\n") i++;
        cells.push(word); word = "";
        if (cells.some(function (x) { return x.length > 0; })) rows.push(cells);
        cells = [];
      } else word += c;
    }
    if (quoted) throw new Error("CSV 引号没有闭合");
    cells.push(word);
    if (cells.some(function (x) { return x.length > 0; })) rows.push(cells);
    return rows;
  }

  // ---------------- 展开重复规则 ----------------
  function startOfWeek(d) {
    var x = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    return new Date(x.getTime() - ((x.getDay() + 6) % 7) * DAY);
  }
  function expandEvent(ev, from, to, out) {
    var guard = 0;
    if (!ev.rule) {
      if (ev.start < to && ev.end > from && ev.exdates.indexOf(ev.start.getTime()) < 0) out.push(ev);
      return;
    }
    var freq = (ev.rule.FREQ || "").toUpperCase();
    if (freq !== "WEEKLY" && freq !== "DAILY") {
      // 其它频率交给近似处理：按原事件本身处理一次，避免整份课表报错。
      if (ev.start < to && ev.end > from) out.push(ev);
      return;
    }
    var interval = Math.max(1, parseInt(ev.rule.INTERVAL || "1", 10) || 1);
    var count = ev.rule.COUNT ? parseInt(ev.rule.COUNT, 10) : null;
    var until = ev.rule.UNTIL ? parseDate(ev.rule.UNTIL) : null;
    var weekdays = (ev.rule.BYDAY || "").split(",").filter(Boolean).map(function (d) {
      return BYDAY[d.replace(/[^A-Z]/g, "").toUpperCase()];
    }).filter(function (n) { return n != null; });
    var duration = ev.end - ev.start;
    var emitted = 0;
    var base = startOfWeek(ev.start);
    var times = ev.start.getHours() * 3600000 + ev.start.getMinutes() * 60000 + ev.start.getSeconds() * 1000;
    var offsets = weekdays.length ? weekdays.map(function (wd) { return (wd + 6) % 7; }) : [(ev.start.getDay() + 6) % 7];
    var step = (freq === "DAILY" ? 1 : 7) * interval;
    for (var cursor = 0; cursor < 3000; cursor++) {
      if (++guard > MAX_SCAN) throw new Error("重复课程过于密集，无法导入此课表");
      var moved = false;
      for (var k = 0; k < offsets.length; k++) {
        var dayOffset = freq === "DAILY" ? cursor * step : cursor * step + offsets[k];
        var startTime = base.getTime() + dayOffset * DAY + times;
        var start = new Date(startTime);
        if (start < ev.start) continue;
        if (until && start > until.date) return;
        if (count != null && emitted >= count) return;
        if (start >= to) continue;
        emitted++;
        if (ev.exdates.indexOf(startTime) >= 0) continue;
        moved = true;
        var end = new Date(startTime + duration);
        if (start < to && end > from) {
          out.push({ summary: ev.summary, location: ev.location, description: ev.description, start: start, end: end, allDay: ev.allDay, status: ev.status, rule: null, exdates: [] });
        }
      }
      // 已经越过查询区间的上界就停：多算一周作为缓冲
      if (base.getTime() + cursor * step * DAY > to.getTime() + 14 * DAY) return;
      if (!moved && cursor > 400) return;
    }
  }

  PM.occurrences = function (sources, from, to) {
    var results = [];
    var seen = {};
    (sources || []).forEach(function (source) {
      var events;
      try { events = source.format === "csv" ? PM.parseCsv(source.content) : PM.parseIcs(source.content); }
      catch (e) { return; }
      var bucket = [];
      events.forEach(function (ev) { expandEvent(ev, from, to, bucket); });
      bucket.forEach(function (ev) {
        if (ev.allDay || ev.status === "CANCELLED") return;
        var key = ev.summary + "|" + ev.start.getTime() + "|" + ev.end.getTime() + "|" + ev.location;
        if (seen[key]) return;
        seen[key] = 1;
        results.push({
          title: (ev.summary || "未命名课程").trim(),
          start: ev.start,
          end: ev.end,
          location: ev.location || "",
          description: ev.description || "",
          source: source.name
        });
      });
    });
    return results.sort(function (a, b) { return a.start - b.start || a.end - b.end; });
  };
  PM.dayOccurrences = function (sources, date) {
    var from = new Date(date.getFullYear(), date.getMonth(), date.getDate());
    return PM.occurrences(sources, from, new Date(from.getTime() + DAY));
  };
  PM.preview = function (name, content, today) {
    var bytes = new TextEncoder ? new TextEncoder().encode(content).length : content.length * 2;
    if (bytes > MAX_BYTES) throw new Error("课表文件不能超过 2 MB");
    var lower = String(name).toLowerCase();
    var format = /\.ics$/.test(lower) ? "ics" : /\.csv$/.test(lower) ? "csv" : null;
    if (!format) throw new Error("请选择 .ics 日历或 .csv 课表");
    var events = format === "ics" ? PM.parseIcs(content) : PM.parseCsv(content);
    if (!events.length) throw new Error("日历中没有课程事件");
    events.forEach(function (ev) {
      if (!ev.allDay && !(ev.end > ev.start)) throw new Error("课程“" + ev.summary + "”的结束时间无效");
    });
    var day = new Date(today.getFullYear(), today.getMonth(), today.getDate());
    var source = { name: name, format: format, content: content, importedAt: PM.stamp() };
    return {
      source: source,
      count: events.length,
      allDay: events.filter(function (e) { return e.allDay; }).length,
      upcoming: PM.occurrences([source], day, new Date(day.getTime() + 366 * DAY)).slice(0, 200)
    };
  };

  // ---------------- 旧版手排时段 → ICS（迁移用，保证老数据不丢） ----------------
  PM.slotsToIcs = function (slots, semester) {
    var weeks = Math.max(1, Math.min(30, semester.totalWeeks || 18));
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(String(semester.week1Monday || ""));
    if (!m) return null;
    var monday = new Date(+m[1], +m[2] - 1, +m[3]);
    function stampUtc(d) {
      // 真正的 UTC 瞬时值（带 Z），解析端按本地时区还原
      return d.getUTCFullYear() + pad(d.getUTCMonth() + 1) + pad(d.getUTCDate()) + "T" + pad(d.getUTCHours()) + pad(d.getUTCMinutes()) + "00Z";
    }
    var lines = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//PressureMap//Mobile Migration//CN", "CALSCALE:GREGORIAN"];
    (slots || []).forEach(function (s, index) {
      var dayOffset = (Number(s.weekday) || 1) - 1;
      var first = new Date(monday.getFullYear(), monday.getMonth(), monday.getDate() + dayOffset);
      var sp = /^(\d{1,2}):(\d{2})$/.exec(s.start || "08:00");
      var ep = /^(\d{1,2}):(\d{2})$/.exec(s.end || "09:40");
      if (!sp || !ep) return;
      var start = new Date(first.getFullYear(), first.getMonth(), first.getDate(), +sp[1], +sp[2]);
      var end = new Date(first.getFullYear(), first.getMonth(), first.getDate(), +ep[1], +ep[2]);
      lines.push("BEGIN:VEVENT");
      lines.push("UID:migrated-" + index + "-" + start.getTime() + "@pressuremap");
      lines.push("DTSTAMP:" + stampUtc(new Date()));
      lines.push("DTSTART:" + stampUtc(start));
      lines.push("DTEND:" + stampUtc(end));
      lines.push("RRULE:FREQ=WEEKLY;COUNT=" + weeks);
      lines.push("SUMMARY:" + String(s.name || "课程").replace(/[,;\\]/g, " "));
      if (s.location) lines.push("LOCATION:" + String(s.location).replace(/[,;\\]/g, " "));
      lines.push("END:VEVENT");
    });
    lines.push("END:VCALENDAR");
    return lines.join("\r\n");
  };
})();
