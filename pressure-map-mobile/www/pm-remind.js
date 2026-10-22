/* 到点提醒：
 *
 * 桌面版的提醒是「轮询 + deliveredKey 去重」（PressureMap.Core/Store.TaskDetails.cs 的
 * ReminderRules.Due）：每隔几秒算一次 now 落在哪个阶段，算出来的 key 和已投递的 key
 * 不同就弹一个窗口。手机上没有常驻轮询，所以这里改成**提前把三个时间点排进系统闹钟**：
 * 截止前 24 小时、截止前 1 小时、截止时刻。
 *
 * 规则本身与桌面保持一致：
 *   - 已完成 / 关掉提醒 / 没有截止时间 → 不提醒
 *   - 阶段：now >= due → 0；now >= due-1h → 60；否则 1440
 *   - key：`截止ISO|阶段|submit|work`
 *   - 文案：已完成但要提交 → 「已完成，记得提交」；否则按阶段给「已到截止时间 /
 *     将在 1 小时内截止 / 将在 24 小时内截止」
 *   - 截止时间：dueDate + dueTime，没填时间按 23:59（同桌面 Models.cs 的 Deadline）
 */
(function (PM) {
  "use strict";

  var MINUTE = 60000;
  var STAGES = [1440, 60, 0];
  /** 排给定的时间点至少要留这么多余量，否则闹钟一设就立刻响。 */
  var LEAD = 30000;

  function pad(n) {
    return (n < 10 ? "0" : "") + n;
  }

  function ymd(d) {
    return d.getFullYear() + "-" + pad(d.getMonth() + 1) + "-" + pad(d.getDate());
  }

  function hm(d) {
    return pad(d.getHours()) + ":" + pad(d.getMinutes());
  }

  /** dueDate + dueTime → Date；没填时间按当天 23:59。 */
  function deadline(todo) {
    if (!todo || !todo.dueDate) return null;
    var m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(todo.dueDate);
    if (!m) return null;
    var t = /^(\d{1,2}):(\d{2})$/.exec(todo.dueTime || "");
    var hour = t ? Math.min(23, Number(t[1])) : 23;
    var minute = t ? Math.min(59, Number(t[2])) : 59;
    return new Date(Number(m[1]), Number(m[2]) - 1, Number(m[3]), hour, minute, 0, 0);
  }

  /** 桌面 ReminderRules.Due 的阶段判定：0 已到期 / 60 一小时内 / 1440 一天内 / null 还早。 */
  function stageOf(due, now) {
    if (!due) return null;
    if (now < due.getTime() - 24 * 60 * MINUTE) return null;
    if (now >= due.getTime()) return 0;
    if (now >= due.getTime() - 60 * MINUTE) return 60;
    return 1440;
  }

  function keyOf(todo, due, stage) {
    return ymd(due) + "T" + hm(due) + "|" + stage + "|" + (todo.completed ? "submit" : "work");
  }

  function labelOf(todo, stage) {
    if (todo.completed && todo.requiresSubmission) return "已完成，记得提交";
    if (stage === 0) return "已到截止时间";
    if (stage === 60) return "将在 1 小时内截止";
    return "将在 24 小时内截止";
  }

  function bodyOf(todo, due, stage) {
    return labelOf(todo, stage) + " · " + (due.getMonth() + 1) + "月" + due.getDate() + "日 " + hm(due);
  }

  function active(todo) {
    return todo && !todo.completed && todo.remindersEnabled !== false && !!todo.dueDate;
  }

  /**
   * 当前这一刻应该立刻弹的那条提醒（等价于桌面 Due），没有就返回 null。
   * delivered 是已经投递过的 key 集合，用来防止同一条重复弹。
   */
  function due(todo, now, delivered) {
    if (!active(todo)) return null;
    var dueAt = deadline(todo);
    if (!dueAt) return null;
    var stage = stageOf(dueAt, now);
    if (stage === null) return null;
    var key = keyOf(todo, dueAt, stage);
    if (delivered && delivered.indexOf(key) >= 0) return null;
    return { key: key, stage: stage, title: todo.title, body: bodyOf(todo, dueAt, stage), at: dueAt.getTime() };
  }

  /**
   * 排进系统闹钟的计划：只保留还没到的时间点，每条自带稳定的通知 id。
   * enabled=false 时返回空数组（全局开关关掉就整体取消）。
   */
  function plan(state, now) {
    var result = [];
    if (!state || state.settings && state.settings.remindersEnabled === false) return result;
    var todos = Array.isArray(state.todos) ? state.todos : [];
    for (var i = 0; i < todos.length; i++) {
      var todo = todos[i];
      if (!active(todo)) continue;
      var dueAt = deadline(todo);
      if (!dueAt) continue;
      var dueMs = dueAt.getTime();
      for (var s = 0; s < STAGES.length; s++) {
        var at = dueMs - STAGES[s] * MINUTE;
        if (at <= now + LEAD) continue;
        result.push({
          id: todo.id + "#" + STAGES[s],
          at: at,
          title: todo.title,
          body: bodyOf(todo, dueAt, STAGES[s]),
          key: keyOf(todo, dueAt, STAGES[s])
        });
      }
    }
    result.sort(function (a, b) {
      return a.at - b.at;
    });
    // 一次最多排 100 条，避免待办上千时把系统闹钟表撑爆
    return result.slice(0, 100);
  }

  function native() {
    return typeof window !== "undefined" && window.AndroidHost && typeof window.AndroidHost.syncReminders === "function"
      ? window.AndroidHost
      : null;
  }

  /** 把计划推给原生排闹钟；网页版没有原生宿主，返回 false。 */
  function sync(state, now) {
    var host = native();
    if (!host) return false;
    var list = plan(state, now || Date.now());
    try {
      host.syncReminders(JSON.stringify(list));
      return list.length;
    } catch (e) {
      return false;
    }
  }

  /** 立即发一条测试提醒（原生排 5 秒后响），返回是否成功。 */
  function test() {
    var host = native();
    if (!host || typeof host.syncReminders !== "function") return false;
    var at = Date.now() + 5000;
    try {
      host.syncReminders(JSON.stringify([
        { id: "test#0", at: at, title: "测试提醒", body: "看到这条说明提醒已经生效", key: "test|0|work" }
      ]));
      return true;
    } catch (e) {
      return false;
    }
  }

  PM.Reminders = {
    MINUTE: MINUTE,
    deadline: deadline,
    stageOf: stageOf,
    keyOf: keyOf,
    labelOf: labelOf,
    active: active,
    due: due,
    plan: plan,
    sync: sync,
    test: test,
    hasNative: function () {
      return !!native();
    }
  };
})(window.PM = window.PM || {});
