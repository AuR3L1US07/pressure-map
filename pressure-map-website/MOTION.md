# PressureMap 动效清单

所有交互均使用原生 CSS / WAAPI / Pointer Events；不依赖运行时库。默认曲线为 `cubic-bezier(.23,1,.32,1)`。减少动画在 CSS 和 JS 两层处理。具体差异见验收报告。

| 位置 | 触发条件 | 时长 | 缓动 | reduced-motion | 降级方式 |
|---|---|---|---|---|---|
| 首屏标题字符 | 初次显示、有限轮播 | 420ms + 自适应错峰，总计≤900ms | ease-out token | 第一条直接显示 | 无 WAAPI 时静态 |
| 首屏副标题、按钮、信任行 | 首次执行脚本 | 420ms，80ms 分级 | ease-out | 直接显示 | 静态 |
| 首屏截图框 | 首次执行脚本 | 420ms，240ms 延迟，仅 transform | ease-out | 静态 | 静态；从不隐藏 LCP 图 |
| 标题轮播 | 3400ms 阅读间隔 | 三句一轮后回第一句并停止 | ease-out | 不轮播 | 无 WAAPI 时不轮播 |
| 分区标题、功能卡、分栏、价值、FAQ、下载 | 共享 observer，15% 可见、底部 -10% | 420ms，70ms 错峰封顶 210ms | ease-out | 直接显示 | 无 observer / JS 时完整显示 |
| 导航指示条 | 滚动、语言变化、resize | 240ms | ease-out | 瞬时 | 原生锚点与高亮保留 |
| 语言滑块 | 点击中 / EN | 240ms | ease-out | 瞬时 | 文字仍可切换 |
| 页面语言内容 | 手动切换 | 淡出120ms / 淡入160ms | ease-out | 立即换文案 | 无 WAAPI 时直接替换 |
| 主题圆形揭示 | 点击主题按钮 | 520ms | ease-out | 立即切换 | 无 View Transition 时 240ms 轻微淡入；禁用颜色过渡 |
| 主题日月图标 | 主题改变 | 240ms | ease-out | 瞬时 | 图标状态保留 |
| 按钮与文字箭头 | 精确指针 hover | 160ms | ease-out | 无位移 | 即时颜色与焦点反馈 |
| 可点元素按下 | pointer / 键盘激活 | 80ms | ease-out | 瞬时 | 原生焦点与激活 |
| 功能卡片 | hover / pointermove | 240ms / 光斑160ms | ease-out | 无位移 | 无精确 hover 时不跟随 |
| 热图 | 首次进入视口 | 420ms + (行+列)×16ms | ease-out | 全部直接显示 | CSS 动画不可用时静态 |
| 任务复选框、划线 | 勾选 / 取消 | 160–240ms | ease-out | 瞬时完成 | 原生 checkbox 可用 |
| 第二项任务提示 | 首次进入视口；首次任务交互取消 | 420ms，缩放 .96→1→.96 | ease-out | 不提示 | 无 WAAPI 时省略 |
| 全部任务完成提示 | 三项全部勾选 | 240ms | ease-out | 直接显示 | role=status 仍更新 |
| 压力圆环与数字 | 首次进入视口 | 900ms | JS 三次 ease-out | 最终62 | 静态最终值；ARIA保持62 |
| 压力详情面板 | 点击圆环 / 关闭 / Esc / 外部点击 | 入场160ms / 退场120ms | ease-out / ease-in | 即时 | hidden 最终状态保留 |
| 压力来源条与数字 | 打开压力详情 | 条420ms，60ms 错峰；数字900ms | ease-out | 直接最终值 | 无 WAAPI 时条静态；数字 rAF |
| 桌面背景光球 | 舞台在视口内 | 16s / 18s，alternate | ease-in-out | 静态 | 无 observer 时保持静态 |
| 玻璃组件与迷你组件 | 舞台首次可见 | 420ms，间隔120ms | ease-out | 静态 | 无 observer / WAAPI 时静态 |
| 组件拖动 | 拖动柄 pointer 或方向键 | 与输入同步，每帧至多一次 | 无插值 | 保留主动拖动 | 不支持 capture 时仍可键盘移动 |
| 组件边缘吸附 | 松手距边缘≤20px | 240ms | ease-out | 瞬时吸附 | 无 WAAPI 时瞬时 |
| 锁定 / 折叠图标 | 控制按钮 | 160ms | ease-out | 瞬时 | ARIA状态保留 |
| 组件任务区折叠 | 折叠按钮 | 240ms，grid rows | ease-out | 瞬时 | 不支持 grid 插值时直接切换布局 |
| 迷你组件 | 精确指针 hover | 160ms，上移2px | ease-out | 无位移 | 可直接点击圆环 |
| 日历截图 | 首次可见 | 900ms，clip-path | ease-out | 完整显示 | CSS.supports 不通过则静态 |
| 日历事实条目 | 首次可见 | 420ms，70ms错峰 | ease-out | 完整显示 | 静态 |
| FAQ 内容 | summary 激活，可中途反向 | 240ms grid + 160ms opacity | ease-out | 原生即时展开 | 不支持 grid 时原生 details |
| FAQ 加减线 | 展开 / 收起 | 240ms | ease-out | 瞬时 | 原生 open 状态保留 |
| 下载卡片 | 首次可见 / 精确 hover | 420ms错峰 / 160ms | ease-out | 静态 | 准备中状态在无 JS 时可见 |
| 下载启动文案 | 已配置平台链接被点击 | 状态保留1200ms，无假进度 | 无 | 同样反馈 | 原生 a download |
| 底部 Toast | 准备中、复制结果 | 240ms；3200ms后隐藏 | ease-out | 瞬时出现消失 | role=status，无抢焦点 |
| 抽屉 | 打开 / 关闭或 Esc | 240ms | ease-out | 即时 | dialog 原生焦点管理 |
| 抽屉链接 | 抽屉打开 | 240ms + 30ms错峰，最多120ms | ease-out | 静态 | 无 WAAPI 时直接显示 |

仅滚动进度条、渐变位置平移未加入。背景、颜色、阴影、宽高不做连续动画；FAQ与组件的 grid 展开采用附件明确允许的例外。
