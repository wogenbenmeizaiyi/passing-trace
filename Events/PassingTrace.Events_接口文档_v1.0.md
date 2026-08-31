# PassingTrace Events 接口文档

> 版本：v1.0
>
> 定位：Events 域（记录域）业务 API 的接口契约与前端交互链路
>
> 适用：passingtrace-mobile（主客户端）与 passingtrace-web（辅助 Web 端），供界面实现使用

> 多媒体上传、语义分析、AI 问答与用户记忆扩展见仓库根目录的
> `PassingTrace_多媒体与AI记忆运行说明_v1.0.md`。Event 创建/修改现在支持
> `mediaIds`，标题、正文与附件至少存在一种即可。
>
> 计划中的 Event 地点、按地点 AI 检索和历史地点导航契约见仓库根目录的
> `PassingTrace_高德定位与历史地点导航技术方案_v0.1.md`；该部分尚未实现，不属于当前 v1.0 接口。

本文档描述 `PassingTrace.Events.Api` 暴露的 HTTP 接口，以及客户端从登录、获取令牌到完成「记录 / 查看 / 修改 / 删除」的完整交互链路。实现界面时只需遵循本文契约，无需了解后端内部实现。

---

## 1. 概述

Events API 是「记录域」的业务服务，提供 Event（痕迹 / 计划统一抽象）的创建、列表、详情、修改与软删除，并扩展私有媒体、异步语义分析、用户记忆和带证据的 AI 问答。

- **认证**：OAuth 2.0 授权码 + PKCE，业务 API 用 JwtBearer 离线验证，不访问身份数据库。
- **授权**：访问令牌必须携带 `passingtrace.api` Scope，audience 为 `passingtrace-api`。
- **用户隔离**：所有数据操作以令牌的 `sub` 声明作为 `user_id`，客户端传入的任何 user_id 一律不作为授权依据。

---

## 2. 认证与授权

### 2.1 信任关系

```text
passingtrace-mobile（主客户端）
passingtrace-web（辅助 Web，手机扫码批准登录）
      │  ① 授权码 + PKCE 登录
      ▼
PassingTrace Identity  (https://localhost:56228)
      │  ② 返回各自独立的 access_token
      ▼
passingtrace-mobile / passingtrace-web  ──③ Bearer access_token──▶  PassingTrace.Events.Api
```

- Identity 是唯一身份源，负责登录、签发令牌。
- Events API 通过 Identity 的发现文档（`.well-known/openid-configuration`）离线验证签名，不引用 Identity 代码，也不读身份数据库。
- 业务用户键取自令牌的 `sub` 声明（long 类型）。

### 2.2 登录与令牌获取（已有实现）

手机端（主）与 Web 端（辅）各自独立执行授权码 + PKCE，请求相同的 `passingtrace.api` Scope，获得各自独立的 Token：

- **手机端** `passingtrace-mobile`：使用 `flutter_appauth` 走系统浏览器 OIDC，Token 与设备密钥保存在 `flutter_secure_storage`。
- **Web 端** `passingtrace-web`：使用 `oidc-client-ts`，配置如下（`passingtrace-web/src/auth/oidc.ts`）：

```ts
{
  authority: 'https://localhost:56228',     // Identity 地址
  client_id: 'passingtrace-web',
  redirect_uri: `${origin}/auth/callback`,
  response_type: 'code',                     // 授权码 + PKCE
  scope: 'openid profile offline_access passingtrace.api',
}
```

关键点：

- `scope` 中必须包含 `passingtrace.api`，否则令牌无业务 API 权限（调用会得到 403）。
- Web 端 `automaticSilentRenew` 已开启，令牌过期前通过 refresh token 静默续期，登录态保存在 `sessionStorage`（`WebStorageStateStore`）。
- Web 端登录需要手机扫码批准；手机端是主身份设备，登录不需要其他设备批准。

### 2.3 调用 Events API

每个请求都必须携带：

```http
Authorization: Bearer <access_token>
```

### 2.4 令牌生命周期

| 项 | 值 |
|---|---|
| Access Token 有效期 | 15 分钟 |
| Refresh Token 有效期 | 30 天 |
| 静默续期 | 由 oidc-client-ts 自动处理 |

前端在每次请求前应使用最新的 `user.access_token`，遇到 401 时触发静默续期或重新登录。

