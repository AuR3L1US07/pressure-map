# 学期压力地图 · PressureMap

桌面应用、移动应用、服务器和官网的统一源码仓库。

| 目录 | 内容 |
| --- | --- |
| [pressure-map](pressure-map/) | C# / .NET 10 / Avalonia 桌面应用 |
| [pressure-map-mobile](pressure-map-mobile/) | Android / iOS 壳工程与离线网页应用 |
| [pressure-map-server](pressure-map-server/) | 服务端源码、API 文档、Docker 部署配置 |
| [pressure-map-website](pressure-map-website/) | 官网静态页面、资源及部署脚本 |

## 开发

各项目的环境要求、运行及部署方式请参阅对应目录的 README.md。
桌面端可在其目录运行 `run.bat`；服务端部署参阅 `pressure-map-server/DEPLOY.md`。
官网可在其目录执行 `python -m http.server 8765 --bind 127.0.0.1`，访问 `/dist/`。
官网可读 JS/CSS 源码额外保存在 `pressure-map-website/source/`，部署文件保存在 `dist/`。

## 仓库内容

此仓库保存 2026-10-22 本地工作区源码快照，包含尚未提交的代码修改，不携带原有 Git 历史。
安装包、构建缓存、本地数据库、账户数据和私密配置不纳入版本控制。
服务端环境配置请从 `.env.example` 开始，在本地配置实际值。
