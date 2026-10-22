# 玻璃组件 Phase 0 / Phase 1 记录

2026-10-20。正式窗口尚未修改，当前只交付独立测试原型。

## Phase 0 调查

(a) Avalonia/Desktop/Fluent 12.1.3，DataGrid 12.1.2；obj/project.assets.json 解析 Avalonia.Skia 12.1.3、SkiaSharp 3.119.4。
(b) 三个组件共用 TodoWindow 顶层窗口。DesktopNative 找到 Explorer 进程 SHELLDLL_DefView，用 SetWindowLongPtr(-8) 设置 owner，未使用 SetParent/WS_CHILD。设置 TOOLWINDOW/NOACTIVATE，移除 APPWINDOW/TOPMOST，Bed 管理 Z 序，Recover 定时恢复关联。
(c) DesktopGlass 控制 AcrylicBlur→Blur→Transparent；ApplyMaterial 只在实际返回模糊模式时使用透明背景，否则不透明。DesktopGlassTint 滑块 35–85% 直接作为 alpha；尚非目标映射。开关实际已影响三个组件。
(d) 没有显式激活/失焦换底色代码。系统合成行为必须实测，不能从枚举判定观感。
(e) 共用 TodoWindow、DesktopGlass.axaml、Ui、CoursePalette；三个内容控件 TodoControl/ScheduleControl/PressureControl 独立。原生交互和几何逻辑不应重写。窗口恢复现为 1 秒计时，课程刷新 30 秒，优化时须保留恢复功能。
(f) 今日课程 View：TodoWindow、ScheduleControl（compact）；无独立 ViewModel。展示依赖 Ui/IconView/CoursePalette、DesktopGlass/Tokens/Components/Soft 样式；入口 MainWindow.Today.cs/MainWindow.axaml.cs，设置 MainWindow.Settings.cs/MainWindow.DesktopSettings.cs、DesktopComponentPreview。数据：Store/CalendarSource/CourseOccurrence/CourseCalendar，保持业务逻辑。
(g) 本机 Windows 11，OS 10.0.26200。探针失焦且关联桌面层时实际返回 AcrylicBlur；这只能证明 API 状态。其他 Windows 10/11 版本及系统 backdrop 行为未验证。

## Phase 1 原型与验证

新增 tools/GlassPlayground 独立项目，不读取用户任务或设置。A 为系统模糊；B 用 SPI 壁纸读取、1/4 降采样、Skia 高斯模糊、位置裁剪。B 目前只支持单屏 Fill，是原型，不是完整 WallpaperBackdropService。

主应用和测试项目均构建通过，0 警告/错误；测试窗口已运行并自动退出。运行：

```powershell
$env:DOTNET_ROOT='E:\AIGC\.tools\dotnet'
& E:\AIGC\.tools\dotnet\dotnet.exe run --project tools/GlassPlayground/GlassPlayground.csproj
```

自动探针可运行编译 DLL 加 --capture。12 秒后保存 output/glass-playground，并记录实际透明模式、失焦状态和耗时。不改变系统壁纸。

第一次探针：A actual=AcrylicBlur，active=False；B actual=Transparent，active=False；壁纸读取成功。B 首次预处理 235 ms，包括解码、缩放模糊、PNG 中转与 Avalonia Bitmap 创建，超过 200 ms 预算。正式服务应异步且消除 PNG 中转。

截图初次被前台应用遮挡，无效；已改为 PrintWindow 捕获窗口本身覆盖。这种捕获用于布局检查，不能证明系统背景合成效果。手动验证：露出桌面、打开对比窗口、点击其他窗口失焦十秒、观察并截图；确认 A 没有退化成实色。切换浅/深主题并检查不同壁纸区域。

六张 tests/wallpapers 为确定性合成夹具。纯白/纯黑/高饱和梯度已生成；密集/夜景/浅色夹具是图案而非照片，因此 Z-1 未完成。按钮暂只预览高饱和图；六图双主题亮暗区矩阵待完成。

CPU/GPU、拖动 fps、三组件增量内存、降级和多屏尚未实测。不得将这些标记通过。

建议倾向 B（采样可控、独立于激活状态），但尚不能做最终路线判定。Phase 1 未完成，禁止进入 Phase 2；需补齐真实桌面视觉矩阵后请用户确认。正式三组件外观尚未改变。

后续计划：Phase 2 共用 WallpaperBackdropService/GlassSurface/GlassWidgetWindow/WidgetHeader/GlassTokens.axaml，保留原生挂载与几何行为；Phase 3 重排课程与设置本地化/动效；Phase 4 套用待办压力并做无障碍/生命周期/性能回归。已有用户未提交文件均保留。

## 逐项状态

跳过表示在本阶段等待决策闸门，不表示放弃。

编号 | 状态 | 改动文件 | 备注或原因
--- | --- | --- | ---
X-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-7 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-8 | 跳过 | — | 按用户要求等待路线闸门后的阶段
X-9 | 跳过 | — | 按用户要求等待路线闸门后的阶段
G-1 | 部分 | tools/GlassPlayground/Program.cs | 路线原型完成，视觉矩阵待完成
G-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
G-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
G-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
G-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
G-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-7 | 跳过 | — | 按用户要求等待路线闸门后的阶段
V-8 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-7 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-8 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-9 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-10 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-11 | 跳过 | — | 按用户要求等待路线闸门后的阶段
W-12 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-7 | 跳过 | — | 按用户要求等待路线闸门后的阶段
M-8 | 跳过 | — | 按用户要求等待路线闸门后的阶段
S-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
S-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
S-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
S-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
S-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-1 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-3 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段
R-7 | 跳过 | — | 按用户要求等待路线闸门后的阶段
Z-1 | 部分 | tests/wallpapers | 合成夹具生成；照片和主题矩阵待完成
Z-2 | 跳过 | — | 按用户要求等待路线闸门后的阶段
Z-3 | 部分 | 本报告 | 仅首次预处理耗时已测
Z-4 | 跳过 | — | 按用户要求等待路线闸门后的阶段
Z-5 | 跳过 | — | 按用户要求等待路线闸门后的阶段
Z-6 | 跳过 | — | 按用户要求等待路线闸门后的阶段

第二次运行 B 预处理 189 ms。两次 235/189 ms 波动，尚不能保证稳定满足预算。
