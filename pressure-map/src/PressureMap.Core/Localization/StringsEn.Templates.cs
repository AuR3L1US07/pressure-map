namespace PressureMap.Localization;

internal static partial class StringsEn
{
    private static void AddTemplates(Dictionary<string, string> map)
    {
        map[" · {0} 门未结束"] = " · {0} still running";
        map[" · {0} 件待提交"] = " · {0} to submit";
        map[" · {0} 项已逾期，请优先处理"] = " · {0} overdue, handle them first";
        map[" · {0} 件已逾期"] = " · {0} overdue";
        map[" · 最近截止 {0} · {1}"] = " · Next deadline {0} · {1}";
        map["{0} 门课程"] = "{0} courses";
        map["无法读取这个课表：{0}"] = "Could not read this schedule: {0}";
        map["待完成 {0}"] = "{0} to finish";
        map["已完成 {0}"] = "{0} done";
        map["待确认 {0}"] = "{0} unconfirmed";
        map["无此要求 {0}"] = "{0} not required";
        map["{0} 件今日任务"] = "{0} tasks today";
        map["{0} · {1} 等效小时"] = "{0} · {1} equivalent hours";
        map["{0} {1} · {2}"] = "{0} {1} · {2}";
        map["{0} · 第 {1} 周"] = "{0} · Week {1}";
        map["{0} 个任务待确认截止周，尚未计入地图。"] = "{0} tasks need a due week and are not on the map yet.";
        map["{0} 个任务截止周待确认，暂不安排。"] = "{0} tasks have an unconfirmed due week and stay unscheduled.";
        map["{0} 个事件 · {1} 个全天事件不显示为课程"] = "{0} events · {1} all-day events are not shown as courses";
        map["已识别 {0} 条排课记录，按校历生成 {1} 次课程。"] = "Recognized {0} lesson records and generated {1} course sessions from the academic calendar.";
        map["{0} 个学期 · {1} 件待办"] = "{0} semesters · {1} to-dos";
        map["{0} 小时 · {1}"] = "{0} hours · {1}";
        map["{0} 件待完成"] = "{0} to finish";
        map["下一节 · {0} · {1}"] = "Next · {0} · {1}";
        map["今日压力 {0}/100 · {1}"] = "Today's pressure {0}/100 · {1}";
        map["另有 {0} 项任务，请在今天视图查看。"] = "{0} more tasks; see the Today view.";
        map["将 {0} 条任务标记为做完，需要提交的任务仍需单独确认提交。"] = "Mark {0} tasks as done; tasks that need submission still require separate confirmation.";
        map["已导入 {0} 条待办。"] = "Imported {0} to-dos.";
        map["已结束 {0} 门课程"] = "{0} courses ended";
        map["底色浓度 {0}"] = "Tint {0}";
        map["当天等效负荷 {0} 小时 / 每日容量 {1} 小时"] = "Equivalent load {0} h / daily capacity {1} h";
        map["正在上课 · {0} · 还有 {1} 分钟"] = "In class · {0} · {1} min left";
        map["每日压力 {0} 分，满分 100，{1}"] = "Daily pressure {0} of 100, {1}";
        map["移除“{0}”及它的课程？"] = "Remove “{0}” and its courses?";
        map["第 {0} 周最忙，{1} 个预警周。"] = "Week {0} is the busiest; {1} warning weeks.";
        map["第 {0} 周 · 压力 {1} · {2}"] = "Week {0} · pressure {1} · {2}";
        map["第 {0} 周：{1} 小时"] = "Week {0}: {1} hours";
        map["第 {0} 周：{1:0.##} 小时"] = "Week {0}: {1:0.##} hours";
        map["第 {0} 周 · 压力 {1:0.##} · {2}"] = "Week {0} · pressure {1:0.##} · {2}";
        map["{0} · {1:0.##} 等效小时"] = "{0} · {1:0.##} equivalent hours";
        map["底色浓度 {0:P0}"] = "Tint {0:P0}";
        map["删除“{0}”及其课程和任务？已导入的桌面待办会保留。"] = "Delete “{0}” and its courses and tasks? Imported desktop to-dos are kept.";
        map["删除“{0}”？"] = "Delete “{0}”?";
        map["接下来 · {0} · {1}"] = "Next · {0} · {1}";
        map["当天等效负荷 {0:0.##} 小时 / 每日容量 {1:0.#} 小时"] = "Equivalent load {0:0.##} h / daily capacity {1:0.#} h";
        map["{0:0.##} 小时 · {1:P0}"] = "{0:0.##} hours · {1:P0}";
        map["课程 {0:0.#} 小时 · 任务折算 {1:0.#} 小时\n每日容量 {2:0.#} 等效小时"] = "Courses {0:0.#} h · tasks converted {1:0.#} h\nDaily capacity {2:0.#} equivalent hours";
        map["这台设备：{0}\n云端：{1}\n\n本机待办：{2}\n云端待办：{3}\n\n选择一份作为当前数据，替换前会备份。取消将保留双方数据并暂停同步。"] = "This device: {0}\nCloud: {1}\n\nLocal to-dos: {2}\nCloud to-dos: {3}\n\nPick one as the current data; a backup is made before replacing. Cancelling keeps both sides and pauses sync.";
        map["文件：{0}\n备份内容：{1}\n当前内容：{2}\n\n确认后替换当前{3}数据，并自动备份当前内容。{4}"] = "File: {0}\nBackup: {1}\nCurrent: {2}\n\nConfirming replaces the current {3} data and backs up the current content.{4}";
    }
}
