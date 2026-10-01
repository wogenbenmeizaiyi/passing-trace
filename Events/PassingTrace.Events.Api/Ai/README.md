# AI 模块结构

按功能放置代码，命名空间与目录一致。根目录只保留注册入口 `AiServiceCollectionExtensions.AddTraceAi`；应用总入口通过它组装 AI 模块。

```text
Ai/
├─ Assistant/              会话接口、历史、标题及流式回答编排
│  ├─ Context/             日历和本轮会话上下文快照
│  └─ Presentation/        回答呈现、完成状态与错误提示
├─ Tools/
│  ├─ Queries/             记录、统计、记忆、历史地点和故事线只读工具
│  └─ Mutations/           记录／故事线／人物写入、删除申请及明确意图校验
├─ Mutations/              幂等操作日志、持久化回执和删除授权应用服务
├─ Models/                 模型配置、客户端工厂和供应商 HTTP／流式适配
├─ Evidence/               工具与聊天共用的证据、引用和导航动作契约
├─ Capabilities/           工具包、Schema 生成、内部 MCP 会话与参数校验
├─ Amap/                   高德工具、外部 MCP 适配及额度保护
├─ Memories/               长期记忆管理接口和应用服务
├─ Semantics/              语义分析查询及重分析入口
└─ Skills/                 场景规则、读取与本轮工具授权
```

`AssistantService.cs` 负责流式问答编排；会话读写、标题和摘要保存位于 `AssistantService.Conversations.cs`。缓存键与缓存有效性由 `AssistantAnswerCache` 负责，导航意图判断由 `AssistantNavigationPolicy` 负责。

`PersonalRecordTools` 和 `PersonalMutationTools` 是请求作用域内的工具入口，通过 partial 文件按对象和操作分类。共享文件只保存本轮身份依赖、上下文、证据／回执状态及公共辅助方法。所有 partial 文件仍属于同一实例，使检索后的证据限制、调用顺序、事件队列和幂等回执保持一致。

`Tools/Mutations` 负责模型参数与业务命令转换；`Mutations` 负责跨操作的持久化与授权流程。删除工具只能申请授权，实际删除由授权决定接口触发。新增具体工具放入对应工具文件，并在独立的 capability package 中暴露；新增会话策略或供应商协议适配放入各自功能目录。

通用认证上下文 `CurrentUserContext` 位于 `Common/`，普通业务服务无需依赖 AI 命名空间。数据访问接口位于 `PassingTrace.Core/Ai`，EF 实现位于 `PassingTrace.Infrastructure/Persistence/Ai`，分析流水线位于 `Ai/PassingTrace.Ai.Worker`；应用服务与工具不依赖 EF。只有注册入口负责绑定这些实现。

执行和持久化约定见 [AI 工具协议](../../../docs/ai-tools-protocol.md) 与 [AI 数据访问](../../../docs/ai-data-access.md)。目录调整不改变 HTTP 路由、SSE 事件、模型工具名或 Skills 资源路径。

人物工具集中在 `PersonalMutationTools.Subjects.cs`，查询证据与写入回执仍属于同一请求实例。人物业务放在 API 的 `Subjects`，领域端口和持久化实现分别放在 Core/Infrastructure 的 `Subjects`，避免把新模块业务堆进 AI 编排服务。
