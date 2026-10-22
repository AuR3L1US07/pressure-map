# PressureMap UI 与动效验收

日期：2026-10-20。修改目录：`E:/AIGC/pressure-map-website/dist`。此报告记录初始本地优化验收。后续已按用户要求设为默认浅色，并发布至 http://121.43.32.85/（20261020-website-ui-light-v1）。

保留纯静态架构、信息顺序、品牌和中文文案含义；没有运行时第三方依赖或CDN。英语文案做了等义精简。配置仍为空，不提供虚构下载链接。

## 验证结果

- Lighthouse移动端：性能 **91**、无障碍 **100**、最佳实践 **100**。
- Lighthouse CLS：**0**；TBT：**0ms**；LCP：约 **3.53s**。本地HTTP、Lighthouse默认移动端模拟，分数会随机器负载浮动。
- 独立4倍CPU追踪：CLS **0**；>50ms任务：72ms, 530ms。初始化与首轮布局仍未达到完全无长任务的目标；后续滚动段未记录到长任务。
- Chromium（本机Chrome）和Playwright WebKit：拖动、锁定、折叠、压力详情、FAQ、准备中提示、主题记忆、三种降级通过。Firefox下载成功，但启动出现Windows“应用程序的并行配置不正确”；未修改系统运行库。
- 320、390、820、1440px无横向溢出。中英切换无新增漏翻；保留的“中文 / English”是双语语言名称，软件截图仍为真实中文截图。
- 可见网页文字字号扫描无<12px项目；浅/深正文的自动对比度扫描无失败。选中语言滑块和玻璃组件由实际背景另行检查。
- 附加检查：键盘方向键/Home、Esc返回焦点、吸附内边距12px、FAQ五次快速反向、30px滚动保留详情/100px关闭详情；模拟发布配置验证链接、版本和SHA展开。

## 体积（明确区分两种口径）

| 文件 | 修改前 | 部署文件（优化后、未gzip） | 净增 | 可读源文件 |
|---|---:|---:|---:|---:|
| app.js | 19,215 B | 27,405 B | 8,190 B | 32,229 B |
| style.css | 26,010 B | 32,123 B | 6,113 B | 33,751 B |

部署文件净增落在8KiB JS / 6KiB CSS内，但这是经过词法/声明优化的文件大小。**如果按附件“未压缩”严格理解为未经minify的可读源码，预算尚未满足，Z-2因此标为部分。** 可读源保存在 `.tools/app.authored.js`、`.tools/style.authored.css`。部署只需dist，不需要安装工具或执行构建。

截图原PNG各96,438 / 88,502 B；裁切WebP为60,034 / 53,052 B，分别减少约38% / 40%。保留同尺寸PNG回退及原始PNG。

## 逐项验收

