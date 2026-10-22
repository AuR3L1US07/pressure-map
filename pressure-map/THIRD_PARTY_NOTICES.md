# 第三方代码与资源

## Filee

本项目直接复用了 [KnifeLemon/Filee](https://github.com/KnifeLemon/Filee) 的 `Styles/Soft.axaml`（移除本项目不使用的 DonutMenu 专用样式）和 `Styles/Icons.axaml`，并依照其 230px 导航侧栏与三栏工作区布局实现界面。参考提交：`a1be20cefad36ea9bfa1b3bb923e3199b78de992`。

Copyright (c) 2026 Myeonggu Kim (KnifeLemon) and Filee contributors. Filee 为 MIT 许可，完整文本见 `licenses/Filee-MIT.txt`。

## Material Design Icons

`Icons.axaml` 中的图标几何来自 Pictogrammers / Material Design Icons，为 Apache-2.0 许可。来源：https://pictogrammers.com ，完整许可见 `licenses/Apache-2.0.txt`。图标在 Filee 中的原始来源注释已保留。

## 运行库

程序依赖 Avalonia、Microsoft.Data.Sqlite、SQLitePCLRaw、SkiaSharp 与 HarfBuzzSharp 等 NuGet 包。直接依赖版本写在项目文件中；传递依赖与包信息由 `obj/project.assets.json` 记录。它们不是本项目作者原创资源，各包许可随包提供。

## DeskBox 研究

Explorer 桌面层机制参考 [Tianyu199509/DeskBox](https://github.com/Tianyu199509/DeskBox) 的 `WidgetLayerService.cs`：关联已有 `SHELLDLL_DefView` 桌面所有者，并管理窗口层级。本项目自行编写 Win32 P/Invoke 实现，没有复制 DeskBox 的源代码或图像资源。参考提交：`bbb9e69721a9faf2defd54f03dbd56fabca9c67a`。

## 课程日历依赖

Ical.Net 5.2.3（ical-org，MIT，Copyright 2007–2026）用于本地 ICS 解析；完整许可见 licenses/Ical.Net-MIT.txt。其依赖 NodaTime 3.2.2（Jon Skeet and contributors，Apache-2.0）用于时区计算，完整许可见 licenses/Apache-2.0.txt。项目主页：https://github.com/ical-org/ical.net 与 https://nodatime.org/ 。

## 网课助手浏览器组件

网课任务统计与登录逻辑从用户指定的本机 E:\刷课 工程迁移，仅包含读取和登录模块。
Node.js（MIT 及发行版所附第三方许可证）与 Playwright Core（Apache-2.0）随 OnlineCourses/.runtime 分发；相应许可证保留在各运行时目录。浏览器由用户本机安装的 Chrome 或 Microsoft Edge 提供。

## PDF 课表解析

PdfPig 0.1.14（Apache-2.0）用于本地读取教务课表文字和坐标。项目主页：https://github.com/UglyToad/PdfPig ，完整许可见 licenses/Apache-2.0.txt。下沙节次时间采用杭州电子科技大学公布的作息：https://www.hdu.edu.cn/770/list.htm 。

## OpenAI navigation icon
OpenAI brand icon from Simple Icons v15.0.0 (CC0-1.0): https://github.com/simple-icons/simple-icons. Used for the AI Assistant navigation item. Brand rights remain with their respective owner.
