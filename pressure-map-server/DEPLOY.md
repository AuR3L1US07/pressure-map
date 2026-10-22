# 部署记录 · 阿里云 ECS

## 2026-10-21：共享 AI 服务

已部署 `/api/ai/plan`，默认 `deepseek-v4-1-flash-261017`，游客可用。`ARK_API_KEY` 仅保存在服务器权限 600 的 `.env`，经 Compose 注入 API 容器；不放入源码或桌面包。`ARK_MODEL` 配置模型，`AI_DAILY_LIMIT` 默认 500 次，按 UTC 日期计算，进程重启后重置。每 IP 每分钟 6 次、并发上限 2 次，单次输入最多 24,000 字符、输出最多 2,048 tokens。更新前源码、Compose 和私有环境文件备份在服务器 `deploy/backups/ai-*`，不要公开该目录。已通过无密钥、未登录的实际桌面 API 调用。

已上线。桌面客户端地址：`https://121.43.32.85/api`

## 服务器

| 项 | 值 |
|---|---|
| 公网 IP | `121.43.32.85` |
| 主机名 | `iZbp1agqtkylra6jstzxkjZ` |
| 系统 | Ubuntu 24.04.2 LTS / x86_64 |
| 规格 | 2 核 / 1.6 GB / 40 GB |
| 部署目录 | `/opt/pressure-map-server` |
| 登录 | `ssh -i ~/.ssh/codex_upload root@121.43.32.85` |

登录靠公钥，`codex-upload` 已写入 `root` 的 `authorized_keys`。

## 已做的系统改动

- 安装 `docker.io 29.1.3` + `docker-compose-v2 2.40.3`，`systemctl enable docker`。
- 新增 4 GB swap（`/.swapfile`，已写 `fstab`）。1.6 GB 内存编译 .NET 会 OOM，不要删。
- Docker 加速：`/etc/docker/daemon.json` 配了 `registry-mirrors`（daocloud / 1ms / xuanyuan）。Docker Hub 直连超时，`mcr.microsoft.com` 可直连。
- 日志轮转：`json-file`，单文件 20 MB × 3。

## 服务形态

`docker compose`（项目原生方案），两个容器：

- `pressure-map-server-api-1`：.NET 10，监听容器内 `8080`，**不映射公网端口**。
- `pressure-map-server-caddy-1`：对外 `80/443`，`/api/*` 反代到 `api:8080`。

数据在命名卷 `pressure-map-server_cloud-data`，容器内路径 `/data/pressure-cloud.db`。**切勿 `docker compose down -v`。**

## HTTPS 现状（无域名）

没有域名，Let's Encrypt 无法签发，因此用的是 IP 模式：

- `deploy/Caddyfile` 来自 `deploy/Caddyfile.ip`：`:443` 直接挂载 `deploy/certs/server.crt|key` 作为默认证书。
- 自签证书 SAN=`IP:121.43.32.85`，有效期到 2029-01-21。
- 不能改回 `tls internal`：Caddy 内部 CA 依赖 SNI 选证书，而 curl / .NET 连裸 IP 时不发 SNI，握手会报 `no certificate available`。当前写法不依赖 SNI。

旧版客户端必须信任该证书。新版客户端对默认 IP 内置精确证书指纹、主机名和有效期校验，无需安装证书。证书已下载到本地 `dist/pressure-map-ip.crt`。

**信任方式（Windows，管理员 PowerShell）：**

```powershell
Import-Certificate -FilePath "E:\AIGC\pressure-map-server\dist\pressure-map-ip.crt" -CertStoreLocation Cert:\LocalMachine\Root
```

或用图形界面：双击 crt → 安装证书 → 本地计算机 → 受信任的根证书颁发机构。装完重开客户端。

**有域名后切回正式证书**：把 `deploy/Caddyfile` 换成仓库里的原版（`{$DOMAIN}` + 自动 HTTPS），`.env` 里 `DOMAIN=你的域名`，再 `docker compose up -d caddy`。

