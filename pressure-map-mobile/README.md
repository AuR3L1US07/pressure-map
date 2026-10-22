# PressureMap 手机版（Android / iOS）

PC 端（`../pressure-map`，Avalonia）的移动版：课表、待办、学期压力预警。
**完全离线**——页面和资源都打包在安装包内，Android 版连 `INTERNET` 权限都没申请。

- **自建服务器地址**：<http://121.43.32.85/app/> —— 挂在官网同一台机器（Caddy 静态目录 `current/app/`），安装包在 <http://121.43.32.85/download/PressureMap-android-1.0.0.apk>，官网下载区的 Android 卡片已直接指向它。
- **在线地址**：<https://pressure-map.app.workbuddy.host/?sharecode=LO7TEIQEZ2> —— 手机浏览器直接打开就能用（iPhone 添加到主屏幕即全屏运行），设置页底部可下载 Android 安装包。
- **Android 安装包**：`dist/PressureMap-android-1.0.0.apk`

```
pressure-map-mobile/
├─ www/                     移动端页面（唯一需要维护的前端，纯 HTML/CSS/JS，无构建步骤）
│  ├─ index.html  app.css  app.js
│  ├─ manifest.webmanifest
│  └─ icons/                应用图标（脚本生成）
├─ site/                    发布用的静态站点（www + 安装包，由 build-site.sh 生成）
├─ android/                 Android 壳工程（原生 WebView，不用 Gradle）
│  ├─ AndroidManifest.xml
│  ├─ src/…/MainActivity.java
│  └─ res/                  图标、启动图、主题
├─ ios/                     iOS 壳工程（WKWebView，Xcode 工程）
│  ├─ PressureMap.xcodeproj
│  ├─ PressureMap/          AppDelegate / ViewController / Info.plist / Assets
│  ├─ ExportOptions.plist
│  └─ build-ios.sh
├─ scripts/
│  ├─ fetch-toolchain.sh    下载 JDK + Android SDK（只需一次）
│  ├─ build-android.sh      产出 APK
│  ├─ build-site.sh         组装可发布的 site/ 目录
│  ├─ prepare-ios.sh        同步 www 到 iOS 工程
│  ├─ make-icons.py         生成各尺寸图标（纯标准库）
│  ├─ qa-capture.mjs        手机尺寸截图 + 控制台异常检查
│  └─ qa-live.mjs           线上链接冒烟检查
├─ dist/                    安装包产物
└─ docs/                    INSTALL.md、截图
```

## 一、Android：已有安装包

`dist/PressureMap-android-1.0.0.apk`（约 92KB，已签名，minSdk 26 = Android 8.0）。

装法：传到手机 → 点开 → 允许「安装未知来源应用」。详见 `docs/INSTALL.md`。

重新构建（改完 `www/` 后）：

```bash
bash scripts/fetch-toolchain.sh   # 首次：下载 JDK 21 + Android SDK（约 300MB，走腾讯云镜像）
bash scripts/build-android.sh     # 产出 dist/PressureMap-android-1.0.0.apk
```

构建链是 `aapt2 → javac → d8 → zipalign → apksigner`，**不依赖 Gradle / Android Studio**，所以整个仓库没有 `build.gradle`，也不需要联网拉依赖。

## 二、iOS：需要一台 Mac

IPA 只能在 macOS + Xcode 上产出（Apple 的签名限制，Windows 无法绕过）。工程已经写好：

```bash
bash scripts/prepare-ios.sh          # 同步页面（build-ios.sh 也会自动跑）
open ios/PressureMap.xcodeproj       # 或直接：
bash ios/build-ios.sh                # 自动签名 → ios/build/ipa/PressureMap.ipa
```

在 Xcode 里只需做一件事：选中 target → Signing & Capabilities → 填自己的 Team（用 Apple ID 登录即有个人团队，免费）。之后连上 iPhone 点 Run 就能装。

- 免费 Apple ID 签的应用 **7 天过期**，到期在 Xcode 里重装一次即可；付费开发者帐号有效期 1 年。
- 想不经过电脑直接装：把 `method` 改成 `ad-hoc`（需在开发者后台登记设备 UDID）或 `app-store` 上传 TestFlight。
- 没有 Mac 的临时方案：见 `docs/INSTALL.md` 的「PWA 添加到主屏幕」。

