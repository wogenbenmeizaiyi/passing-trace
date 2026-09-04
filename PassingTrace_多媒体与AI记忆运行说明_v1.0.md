# PassingTrace 多媒体、AI 分析与用户记忆运行说明

> 版本：v1.0
>
> 适用分支：`codex/media-ai-memory`
>
> 适用环境：.NET 10、Aspire 13、PostgreSQL 18 + pgvector、MinIO、Redis

本文描述已经落地的多媒体记录、异步语义分析、用户长期记忆与 AI 问答能力。原始记录始终是事实源，AI 结果是可重算、可追溯的派生数据。

> 计划中的高德定位、历史地点 AI 检索和导航操作见
> `PassingTrace_高德定位与历史地点导航技术方案_v0.1.md`。该方案当前仅为技术决策稿，尚未表示接口已经实现。

## 1. 运行结构

```text
Flutter / Vue
   │ Bearer Token
   ▼
Events API ──事务──▶ PostgreSQL 18 + pgvector
   │                    │ Event / Revision / Outbox / Semantic / Memory
   │ 预签名 URL          │
   ▼                    ▼
私有 MinIO ◀──── AI Worker ────▶ 百炼 Qwen
                         │          qwen3.8-max
                         │          qwen3.7-plus（429/5xx 回退）
                         └────────▶ text-embedding-v4（1024 维）

Events API ──24 小时问答缓存──▶ Redis
```

- API 创建或修改 Event 时，在同一数据库事务中保存 SourceRevision、附件快照、Outbox 和用户数据水位。
- Worker 用 PostgreSQL 租约和 `FOR UPDATE SKIP LOCKED` 领取任务，失败按退避策略重试，5 次后进入死信。
- 图片会生成最长边 2048px、最大 8MB 的 AI 副本以及 480px 缩略图；视频和普通文件本期不送入模型。
- App 内 Agent 使用 Microsoft Agent Framework 的单一 `ChatClientAgent`，工具只从服务端作用域读取当前 JWT 用户，模型不能指定 `userId`。

## 2. 本地 Aspire 配置

首次运行前，在 AppHost 的 User Secrets 中写入本地参数。以下值仅为示例，请自行替换：

```powershell
dotnet user-secrets set "Parameters:postgres-password" "your-postgres-password" --project AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:minio-access-key" "passingtrace-local" --project AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:minio-secret-key" "your-minio-secret" --project AppHost/AppHost.csproj
dotnet user-secrets set "Parameters:qwen-api-key" "your-bailian-api-key" --project AppHost/AppHost.csproj
```

浏览器在本机使用时，对象存储公开端点保持默认值 `http://localhost:9000`。手机与电脑在同一路由器下真机调试时，预签名 URL 必须使用电脑的局域网地址：

```powershell
dotnet user-secrets set "Parameters:object-storage-public-endpoint" "http://192.168.1.10:9000" --project AppHost/AppHost.csproj
```

同时放行 Windows 防火墙中的 Identity、Events API 与 9000 端口，并把手机端 Identity / Events API 地址改为同一台电脑的局域网 IP。对象存储内部访问仍由 Aspire 使用容器 endpoint，不受该公开地址影响。

启动：

```powershell
dotnet run --project AppHost/AppHost.csproj
```

开发环境由 Events API 自动执行 EF Core migration；生产环境不会自动迁移，部署前必须独立执行迁移。PostgreSQL 18 的卷挂载点为 `/var/lib/postgresql`，卷名为 `passingtrace-postgres18-data`；不要改回旧镜像使用的 `/var/lib/postgresql/data`。

## 3. 媒体上传协议

每条 Event 最多关联 10 个附件：

| 类型 | 支持格式 | 单文件上限 | AI 处理 |
|---|---|---:|---|
| 图片 | JPEG、PNG、WebP | 20MB | 是 |
| 视频 | MP4、MOV、WebM | 1GB | 否 |
| 文件 | 常见文档、压缩包、数据文件 | 200MB | 否 |

可执行文件与脚本会被拒绝。小于 100MB 使用一次预签名 PUT；大文件使用 16MiB 分片。客户端流程：

1. `POST /api/v1/media/uploads` 申请上传会话。
2. 按返回的 `uploadUrl` 直接 PUT，或调用 `POST /api/v1/media/{id}/parts` 获取各分片 URL 后 PUT。
3. `POST /api/v1/media/{id}/confirm` 确认上传。服务端核对用户归属、对象存在、实际大小、SHA-256 和文件魔数 MIME。
4. 创建或修改 Event 时把确认后的 ID 按显示顺序放入 `mediaIds`。

