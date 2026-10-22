# 云服务 API v1

生产基地址示例：`https://cloud.example.com/api`。下列路径相对于基地址。JSON 使用 snake_case，除注册、登录、健康检查和 AI 今日建议外均需 `Authorization: Bearer <access_token>`。响应禁止缓存。不允许未知的请求模型和快照字段。

## 认证

`POST /v1/auth/register` 无需登录，请求 `{"account":"student","name":"显示名","password":"至少12位的密码"}`。
成功返回 HTTP 201 `{"ok":true}`；客户端随后调用登录接口。账号为 3～128 位 ASCII 字母、数字或 `@._+-`，不区分大小写；显示名 1～100 字，密码 12～128 字符。无效输入或重复账号返回 400 和 message。注册与登录共享每 IP 每分钟 10 次限流。账号持久化到 users 表，密码仅保存哈希。


`POST /v1/auth/login`

```json
{"account":"student@example.com","password":"用户设置的密码"}
```

```json
{
  "access_token":"不可预测的会话令牌",
  "token_type":"Bearer",
  "expires_at":"2026-10-26T08:00:00+00:00",
  "user":{"id":"服务端用户标识","name":"学生"}
}
```

错误账号、密码、禁用或锁定统一返回 401。连续 5 次密码失败锁定 15 分钟；登录路由每 IP 每分钟 10 次。不要在日志、URL 或同步数据中记录密码和令牌。

- `GET /v1/auth/me` → `{"id":"…","name":"…"}`。
- `POST /v1/auth/logout` → `{"ok":true}`，撤销本次令牌。
- `POST /v1/auth/password`，请求 `{"current_password":"…","new_password":"…"}`。成功返回 `{"ok":true,"login_required":true}`，撤销该账号所有设备令牌。
- `GET /health` → `{"status":"ok"}`，无需认证，只表示进程可响应；不泄露数据库路径和用户信息。

## 云数据

`GET /v1/data`

```json
{
  "revision":null,
  "snapshot":{"schema_version":1,"semesters":[],"desktop_todos":[],"course_calendars":[]}
}
```

新账户 revision 为 null；有数据后为不透明字符串。客户端不能自行推算版本号。

`PUT /v1/data`

```json
{
  "base_revision":null,
  "snapshot":{
    "schema_version":1,
    "semesters":[],
    "desktop_todos":[{
      "id":"固定的待办唯一标识",
      "title":"提交高数作业",
      "due_date":"2026-10-20",
      "due_time":"18:00",
      "important":true,
      "completed":false,
      "course_name":"高等数学",
      "planned_date":"2026-10-19",
      "task_kind":"作业",
      "estimated_hours":2,
      "repeat_days":7,
      "requires_submission":true,
      "submitted":false,
      "reminders_enabled":true,
      "steps":[{"id":"step-1","title":"检查答案","completed":false}]
    }],
    "course_calendars":[]
  }
}
```

成功响应 `{"revision":"新的版本标识"}`。下一次上传必须携带此版本。版本检查和保存位于同一 SQLite 写事务；两个设备同时使用同一版本上传，最多一个成功，另一个返回 409。空快照同样是有效的整份替换，会清空账号业务数据；客户端必须在发送前确认删除意图。

快照与桌面 `Store.Snapshot()` 导出格式一致。除可选 `exported_at` 外，顶层为 `schema_version`、`semesters`、`desktop_todos`、`course_calendars`。待办来源、创建和更新时间也会原样保存。repeat_days 为 0～365 的整数，0 表示不重复，客户端在任务完全完成后生成下一次，服务端原样保存。课表项为 `{name,format,content,imported_at}`，format 是 ics/csv，content 保存原文。服务端检查日历结构、文件名和容量，日历事件解析/重复规则由客户端进一步验证。

限制：最多 50 学期，每学期 10 课程、300 任务；账号 1000 待办，每条 20 子任务；20 份课表，每份 UTF-8 内容不超过 2 MiB；快照总计不超过 64 MiB。普通数据路由每账号每分钟 120 次。具体字段约束在 `SnapshotValidator.cs`。

## 客户端同步约定

1. 本地按“服务器地址 + 用户 ID”隔离账户数据库，游客数据不自动归入任何账号。
2. 为每个账号保留最后同步 revision 及本地业务内容摘要，摘要忽略导出时间等无意义变化。
3. 先 GET：仅云端变化时，先备份本地，再在事务中替换本地数据；仅本地变化时，PUT 并携带基准 revision；两边都有变化时暂停并提示冲突。
4. 409 时重新 GET，保留本地和远端副本供用户选择。不得循环把新 revision 填回旧快照强制覆盖。
5. 网络失败时继续写本地，保留待同步状态；上传成功响应丢失时，先比较服务端内容，再决定是否重试。
6. 下载期间若本地有新编辑，应阻止旧下载结果覆盖它。只有成功保存本地快照后才能更新同步基准。
7. 服务端当前使用完整快照协议，不提供增量事件、WebSocket 推送或自动字段级合并。轮询、联网重试和应用前校验属于客户端职责。

## 错误

| 状态码 | 含义 |
|---|---|
| 400 | 非 HTTPS、无效 JSON、字段或数据版本错误 |
| 401 | 未登录、登录过期、账号状态或密码错误 |
| 409 | base_revision 不匹配，必须处理数据冲突 |
| 413 | 请求体超出限制 |
| 429 | 请求过于频繁，等待后重试 |
| 503 | 数据存储暂时不可用，保留本地数据后重试 |

错误体通常为 `{error,message?}`；客户端按状态码处理，不依赖中文文案。登录返回的令牌只授予该用户的数据权限，没有从请求中指定其他用户 ID 的接口。当前不开放自助注册、邮件验证码或忘记密码邮件；密码重置由管理员命令完成。
# AI 今日建议

`POST /ai/plan`（公网地址 `/api/ai/plan`），无需登录。请求 `{ "context": "课程与待办 JSON 文本", "english": false }`，返回 `{ "advice": "安排建议" }`。课程与待办文本上限 24,000 字符，每 IP 每分钟最多 6 次、最多同时处理 2 次，默认每日总量 500 次（UTC 日期，进程重启后重置）。超限返回 429，供应商异常返回 502，超时返回 504，未配置服务端密钥返回 503。模型和密钥由服务端环境变量提供，客户端无法读取或覆盖；供应商错误正文不下发。

