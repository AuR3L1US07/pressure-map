# 学期压力地图 · 云服务

独立的 C# / ASP.NET Core 10 服务端。整个文件夹可以单独复制到服务器，不依赖桌面软件目录。当前线上实例为 `https://121.43.32.85/api`，部署细节见 DEPLOY.md。

## 已实现

- 账号密码登录、退出、查看当前账号、修改密码；管理员创建账号、重置密码和禁用账号。
- 密码使用 ASP.NET Identity 的 PBKDF2 加盐哈希；数据库只保存会话令牌的 SHA-256 摘要。会话默认 7 天，修改密码、重置密码或禁用账号后失效。
- 用户数据隔离，SQLite 持久化。同步学期、课程、课程任务、桌面待办（含子任务、截止时间、提交状态）、导入的 ICS/CSV 课表原文。
- 多设备通过同一账号上传和下载完整数据快照。上传必须携带当前版本号；并发修改返回 409，避免直接覆盖。保留最近 5 个历史快照。
- 数据格式、大小、数量检查；登录 IP 限流与账号连续失败锁定；在线数据库备份。
- Docker Compose、Caddy HTTPS 入口和本地自动验证。

**交付边界：**服务端可独立部署，桌面客户端已接入首次同步、30 秒轮询、离线重试、冲突预览选择和事务恢复。已在本地 HTTPS 验证两台真实客户端的完整同步引擎；公网实例已上线（`https://121.43.32.85/api`，暂用自签证书，见 DEPLOY.md）。同步以整个账号快照为单位，双方都有修改时备份双方并由用户选择，不做字段级自动合并。压力分数由数据重新计算，窗口位置、玻璃和通知投递状态不在快照中。

## 目录

- `src/PressureMap.Server`：API、账号管理、SQLite 存储、数据校验。
- `tests/PressureMap.Server.Checks`：真实 Kestrel HTTPS 集成检查。
- `Dockerfile` / `compose.yaml`：服务端及 HTTPS 代理。
- `deploy/Caddyfile`：公网 `/api/` 路由。
- `API.md`：请求格式、版本同步流程和错误码。
- `verify.ps1`：Windows 一键编译与验证。

## 本地验证

需要 .NET 10 SDK。在本目录运行：

```powershell
dotnet build PressureMap.Server.slnx -m:1
dotnet run --project tests/PressureMap.Server.Checks --no-build
```

也可 `./verify.ps1`。本机 SDK 位于 `E:/AIGC/.tools/dotnet/dotnet.exe`，脚本会自动识别。若当前环境无法访问 NuGet 安全公告，可临时 `./verify.ps1 -SkipAudit`；这仅跳过漏洞公告联网查询，不能视为通过漏洞审计。联网部署前应正常执行包审计。

测试使用随机密码、自签名证书（仅测试 HTTP 客户端固定信任该证书），不修改系统信任库、不访问真实用户数据库。测试数据库存放于 `.test-temp/随机目录`，不得上传到服务器。本次在 Windows 本地通过 **40 项检查**；独立复制后没有桌面 Core 项目时跳过额外的桌面客户端兼容检查。已在服务器完成 Docker 镜像构建和注册接口部署，公网三端同步验收通过。

开发调试可以用只绑定本机的 HTTP：

```powershell
$env:Cloud__DataDirectory = "$PWD/data"
$env:Cloud__AllowInsecureLoopback = 'true'
dotnet run --project src/PressureMap.Server -- --urls http://127.0.0.1:5080
```

此开关仅放行回环请求。生产配置默认关闭；现有桌面客户端只接受 HTTPS。不要使用此 HTTP 调试地址作为桌面账号服务地址。

## 部署：Docker + HTTPS

当前线上实例（阿里云 ECS，暂无域名）的实际配置、自签证书和运维命令见 DEPLOY.md。下面是通用步骤。

准备 Linux 服务器、Docker Compose、指向服务器的域名及开放的 80/443 端口。若服务器网络与 `172.30.88.0/24` 冲突，同时修改 Compose 子网、两个静态地址和 `Cloud__KnownProxies__0`。