| 编号 | 状态 | 改动文件 | 备注或跳过原因 |
|---|---|---|---|
| B-1 | 完成 | style.css | 强主色和深色文字 token；浅/深两套正文对比度扫描无失败。玻璃组件另行检查，截图中的产品文字不属于网页文字。 |
| B-2 | 完成 | style.css | 可见文字最小12px；正文16px，手机介绍14px；trust换行。320px品牌保留可读名称。 |
| B-3 | 完成 | style.css, app.js | 中英标题字距分开；390/1440固定标题舞台，首句两行。 |
| B-4 | 完成 | index.html, style.css | 顶栏常驻下载入口，沿用翻译表。 |
| B-5 | 部分 | style.css | 清理确认失效规则、重复声明与非合成属性过渡；未做每阶段逐像素零差异证明，仍保留必要响应式覆盖。 |
| B-6 | 完成 | app.js | 移除旧下载弹窗和废弃文案条目；清除无用途的首屏/窗口栏翻译。 |
| B-7 | 完成 | index.html, assets | 扫描裁掉近黑边；1475×1026 WebP与同尺寸裁切PNG回退；原PNG保留。 |
| A-1 | 完成 | app.js, style.css | 单个共享observer，0.15阈值，底部-10%；一次入场，视口内直接显示。 |
| A-2 | 部分 | app.js, style.css | 滚动8px切换半透明模糊顶栏，固定高度；不连续插值背景/blur，遵守仅合成属性动画的硬约束。 |
| A-3 | 完成 | app.js, style.css | 单条3px导航线，以translate和scaleX改变位置与长度；语言和resize重算。 |
| A-4 | 完成 | app.js, style.css | 单滑块；main/footer淡出120ms再淡入160ms；记忆语言并读取浏览器默认语言。 |
| A-5 | 部分 | index.html, app.js, style.css | 默认浅色（后续用户要求）/持久化偏好、head提前应用、520ms圆形View Transition、截图降亮；图标240ms而非280ms。无接口时轻淡入，未采用颜色过渡以遵守硬约束。 |
| A-6 | 完成 | style.css | 按钮hover上浮、按下、焦点环；箭头按各自方向移动3px；仅精确hover设备启用悬停运动。 |
| A-7 | 跳过 | — | 可选滚动进度条未加入，保持安静的导航。 |
| A-8 | 完成 | app.js, style.css | 单条role=status toast；3200ms后隐藏；悬停/聚焦暂停；不抢焦点。 |
| H-1 | 完成 | app.js | 字符总时长≤900ms；副标题/操作/信任/截图分级；截图只变换位移。 |
| H-2 | 完成 | app.js | 三句一轮回第一句停止；首屏hover或焦点暂停；减少动画静态首句；ARIA名称稳定。 |
| H-3 | 跳过 | style.css | background-position动画与硬约束“只对transform/opacity/clip-path动画”冲突，保留静态渐变。 |
| H-4 | 完成 | index.html, style.css | 去掉重复窗口栏与三点，采用干净圆角截图框。 |
| F-1 | 完成 | app.js, style.css | 卡片上浮/边框反馈；rAF节流指针光斑，最高0.08透明度。 |
| F-2 | 部分 | app.js, style.css | 一次性对角错峰420ms，保留色阶、ARIA与原生title气泡（含示意数据）；缩放用.96→1遵守硬约束，未用.6起始。 |
| F-3 | 部分 | app.js, style.css | 任务划线与完成提示已实现；一次呼吸可交互取消。提示采用420ms和.96–1缩放，未加额外扩散圆环。 |
| F-4 | 完成 | app.js, style.css | rAF同步0→62数字与圆环，900ms；两位数字固定占位；ARIA保留最终62。JS曲线为三次ease-out近似。 |
| F-5 | 部分 | app.js, style.css | 开/退场、条增长和计数、Esc/外部关闭、80px滚动阈值均可用；入场160ms，未用180ms。 |
| D-1 | 完成 | app.js, style.css | 16/18秒慢漂移；同一observer暂停视口外动画；无observer及减少动画时静止。 |
| D-2 | 完成 | app.js | 玻璃/迷你组件24px入场，间隔120ms。 |
| D-3 | 部分 | app.js, style.css | 拖动capture、舞台12px限制、边缘吸附、方向键/Home/双击复位、锁定、grid折叠与ARIA均已验证；吸附240ms而非220ms。 |
| D-4 | 完成 | style.css | 迷你圆环保持详情交互，hover上浮2px。 |
| C-1 | 部分 | app.js | clip-path揭开与特性检测已实现；实际900ms，未用600ms或额外阴影淡入。 |
| C-2 | 完成 | app.js | 两条事实信息使用共享入场和错峰。 |
| Q-1 | 完成 | app.js, style.css | 采用硬约束明确允许的grid展开方案；快速连续点击测试通过；原生details/summary及键盘保留。 |
| Q-2 | 完成 | style.css | 两根CSS线替换字符加减号，展开隐藏竖线并旋转横线。 |
| Q-3 | 部分 | style.css | 文字160ms淡入淡出，未额外延迟60ms。 |
| L-1 | 完成 | app.js | 保留downloadUrls；相邻releaseInfo为三平台分别生成空version/size/sha256/date对象；空信息不渲染。 |
| L-2 | 完成 | index.html, app.js, style.css | 三平台统一准备中胶囊、可聚焦aria-disabled按钮和轻提示；无JS也有静态状态。 |
| L-3 | 部分 | app.js | 填写url后生成a download；元信息、可展开校验值、复制结果、1200ms下载启动文字已实现并测试；未添加勾选描边动画。 |
| L-4 | 完成 | app.js, style.css | 卡片入场错峰、hover与较柔和准备中视觉；三个平台同一逻辑。 |
| L-5 | 完成 | index.html, app.js, style.css | 旧下载模态框和对应代码移除；顶栏入口滚动到下载区。 |
| L-6 | 完成 | index.html, app.js | 增加中英运行环境说明；静态无JS也可见。 |
| W-1 | 部分 | app.js, style.css | Esc/cancel拦截并播放240ms退场后close；未做200ms ease-in及starting-style遮罩渐变。 |
| W-2 | 完成 | app.js | 链接30ms错峰，最多120ms。 |
| W-3 | 完成 | app.js | 遮罩/链接关闭，焦点回菜单；已验证Esc与返回焦点。 |
| Z-1 | 部分 | app.js, style.css | 减少动画初始状态、静态标题/热图/光球、最终分数、即时FAQ与吸附均已验证；中途改变偏好会取消WAAPI，已开始的rAF数字会在本轮结束。 |
| Z-2 | 部分 | app.js, style.css | 单observer与rAF输入；CLS0、Lighthouse性能≥90。4倍CPU首次初始化仍有长任务；未压缩可读源码预算仍超限，详见下面双口径体积。 |
| Z-3 | 完成 | index.html, app.js, style.css | 键盘、ARIA、焦点、状态提示、对比度检查完成；Lighthouse无障碍100。自动文字扫描排除产品截图、透明渐变及玻璃混合表面。 |
| Z-4 | 部分 | 验证文件 | Chromium与WebKit通过交互及三种降级条件；Firefox因本机Windows并行配置错误无法启动，未冒充通过。 |
| Z-5 | 部分 | 验证文件, backup | 最终中英/浅深/320–1440回归无溢出与控制台错误；每阶段文件快照齐全，但阶段1/2没有独立全矩阵截图和逐像素自动对比。 |

