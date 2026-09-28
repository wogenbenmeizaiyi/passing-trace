# AI 应用层的数据访问

AI 问答、管理接口与后台分析使用以下依赖方向：

```text
Controller / MCP / 后台任务
  → 应用服务、工具与 SemanticPipeline
  → Core/Ai 中的查询和仓储接口
  → Infrastructure/Persistence/Ai 中的 EF Core 实现
  → PostgreSQL / pgvector
```

## 职责

| Core 接口 | 数据访问职责 |
| --- | --- |
| `IPersonalRecordQueries` | 记录、故事线、记忆、地点检索和数据库聚合 |
| `IAiConversationRepository` | 会话、消息、摘要、标题条件更新和数据版本 |
| `IAiEvidenceQueries` | 重新核对历史证据中的好友、共同记录和分享权限 |
| `IUserMemoryRepository` | 记忆读写、指纹查重、向量保存和数据版本更新 |
| `IEventSemanticRepository` | 分析结果查询和重新分析入队 |
| `ISocialAiQueries` | 好友活动统计和分享检索 |
| `IAnalysisOutbox` | 向当前工作单元追加分析任务及数据版本变更 |
| `ISemanticPipelineRepository` | 后台分析的记录与附件读取、标签和索引写入、旧版本失效 |
| `IAnalysisJobRepository` | 任务领取、租约持久化、历史补算、过期数据与孤立附件查询 |

应用服务负责参数归一化、业务校验、模型调用、RRF 排名合并、证据快照与响应组装。工具从认证上下文取得用户身份；模型工具参数中不提供 `userId`。

查询接口返回已执行的数据，使用领域类型或专用结果类型；`IQueryable`、EF 表达式、`TraceDbContext` 和 pgvector 类型只存在于持久化实现。向量通过 `float[]` 跨越边界。按 ID 读取也会重新检查所有权、软删除及共同记录权限。

数据库负责筛选、分组、排序及数量限制，应用层负责月份文本等展示格式。金额统计保留“没有可核实金额”和“合计为零”的区别；历史证据继续按当前好友与分享权限过滤。

## 工作单元

接口实现均注册为 scoped，复用当前请求或后台任务作用域中的 `TraceDbContext`。会话、单条记忆与后台流水线的 `Find` 返回可修改的领域对象，再由仓储保存；只读检索使用 `AsNoTracking`。`IAnalysisOutbox` 只追加变更，提交仍由调用方的工作单元完成。批量拒绝记忆与数据版本更新使用同一事务。

MCP 工具保持串行执行，避免并发使用同一持久化工作单元和证据集合。

后台 Worker 保留任务分派、重试与模型编排，任务领取的 `FOR UPDATE SKIP LOCKED` 和事务放在持久化层。历史补算在数据库中筛选缺失的当前修订任务。`Program.cs` 和依赖注册入口负责组装 EF 实现。

## 验证

`AiPersistenceBoundaryTests` 检查应用服务、Worker 和 Core 接口边界；`AiRepositoryTests` 使用真实 PostgreSQL 验证用户隔离、共同记录权限撤销、向量查询、会话删除、重新分析入队及记忆数据版本。`AnalysisJobRepositoryTests` 验证并发领取、历史补算与维护；`SemanticPipelinePersistenceTests` 使用假模型验证分析落库、幂等、人工标签、记忆拒绝和过期分析结果。现有工具、统计、MCP 和社交测试继续覆盖对外行为。