## 三、功能（桌面端去掉悬浮窗之后的部分）

| 页面 | 内容 |
| --- | --- |
| 今日 | 本周压力环、今日课程、今天要做、最近截止 |
| 课表 | 周条切换、当天课程列表、增删改（课程可关联到压力分析） |
| 待办 | 未完成/今天/已完成/全部筛选、截止倒计时、重要标记、需提交、子任务计数、勾选完成 |
| 压力 | 学期压力柱状图（阈值虚线、预警配色）、本周各课程压力、按可用时间倒推的开工建议 |
| 设置 | 学期参数、主题（跟随系统/浅色/深色）、课表起始日、导出/导入 JSON 备份、示例数据、清空 |

没有做的：桌面悬浮组件、Windows 云同步、在线课程抓取、AI 解析（这些依赖桌面端或服务器，见 `../pressure-map` 与 `../pressure-map-server`）。

### 数据与算法

- 数据只存在手机本地（`localStorage`，键 `pressuremap.mobile.v1`）。卸载即清空，换机用「设置 → 导出备份」。
- 压力算法与桌面端 `PressureMap.Core/Rules.cs` 一致：任务权重（考试 5 / 大作业 4 / 报告 3 / 测验 2 / 其他 2）在截止周按 1.0 / 0.6 / 0.3 衰减计入前两周；阈值 1.4 倍为红色预警。
- 开工建议：每周可用时间 = 每周可用小时 × 0.4，压力预警的周减半，从截止周往前倒推分配。

## 四、开发与验证

```bash
# 本地预览（浏览器 390×844 就是手机效果）
python -m http.server 8799 --directory www

# 五个页面的浅色/深色截图 + JS 异常检查，输出到 docs/screenshots/
node scripts/qa-capture.mjs
```

改界面只动 `www/`，改完跑 `build-android.sh` 重新出包；iOS 侧 `build-ios.sh` 会自动同步 `www/`。

### 网页版：重新发布

```bash
bash scripts/build-site.sh        # 组装 site/ = www/ 全量 + dist 里最新的 APK
# 再把 site/ 目录发布出去（已发布的地址见文首，重新发布会覆盖同一个链接）

node scripts/qa-live.mjs "https://pressure-map.app.workbuddy.host/?sharecode=LO7TEIQEZ2"
# 线上冒烟：CSS/JS 是否真的加载、APK 能否下载、安装入口是否显示、有无 JS 异常
```

### 发到自建服务器（121.43.32.85）

```bash
bash scripts/build-site.sh                                  # 1. 先出 site/
python ../pressure-map-website/deploy/pack-mobile-v1.py     # 2. 打包成官网新版本（含 app/ 与 download/）
scp -i ~/.ssh/codex_upload ../pressure-map-website/deploy/website-*.tar.gz root@121.43.32.85:/tmp/
scp -i ~/.ssh/codex_upload ../pressure-map-website/deploy/update-mobile-v1.sh root@121.43.32.85:/tmp/
ssh -i ~/.ssh/codex_upload root@121.43.32.85 'sh /tmp/update-mobile-v1.sh'   # 3. 原子切换 + 自校验 + 失败回滚
node ../pressure-map-website/deploy/check-mobile-live.mjs http://121.43.32.85 # 4. 公网验收
```

服务器上手机版在 `current/app/`（即 `/app/`），安装包在 `current/download/`，页面里的下载链接写的是 `../download/…`，站内只存一份包。发布细节见 `../pressure-map-website/DEPLOY.md`。

网页版和安装包共用同一份 `www/`，区别只有两处：原生壳里会隐藏「装到手机上」这张卡片（靠 `AndroidHost` / `webkit.messageHandlers` 判断），关于一栏显示「Android / iOS」而不是「网页版」。

> 部署域名要求每个请求都带 `sharecode`，首次访问会下发 Cookie，之后子资源正常加载；只有 `manifest.webmanifest` 因为浏览器不带 Cookie 而 403，仅影响「安装到桌面」的图标清单，页面功能不受影响。

## 五、已知限制