## 交付与复现

- `verification/index.html`：12组改前/改后全页截图（1440/820/390 × 中英 × 浅深）与拖动、吸附、折叠、详情、FAQ、主题三帧序列。
- `MOTION.md`：每条动效的触发、时长、曲线、减少动画和降级行为。
- `verification/lighthouse-mobile.report.html` / `.json`：完整Lighthouse报告。
- `verification/performance-trace.json`：可导入Chrome Performance的4倍CPU记录。
- `verification/checks.json`、`detail-checks.json`、`metrics.json`：检查结果。
- `backup/phase1` 至 `phase4`：因无Git，保存每阶段开始前的三文件快照。
- 本地启动：`python -m http.server 8765 --bind 127.0.0.1`，打开 `/dist/` 或 `/verification/`。
- `.tools` 内检查脚本用于本地QA，不随官网部署；没有向浏览器引入Playwright、Terser、PostCSS或其他库。

## 仍未完成

渐变平移、顶部颜色/模糊连续过渡因与硬约束冲突未实现；可选滚动进度条略过。部分精确时长采用统一token近似，完成提示未做扩散圆环，下载启动未做勾选描边，抽屉遮罩未做discrete过渡。首次初始化长任务、可读源码预算、Firefox环境验收和每阶段逐像素证明仍未满足。详见表中“部分/跳过”，没有将这些项目计为完成。

## 默认浅色与线上发布补充

- 首次访问默认浅色，不随系统深色设置改变；手动选择主题仍记忆。存储不可用、JS禁用时同样默认浅色。
- 20261020-website-ui-light-v1 已发布，CSS/JS缓存版本为20261020-2，上一版20261019-website-v5保留。
- Chromium与WebKit，320/390/1440px公网验证通过：默认浅色、语言、键盘移动、折叠、压力详情、FAQ、准备中提示、主题记忆，无横向溢出、无控制台错误。
- 17个部署文件的公网SHA-256与本地一致；API健康检查返回ok。API与Caddy均未重建。
- 结果：verification/default-light.json、verification/public-release.json；公网截图：verification/public-*-light.png。
