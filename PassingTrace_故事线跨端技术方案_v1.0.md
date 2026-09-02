# 星期八故事线跨端技术方案

> 版本：v1.0
> 状态：已实现首版
> 范围：.NET 10 / PostgreSQL 18、Vue 3 + Vue Flow、Flutter Android、个人 AI

## 1. 目标

故事线把分散的当下记录和未来安排组织成一段有阶段、有先后、有分支与汇合的完整经历。数据库只保存平台无关的语义图；Web 画布坐标是独立投影，手机不读取或修改坐标。

```text
Event + 固定 SourceRevision
        │
        ▼
StorylineRevision ── Stage / Node / Edge
        ├── WebLayout：Vue Flow 坐标、视口、阶段框
        ├── MobileOutline：服务端拓扑排序、深度、分支/汇合提示
        └── SearchIndex：标题、阶段、关系、节点与 1024 维向量
```

## 2. 数据模型与不变量

- `Storyline` 保存用户、当前标题/说明/分类/状态、当前修订、封面、派生时间范围、软删除与 PostgreSQL `xmin` 并发令牌。
- `StorylineRevision` 是不可变快照，保存幂等键、内容哈希和布局状态。
- `StorylineStage` 只负责语义分组和顺序，不保存正文或附件。
- `StorylineNode` 使用稳定 UUID，固定引用 `eventId + sourceRevision`，并保存阶段、同层顺序和重要性。
- `StorylineEdge` 支持 `Sequence / Branch / Parallel / Related`，禁止自环、重复边、无效端点和有向循环。
- `StorylineWebLayout`、`StorylineWebNodeLayout`、`StorylineWebStageLayout` 与语义图分表保存。
- `StorylineSearchIndex` 保存当前修订的检索文本、`tsvector` 和 `vector(1024)`。

领域服务统一执行以下约束：每条故事线最多 500 个节点、50 个阶段、1000 条边；同一修订内一个 Event 只能出现一次；节点 Event 必须属于当前 JWT 用户；完成态要求所有节点位于同一个弱连通图。删除故事线只解除组织关系，不删除 Event、附件或地点。

## 3. 修订、并发与幂等

Web 完整保存和手机增量操作最终进入同一个 `StorylineService`：

- 写请求必须携带 `If-Match`，值来自上次响应的 `version`（PostgreSQL `xmin`）。
- 创建、保存、恢复和手机变更必须携带 `Idempotency-Key`。
- 每次成功写入都生成新的不可变修订、更新当前搜索索引水位，并写入 `storyline.index` Outbox。
- 并发冲突返回 `409`，客户端不得静默覆盖较新的服务端图。
- 手机快捷操作返回上一修订号；撤销是带并发令牌的修订恢复，发生下一次操作后旧撤销入口自然失效。

手机新增节点时，旧节点 Web 坐标复制到新修订；新节点没有坐标，修订标记为 `NeedsArrangement`。Web 打开后可使用 Dagre 自动排版。

## 4. 故事线内创建轻量计划

编辑器使用 `nodeType=new-plan` 的临时节点。后端在单一数据库事务中：

1. 校验整个语义图和已有 Event 所有权。
2. 创建 `EventKind.Plan`、`SourceRevision 1`、基础搜索索引和 AI Outbox。
3. 把临时节点替换为固定 Event 修订引用。
4. 保存 StorylineRevision、边、阶段和 Web 布局。
5. 更新用户数据水位并返回 `nodeKey → eventId` 映射。

任何步骤失败都会回滚，计划幂等键由故事线操作幂等键和临时节点 UUID 派生。计划保存后会正常出现在“未来安排”中；从故事线移除不会删除计划。

## 5. Web 与手机交互

### Web

Web 提供故事线列表、纵向阅读页、修订阅读页和三栏 Vue Flow 编辑器：左侧记录库与轻量计划，中间画布，右侧属性。编辑器支持自定义节点/边、阶段、分支和汇合、MiniMap、缩放平移、撤销重做、Dagre 自动排版、键盘和按钮移动替代操作、`sessionStorage` 草稿恢复及未保存离开拦截。

### Android

底部导航为“记录 / 故事线 / 问 AI”。手机支持列表筛选、分步创建、选择第一条已有记录或直接创建计划、纵向阶段时间线、图片封面与节点图片、添加已有记录、添加计划、创建顺序或分支、同步修订、移动阶段和安全移除。

分支同时使用缩进、左侧结构线和文字说明；汇合显示前驱数量，不只依赖颜色。复杂分支/汇合删除会提示使用 Web 整理。主要触控目标不小于 48dp，动画尊重系统 reduced-motion 设置。

## 6. API

- `GET /api/v1/storyline-taxonomy`
- `GET /api/v1/storylines`
- `POST /api/v1/storylines`
- `GET /api/v1/storylines/{id}`
- `PUT /api/v1/storylines/{id}`
- `DELETE /api/v1/storylines/{id}`
- `GET /api/v1/storylines/{id}/revisions`
- `GET /api/v1/storylines/{id}/revisions/{revision}`
- `POST /api/v1/storylines/{id}/revisions/{revision}/restore`
- `POST /api/v1/storylines/{id}/changes`

`GET /storylines/{id}` 返回语义 `stages/nodes/edges`、手机使用的 `outline[]` 和可选 `webCanvasLayout`。`outline` 包含拓扑顺序、深度、入/出边数量、是否开启分支和是否为汇合点。

`/changes` 只接受白名单操作：`add-existing-event`、`add-plan`、`sync-node`、`move-node-to-stage`、`remove-node`、`remove-node-and-reconnect`、`update-metadata`。

## 7. AI 检索

Worker 消费 `storyline.index` 和 `storyline.removed`，为当前修订生成检索文本及向量。问答 Agent 新增只读 `SearchMyStorylines` 与 `GetMyStorylineEvidence`；工具从服务端当前用户上下文取用户 ID，模型不能跨用户检索，也不能创建计划、修改连线或恢复修订。

证据返回故事线、阶段、拓扑关系以及固定 Event 修订。计划节点明确标注待执行、已完成或已取消；精确统计按去重 Event 执行，不能因为同一 Event 出现在多条故事线而重复计数。

## 8. 本地验证

```powershell
dotnet test tests/PassingTrace.Events.IntegrationTests/PassingTrace.Events.IntegrationTests.csproj -c Release --filter StorylineServiceTests

cd passingtrace-web
corepack pnpm test:unit
corepack pnpm build
corepack pnpm lint

cd ..\passingtrace-mobile
flutter analyze
flutter test
flutter build apk --debug
```

后端故事线测试使用 PostgreSQL 18 + pgvector Testcontainer，因此运行测试时 Docker Desktop 必须可用。
