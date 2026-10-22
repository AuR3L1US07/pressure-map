# PressureMap 官网

纯静态官网，部署文件位于 dist/，无需框架、构建步骤、CDN或运行时依赖。

## 本地查看

在本目录执行 `python -m http.server 8765 --bind 127.0.0.1`。

- 网站：http://127.0.0.1:8765/dist/
- 改前/改后与交互帧：http://127.0.0.1:8765/verification/

## 本次改动

- 主题默认浅色并记忆手动选择；语言读取浏览器偏好并记忆选择。
- 一次性滚动入场、有限首屏轮播、可中断FAQ、压力来源面板进退场与计数。
- 真实可拖动、吸附、键盘移动、锁定和折叠的桌面组件演示。
- 三个平台统一的“安装包准备中”状态和轻提示，无占位下载模态框。
- 字号、对比度、移动端布局、截图WebP与PNG回退、减少动画和旧接口降级。

完整状态、限制、性能结果见 UI-REPORT.md；动效清单见 MOTION.md。backup/phase1 至 phase4 保存阶段快照。可读源保留于 .tools/app.authored.js 和 .tools/style.authored.css，dist 中为优化后的独立可部署文件。

## 配置真实发布

编辑 dist/app.js 中保留名称的 downloadUrls 与相邻 releaseInfo。三个平台默认URL为空，版本、大小、SHA-256、日期也为空。填入URL后会生成原生下载链接；元信息有值才显示。可以直接编辑部署文件；本地QA工具不是部署前置条件。

截图仍为真实中文版软件界面；英文切换翻译网页和图片替代文字。

## 部署

已发布至 http://121.43.32.85/，当前版本为 20261020-website-task-spacing-v1。历史部署见 DEPLOY.md；其中旧版本的交互描述仅代表历史状态。

部署仅上传 dist/index.html、style.css、app.js、assets/。不要上传 .tools、浏览器测试依赖、基线截图或备份；本次不包含安装包、账户数据或学习数据。