- APK 用自签名密钥（`android/keystore/`），适合自用/内部分发；上架 Google Play 需换正式密钥并调高 targetSdk（当前 34，Play 新应用要求 35+）。
- iOS 工程未在本机构建验证（Windows 无 Xcode），首次在 Mac 上打开若提示签名相关错误，按 Xcode 提示选 Team 即可。
- 到点提醒、在线课程抓取、教务 PDF 导入、中英切换已补齐（见 2.2 一节），其中在线课程抓取需要真实账号做一次端到端确认。

## 2.0：与桌面版功能对照

2.0 把手机版的数据结构改成与桌面版**同一份快照格式**（`schema_version/semesters/desktop_todos/course_calendars`），
所以云同步、备份导入导出、多学期都能和电脑端互通。

| 功能 | 桌面版 | 手机版 2.0 |
|---|---|---|
| 今日 / 课表 / 待办 / 压力 | ✅ | ✅ |
| 多学期管理（新建、切换、删除） | ✅ | ✅ |
| 压力算法（权重、衰减、阈值、倒推开工） | ✅ | ✅ 逐条移植 |
| 课表导入（ICS / CSV，含 RRULE、EXDATE） | ✅ | ✅ |
| 备份导出 / 导入（与桌面版通用） | ✅ | ✅ |
| 云账号：注册、登录、退出、多账号切换 | ✅ | ✅ |
| 云同步：上传 / 下载 / 冲突二选一 | ✅ | ✅ |
| AI 助手（OpenAI 兼容接口） | ✅ | ✅ |
| 待办细节：计划日期、类型、重复、提醒、子任务 | ✅ | ✅ |
| 在线课程抓取（超星 / 中国大学 MOOC） | ✅ Playwright 多标签全量扫描 | ✅ 会话式只读抓取（提取脚本经合成页面验证，线上结构待真实账号确认） |
| 教务 PDF 课表导入（杭州电子科技大学） | ✅ PdfPig | ✅ pdf.js + 同版式移植（与真实 ICS 156/156 一致） |
| 界面语言切换（中 / 英） | ✅ | ✅ 渲染后替换，英文模式零残留中文 |
| 到点通知提醒 | ✅ 托盘提醒 | ✅ 系统通知（提前 24h / 1h / 到期） |
| 桌面悬浮玻璃组件 | ✅ | — 手机上不存在这个概念 |

UI 上用底部第 5 个 tab「AI」承载 AI 助手；云账号、学期管理、AI 接口、课表来源都在「设置」页。

### 回归脚本

```bash
node scripts/qa-data.mjs     # 快照转换/哈希、ICS/CSV/RRULE/EXDATE（用真实备份与真实课表）
node scripts/qa-sync.mjs     # 云同步全场景（本地模拟服务端，不碰生产数据）
node scripts/qa-ai.mjs       # AI 请求体与桌面版同构、错误提示
node scripts/qa-capture.mjs  # 390×844 手机模拟截图 + JS 异常检查
```

`qa-data.mjs` 会读 `%APPDATA%\学期压力地图\backups` 里最新的桌面备份和 `pressure-map/output/*.ics` 做对照，
没有这两样时自动跳过对应项。

`qa-capture.mjs` 默认连 `http://127.0.0.1:8799`，要先把**组装后的站点**喂给它（`www/` 里没有 APK，
直接对着 `www/` 跑会报「安装包链接不可用」）：

```bash
bash scripts/build-site.sh
python -m http.server 8799 --directory site --bind 127.0.0.1 &
node scripts/qa-capture.mjs
```

## 2.1：修掉「装到手机上就点不动」的三个坑

2.0 的 APK 在浏览器里跑得好好的，装成 APK 之后有几处会**静默失效**，因为 WebView 和 Chrome 的行为并不一样：

- **`confirm()` 在 WebView 里不会弹窗，直接返回 false。** 全站 11 处 `confirm`（删除待办 / 删除课程 / 删除学期 /
  删除课表 / 退出登录 / 清理缓存 / 载入示例 / 清空全部）在装机后全部变成「点了没反应」。
  2.1 在 `MainActivity` 里接管了 `onJsAlert` / `onJsConfirm` / `onJsPrompt`，用系统对话框实现，
  并用一个 `Settle` 对象保证 `JsResult` **只被答复一次**（按钮回调、Dialog 被 dismiss、Activity 销毁三条路径都会碰它）。
  返回键、Activity 销毁时都会走 `cancel()`，不会把网页卡在等待回调上。
