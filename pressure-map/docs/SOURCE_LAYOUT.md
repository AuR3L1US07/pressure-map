# 源码目录与维护

| 目录 / 文件 | 用途 |
| --- | --- |
| `src/PressureMap.Core` | 数据库、学期规则、任务与课程日历等业务代码 |
| `src/PressureMap.App` | Avalonia 界面与 Windows 桌面服务 |
| `src/PressureMap.App/Views` | 界面页面与控件，一个页面组一个文件 |
| `src/PressureMap.App/Views/Smoke` | `--ui-smoke` 界面自检脚本，只在验证时运行 |
| `src/PressureMap.App/Services` | 云同步、网课读取、桌面原生互操作等系统服务 |
| `tests/PressureMap.Checks` | 核心回归检查 |
| `tests/OnlineCourses` | 网课读取和登录回归检查 |
| `tests/fixtures` | 必须保留的测试夹具 |
| `assets` | 程序图标与头像；`assets/design` 放设计源文件，不参与构建 |
| `static` | 示例数据等运行资源 |
| `docs` | 功能说明；其中的 `*.png` 是界面自检的基准截图，不能删 |
| `licenses` | 第三方许可证 |
| `dist/PressureMap` | 便携发布包，由 `build.bat` 生成，`run.bat` 不再使用 |
| `scripts/Run.ps1` | `run.bat` 的启动实现：关旧实例 → 增量编译 → 启动 |
| `scripts/Clean.ps1` | 可重复执行的项目缓存清理工具 |

`run.bat` 是日常入口，四步：结束仍在运行的旧实例（应用是单实例，不结束旧进程只会激活旧窗口，看不到新代码）→ 扫描 `src`、`tests` 的最新修改时间（跳过 `bin`、`obj`、`.runtime`，只扫约 160 个文件）→ 只有源码比 `bin` 产物新时才增量编译，且 Core 没改动时不再连带编译它 → 启动 `bin\Release\net10.0\PressureMap.exe`。实测无改动约 0.8 秒起、改一个界面文件约 6 秒起、改 Core 约 9 秒起，应用窗口本身约 1.4 秒。任何一步失败都会停在窗口里显示原因，不会再出现"双击后什么都没发生"。加 `-NoBuild` 可跳过编译，加 `-DryRun` 只打印判断结果。

`build.bat` 执行完整回归检查并发布到 `dist/PressureMap`（自带运行时，约 1 分钟），只在上线分发时用。开发调试也可用 `dev.ps1 -Mode Run`。不要另外生成带版本后缀的发布目录。

## 清理

双击 `clean.bat`，或执行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Clean.ps1
```

清理范围仅限本项目 `.csproj` 所在目录的 `bin`、`obj`，以及 `.test-temp`、`.release-stage`、`test-results`。不会删除正式发布、源码、测试夹具、文档、许可证、旧源码备份或 AppData 中的用户数据。

清除已核对的旧发布目录可加 `-OldReleases`。脚本仅识别此前生成的固定目录名，保留 `dist/PressureMap`；遇到运行中的旧版本或目录内用户数据会停止。可加 `-WhatIf` 预览。

工作区 `.tools` 是共享 SDK 和包依赖，`OnlineCourses/.runtime` 是随程序分发的浏览器组件，不属于待清缓存。