## 安全与备份（2026-10-19 加固）

- SSH：`/etc/ssh/sshd_config.d/99-hardening.conf` — 关闭密码登录、`PermitRootLogin prohibit-password`、`MaxAuthTries 3`。只接受 `codex-upload` 密钥，改配置前先确认密钥可用，否则会把自己锁在外面。
- fail2ban：`/etc/fail2ban/jail.d/sshd.local`，10 分钟内 5 次失败封禁 1 小时，`fail2ban-client status sshd` 查看。
- 自动安全更新：`unattended-upgrades` 已启用（`/etc/apt/apt.conf.d/20auto-upgrades`），不自动重启。
- 每日备份：`deploy/backup.sh` + root crontab `30 3 * * *`，导出到 `/opt/pressure-map-backups/backup-YYYY-MM-DD.db`，保留 14 天，日志 `/var/log/pm-backup.log`。
  - 手动执行：`/bin/sh /opt/pressure-map-server/deploy/backup.sh`
  - 备份仍在同一台机器上，只是多一层保险。要防整机故障，需要把目录同步到 OSS 或另一台机器（需要 OSS 凭证，尚未配置）。

## 常用命令

以下命令都在 `/opt/pressure-map-server` 下执行。

```sh
docker compose ps                      # 状态
docker compose logs -f api             # API 日志
docker compose up -d --build           # 重新构建并启动
docker compose restart caddy           # 改完 Caddyfile 后重载
curl -sk https://127.0.0.1/api/health  # 本机探活，应返回 {"status":"ok"}
```

## 账号

没有默认账号。现已开放 `/v1/auth/register`，用户可在软件中注册；管理员也可以在服务器上创建。密码 12～128 字符，交互输入或管道传入：

```sh
docker compose exec api dotnet PressureMap.Server.dll user-create --account 账号 --name 显示名
docker compose exec api dotnet PressureMap.Server.dll user-password --account 账号
docker compose exec api dotnet PressureMap.Server.dll user-disable --account 账号
```

## 备份

```sh
docker compose exec api dotnet PressureMap.Server.dll backup --output /data/backup-$(date +%F).db
docker compose cp api:/data/backup-$(date +%F).db ./backup-$(date +%F).db
```

备份含账号哈希与会话，拷出后不要留在 Web 目录，建议存到服务器之外。

## 已建账号

- 账号：`admin@local`，显示名：管理员
- 初始密码为随机 24 位，只在会话里给过；请在客户端「修改密码」里换掉，别把它写进代码或文档。

需要更多用户时用上面的 `user-create` 命令继续加。

## 已验证

- `https://121.43.32.85/api/health` → `{"status":"ok"}`，公网可达。
- `http://` → `301` 跳 HTTPS。
- 错误密码登录 → `401`；未带令牌访问 `/api/v1/data` → `401`。
- Caddy 与 API 均 `Up`，重启策略 `unless-stopped`，Docker 随开机启动。

## 未做 / 待办

- 2026-10-19 已部署注册 API，使用三个独立 CloudClient/Store 实测公网注册、课程/课表/待办上传、双向修改及全新设备恢复通过。
- 新客户端内置默认 IP 证书指纹校验，无需每台安装信任；轮换证书时需更新客户端指纹。
- 安全组：22/80/443 当前可从公网访问，如需收紧，把 80/443 限制到必要来源。

## 注册与同步验收（2026-10-19）

更新前已备份数据库和三个服务端源码文件。注册请求使用持久化 users 表和密码哈希；课程等数据使用原有 cloud_data 版本快照及 cloud_history 历史。40 项服务端集成检查、116 项客户端数据检查通过。公网验收报告：`E:/AIGC/.test-temp/live-ffc98a82a7284ef38001eccbce17343e/verification.json`。