---

## 3. 通用约定

### 3.1 Base URL 与 Content-Type

- 本地独立调试：`https://localhost:54933`（或 AppHost 分配的端口）。
- 前端需通过环境变量配置 Events API 地址（建议新增 `VITE_EVENTS_API_BASE_URL`，当前 AppHost 尚未注入该地址，需前端实现时补齐）。
- 请求 / 响应体均为 `application/json`，字段名为 **camelCase**。

### 3.2 枚举映射

枚举在 JSON 中使用**数字**，映射如下：

| 枚举 | 值 | 含义 |
|---|---|---|
| `kind` | `0` | trace（已经发生的记录） |
| `kind` | `1` | plan（未来计划） |
| `status` | `0` | planned（待执行） |
| `status` | `1` | completed（已完成） |
| `status` | `2` | cancelled（已取消） |
| `visibility` | `0` | private（V1 唯一取值） |

### 3.3 幂等键 Idempotency-Key

创建 Event 时，客户端可传 `Idempotency-Key` 请求头（可选，建议 UUID）：

- 同一 `user + Idempotency-Key` 的重复请求只会创建一条 Event。
- 网络超时后的**重试必须复用同一个幂等键**，否则会创建重复记录。
- 同一个幂等键但请求内容不同时，返回 `409` 幂等冲突。

### 3.4 乐观并发 If-Match

修改 / 删除 Event 时必须携带 `If-Match` 请求头，值来自上次读取响应中的 `version` 字段：

```http
PATCH /api/v1/events/42
If-Match: 1284
```

- `version` 是服务端返回的并发令牌（PostgreSQL `xmin`），每次写入都会变化。
- 版本过期时返回 `409` 版本冲突，客户端需重新拉取详情后提示用户重试。
- 缺失或非法时返回 `428 Precondition Required`。

### 3.5 游标分页

列表接口使用游标分页（非页码）：

- 请求传 `limit`（默认 50）与 `cursor`（上一页最后一条的 `id`）。
- 响应返回 `items` 与 `nextCursor`；`nextCursor` 为 `null` 表示没有下一页。

### 3.6 时间与时区

- 所有时间字段为 ISO 8601 带偏移，如 `2026-08-18T10:30:00+09:00`。
- `timezone` 为用户创建时使用的 IANA 时区名（如 `Asia/Tokyo`），缺省为 `UTC`。

### 3.7 错误格式与状态码

错误统一返回 `application/problem+json`（ProblemDetails）：

```json
{
  "status": 404,
  "title": "资源不存在",
  "detail": "未找到用户 1 的事件 42。"
}
```

| 状态码 | 含义 | 客户端处理 |
|---|---|---|
| `400` | 请求不合法（如标题、正文与附件同时为空） | 展示校验错误 |
| `401` | 无令牌 / 过期 / 签名错误 | 刷新令牌或重新登录 |
| `403` | 令牌有效但缺 `passingtrace.api` Scope | 检查登录 scope 配置 |
| `404` | 资源不存在或无权访问（不区分，防枚举） | 提示不存在 |
| `409` | 版本冲突 / 幂等冲突 | 版本冲突：重新加载；幂等冲突：提示内容不一致 |
| `428` | 缺少 `If-Match` | 前端实现 bug，补传版本 |
| `500` | 内部错误 | 提示稍后重试 |

---

## 4. 数据模型

### 4.1 EventResponse（列表项 / 详情 / 创建结果）

