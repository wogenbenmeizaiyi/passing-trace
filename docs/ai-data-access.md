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

API 内的 AI 代码按功能组织，目录与命名空间对应；完整目录说明见 [AI 模块结构](../Events/PassingTrace.Events.Api/Ai/README.md)。`Assistant` 编排会话，`Tools/Queries` 与 `Tools/Mutations` 提供模型工具，`Mutations` 保存操作回执并处理删除授权，`Models` 适配模型供应商。模块统一由 `AiServiceCollectionExtensions.AddTraceAi` 注册；普通业务服务使用 `Common/CurrentUserContext`，不依赖 AI 目录中的认证类型。

| Core 接口 | 数据访问职责 |
| --- | --- |
| `IPersonalRecordQueries` | 记录、故事线、记忆、地点检索和数据库聚合 |
| `IAiConversationRepository` | 会话、消息、摘要、标题条件更新和数据版本 |
| `IAiMutationRepository` | 写入操作日志、授权所有权与状态、来源消息校验、事务锁及回执原子提交 |
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

`PersonalMutationTools` 通过 `AiMutationService`、Core 仓储与现有 `EventService`／`StorylineService` 执行写入，不引用 EF 或查询数据库。记录局部修改先读取本人当前版本，再合并指定字段；原始附件、地点、分类、参与者及未指定信息保留。故事线使用现有修订、图关系校验、搜索索引和分析入队逻辑；已有事务时故事线服务加入外层工作单元。

`IAiMutationRepository.ExecuteAsync` 在持久化层开启事务并取得操作键的 PostgreSQL advisory lock。同轮幂等键由用户、会话、已保存来源消息、操作及归一化参数的 SHA-256 生成；数据库对用户与操作键增加唯一约束。业务变更、搜索索引、分析任务、数据版本、操作日志及独立聊天回执全部成功才提交；异常回滚并清除已回滚的跟踪实体。

删除申请仅保存 `ai_mutation_operation` 待授权日志及目标版本，有效期 15 分钟。决定接口使用授权 ID 串行锁定请求，重新检查所属用户及未删除会话；确认前由持久化端口锁定本人目标行并刷新已跟踪的目标，防止核对版本之后发生并发编辑。实际删除仍复用软删除应用服务，授权完成状态与回执一起提交。重复决定返回日志中的原回执；取消、过期及版本变化不删除内容。具体工具及接口见 [AI 工具协议与执行边界](ai-tools-protocol.md)。

操作日志迁移为 `20260930115412_AddAiMutationOperations`。创建／编辑回执也保存为 `mutation-receipt-v1` 助手消息，采用现有消息保留期限；它们不阻止首次正常回答生成会话标题。成功写入轮跳过答案缓存并重新读取数据版本，历史回执的真实 ID 与标题提供给后续聊天上下文。模型或网络随后失败不回滚已成功提交的操作。

后台 Worker 保留任务分派、重试与模型编排，任务领取的 `FOR UPDATE SKIP LOCKED` 和事务放在持久化层。历史补算在数据库中筛选缺失的当前修订任务。`Program.cs` 和依赖注册入口负责组装 EF 实现。

## 验证

`AiPersistenceBoundaryTests` 检查应用服务、Worker 和 Core 接口边界；`AiRepositoryTests` 使用真实 PostgreSQL 验证用户隔离、共同记录权限撤销、向量查询、会话删除、重新分析入队及记忆数据版本。`AnalysisJobRepositoryTests` 验证并发领取、历史补算与维护；`SemanticPipelinePersistenceTests` 使用假模型验证分析落库、幂等、人工标签、记忆拒绝和过期分析结果。现有工具、统计、MCP 和社交测试继续覆盖对外行为。
