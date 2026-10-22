/* 杭州电子科技大学教务课表 PDF 导入（对应桌面版 PressureMap.Core/HduPdfCalendar.cs）。
 *
 * 教务 PDF 是「七列一张表」：第一页表头是 星期一…星期日，每个格子是多行文字
 *
 *     高等数学A1
 *     (1-2节)1-16周/ 校 区 :下沙/ 场 地 :第6教研楼北202/ 教 师 :童丽云/ 教 学 班 :…
 *
 * 桌面的做法：用 PdfPig 取每个字形的坐标，按表头算出七列的中心和列宽，
 * 把每个格子里的字按 Y 分行、按 X 排序拼回文本，再用正则拆字段。
 * 这里用 pdf.js 的 getTextContent 取同样的 (x, y, 宽度)，逻辑逐行照搬，
 * 保证同一个 PDF 在电脑和手机上导出的 .ics 一致。
 */
(function (PM) {
  "use strict";

  var MAX_BYTES = 10 * 1024 * 1024;
  var START_TIMES = ["08:05", "08:55", "10:00", "10:50", "11:40", "13:30", "14:20", "15:15", "16:05", "18:30", "19:20", "20:10"];
  var END_TIMES = ["08:50", "09:40", "10:45", "11:35", "12:25", "14:15", "15:05", "16:00", "16:50", "19:15", "20:05", "20:55"];
  var WEEK_CHARS = ["一", "二", "三", "四", "五", "六", "日"];

  var SEMESTERS = [
    { id: "2026-2027-1", name: "杭电 2026—2027 学年第一学期", weekOne: [2026, 8, 14] }
  ];

  var CELL = /(?<title>[^\r\n]+)\r?\n\((?<start>\d+)(?:-(?<end>\d+))?节\)(?<weeks>[^/]+)\/\s*校\s*区\s*:(?<campus>[^/]+)\/\s*场\s*地\s*:(?<room>[^/]+)\/\s*教\s*师\s*:(?<teacher>[^/]+)\/\s*教\s*学\s*班\s*:/;

  function semesterOf(id) {
    for (var i = 0; i < SEMESTERS.length; i++) {
      if (SEMESTERS[i].id === id) return SEMESTERS[i];
    }
    return null;
  }

  /** 首页上的学期标识：「2026-2027学年第1学期」。 */
  function tokenOf(sem) {
    var parts = String(sem.id).split("-");
    return parts.length === 3 ? parts[0] + "-" + parts[1] + "学年第" + parts[2] + "学期" : sem.name;
  }

  function ymd(date) {
    var p = function (n) { return (n < 10 ? "0" : "") + n; };
    return date.getFullYear() + "-" + p(date.getMonth() + 1) + "-" + p(date.getDate());
  }

  /** 校历里的停课与调课（2026—2027 第一学期），与桌面版逐条一致。 */
  function adjustDate(nominal) {
    var key = ymd(nominal);
    if (key === "2026-10-06") return new Date(2026, 8, 20);
    if (key === "2026-10-07") return new Date(2026, 9, 10);
    var stop =
      (key >= "2026-09-25" && key <= "2026-09-27") ||
      (key >= "2026-10-01" && key <= "2026-10-07") ||
      key === "2027-01-01" ||
      key >= "2027-01-10" ||
      key === "2026-09-20" ||
      key === "2026-10-10";
    return stop ? null : new Date(nominal.getTime());
  }

  /** 周次表达式 → 周次数组，如「1-16周(单)」「3,5,9-11周」。 */
  function parseWeeks(expression) {
    var normalized = String(expression)
      .replace(/\s+/g, "")
      .replace(/（/g, "(").replace(/）/g, ")")
      .replace(/，/g, ",").replace(/、/g, ",")
      .replace(/第/g, "");
    var set = {};
    var parts = normalized.split(",");
    for (var i = 0; i < parts.length; i++) {
      var part = parts[i];
      if (!part) continue;
      var m = /^(\d+)(?:-(\d+))?周(?:\(([单双])\))?$/.exec(part);
      if (!m) throw new Error("无法识别周次：" + expression);
      var first = Number(m[1]);
      var last = m[2] ? Number(m[2]) : first;
      if (first < 1 || last > 20 || last < first) throw new Error("周次超出当前学期范围：" + expression);
      for (var w = first; w <= last; w++) {
        if (!m[3] || (w % 2 === (m[3] === "单" ? 1 : 0))) set[w] = true;
      }
    }
    var weeks = Object.keys(set).map(Number).sort(function (a, b) { return a - b; });
    if (!weeks.length) throw new Error("周次没有有效教学周：" + expression);
    return weeks;
  }

  // ---------------- PDF → 带坐标的字形 ----------------

  /**
   * 把 pdf.js 的 textContent.items 拆成「一字一个」的字形列表。
   * pdf.js 给的是整段字符串 + 起点 x + 总宽度，按字符均分还原每个字的
   * left/right（中文等宽，误差在亚像素级）。
   */
  function lettersOf(items) {
    var letters = [];
    for (var i = 0; i < items.length; i++) {
      var item = items[i];
      if (!item || typeof item.str !== "string" || !item.str) continue;
      if (!item.transform) continue;
      var x = item.transform[4];
      var y = item.transform[5];
      var width = typeof item.width === "number" && item.width > 0 ? item.width : 0;
      var chars = Array.from(item.str);
      if (!width) width = chars.length * (item.height || 10) * 0.5;
      var step = width / chars.length;
      for (var c = 0; c < chars.length; c++) {
        // 空格要保留：课程名里真的有空格（「大学英语 A」），丢了就和桌面导出的对不上
        if (chars[c] === "\n" || chars[c] === "\r") continue;
        letters.push({ v: chars[c], x: x + step * c, right: x + step * (c + 1), y: y });
      }
    }
    return letters;
  }

  /** 按 Y 聚成行（3pt 内算同一行），行内按 X 排序拼字符串。 */
  function linesOf(letters) {
    var buckets = {};
    for (var i = 0; i < letters.length; i++) {
      var key = Math.round(letters[i].y / 3) * 3;
      if (!buckets[key]) buckets[key] = [];
      buckets[key].push(letters[i]);
    }
    return Object.keys(buckets).map(Number).map(function (y) {
      var row = buckets[y].slice().sort(function (a, b) { return a.x - b.x; });
      return { y: y, letters: row, text: row.map(function (l) { return l.v; }).join("") };
    });
  }

  function pdfjs() {
    if (typeof window === "undefined" || !window.pdfjsLib) {
      throw new Error("PDF 解析组件没有加载");
    }
    var lib = window.pdfjsLib;
    if (lib.GlobalWorkerOptions && !lib.GlobalWorkerOptions.workerSrc) {
      lib.GlobalWorkerOptions.workerSrc = "vendor/pdf.worker.min.js";
    }
    return lib;
  }

  function openDocument(bytes) {
    var lib = pdfjs();
    var data = bytes instanceof Uint8Array ? new Uint8Array(bytes) : new Uint8Array(bytes);
    return lib.getDocument({ data: data, isEvalSupported: false, useSystemFonts: false }).promise;
  }

  function firstPageText(doc) {
    return doc.getPage(1).then(function (page) {
      return page.getTextContent();
    }).then(function (content) {
      return lettersOf(content.items).map(function (l) { return l.v; }).join("");
    });
  }

  /** 判断是不是杭电教务课表，并尽量匹配学期预设。 */
  function probe(bytes) {
    if (!bytes || !bytes.byteLength || bytes.byteLength > MAX_BYTES) {
      return Promise.resolve({ isSchedule: false, semester: null });
    }
    return Promise.resolve().then(function () {
      return openDocument(bytes);
    }).then(function (doc) {
      if (doc.numPages < 1 || doc.numPages > 10) return { isSchedule: false, semester: null };
      return firstPageText(doc).then(function (text) {
        var flat = text.replace(/\s+/g, "");
        var semester = null;
        for (var i = 0; i < SEMESTERS.length; i++) {
          if (flat.indexOf(tokenOf(SEMESTERS[i])) >= 0) semester = SEMESTERS[i];
        }
        var known = !!semester || flat.indexOf("杭州电子科技大学") >= 0 || flat.indexOf("学年第") >= 0;
        return { isSchedule: known, semester: semester ? semester.id : null };
      });
    }).catch(function () {
      return { isSchedule: false, semester: null };
    });
  }

  /** 解析课表 PDF，返回 { name, ics, lessons, warnings }。 */
  function convert(name, bytes, semesterId) {
    var semester = semesterOf(semesterId);
    if (!semester) return Promise.reject(new Error("请选择支持的学期。"));
    if (!bytes || !bytes.byteLength) return Promise.reject(new Error("PDF 是空的。"));
    if (bytes.byteLength > MAX_BYTES) return Promise.reject(new Error("PDF 不能超过 10 MB。"));

    return openDocument(bytes).then(function (doc) {
      if (doc.numPages > 10) throw new Error("课表 PDF 最多支持 10 页。");
      return firstPageText(doc).then(function (text) {
        if (text.replace(/\s+/g, "").indexOf(tokenOf(semester)) < 0) {
          throw new Error("PDF 学期与所选学期不符，或不是可提取文字的杭电教务课表。");
        }
        return collect(doc, semester);
      }).then(function (events) {
        if (!events.lessons || !events.list.length) {
          throw new Error("未识别到有具体上课时间的课程，请使用教务处导出的文字版 PDF。");
        }
        return {
          name: String(name).replace(/\.[^.]+$/, "") + ".ics",
          ics: toIcs(events.list),
          lessons: events.lessons,
          warnings: []
        };
      });
    });
  }

  function collect(doc, semester) {
    return doc.getPage(1).then(function (page) {
      return page.getTextContent();
    }).then(function (content) {
      var letters = lettersOf(content.items);
      var lines = linesOf(letters);
      var header = null;
      for (var i = 0; i < lines.length; i++) {
        if (lines[i].text.indexOf("星期一") >= 0) { header = lines[i].letters; break; }
      }
      if (!header) throw new Error("未找到星期表头，暂不支持此 PDF 版式。");
      var headerText = header.map(function (l) { return l.v; }).join("");
      var headerY = header.length ? Math.max.apply(null, header.map(function (l) { return l.y; })) : 0;
      var centers = [];
      for (var d = 0; d < 7; d++) {
        var idx = headerText.indexOf("星期" + WEEK_CHARS[d]);
        if (idx < 0 || idx + 2 >= header.length) throw new Error("无法识别课表星期列。");
        centers.push((header[idx].x + header[idx + 2].right) / 2);
      }
      var width = centers[1] - centers[0];

      var columns = ["", "", "", "", "", "", ""];
      var pages = [];
      for (var p = 1; p <= doc.numPages; p++) pages.push(p);

      return pages.reduce(function (chain, pageNumber) {
        return chain.then(function () {
          return doc.getPage(pageNumber).then(function (page) {
            return page.getTextContent();
          }).then(function (content) {
            var pageLetters = lettersOf(content.items);
            var footerY = null;
            var pageLines = linesOf(pageLetters);
            for (var i = 0; i < pageLines.length; i++) {
              if (pageLines[i].text.indexOf("其他课程") >= 0) { footerY = pageLines[i].y; break; }
            }
            for (var day = 0; day < 7; day++) {
              var from = centers[day] - width / 2;
              var to = centers[day] + width / 2;
              var picked = pageLetters.filter(function (l) {
                if (l.x < from || l.x >= to) return false;
                if (pageNumber === 1 && !(l.y < headerY - 5)) return false;
                if (footerY !== null && !(l.y > footerY + 5)) return false;
                return true;
              });
              var rows = linesOf(picked).sort(function (a, b) { return b.y - a.y; });
              columns[day] += rows.map(function (r) { return r.text; }).join("\n") + "\n";
            }
          });
        });
      }, Promise.resolve()).then(function () {
        return readColumns(columns, semester);
      });
    });
  }

  function readColumns(columns, semester) {
    var events = [];
    var lessons = 0;
    var weekOne = new Date(semester.weekOne[0], semester.weekOne[1], semester.weekOne[2]);
    for (var day = 0; day < 7; day++) {
      var text = columns[day];
      var matches = [];
      var re = new RegExp(CELL.source, "g");
      var m;
      while ((m = re.exec(text)) !== null) {
        matches.push(m);
        if (m.index === re.lastIndex) re.lastIndex++;
      }
      var counted = (text.match(/\(\d+(?:-\d+)?节\)/g) || []).length;
      if (counted !== matches.length) {
        throw new Error("有课程信息未能完整解析，请检查 PDF 版式，未导入任何课程。");
      }
      for (var i = 0; i < matches.length; i++) {
        var g = matches[i].groups;
        var field = function (key) { return String(g[key] || "").replace(/\s+/g, ""); };
        var start = Number(field("start"));
        var end = g.end ? Number(field("end")) : start;
        if (start < 1 || end > 12 || end < start) throw new Error("课程节次无效。");
        if (field("campus") !== "下沙") {
          throw new Error("当前预设仅支持下沙校区作息，请勿使用其他校区课表。");
        }
        var weeks = parseWeeks(field("weeks"));
        for (var w = 0; w < weeks.length; w++) {
          var week = weeks[w];
          var nominal = new Date(weekOne.getTime());
          nominal.setDate(nominal.getDate() + (week - 1) * 7 + day);
          var date = adjustDate(nominal);
          if (!date) continue;
          var dayText = ymd(date);
          var startMinutes = toMinutes(START_TIMES[start - 1]);
          var endMinutes = toMinutes(END_TIMES[end - 1]);
          var description = "教师：" + field("teacher") + "\n校区：" + field("campus") +
            "\n第" + week + "周 · 第" + start + "—" + end + "节" +
            (dayText !== ymd(nominal) ? "\n调课：补" + ymd(nominal) + "课程" : "");
          events.push({
            title: String(g.title).trim(),
            start: new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, startMinutes, 0, 0),
            end: new Date(date.getFullYear(), date.getMonth(), date.getDate(), 0, endMinutes, 0, 0),
            location: field("room"),
            description: description,
            day: day
          });
        }
        lessons++;
      }
    }
    return { list: events, lessons: lessons };
  }

  function toMinutes(hhmm) {
    var parts = hhmm.split(":");
    return Number(parts[0]) * 60 + Number(parts[1]);
  }

  // ---------------- ICS 输出（与桌面版同格式） ----------------

  function utf8Length(ch) {
    var code = ch.codePointAt(0);
    if (code < 0x80) return 1;
    if (code < 0x800) return 2;
    if (code < 0x10000) return 3;
    return 4;
  }

  function fold(value) {
    var out = "";
    var size = 0;
    var chars = Array.from(value);
    for (var i = 0; i < chars.length; i++) {
      var len = utf8Length(chars[i]);
      if (size + len > 75) { out += "\r\n "; size = 1; }
      out += chars[i];
      size += len;
    }
    return out + "\r\n";
  }

  function escapeText(value) {
    return String(value).replace(/\\/g, "\\\\").replace(/\r/g, "").replace(/\n/g, "\\n").replace(/;/g, "\\;").replace(/,/g, "\\,");
  }

  /**
   * 本地时间 → ICS 的 UTC 串。
   * 与桌面版写法一致：直接把「本地钟点」减 8 小时再打 Z，
   * 不能用 getUTC*（那会按设备真实时区再折算一次，非 UTC+8 的机器就全错）。
   */
  function utcStamp(date) {
    var p = function (n) { return (n < 10 ? "0" : "") + n; };
    var shifted = new Date(date.getTime() - 8 * 3600000);
    return shifted.getFullYear() + p(shifted.getMonth() + 1) + p(shifted.getDate()) + "T" +
      p(shifted.getHours()) + p(shifted.getMinutes()) + p(shifted.getSeconds()) + "Z";
  }

  function toIcs(events) {
    var seen = {};
    var sorted = events.filter(function (e) {
      var key = e.title + "|" + e.start.getTime() + "|" + e.end.getTime() + "|" + e.location;
      if (seen[key]) return false;
      seen[key] = true;
      return true;
    }).sort(function (a, b) { return a.start - b.start; });

    var lines = ["BEGIN:VCALENDAR", "VERSION:2.0", "PRODID:-//PressureMap//HDU Calendar//CN", "CALSCALE:GREGORIAN"];
    var out = lines.map(fold).join("");
    var stamp = utcStamp(new Date());
    for (var i = 0; i < sorted.length; i++) {
      var e = sorted[i];
      var uid = PM.sha256(e.title + "|" + e.start.toISOString() + "|" + e.end.toISOString() + "|" + e.location);
      out += fold("BEGIN:VEVENT");
      out += fold("UID:" + uid + "@hdu.pressuremap");
      out += fold("DTSTAMP:" + stamp);
      out += fold("DTSTART:" + utcStamp(e.start));
      out += fold("DTEND:" + utcStamp(e.end));
      out += fold("SUMMARY:" + escapeText(e.title));
      out += fold("LOCATION:" + escapeText(e.location));
      out += fold("DESCRIPTION:" + escapeText(e.description));
      out += fold("END:VEVENT");
    }
    out += fold("END:VCALENDAR");
    return out;
  }

  PM.Hdu = {
    SEMESTERS: SEMESTERS,
    MAX_BYTES: MAX_BYTES,
    semesterOf: semesterOf,
    tokenOf: tokenOf,
    adjustDate: adjustDate,
    parseWeeks: parseWeeks,
    lettersOf: lettersOf,
    linesOf: linesOf,
    probe: probe,
    convert: convert,
    toIcs: toIcs
  };
})(window.PM = window.PM || {});