```json
{
  "id": 42,
  "kind": 0,
  "status": 1,
  "title": "涩谷烤肉",
  "rawContent": "今天和朋友去了涩谷吃烤肉，花了 6800 日元。",
  "happenedAt": "2026-08-18T19:30:00+09:00",
  "plannedAt": null,
  "completedAt": null,
  "timezone": "Asia/Tokyo",
  "visibility": 0,
  "sourceRevision": 1,
  "version": 1284,
  "createdAt": "2026-08-18T19:32:10+09:00",
  "updatedAt": "2026-08-18T19:32:10+09:00"
}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | long | Event 主键 |
| `kind` | int | 0=trace，1=plan（创建后不可变） |
| `status` | int | 0=planned，1=completed，2=cancelled |
| `title` | string? | 标题，可与原文同时为空则非法 |
| `rawContent` | string? | 用户原始自然语言 |
| `happenedAt` | string? | 实际发生时间 |
| `plannedAt` | string? | 计划发生时间 |
| `completedAt` | string? | 完成操作时间 |
| `timezone` | string | IANA 时区名 |
| `visibility` | int | 恒为 0（private） |
| `sourceRevision` | int | 当前 Source 修订版本 |
| `version` | uint | 并发令牌，用于 `If-Match` |
| `createdAt` | string | 创建时间 |
| `updatedAt` | string | 最后更新时间 |

### 4.2 请求体

**CreateEventRequest**

```json
{
  "kind": 0,
  "title": "涩谷烤肉",
  "rawContent": "今天和朋友去了涩谷吃烤肉。",
  "happenedAt": "2026-08-18T19:30:00+09:00",
  "plannedAt": null,
  "timezone": "Asia/Tokyo"
}
```

**UpdateEventRequest**（`kind` 不可修改）

```json
{
  "title": "涩谷烤肉（更新）",
  "rawContent": "补充：之后唱了两个小时 KTV。",
  "happenedAt": "2026-08-18T19:30:00+09:00",
  "plannedAt": null,
  "timezone": "Asia/Tokyo"
}
```

约束：`title` 与 `rawContent` 不能同时为空。

---

## 5. 接口

### 5.1 创建 Event

```http
POST /api/v1/events
Authorization: Bearer <access_token>
Idempotency-Key: <uuid>          # 可选
Content-Type: application/json

{ "kind": 0, "title": "…", "rawContent": "…", "happenedAt": "…", "timezone": "…" }
```

**响应**

- `201 Created`，返回 `EventResponse`，`Location` 头指向详情。
- `400`：标题与原文同时为空。
- `409`：幂等键与不同内容冲突。
- `401/403`：未认证 / 缺 Scope。

### 5.2 查询 Event 列表

```http
GET /api/v1/events?limit=50&cursor=42&kind=0&status=1&from=…&to=…
Authorization: Bearer <access_token>
```

| 查询参数 | 类型 | 说明 |
|---|---|---|
| `limit` | int | 每页数量，默认 50 |
| `cursor` | long? | 上一页最后一条的 `id` |
| `kind` | int? | 0/1 筛选 |
| `status` | int? | 0/1/2 筛选 |
| `from` | string? | 创建时间下界（ISO 8601） |
| `to` | string? | 创建时间上界（ISO 8601） |

**响应** `200`

```json
{
  "items": [ { "…": "EventResponse" } ],
  "nextCursor": 18
}
```

列表按 `createdAt` 倒序；`nextCursor` 为 `null` 表示已到末尾。已删除的记录不会返回。

### 5.3 查询 Event 详情

```http
GET /api/v1/events/{id}
Authorization: Bearer <access_token>
```

**响应**

- `200`：返回 `EventResponse`。
- `404`：不存在或无权访问。

### 5.4 修改 Source

```http
PATCH /api/v1/events/{id}
Authorization: Bearer <access_token>
If-Match: <version>              # 必填
Content-Type: application/json

{ "title": "…", "rawContent": "…", "happenedAt": "…", "plannedAt": "…", "timezone": "…" }
```

**响应**

- `200`：返回更新后的 `EventResponse`（`sourceRevision` +1，`version` 变化）。
- `404`：不存在 / 无权 / 已删除。
- `409`：版本冲突（`If-Match` 过期）。
- `428`：缺少 `If-Match`。

### 5.5 软删除 Event

```http
DELETE /api/v1/events/{id}
Authorization: Bearer <access_token>
If-Match: <version>              # 必填
```

**响应**

- `204 No Content`：删除成功。
- `404`：不存在 / 无权。
- `409`：版本冲突。
- `428`：缺少 `If-Match`。

删除为软删除（进入可恢复删除期），删除后不再出现在列表 / 详情中。

---

## 6. 交互逻辑链路

### 6.1 应用启动与登录态恢复

```text
App 启动
  → authStore.restore() 从 sessionStorage 恢复 user
  → 若未登录 → 展示登录入口
  → 若已登录 → 进入主界面（Event 列表）