- **`setAllowContentAccess(false)` 会让 `<input type=file>` 的读取失败。** 选完文件拿到的是 `content://` URI，
  关掉这个开关后 `FileReader` 读不到内容 → 课表导入和备份导入都是「选了文件没反应」。2.1 改为 `true`
  （`setAllowFileAccess(false)` 保留，仍然禁止网页导航到 `file://`）。
- **`FileChooserParams.createIntent()` 会把 `accept=".ics,.csv"` 翻译成 `text/calendar` 之类的 MIME 列表**，
  很多文件管理器映射不上，`.ics` 会直接置灰选不了。2.1 自己构造 `ACTION_GET_CONTENT` + `*/*`，
  扩展名交给网页自己校验。

版本号：`versionCode=3` / `versionName=2.1.0`，APK `dist/PressureMap-android-2.1.0.apk`（124,781 字节）。
线上地址：`https://121.43.32.85/download/PressureMap-android-2.1.0.apk`。

## 2.2：补齐最后四块

2.1 让 App 能正常用，2.2 把和桌面版的最后四项差距补上。每一项的**验证方式**都写在下面，
因为其中有一项在本机没法端到端验证，得说清楚。

### 1. 到点提醒（`www/pm-remind.js` + `Reminders.java`）

桌面是「轮询 + deliveredKey 去重」，手机没有常驻轮询，改成**提前把三个时间点排进系统闹钟**：
截止前 24 小时、1 小时、到期。规则与桌面 `ReminderRules.Due` 逐条一致
（已完成 / 关提醒 / 无截止 → 不排；没填时间按 23:59；已完成待提交的文案不同）。

- 原生：`AlarmManager.setExactAndAllowWhileIdle`，被拒时退化 `setAndAllowWhileIdle`；
  `ReminderReceiver` 弹通知；`BootReceiver` 在重启 / 覆盖安装后按上次计划重排。
- 权限：`POST_NOTIFICATIONS`（Android 13+，第一次真正排上提醒时才问）、
  `RECEIVE_BOOT_COMPLETED`、`USE_EXACT_ALARM`。
- 设置页有「发一条测试提醒」，5 秒后响，用来确认权限没问题。
- 验证：`node scripts/qa-remind.mjs` —— 35 项，覆盖阶段判定、key 格式、文案、
  去重、只排未来时间点、总开关、脏数据。

### 2. 中英切换（`www/pm-i18n.js`）

没有把上千处字符串改成 `T("…")` 调用，而是**渲染后按文本节点替换**：
整串命中字典就换，动态片段交给正则规则。中文模式行为完全不变，用户自己写的
待办标题 / 课程名不在字典里，不会被翻译。

- 切回中文时用节点上缓存的原文还原（静态标签不在 `render()` 的重画范围内，不缓存就复原不了）。
- `window.confirm` / `alert` 包了一层，原生对话框文案也会跟着语言走。
- 验证：`node scripts/qa-i18n.mjs` —— 真跑界面，切到英文后扫描 6 个页面 + 弹层的
  所有文本节点和 placeholder/title，**要求零残留中文**；再切回中文断言完整复原。

### 3. 教务 PDF 课表导入（`www/pm-hdu.js` + `vendor/pdf.js`）

桌面用 PdfPig 取字形坐标，这里换成 pdf.js 的 `getTextContent`，按同样的几何算法还原：
表头算出七列中心与列宽 → 每列按 Y 分行、按 X 拼字 → 正则拆出课程名 / 节次 / 周次 /
校区 / 场地 / 教师 → 套用校历的停课与调课 → 输出与桌面同格式的 ICS。