1. 上传此文件夹，排除 `bin`、`obj`、`.test-temp`、本地 `data`、`.env`、证书私钥。
2. 复制 `.env.example` 为 `.env`，把 `DOMAIN` 改成实际域名。
3. 执行 `docker compose up -d --build`。
4. 创建账号（密码交互输入，不写入命令历史）：

```sh
docker compose exec api dotnet PressureMap.Server.dll user-create --account student@example.com --name 学生
docker compose exec api dotnet PressureMap.Server.dll user-password --account student@example.com
docker compose exec api dotnet PressureMap.Server.dll user-disable --account student@example.com
```

密码为 12～128 字符。没有默认账号或默认密码；用户可通过软件内注册或 `POST /v1/auth/register` 创建账号，管理员也可创建。账号不区分大小写，支持字母、数字和 `@._+-`，长度 3～128。

最终 API 基地址：`https://实际域名/api`。Caddy 自动申请和续期证书。API 容器不映射公网端口，仅 Caddy 对外提供服务；代理信任限制为配置的 Caddy 地址。不要把数据库卷放在 Web 静态文件目录。

没有域名时用 IP 模式：把 `deploy/Caddyfile.ip` 覆盖到 `deploy/Caddyfile`，自签证书放到 `deploy/certs/server.crt` 与 `server.key`（SAN 必须是该 IP），`.env` 的 `DOMAIN` 填 IP。此时不要用 `tls internal`：客户端连接裸 IP 时不发送 SNI，Caddy 选不出证书，握手会报 `no certificate available`。默认 IP 的证书指纹已内置客户端；自建地址需使用系统信任的证书。

桌面软件的服务地址环境变量是：

```powershell
$env:PRESSURE_MAP_API_URL = 'https://实际域名/api'
```

执行后从同一个终端启动软件。也可直接在桌面软件“数据与备份 → 账号与云同步”保存此地址。

## 备份和恢复

数据在 Docker 命名卷 `cloud-data` 内的 `/data/pressure-cloud.db`；更新容器不会清空。不要执行 `docker compose down -v`。

在线备份示例（每次使用新的文件名）：

```sh
docker compose exec api dotnet PressureMap.Server.dll backup --output /data/backup-2026-10-19.db
docker compose cp api:/data/backup-2026-10-19.db ./backup-2026-10-19.db
```

备份包含账号哈希、当前会话、快照和历史记录，应限制访问并在服务器之外保存。备份是 SQLite Backup API 生成的一致性副本，不要只复制正在写入的主 `.db` 而漏掉 WAL 文件。

恢复应在停服后操作：先保留现有整个数据卷副本，再用确认过的备份替换数据库；妥善处理旧 WAL/SHM，恢复目录必须对容器 app 用户可写。重启后验证账号及数据再开放流量。普通数据冲突优先在客户端处理，数据库恢复会影响全部账号。

## 配置

.NET 环境变量使用双下划线，例如 `Cloud__SessionHours=168`。

| 配置 | 默认 | 含义 |
|---|---|---|
| `Cloud__DataDirectory` | 程序目录下 data | 数据库存放目录，容器配置为 /data |
| `Cloud__SessionHours` | 168 | 会话期限，1～720 小时 |
| `Cloud__HistoryLimit` | 5 | 每个用户保留的历史快照数，1～20 |
| `Cloud__MaxSnapshotBytes` | 67108864 | 单次快照上限，最大 64 MiB |
| `Cloud__KnownProxies__0` | 无额外代理 | 可信反向代理 IP |
| `Cloud__AllowInsecureLoopback` | false | 仅本机 HTTP 调试 |

当前面向单台服务器、单个 API 实例；扩容多实例应先迁移数据库和全局限流。HTTPS 保护传输，服务器磁盘上的 SQLite 文件并未额外应用层加密，应通过服务器权限、备份权限和磁盘加密保护。