```

### 6.2 首次登录

```text
用户点击「登录」
  → oidc.signinRedirect() 跳转 Identity 登录页
  → 用户在 Identity 完成登录 / 扫码
  → 回调 /auth/callback
  → oidc.signinRedirectCallback() 拿到 user（含 access_token）
  → 跳转到目标页
```

### 6.3 Event 列表页

```text
进入列表页
  → GET /api/v1/events?limit=50
  → 渲染 items
  → 有 nextCursor 时，滚动到底部触发加载更多：
       GET /api/v1/events?limit=50&cursor=<nextCursor>
  → 筛选时带 kind / status / from / to 重新请求（cursor 清空）
```

### 6.4 创建 Event

```text
用户点击「新建」
  → 进入表单（类型 trace/plan、标题、正文、时间、时区）
  → 提交时生成幂等键 uuid 并暂存内存
  → POST /api/v1/events，携带 Idempotency-Key: uuid
  → 201：跳转详情或刷新列表，丢弃暂存幂等键
  → 网络超时重试：复用同一个 uuid（避免重复创建）
  → 409：提示内容与已存在记录不一致，丢弃幂等键
```

创建和修改请求可附带：

```json
{
  "classification": {
    "primaryCategoryKey": "food",
    "tags": [{ "taxonomyKey": "dining" }, { "name": "老张推荐" }],
    "suppressedAiTagKeys": []
  },
  "locations": [{
    "name": "西湖风景名胜区",
    "providerPoiId": "B000A",
    "latitude": 30.249,
    "longitude": 120.143,
    "coordinateSystem": "GCJ02",
    "source": 3
  }]
}
```

`classification` 或 `locations` 在 PATCH 中省略表示继承上一修订，传空对象/空数组表示清除。每条记录 V1 最多一个地点、10 个行为标签。响应同时返回 `manualClassification`、`effectiveClassification` 和当前修订 `locations[]`。

### 6.5 编辑 Event

```text
进入详情页
  → GET /api/v1/events/{id}，缓存 version
  → 用户修改字段后提交
  → PATCH /api/v1/events/{id}，携带 If-Match: <缓存的 version>
  → 200：用响应覆盖本地数据（version 已更新）
  → 409：提示「内容已被修改」，重新 GET 详情后让用户重试
  → 428：前端实现缺陷，应始终携带 version
```

### 6.6 删除 Event

```text
用户点击「删除」→ 确认弹窗
  → DELETE /api/v1/events/{id}，携带 If-Match: <version>
  → 204：从列表移除该条
  → 409：提示版本冲突，重新加载后重试
```

### 6.7 分类与地点

- `GET /api/v1/event-taxonomy`：取得版本化主分类与建议行为标签。
- `POST /api/v1/places/search`：`mode=nearby|keyword`，坐标放 JSON body，不放 URL。
- `GET /api/v1/events/{eventId}/locations/{locationId}/navigation-target`：仅返回当前用户当前修订的可信导航目标。
- Event 列表支持 `categoryKey` 与逗号分隔的 `tagKeys`。

---

## 7. 前端接入要点

1. **令牌注入**：封装一个带 `Authorization: Bearer` 的 HTTP 客户端，统一从 `authStore.user.access_token` 取最新令牌。
2. **401 处理**：拦截 401，先尝试静默续期，失败则跳转登录。
3. **幂等键**：创建请求的幂等键必须在「一次用户操作」内保持一致，尤其是重试场景；成功后即丢弃。
4. **版本管理**：详情 / 列表拿到 `version` 后缓存，编辑 / 删除原样回传 `If-Match`；遇到 `409` 重新拉取。
5. **枚举映射**：前端将数字枚举映射为可读文案（`0`→trace 等），提交时转回数字。
6. **分页**：用 `nextCursor` 实现「加载更多」，不要用页码。
7. **时间展示**：所有时间字段已是带偏移的 ISO 8601，可直接用 `new Date(...)` 解析后按用户时区展示。

---

## 8. 待补齐（非本次范围）

- AppHost 尚未向 `passingtrace-web` 注入 Events API 地址，前端实现需新增环境变量（如 `VITE_EVENTS_API_BASE_URL`）并在 AppHost 中注入。
- 接口枚举当前为数字；如需更友好的字符串枚举（`"trace"` 等），需在 API 侧启用 `JsonStringEnumConverter`，属后续可选调整。
