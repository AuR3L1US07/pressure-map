/* 界面中英切换：
 *
 * 手机版的界面文案散在 index.html、app.js、pm-ui.js 里，没有统一的取词层。
 * 与其把上千处字符串改写成 T("…") 调用（改一处漏三处），这里采用**渲染后替换**：
 * 每次界面更新完，遍历文本节点，整串命中字典就换掉，动态片段交给正则规则。
 *
 * 好处：
 *   - 不用碰任何已有渲染代码，中文模式的行为完全不变（apply 只在 en 模式生效）
 *   - 用户自己写的内容（待办标题、课程名、备注）不在字典里，自然不会被翻译
 *   - 漏词可以被 scripts/qa-i18n.mjs 静态扫出来，改完是 0 才算过
 *
 * 坏处：字典必须跟着界面文案走，改文案就得改字典——这正是 qa-i18n.mjs 存在的意义。
 */
(function (PM) {
  "use strict";

  var dict = {
    // ---- 品牌与导航 ----
    "学期压力地图": "Semester Pressure Map",
    "学期压力地图 · 手机版": "Semester Pressure Map · Mobile",
    "· 学期压力地图": "· Semester Pressure Map",
    "今日": "Today",
    "课表": "Courses",
    "待办": "Tasks",
    "压力": "Load",
    "设置": "Settings",
    "AI": "AI",

    // ---- 通用 ----
    "新建": "New",
    "关闭": "Off",
    "开启": "On",
    "保存": "Save",
    "删除": "Delete",
    "取消": "Cancel",
    "确定": "OK",
    "导入": "Import",
    "创建": "Create",
    "已完成": "Done",
    "未完成": "Open",
    "全部": "All",
    "今天": "Today",
    "明天": "Tomorrow",
    "后天": "In 2 days",
    "已逾期": "Overdue",
    "无截止": "No due date",
    "未填写": "Not set",
    "未命名": "Untitled",
    "未命名课程": "Untitled course",
    "未分类": "Uncategorized",
    "未导入": "None",
    "每天": "per day",
    "天": "d",
    "月": "",
    "日": "",
    "周": "w",
    "节": "period",
    "个": "",
    "门": "courses",
    "条": "tasks",
    "份": "files",
    "项": "items",
    "小时": "h",
    "已开启": "On",
    "已关闭": "Off",
    "未授权": "Blocked",
    "网页版不可用": "App only",

    // ---- 周几 ----
    "周一": "Mon",
    "周二": "Tue",
    "周三": "Wed",
    "周四": "Thu",
    "周五": "Fri",
    "周六": "Sat",
    "周日": "Sun",
    "星期": "Weekday",

    // ---- 今日 ----
    "本周压力": "This week's load",
    "正常": "Normal",
    "压力偏高": "Heavier than usual",
    "节奏正常": "Steady pace",
    "轻松": "Light",
    "预警": "Watch",
    "红色预警": "Critical",
    "压力值": "Load score",
    "今日课程": "Today's classes",
    "整周课表": "This week",
    "今天要做": "For today",
    "全部待办": "All tasks",
    "最近截止": "Due soon",
    "还没有完成的事": "Nothing done yet",
    "没有待办，点右下角新建": "No tasks — tap + to add one",
    "这学期暂时没有安排任务": "No tasks scheduled this semester",
    "待办未完成": "open tasks",
    "任务总数": "tasks in total",
    "今天没有课，可以补一补作业": "No classes today — good time to catch up",
    "今天没有截止的事，做点想做的": "Nothing due today. Do something you enjoy.",
    "后面几天还没有安排": "Nothing planned for the next few days",
    "未填写教室": "Room not set",
    "重要": "Important",
    "子任务": "Subtasks",
    "已提交": "Submitted",
    "待提交": "To submit",
    "需提交": "Needs submission",
    "要做事": "To do",
    "要做的事": "Tasks",
    "考试": "Exam",
    "大作业": "Project",
    "报告": "Report",
    "测验": "Quiz",
    "其他": "Other",
    "实验": "Lab",
    "复习": "Review",

    // ---- 课表 ----
    "导入课表": "Import schedule",
    "课表来源": "Schedule sources",
    "导入教务导出的 .ics / .csv 后按真实日期显示，调课、停课、单双周都能正确反映。": "Import an .ics / .csv exported from your school system. Classes are shown on their real dates, so rescheduled and cancelled weeks are handled correctly.",
    "这天没有课；点右上角可导入课表": "No classes today. Tap the top-right button to import a schedule.",
    "ICS 日历": "ICS calendar",
    "CSV 课表": "CSV timetable",
    "还没有导入课表。支持教务导出的 .ics / .csv，导入后按真实日期显示，调课停课都能反映。": "No schedule imported yet. Both .ics and .csv from your school system work; classes land on their real dates.",
    "接下来一年没有可显示的课程。": "No classes in the next year.",
    "已导入": "Imported",
    "这条课程来自课表，删除课表可移除": "This class comes from an imported schedule. Remove the schedule to delete it.",
    "时间": "Time",
    "地点": "Location",
    "备注": "Notes",
    "来源": "Source",
    "删除这份课表": "Remove this schedule",
    "已删除课表": "Schedule removed",
    "课表文件不能超过 2 MB": "Schedule files must be under 2 MB",
    "无法解析这个文件": "Could not read this file",
    "个事件": "events",
    "· 含": "· incl.",
    "个全天事件（已忽略）": "all-day events, ignored",
    "请选择 .ics 日历或 .csv 课表": "Pick an .ics calendar or a .csv timetable",
    "日历中没有课程事件": "No class events in this calendar",
    "不是完整的 ICS 日历文件": "Not a complete ICS calendar file",
    "CSV 课表没有课程数据": "This CSV has no class rows",
    "CSV 最多导入 5000 条课程": "A CSV can hold at most 5000 classes",
    "CSV 引号没有闭合": "Unclosed quote in the CSV",
    "重复课程过于密集，无法导入此课表": "Too many duplicate classes; this schedule cannot be imported",
    "CSV 需要「日期、课程、开始时间、结束时间」列，可选「教室、教师」": "The CSV needs date, course, start and end columns; room and teacher are optional",
    "日期": "Date",
    "课程名称": "Course",
    "开始时间": "Start",
    "结束时间": "End",
    "教室": "Room",
    "教师": "Teacher",
    "课程": "Courses",

    // ---- 压力 ----
    "学期压力曲线": "Semester load curve",
    "本周课程压力": "Load by week",
    "什么时候开始做": "When to start",
    "按每周可用时间倒推：压力预警的周可用时间减半。": "Worked backwards from your weekly capacity. Weeks flagged in red are treated as having half the time.",
    "这一周还没有任务压力": "No task load this week",
    "这一周没有需要提前开始的任务": "Nothing needs to start early this week",

    // ---- AI ----
    "今天的情况": "Today at a glance",
    "补充要求（可选）": "Extra instructions (optional)",
    "生成今日建议": "Generate today's plan",
    "生成时会把今天的课程时间、相关待办和补充要求发送到你配置的 AI 接口。": "Your class times, related tasks and extra instructions are sent to the AI endpoint you configured.",
    "安排建议": "Suggested plan",
    "生成后，这里会显示今天的安排建议。": "Your plan will appear here once generated.",
    "例如：晚上 8 点后有空，优先完成实验报告": "e.g. free after 8 pm, finish the lab report first",
    "已配置": "Configured",
    "未配置": "Not configured",
    "更新于": "Updated",
    "· 建议仅供参考，任务不会被自动修改。": "· Suggestions only — your tasks are not modified.",
    "先到设置里填写 AI 接口地址、模型和 Key": "Fill in the endpoint, model and key in Settings first",
    "正在生成建议…": "Generating…",
    "已生成建议": "Plan ready",
    "生成失败": "Generation failed",
    "接口地址必须是 https:// 开头": "Endpoint must start with https://",
    "已保存": "Saved",
    "已保存，但配置还不完整": "Saved, but the configuration is incomplete",
    "清除 AI 接口配置（密钥）？": "Clear the AI endpoint configuration (and key)?",
    "已清除": "Cleared",

    // ---- 设置：学期 ----
    "学期管理": "Semesters",
    "当前学期": "Current semester",
    "新建学期": "New semester",
    "删除当前学期": "Delete current semester",
    "学期": "Semester",
    "学期名称": "Semester name",
    "总周数": "Total weeks",
    "高压阈值": "Heavy-load threshold",
    "每周可用小时": "Hours available per week",
    "第 1 周周一": "Monday of week 1",
    "新学期": "New semester",
    "个学期": "semesters",
    "已切换到": "Switched to",
    "学期名称不能为空": "Semester name cannot be empty",
    "已创建": "Created",
    "至少保留一个学期": "Keep at least one semester",
    "第 1 周需要从周一开始": "Week 1 must start on a Monday",
    "删除「": "Delete “",
    "」及其课程、任务？": "” with all its courses and tasks?",

    // ---- 设置：外观与提醒 ----
    "外观与语言": "Appearance and language",
    "主题": "Theme",
    "跟随系统": "System",
    "浅色": "Light",
    "深色": "Dark",
    "浅色主题": "Light theme",
    "深色主题": "Dark theme",
    "切换主题": "Switch theme",
    "课表起始日": "Week starts on",
    "界面语言": "Language",
    "到点提醒": "Reminders",
    "任务截止提醒": "Deadline reminders",
    "发一条测试提醒": "Send a test reminder",
    "截止前 24 小时、1 小时和到期时各提醒一次；已完成但仍需提交的任务，会在到期时提醒提交。": "You get one reminder 24 hours before, one an hour before, and one at the deadline. Tasks marked done but not yet submitted get a submission nudge at the deadline.",
    "测试提醒": "Test reminder",
    "看到这条说明提醒已经生效": "If you can see this, reminders are working",
    "网页版没有系统通知，装成 App 才能提醒": "The web version has no system notifications — install the app to get reminders",
    "5 秒后发一条测试提醒": "A test reminder will arrive in 5 seconds",
    "没发出去，检查系统的通知权限": "Not sent — check the notification permission",
    "已完成，记得提交": "Done — remember to submit",
    "已到截止时间": "The deadline has passed",
    "将在 1 小时内截止": "Due within an hour",
    "将在 24 小时内截止": "Due within 24 hours",

    // ---- 设置：云 ----
    "云账号与同步": "Account and sync",
    "云同步": "Cloud sync",
    "未登录": "Not signed in",
    "已登录": "Signed in",
    "账号": "Account",
    "服务器": "Server",
    "状态": "Status",
    "登录": "Sign in",
    "注册新账号": "Create account",
    "退出登录": "Sign out",
    "立即同步": "Sync now",
    "切换 / 添加账号": "Switch / add account",
    "云服务地址": "Server URL",
    "密码": "Password",
    "手机号或邮箱": "Phone or email",
    "昵称（注册时用）": "Display name (for sign-up)",
    "本机登录过的账号：": "Accounts used on this device:",
    "登录后手机与电脑共用同一份数据；不登录则一切只存本机。": "Signed in, your phone and computer share one dataset. Signed out, everything stays on this device.",
    "云端和本机都改过数据，且改动时间重叠。请选择保留哪一份（另一份会被覆盖）。": "Both the cloud and this device changed, and the changes overlap. Pick the copy to keep — the other will be overwritten.",
    "用本机数据覆盖云端": "Keep this device's data",
    "用云端数据覆盖本机": "Keep the cloud data",
    "云服务地址必须是有效的 HTTPS 地址": "The server URL must be a valid HTTPS address",
    "请填写账号和密码": "Enter your account and password",
    "注册需要一个昵称": "A display name is needed to sign up",
    "正在注册…": "Signing up…",
    "正在登录…": "Signing in…",
    "已登录，准备同步": "Signed in, ready to sync",
    "已登录并同步": "Signed in and synced",
    "登录失败": "Sign-in failed",
    "退出登录？本机数据会保留，但不再与云端同步。": "Sign out? Local data stays, but syncing stops.",
    "已退出登录": "Signed out",
    "这个账号需要重新登录": "This account needs to sign in again",
    "本地模式": "Local only",
    "无需同步": "Nothing to sync",
    "已是最新": "Up to date",
    "已上传到云端": "Uploaded to cloud",
    "已从云端更新": "Updated from cloud",
    "发现冲突，需要你选择保留哪份": "Conflict — choose which copy to keep",
    "同步完成": "Sync complete",
    "请先登录云账号": "Sign in to an account first",
    "正在同步…": "Syncing…",
    "同步失败": "Sync failed",
    "已恢复登录": "Session restored",

    // ---- 在线课程 ----
    "在线课程": "Online courses",
    "未同步": "Not synced",
    "已同步": "Synced",
    "读取超星学习通": "Read from Chaoxing",
    "读取中国大学 MOOC": "Read from MOOC",
    "在 App 内打开课程页并登录，再点顶部「读取这一页」，未完成的作业会同步成待办。密码不会保存在本机。": "Sign in to your course site in the in-app browser, then tap “Read this page”. Outstanding assignments are added as tasks. Your password is never stored.",
    "网页版不能直接抓取，装成 App 后可用": "Scraping needs the app — it is not available in the browser",
    "打不开内嵌浏览窗口": "Could not open the in-app browser",
    "在弹出的页面里登录，然后点顶部「读取这一页」": "Sign in on the page, then tap “Read this page”",
    "读取结果无法解析": "Could not read the result",
    "读取失败：": "Read failed: ",
    "页面结构可能变了": "the page layout may have changed",

    // ---- 设置：AI 接口 ----
    "AI 接口": "AI endpoint",
    "接口地址（OpenAI 兼容）": "Endpoint (OpenAI-compatible)",
    "模型": "Model",
    "每天可用小时": "Hours available per day",
    "保存接口配置": "Save endpoint",
    "清除配置": "Clear",
    "密钥只存在这台设备上，不会上传到云服务。": "The key stays on this device and is never uploaded.",

    // ---- 设置：数据 ----
    "数据": "Data",
    "本地": "Local",
    "导出备份（JSON）": "Export backup (JSON)",
    "导入备份": "Import backup",
    "清理本地缓存": "Clear local cache",
    "载入示例学期": "Load sample semester",
    "清空全部数据": "Erase all data",
    "备份与电脑版通用：手机导出的 JSON，电脑版「导入备份」能直接读，反之亦然。": "Backups are interchangeable with the desktop app: a JSON exported here opens there, and the other way round.",
    "备份内容": "Backup contents",
    "复制下面全部内容保存": "Copy everything below and save it",
    "先粘贴备份内容": "Paste the backup first",
    "导入成功": "Imported",
    "导入失败：": "Import failed: ",
    "文件格式不对": "Wrong file format",
    "不是有效的备份文件": "Not a valid backup file",
    "没有可导入的数据": "No data to import",
    "数据已升级，课表迁移为日历格式": "Data upgraded; the timetable was migrated to calendar format",
    "手机版课表（迁移）.ics": "Mobile timetable (migrated).ics",
    "保存失败，存储空间可能已满": "Save failed — storage may be full",
    "保存失败": "Save failed",
    "已保存到": "Saved to ",
    "已保存到「文件」App / 我的 iPhone / PressureMap": "Saved to Files / On My iPhone / PressureMap",
    "已导出备份": "Backup exported",
    "载入示例数据，可在设置里清空": "Sample data loaded. You can erase it in Settings.",
    "载入示例学期会覆盖现在的课程、任务和课表，继续？": "Loading the sample semester replaces your courses, tasks and schedules. Continue?",
    "已载入示例学期": "Sample semester loaded",
    "清空所有课程、任务、待办和课表？此操作不可撤销。": "Erase all courses, tasks, todos and schedules? This cannot be undone.",
    "已清空": "Erased",
    "清理本地缓存？会删掉 1.x 旧版数据残留、课表/备份的临时副本，不影响学期、待办和登录状态。": "Clear local cache? This removes leftovers from 1.x and temporary copies of schedules and backups. Semesters, tasks and sign-in are untouched.",
    "旧版数据": "Legacy data",
    "已清理": "Cleared",
    "没有需要清理的缓存": "Nothing to clear",

    // ---- 设置：安装 ----
    "装到手机上": "Get the app",
    "下载 Android 安装包（APK）": "Download Android APK",
    "iPhone 用 Safari 打开本页，点底部分享按钮 →「添加到主屏幕」，就会像 App 一样全屏运行。": "On iPhone, open this page in Safari, tap Share → Add to Home Screen, and it runs full-screen like an app.",
    "版本": "Version",
    "· 网页版": "· Web",
    "版本 1.0.0": "Version 1.0.0",

    // ---- 待办表单 ----
    "添加任务": "Add task",
    "新建待办": "New task",
    "编辑待办": "Edit task",
    "先写点内容": "Write something first",
    "已更新": "Updated",
    "已添加": "Added",
    "已删除": "Deleted",
    "删除这条待办？": "Delete this task?",
    "任务名": "Task name",
    "任务名不能为空": "Task name cannot be empty",
    "课程名": "Course name",
    "课程名不能为空": "Course name cannot be empty",
    "关联课程（用于压力分析）": "Course (used in load analysis)",
    "不选课程": "No course",
    "不关联": "None",
    "计划哪天做": "Planned for",
    "截止日期": "Due date",
    "截止时间": "Due time",
    "预计要花几小时": "Estimated hours",
    "类型": "Type",
    "每隔几天重复（0 不重复）": "Repeat every N days (0 = never)",
    "子任务（每行一个，已完成的行首加 [x]）": "Subtasks, one per line. Prefix finished ones with [x].",
    "标记为重要": "Mark as important",
    "需要提交": "Needs submission",
    "例如：数据结构实验报告 3": "e.g. Lab report 3",
    "例如：数据结构": "e.g. Data structures",
    "例如：期中考试": "e.g. Midterm exam",
    "例如：2027 春季学期": "e.g. Spring 2027",
    "教学楼 + 教室": "Building + room",
    "画测试用例图\n[x] 补运行截图": "Draw test case diagram\n[x] Add screenshots",
    "作业": "Assignment",
    "先到设置里添加课程再建任务": "Add a course in Settings before creating tasks",
    "截止周": "Due week",
    "预计小时（留空按类型默认）": "Estimated hours (blank = type default)",
    "已添加到第": "Added to week ",

    // ---- 课程表单 ----
    "添加课程": "Add course",
    "编辑课程": "Edit course",
    "星期": "Weekday",
    "开始": "Start",
    "结束": "End",
    "结束时间要晚于开始时间": "The end time must be after the start time",
    "删除这节课？": "Delete this class?",
    "未分类课程": "Uncategorized course",

    // ---- 示例数据 ----
    "2026 秋季学期": "Fall 2026",
    "高等数学": "Calculus",
    "数据结构": "Data structures",
    "大学英语": "College English",
    "计算机网络": "Computer networks",
    "概率论": "Probability",
    "体育": "PE",
    "操场": "Playground",
    "第三章习题": "Chapter 3 exercises",
    "期中考试": "Midterm",
    "链表实验报告": "Linked list lab report",
    "课程设计开题": "Project proposal",
    "课程设计答辩": "Project defence",
    "单元测验 2": "Unit quiz 2",
    "英语演讲稿": "English speech draft",
    "网络实验一": "Networking lab 1",
    "期末大作业": "Final project",
    "概率论作业 4": "Probability homework 4",
    "期末考试": "Final exam",
    "数据结构实验报告 3": "Data structures lab report 3",
    "画测试用例图": "Draw test case diagram",
    "补运行截图": "Add run screenshots",
    "背 60 个单词": "Memorise 60 words",
    "网络实验一 抓包分析": "Networking lab 1 packet analysis",
    "高数第三章习题 1-12": "Calculus chapter 3, exercises 1-12",
    "准备课程设计开题 PPT": "Prepare the project proposal deck",

    // ---- 其它 ----
    "完成了 ·": "Done ·",
    "没有可用的文件选择器": "No file picker available",
    "看到这条说明提醒已经生效": "If you can see this, reminders are working"
  };

  var WEEKDAY = { "周一": "Mon", "周二": "Tue", "周三": "Wed", "周四": "Thu", "周五": "Fri", "周六": "Sat", "周日": "Sun", "周天": "Sun" };
  var MONTH = ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

  function wd(name) {
    return WEEKDAY[name] || name;
  }

  /** 动态文案：正则先跑，顺序敏感（具体的放前面）。 */
  var rules = [
    // ---- 先处理带前缀的复合串，否则会被下面的通用规则截断 ----
    [/^第 (\d+) 周 · 本周压力$/, "Week $1 · Load"],
    [/^第 (\d+) 周 · (周[一二三四五六日天]) · (\d+) 节$/, function (_, w, d, n) {
      return "Week " + w + " · " + wd(d) + " · " + n + " periods";
    }],
    [/^第 (\d+) \/ (\d+) 周$/, "Week $1 / $2"],
    [/^(\d+) 个学期$/, "$1 semesters"],
    [/^(.+)（(\d+) 周）$/, function (_, name, n) { return name + " (" + n + " weeks)"; }],
    [/^(今天|明天|后天) (\d{2}:\d{2})$/, function (_, when, time) {
      return (when === "今天" ? "Today" : when === "明天" ? "Tomorrow" : "In 2 days") + " " + time;
    }],
    [/^子任务 (\d+)\/(\d+)$/, "Subtasks $1/$2"],
    [/^共 (\d+) 条 · 未完成 (\d+) 条 · 已完成 (\d+) 条$/, "$1 tasks · $2 open · $3 done"],
    [/^今日课程 (\d+) 节 · 相关待办 (\d+) 项 · 每天可用 (.+) 小时$/, "Today: $1 classes · $2 related tasks · $3 h available"],
    [/^([一二三四五六日])$/, function (s) { return wd("周" + s); }],
    // 顶部副标题：第 3 周 · 周一 · 10月21日
    [/^第 (\d+) 周 · (周[一二三四五六日天]) · (\d+)月(\d+)日$/, function (_, w, d, m, day) {
      return "Week " + w + " · " + wd(d) + " · " + MONTH[Number(m) - 1] + " " + day;
    }],
    [/^第 (\d+) 周 · (周[一二三四五六日天])$/, function (_, w, d) { return "Week " + w + " · " + wd(d); }],
    [/^第 (\d+) 周$/, "Week $1"],
    [/^(\d+)月(\d+)日$/, function (_, m, d) { return MONTH[Number(m) - 1] + " " + d; }],
    [/^周[一二三四五六日天]$/, function (s) { return wd(s); }],
    // 今日概览
    [/^今天 (\d+) 节课 · 相关待办 (\d+) 项 · 每天可用 (.+) 小时$/, "Today: $1 classes · $2 related tasks · $3 h available"],
    [/^(\d+) 节 · 相关待办 (\d+) 项 · 每天可用 (.+) 小时$/, "$1 classes · $2 related tasks · $3 h available"],
    [/^今天没有课。$/, "No classes today."],
    [/^今天没有相关待办。$/, "Nothing due today."],
    // 压力
    [/^阈值 (.+)$/, "Threshold $1"],
    [/^压力值 (.+)$/, "Load $1"],
    [/^共 (\d+) 门课程$/, "$1 courses"],
    [/^还有 (\d+) 个任务在这周之后截止$/, "$1 more tasks due after this week"],
    [/^周开始 → 第 (\d+) 周截止$/, "Starts week 1 → due week $1"],
    [/^第 (\d+) 周有压力预警，可用时间减半$/, "Week $1 is flagged — capacity halved"],
    [/^· 共 (.+) 小时$/, "· $1 h total"],
    [/^· 缺少 (.+) 小时$/, "· $1 h short"],
    // 列表
    [/^共 (\d+) 条 · 未完成$/, "$1 open"],
    [/^共 (\d+) 条 · 已完成$/, "$1 done"],
    [/^共 (\d+) 条$/, "$1 tasks"],
    [/^课程 (\d+) 门 · 任务 (\d+) 个 · 课表 (\d+) 份$/, "$1 courses · $2 tasks · $3 schedules"],
    [/^课程 (\d+) 门 · 任务 (\d+) 个 · 课表 未导入$/, "$1 courses · $2 tasks · no schedule"],
    [/^课程与任务共 (\d+) 门 \/ (\d+) 个$/, "$1 courses / $2 tasks"],
    [/^版本 (.+) · 网页版$/, "Version $1 · Web"],
    [/^版本 (.+) · Android \/ iOS$/, "Version $1 · Android / iOS"],
    [/^版本 (.+)$/, "Version $1"],
    // 课表
    [/^(\d+) 个事件 · 含 (\d+) 个全天事件（已忽略）$/, "$1 events · $2 all-day ignored"],
    [/^(\d+) 个事件$/, "$1 events"],
    [/^单个课表最多包含 (\d+) 条课程$/, "One schedule can hold at most $1 classes"],
    [/^CSV 第 (\d+) 行无效：日期用 yyyy-MM-dd，时间用 HH:mm，结束时间必须晚于开始时间$/, "CSV line $1 is invalid: use yyyy-MM-dd for dates and HH:mm for times, and the end must be after the start"],
    [/^CSV 第 (\d+) 行无效：结束时间必须晚于开始时间$/, "CSV line $1 is invalid: the end must be after the start"],
    [/^课程“(.+)”缺少开始时间$/, "Course “$1” has no start time"],
    [/^“(.+)”的结束时间无效$/, "The end time of “$1” is invalid"],
    // 学期
    [/^（第 (\d+) 周）$/, "(week $1)"],
    [/^已切换到 (.+)$/, "Switched to $1"],
    [/^已添加到第 (\d+) 周$/, "Added to week $1"],
    [/^更新于 (.+)$/, "Updated $1"]
  ];

  var observer = null;
  var scheduled = false;

  function lang() {
    var st = window.PMUI && PMUI.state ? PMUI.state() : null;
    return st && st.settings && st.settings.language === "en" ? "en" : "zh";
  }

  function translate(text) {
    var key = String(text).trim();
    if (!key) return null;
    if (dict[key] !== undefined) return String(text).replace(key, dict[key]);
    for (var i = 0; i < rules.length; i++) {
      if (rules[i][0].test(key)) return String(text).replace(key, key.replace(rules[i][0], rules[i][1]));
    }
    return null;
  }

  function walk(root) {
    if (!root || !root.ownerDocument) return;
    var doc = root.ownerDocument;
    var walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
    var node;
    while ((node = walker.nextNode())) {
      if (node.__pmZh !== undefined) continue; // 已经翻过，留着原串等切回中文
      var parent = node.parentNode;
      if (!parent) continue;
      var tag = parent.nodeName;
      if (tag === "SCRIPT" || tag === "STYLE" || tag === "TEXTAREA") continue;
      if (parent.closest && parent.closest("[data-i18n-skip]")) continue;
      var next = translate(node.nodeValue);
      if (next === null) continue;
      node.__pmZh = node.nodeValue;
      node.nodeValue = next;
    }
    var nodes = root.querySelectorAll ? root.querySelectorAll("[placeholder],[title]") : [];
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      var p = el.getAttribute("placeholder");
      if (p) {
        var np = translate(p);
        if (np !== null) {
          el.__pmZh = el.__pmZh || {};
          if (el.__pmZh.placeholder === undefined) el.__pmZh.placeholder = p;
          el.setAttribute("placeholder", np);
        }
      }
      var t = el.getAttribute("title");
      if (t) {
        var nt = translate(t);
        if (nt !== null) {
          el.__pmZh = el.__pmZh || {};
          if (el.__pmZh.title === undefined) el.__pmZh.title = t;
          el.setAttribute("title", nt);
        }
      }
    }
  }

  /** 切回中文：把缓存下来的原文写回去（静态标签不会被 render 重画，必须显式还原）。 */
  function restore(root) {
    if (!root || !root.ownerDocument) return;
    var doc = root.ownerDocument;
    var walker = doc.createTreeWalker(root, NodeFilter.SHOW_TEXT, null);
    var node;
    while ((node = walker.nextNode())) {
      if (node.__pmZh === undefined) continue;
      node.nodeValue = node.__pmZh;
      delete node.__pmZh;
    }
    var nodes = root.querySelectorAll ? root.querySelectorAll("[placeholder],[title]") : [];
    for (var i = 0; i < nodes.length; i++) {
      var el = nodes[i];
      if (!el.__pmZh) continue;
      if (el.__pmZh.placeholder !== undefined) el.setAttribute("placeholder", el.__pmZh.placeholder);
      if (el.__pmZh.title !== undefined) el.setAttribute("title", el.__pmZh.title);
      delete el.__pmZh;
    }
  }

  /** 英文模式：整页翻一遍。中文模式：先把原文写回，再让界面重画一次。 */
  function apply() {
    if (typeof document === "undefined" || !document.body) return;
    var isEn = lang() === "en";
    document.documentElement.lang = isEn ? "en" : "zh-CN";
    if (isEn) walk(document.body);
    else {
      restore(document.body);
      if (window.PMUI && PMUI.render) PMUI.render();
    }
  }

  function schedule() {
    if (scheduled) return;
    scheduled = true;
    setTimeout(function () {
      scheduled = false;
      if (lang() === "en") walk(document.body);
    }, 0);
  }

  function observe() {
    if (typeof MutationObserver === "undefined" || !document.body || observer) return;
    observer = new MutationObserver(function () {
      if (lang() === "en") schedule();
    });
    observer.observe(document.body, { childList: true, subtree: true, characterData: true });
  }

  /** 原生对话框（confirm/alert）的文案不在 DOM 里，只能包一层。 */
  function wrapDialogs() {
    if (typeof window.confirm !== "function" || window.confirm.__pmWrapped) return;
    var nativeConfirm = window.confirm;
    var nativeAlert = window.alert;
    var confirmWrap = function (message) {
      var t = lang() === "en" ? translate(message) : null;
      return nativeConfirm(t === null ? message : t);
    };
    confirmWrap.__pmWrapped = true;
    window.confirm = confirmWrap;
    if (typeof nativeAlert === "function" && !nativeAlert.__pmWrapped) {
      var alertWrap = function (message) {
        var t = lang() === "en" ? translate(message) : null;
        return nativeAlert(t === null ? message : t);
      };
      alertWrap.__pmWrapped = true;
      window.alert = alertWrap;
    }
  }

  function boot() {
    wrapDialogs();
    observe();
    apply();
  }

  window.PMI18N = {
    dict: dict,
    rules: rules,
    translate: translate,
    apply: apply,
    boot: boot,
    lang: lang
  };
  PM.I18N = window.PMI18N;
  PM.t = function (text) {
    var t = translate(text);
    return t === null ? String(text) : t;
  };

  // app.js 的 init 也挂在 DOMContentLoaded 上，按脚本顺序这里会后跑，
  // 此时 state 已经就绪，可以直接按当前语言翻一遍。
  if (typeof document !== "undefined") {
    if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
    else boot();
  }
})(window.PM = window.PM || {});