- 内置 pdf.js 3.11（320 KB + 1.1 MB worker），完全离线，不走网络。
- 验证：`node scripts/qa-hdu.mjs` —— 手边没有原始教务 PDF，就用
  `scripts/make-hdu-pdf.py` **从真实 ICS 反推一份同版式 PDF**（156 条事件 → 15 个课程格），
  再让解析器读回来，要求生成的 ICS 与原始 ICS 的 `(课程, 开始, 结束, 地点)` 集合**完全相等**。
  23 项全过，包含周次表达式、单双周、调课搬日、以及真实 UI 的「选文件 → 预览 → 确认导入」全流程。

### 4. 在线课程抓取（`www/pm-online.js` + 内嵌浏览窗口）

**这块和桌面版不是同一种实现，必须说明。** 桌面用 Playwright 开真 Chrome，
可以同时开多个标签、遍历 iframe、一路点「章节 → 学习记录 → 作业」，所以能读到
每门课的「任务点 x/y」。Android WebView **没有 iframe 枚举 API、也没有多标签**，
同一条路子搬不过来。

手机上的做法是「会话式只读抓取」：

1. 设置页点「读取超星学习通 / 读取中国大学 MOOC」，App 打开内嵌浏览窗口；
2. 你自己在里面登录（**密码不落盘**，比桌面存账号更安全）；
3. 点顶部「读取这一页」，App 往当前页注入提取脚本，脚本把课程与未完成作业回传；
4. 作业合并进待办，`sourceKey = "chaoxing:…"`，重复同步只更新不新增。

- 原生：`MainActivity` 里的覆盖层 WebView + `CookieManager` 第三方 Cookie（登录态必须靠它），
  结果通过 `AndroidHost.scrapeResult` 回传，`JSONObject.quote` 转义后交给主页面。
- 验证：`node scripts/qa-online.mjs` —— 25 项，用照搬桌面选择器的合成页面验证
  提取脚本（课程名、任务点进度、作业列表）、时间解析、合并去重、字段完整性。
- **未验证**：线上站点结构。合成页面只能证明「脚本对这套 DOM 有效」，不能证明
  学习通 / MOOC 没改版。装好 App 后用真实账号点一次就能确认；若返回 0 条，
  多半是选择器过期，改 `pm-online.js` 里 `adapters.*.script` 即可。

### 回归脚本

```bash
node scripts/qa-remind.mjs   # 到点提醒规则（与桌面 ReminderRules 对齐，35 项）
node scripts/qa-i18n.mjs     # 中英切换：英文零残留 + 切回中文复原
node scripts/qa-hdu.mjs      # 教务 PDF：合成 PDF ⇄ 真实 ICS 逐条比对（23 项）
node scripts/qa-online.mjs   # 在线课程：提取脚本 + 待办合并（25 项）
```

## 2.3：界面图标与桌面端同源
之前手机版有一套自己画的线条图标，形状和桌面版对不上。2.3 把图标几何**全部换成桌面端的路径**，
不再有第二套造型。
- 来源：桌面端界面图标其实有两套 —— `Styles/Icons.axaml` 里的 Material Design Icons（只用于品牌圆环和
  GitHub 图标），以及 `Views/IconView.cs` 里那套 24×24、线宽 1.75 的自绘线条几何（侧栏导航实际用的就是它）。
  手机版对齐的是**后者**，因为桌面用户看到的 90% 图标来自它。
- 对应关系：今日 = `Home`、课表 = `Calendar`、待办 = `Todo`、压力 = 桌面环形压力圈的同心圆、
  AI = `GPT`、设置 = `Settings`、新增 = `Plus`、删除 = `Delete`、箭头 = `Chevron`、重要 = `Star`。
  顶栏还补上了桌面的品牌圆环（`Icon.Donut`，`fill-rule="evenodd"` 挖空）。
- 勾选框踩到一个坑：原来 `#i-check` 自己就带一个方框，而 `.tick` 又画了一层圆角框，换几何后会出现
  **框中框**。改成 `.tick` 只画外框、图标只画勾。
- 另有 5 个桌面端没有对应物的图标保留自绘（主题的太阳/月亮、弹层关闭的 ×、地点针），
  语义上都属于「桌面端用原生控件、没有图片标」的场景。
- 回归：`qa-capture.mjs` 新增两项断言 —— 底部导航图标必须是这 6 个 id，
  `.tick` 内部的图标必须小于外框（防框中框复发），并多出一张 `light-todos-done.png` 截图。
