# 设计与实现来源

主界面参考 [Filee](https://github.com/KnifeLemon/Filee)，固定参考提交 a1be20cefad36ea9bfa1b3bb923e3199b78de992。采用 230 像素侧栏、浅色背景、淡紫选中状态、圆角控件、三栏待办配置和轻量过渡。Styles/Soft.axaml 与 Icons.axaml 来自其 MIT 许可源码，移除了本项目不使用的 DonutMenu 类型样式，保留图标来源。应用名称与功能内容使用本项目自己的内容。

桌面待办参考 [DeskBox](https://github.com/Tianyu199509/DeskBox)，参考提交 bbb9e69721a9faf2defd54f03dbd56fabca9c67a。独立实现 Explorer SHELLDLL_DefView 所有者关联、TOOLWINDOW/NOACTIVATE 和桌面层级管理，未复制其源码、图片或图标。该关联方式与 DeskBox 一致，区别于普通悬浮窗口；并非使用 SetParent 创建子窗口。支持宿主丢失恢复、折叠、锁定和位置持久化。

验证：98 项核心检查通过，覆盖旧 Python 数据库、压力值与安排计算一致性、导入去重、完成状态、事务、配置与 HTTP 失败处理。便携包真实界面测试覆盖 9 页、4 个规划步骤、可跳过登录、待办新增、保存失败保护以及 Windows 桌面所有者和窗口标志。测试报告与截图位于 docs，测试使用独立示例数据。

依赖声明与许可证见 THIRD_PARTY_NOTICES.md 和 licenses。服务器未在本次实现范围内，云同步仍待接入。

本机课程助手参考：经用户指定，读取 E:\刷课\ui_motion.py 与 qt_app.py，对照 56 像素一体标题栏、40 像素窗口按钮、560 毫秒阻尼倾斜及右侧从上方分组进入的节奏。使用 Avalonia 独立实现，页面入口使用位移与透明度，分组间隔 25 毫秒、每组 280 毫秒；支持快速切换复位、首次交互取消启动动画和系统/应用减少动画设置。原生命中测试验证标题栏拖动与边缘缩放，界面检查验证最小化、最大化、还原及快速切换。

标题栏最终采用与 PageBrush 同色的 40 像素无色带方案，仅保留窗口操作按钮；唯一品牌标识置于侧栏顶部。

桌面玻璃使用 Avalonia AcrylicBlur / Blur 的系统背景合成，不对文字或整个窗口降低透明度，也不将前景送入模糊滤镜。颜色底层、高光边界与内容分层绘制，支持浅深主题、35%～85% 底色浓度和实色回退。使用独立条纹窗口在桌面层验证真实模糊；测试不修改壁纸。实现依据 [Avalonia Windows 平台说明](https://docs.avaloniaui.net/docs/platform-specific-guides/windows) 与 [SetWindowRgn 的区域生命周期说明](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowrgn)。

课程日历参考用户提供的苹果日历截图中的周日期栏、按时间高度排列课程与当前时间线，继续使用 Filee 配色和控件。桌面采用更易扫读的时间列表，复用原有 Acrylic 材质及 Explorer 桌面层机制；课程与待办分别保存几何状态。ICS 通过 Ical.Net 5.2.3 解析，原始日历保留在 SQLite 中，不将无限重复课程提前展开成固定学期。新增回归覆盖单双周、EXDATE、RECURRENCE-ID、时区、跨日、同名导入更新及失败保留旧数据。

新增学生今天总览、课程关联任务、精确截止、子任务和独立提交状态。日负荷圆环使用可解释的 0～100 规则（见 DAILY_PRESSURE.md），主窗口和桌面复用同一控件与计算方法。提醒为进程内浮层，支持持久化去重与延后，退出应用后不运行。真实界面验证覆盖新建课程任务、子任务保存、提交按钮、圆环一致性、完成后降分和提醒延后。
