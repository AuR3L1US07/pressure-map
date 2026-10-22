# 官网部署记录

日期：2026-10-19

## 当前部署

- 地址：http://121.43.32.85/
- 服务：复用 pressure-map-server 的 Docker Compose / Caddy 2.11.7。
- 官网目录：/opt/pressure-map-website
- 发布版本：/opt/pressure-map-website/releases/20261019-website-v1
- 当前版本软链接：/opt/pressure-map-website/current
- Caddy 容器挂载：/opt/pressure-map-website:/srv/pressure-map-website:ro
- Compose 扩展：/opt/pressure-map-server/compose.override.yaml
- Caddy 配置：/opt/pressure-map-server/deploy/Caddyfile
- 原配置备份：/opt/pressure-map-server/deploy/website-backup-20261019-website-v1

静态官网无需 Node.js 或 .NET 运行时，现有环境足够，未额外安装服务。

HTTP 首页提供官网；/api 与 /api/* 继续跳转 HTTPS。HTTPS 的原有 /api/* 路由及证书保持使用，其他路径同样提供官网。当前 IP HTTPS 仍使用原有自签证书，对外官网分享 HTTP 地址。

## 已验证

- Caddy 配置校验通过。
- 服务器本机 HTTP 页面与 app.js 正常，包含新版标题和轮播代码。
- HTTPS API /api/health 返回 {"status":"ok"}。
- Caddy 与 API 均正常运行，API 容器未重建。
- 上传包不含安装包与数据库。
- 本地浏览器已验证：默认浅色、中英文、主题切换、侧栏开关及跳转、三平台按钮提示、手机无横向溢出、渐变字与标题轮播暂停。
- 公网浏览器页面验收未完成：浏览器网络访问授权被拒绝。未绕过该限制。

## 后续更新

1. 修改本地 dist 中的网页。
2. 打包 index.html、style.css、app.js、assets；安装包链接配置在 app.js 顶部 downloadUrls。
3. 上传到 /opt/pressure-map-website/releases 下新的独立版本目录。
4. 将 current 软链接切换到新版本，不覆盖旧版本。网站文件更新无需重建 API。
5. 检查首页、资源和 API 健康状态。

本次使用的配置和首次发布脚本保存在 deploy/。deploy-first-release.sh 仅用于首次发布，不能直接重复执行；其保护检查会拒绝已有官网和已有 Compose 扩展。

## 回滚本次首次部署

在服务器 /opt/pressure-map-server 中，将备份目录内的 Caddyfile 复制回 deploy/Caddyfile；将本次新建的 compose.override.yaml 移至备份目录，然后运行 docker compose up -d --no-deps caddy。保留 website 发布目录供后续排查。不要使用 docker compose down -v。

## 2026-10-19 标题与截图更新（v2）

- 当前版本：/opt/pressure-map-website/releases/20261019-website-v2。
- 上一版 releases/20261019-website-v1 保留，可通过 current 软链接回滚。
- 首屏使用内置 PressureMap Display 字体（Noto Serif SC 衍生，600 字重）；字集压缩后 17,864 字节，含 OFL 许可证和说明。
- 保留前两组中文文案，新增“把眼前的压力，变成从容有序的节奏。”，中英文均三组。
- 每组切换周期由 5.4 秒缩短为 3.4 秒；逐字延迟 35ms，单字入场 420ms，完整文案停留约 2 秒。
- 首页与课程截图来自 19:47 的最新 UI 验证目录 ui-44d0263110fc4ca2abccbe8e13941737，使用新的 v2 图片文件名避开旧缓存。
- HTML 的 CSS / JS 引用添加版本参数 20261019-2。
- 本地浏览器确认字体已真实加载、390px 手机无溢出；服务器字体下载 hash 与本地一致，API 健康检查正常。

## v3 首屏精简
已发布 20261019-website-v3：删除版本胶囊标签及播放/暂停按钮，保留自动轮播。旧版 v2 保留用于回滚。服务器验证删除项不存在、轮播代码与字体正常、API 健康检查通过。

## v4 勾选与压力来源交互
已发布 20261019-website-v4。待办示意使用原生可访问复选框、自绘对勾、140–240ms 可中断过渡与逐行完成线，支持减少动态效果。主圆环及桌面示意小圆环可打开压力来源面板，示例总分62，由课程26、任务22、截止14组成，非真实用户数据。支持中英文、键盘、Esc与遮罩关闭，手机暗色无溢出。服务器新组件存在检查和API健康检查通过；上一版v3保留。

## v5 统一图标
已发布 20261019-website-v5：字符箭头、下载箭头、清单勾号、预览窗口圆点和更多图标均替换为SVG或CSS图形，共14个SVG图标；移除易被系统渲染成彩色emoji的箭头字符，中英文检查通过。服务器新版图标与API健康检查正常；v4保留。

## 2026-10-20 UI动效与默认浅色发布

- 当前版本：/opt/pressure-map-website/releases/20261020-website-ui-light-v1。
- current已原子切换至该版本；上一版releases/20261019-website-v5完整保留。
- 默认浅色，读取手动保存的主题，不再跟随系统深色；存储不可用时浅色。资源版本20261020-2。
- 采用deploy/website-20261020-ui-light.tar.gz与deploy/update-ui-light.sh；切换前校验manifest，切换后比较首页、JS/CSS、WebP与字体内容，失败自动回滚。
- 公网Chromium/WebKit验证320/390/1440：浅色默认、主题记忆、语言、组件键盘移动/折叠、压力详情、FAQ、下载准备中提示正常，无溢出与控制台错误。
- 17个部署文件经公网下载计算SHA-256，与本地全部一致；API /api/health返回ok。没有重建API或Caddy。
- 线上地址：http://121.43.32.85/。结果保存在verification/public-release.json，Firefox环境限制仍见UI-REPORT.md。

## 2026-10-20 任务示意行留白修正

- 发布20261020-website-task-spacing-v1，上一版20261020-website-ui-light-v1保留；资源版本20261020-3。
- 任务示意每行左右padding各12px，复选框点击区同步对齐；“复习/作业/待提交”等右侧标签左收12px。
- Chromium/WebKit × 320/390/820/1440 × 中英共16组合通过：左右留白12px、任务与类型间隔≥10px、复选框可操作、无溢出与脚本错误。
- 部署前manifest校验、部署后线上HTML/CSS/JS与资源逐字节比较通过；API健康状态ok；未重启服务。
- 结果：verification/task-spacing-local.json 与 task-spacing-public.json；截图task-spacing-390-zh.png、task-spacing-820-en.png等。

## 2026-10-20 手机版上传到服务器

- 当前版本：`/opt/pressure-map-website/releases/20261020-mobile-v1`；上一版 `20261020-website-task-spacing-v1` 完整保留，可回滚。
- 手机版应用挂在站内 `/app/`（地址 http://121.43.32.85/app/ ），与官网同源，无需额外服务；页面与 `pressure-map-mobile/www/` 是同一份代码。
- Android 安装包放在 `/download/PressureMap-android-1.0.0.apk`（91,672 字节，sha256 `5ae5c954…26fe0f6`，Caddy 返回 `application/vnd.android.package-archive`）。站内只存一份，`/app/` 页面里的下载链接指向 `../download/…`。
- `dist/app.js` 的 `downloadUrls.android` 由空改成该路径，并补上 `releaseInfo.android`（版本/大小/日期/SHA-256）。卡片由脚本自动从 `<button disabled>` 变成 `<a download>`，文案自动变「下载 Android 版」，并逐字校验哈希与 `app.js` 声明一致。
- 下载区新增一行「不想安装？直接在浏览器里用手机版」链接到 `app/`，中文/英文两套文案都进了 i18n 字典（`不想安装？直接在浏览器里用手机版|No install needed — use it in your browser`）。
- 打包与部署：`deploy/pack-mobile-v1.py`（生成 tar + manifest.sha256）→ scp → `deploy/update-mobile-v1.sh`（解包校验 → 原子切换软链 → 线上逐字节 compare → API 健康检查，失败自动回滚）。
- 验收：`node deploy/check-mobile-live.mjs http://121.43.32.85`，结果写 `verification/mobile-v1.json`，截图 `verification/app-today-390.png`、`app-settings-390.png`、`download-1440-zh.png`、`download-1440-en.png`。覆盖：`/app/` 样式生效与 5 个 tab、安装卡片可见、APK 实际可取 91,672 字节、Android 卡片已成链接且无 pending、中英文文案。

## 2026-10-20 Windows 安装包发布

- 当前版本：`20261020-windows-v1`；上一版 `20261020-mobile-v1` 保留。
- 下载：`http://121.43.32.85/download/PressureMap-2.0.0-setup.exe`，59.6 MB，Windows x64，自包含运行时。
- SHA-256：`b4544bc8706dfc67483b75be30160a5251718213b86b7858cdb0ab484dfb87ed`。
- 从最新工作区源码运行 `dev.ps1 -Mode Package`；安装向导包含许可协议、安装位置和卸载功能。临时目录实际安装、自检、卸载退出码均为 0。
- 145 项核心检查、10 项网课检查通过。Check 最后的开发目录界面编译因正在运行的 PressureMap 锁文件失败；Package 的独立 win-x64 发布编译与自检成功。
- `deploy/update-windows-v1.sh` 复制当前网站后加入安装包与下载元信息，原子切换 current，失败回滚。服务器验证官网、Windows 安装包、手机版与 Android 包逐字节一致，API 健康正常。
- 公网完整下载 SHA-256 与本地产物一致；`check-mobile-live.mjs` 验证 Windows 卡片已为下载链接并展示版本、大小与校验值，中英文官网和手机版入口正常。