只有当前用户能获取 `GET /api/v1/media/{id}/access` 返回的短期下载地址。公共 API 不返回 S3 Object Key。未确认、未关联且超过 24 小时的对象由 Worker 清理，未完成的分片上传会被中止。

Event 的标题、正文、附件至少存在一种，因此允许纯图片、纯视频或纯文件记录。附件顺序或集合变化会产生新的 SourceRevision，并保留该修订的附件 ID 快照。

## 4. AI 数据与检索

数据库按用途分层：

- 事实层：`Event`、`SourceRevision`、`MediaAsset` 及修订附件快照。AI 不修改此层。
- 分析层：每次处理产生新的 `EventSemanticRun`；可检索 mention 和金额分别落入 `SemanticMention`、`ExpenseFact`。
- 搜索层：当前有效修订写入 `EventSearchIndex`，包含检索文本、PostgreSQL 全文/`pg_trgm` 索引及 1024 维向量。
- 分类层：用户确认内容写入 `SourceRevisionLabel`，AI 分类写入 `SemanticEnvelope v2`，当前合并结果投影到 `EventLabelIndex`。人工主分类优先，AI 行为标签只在置信度不低于 0.70 时补充。
- 地点层：用户确认地点写入修订级 `EventLocation`；只有当前修订、GCJ02 且带坐标的地点可以生成导航目标。AI 从文字或图片推断的地点不能直接导航。
- 记忆层：`UserMemory` 必须关联 `UserMemoryEvidence`。用户确认或纠正优先；删除会写成 rejected，原 Source 未变化时不会自动重建。
- 对话层：原始 `AiMessage` 保留 30 天，`ConversationSummary` 长期保留。用户可以删除单个会话、单条记忆或全部记忆。

问答请求会合并关系型过滤、全文/`pg_trgm` 和向量候选，再用 RRF 合并排序。精确次数、金额、趋势和计划完成率只走白名单参数化 SQL，不允许模型生成 SQL。回答证据通过 Event ID 返回，不暴露 Object Key 或预签名 URL。

Qwen API Key 未配置时，记录与媒体功能仍能工作；AI 分析任务会重试并最终进入死信，AI 问答会返回明确的未配置错误。新 Key 配置完成后可以调用重新分析接口恢复单条记录。

## 5. 公共接口

所有接口都要求 `passingtrace.api` Scope，并从 Access Token 的 `sub` 获取用户身份。

| 范围 | 接口 |
|---|---|
| 媒体 | `POST /api/v1/media/uploads`、`POST /api/v1/media/{id}/parts`、`POST /api/v1/media/{id}/confirm`、`GET /api/v1/media/{id}/access`、`DELETE /api/v1/media/{id}` |
| Event | 创建/修改增加 `mediaIds`、`classification`、`locations`；响应增加媒体、语义、人工/生效分类和当前地点 |
| 分类与地点 | `GET /api/v1/event-taxonomy`、`POST /api/v1/places/search`、`GET /api/v1/events/{eventId}/locations/{locationId}/navigation-target` |
| 语义 | `GET /api/v1/events/{id}/semantic`、`POST /api/v1/events/{id}/semantic/reparse` |
| 对话 | `GET/POST /api/v1/ai/conversations`、`GET/DELETE /api/v1/ai/conversations/{id}` |
| 问答 | `POST /api/v1/ai/conversations/{id}/messages`，SSE 事件为 `delta`、`evidence`、`done`、`error` |
| 记忆 | `GET/DELETE /api/v1/ai/memories`、`PATCH/DELETE /api/v1/ai/memories/{id}` |

## 6. 验证命令

```powershell
dotnet build PassingTrace.slnx --no-restore
dotnet test PassingTrace.slnx --no-restore

cd passingtrace-web
corepack pnpm test:unit --run
corepack pnpm build

cd ../passingtrace-mobile
flutter analyze
flutter test
```

Events 集成测试包含真实 Testcontainers 用例，会临时启动 `pgvector/pgvector:pg18-trixie` 与 MinIO，验证 migration、1024 维向量和私有对象读写，因此运行测试时 Docker Desktop 必须处于 Running 状态。

## 7. 当前边界

- 本期不解析或转码视频，不读取 PDF、Office、压缩包正文。
- 本期不对外暴露 MCP Server。Agent 在同一进程调用只读 Typed Tools；以后可以直接把这些应用服务包装成只读 MCP 工具。
- Agent 不拥有修改或删除 Event 的工具，也不能跨用户搜索。
- 密钥、原文、图片、Token 和预签名 URL 不应进入日志或 Trace。
- Android 高德 Key 写入未跟踪的 `passingtrace-mobile/android/local.properties`：`AMAP_ANDROID_KEY=...`；高德 Web 服务 Key 通过 AppHost User Secrets 的 `Parameters:amap-web-service-key` 提供。
