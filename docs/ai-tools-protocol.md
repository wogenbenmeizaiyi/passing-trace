# AI 工具协议与执行边界

## 人物与专属内容

`subjects` 场景解锁 `QueryMySubjects`、`QueryMySubjectTimeline`、`QuerySubjectFieldPresets` 和 `PreviewMySubjectLifecycle`。所有查询限定本人档案；时间轴返回 `Event`、`SubjectEntry`、`Milestone` 三类来源，不能把专属内容当作我的原记录统计。与平台好友、协作参与者、原故事线图分别维护。

明确写入指令在 `mutations` 场景解锁 `CreateMySubject`、`UpdateMySubject`、`RelateMySubjects`、`UpdateMySubjectRelation`、`CreateMySubjectEntry`、`UpdateMySubjectEntry`、`DecideMySubjectPlan`、`UpdateMySubjectLifecycle`。`RequestDeleteMySubjectContent` 仅申请 `Subject`／`SubjectEntry`／`SubjectRelation` 的按钮授权，实际删除不暴露给模型。来源、名称或对象不明确先澄清，不同时写入 Event 与 SubjectEntry；摘要、共同出现和关系线不自动写入。

原 `CreateMyRecord`／`UpdateMyRecord` 支持明确的 `subjectIds`，只添加时间轴入口，不推导关系或授予协作权限。专属计划完成需明确实际时间与实际字段，预期值不自动采用；生命周期预览默认保留所属计划，自身不能结束或删除。

写入沿用服务端幂等键、原子业务事务与持久化 `mutation-result` 回执。证据增加 `subjects`（subjectId、revision、title）和 `subjectEntries`（entryId、subjectId、revision、title、kind），引用为 `[Subject #真实GUID]`、`[SubjectEntry #真实GUID]`。Web 和手机仅根据真实证据生成详情链接，保留返回聊天；模型漏写引用、随后生成失败或刷新仍能展示已成功回执。写入轮不读写回答缓存，成功刷新数据版本。

手动人物删除由 `POST /api/v1/subjects/delete-requests` 进入同一聊天授权链，`GET .../approvals` 和 `POST .../approvals/{id}/decision` 保持原协议。确认时重新取得用户级图事务锁，校验版本和连通；取消、到期、换用户或断连均不删除。模块接口、来源和媒体约定见 [人物模块](subjects.md)。

## 当前调用链

```text
用户消息 → ChatClientAgent（模型原生 tools / tool_calls）
  ├─ 个人记录／写入包 → MCP client → 进程内 JSON-RPC 管道 → MCP server → 参数校验 → 应用工具
  └─ 高德适配包 → 官方高德 Streamable HTTP MCP
```

内部 MCP 使用现有官方 `ModelContextProtocol 2.2.0` 客户端、服务端和 Stream transports，采用双方 SDK 支持的 `2026-07-28` 协议，实际执行工具发现、`tools/list`、`tools/call`。该版本支持直接返回数组等结构化内容，与个人记录工具的列表结果一致。不是从聊天正文中提取 JSON，也不是用提示词模拟 MCP。

内置 MCP 工具没有新增端口、命令执行器或第三方进程。每轮回答创建独立 MCP 会话；结束、失败或取消后释放客户端、服务端和管道。任一工具调用取消也会结束本轮连接，确保服务端查询不在后台继续运行。此实现暂不提供给外部 MCP 客户端连接。高德仍沿用现有外部 MCP 配置、协议协商和额度保护。删除授权通过下述独立 HTTP 接口处理，不通过模型确认。

## 参数与错误

- 工具名、参数类型、必填、枚举以及校验注解从 .NET 工具定义生成标准 JSON Schema。
- `ValidatedMcpServerTool` 使用 JsonSchema.Net（Draft 2020-12）在执行前校验；拒绝未声明的根参数并启用格式校验。
- 模型不能传 `userId`、SQL、服务实例或取消令牌。无效参数不会执行查询，也不加入证据快照。
- 格式失败以标准 MCP `isError` 返回，只允许一次纠正；第二次无效或业务执行失败终止本轮，向用户返回中文提示。
- 可能查不到单条结果的工具使用非空 `{ result: 值或 null }` 容器，明确区分“未找到”和协议失败；数组与非空对象仍保留自然结构。
- 不把错误解释为零金额、空证据或成功，不自动重复执行成功调用。
- 参数通过校验不代表模型理解一定正确。时间范围解释、记录所有权、已检索证据限制等仍由应用服务执行，提示词只负责说明工具用途和回答方式。

## 身份、状态与隐私

MCP 服务端复用当前请求内已绑定的 `PersonalRecordTools` 和 `PersonalMutationTools`，而非创建新的 DbContext 或全局工具实例。用户身份来自已认证请求，不进入模型参数。预检索和工具调用共享同一证据快照，不同用户/请求之间不共享 MCP 会话。

同一会话工具调用串行执行，以保护 DbContext 和快照集合。内部协议不配置帧日志，错误不包含参数原值或底层异常。模型不获得任意 CLI/shell 执行权限。

应用工具通过 Core 中的查询接口调用 `Infrastructure/Persistence/Ai`，数据库查询在持久化层执行。接口只返回已执行的数据；参数归一化、排名合并和证据整理保留在应用层。具体职责与工作单元约定见 [AI 应用层的数据访问](ai-data-access.md)。

## 扩展

工具实现分别位于 `Ai/Tools/Queries` 和 `Ai/Tools/Mutations`，工具包与 Schema 工厂分别放在 `Ai/Capabilities` 的独立文件中。添加工具时沿用所属请求实例的证据或回执状态，按对象放置方法，并在对应工具包显式注册；不要把新操作加入会话编排服务。完整目录约定见 [AI 模块结构](../Events/PassingTrace.Events.Api/Ai/README.md)。

`IAiCapabilityPackage.UsesInternalMcp` 默认为 `true`。新增内部工具包注册后走相同 MCP 会话、校验、错误和释放边界。`WriteTools` 明确声明有副作用的工具，协议中不会标为只读。高德因已经有外部 MCP 适配与配额策略，显式设为 `false`，避免重复包裹。

后端的确定性预检索、证据整理等应用逻辑仍可直接调用服务；模型自主选择的个人工具调用均经过 MCP。写入工具需要读取 `mutations` 场景规则，并且服务端检查用户的明确操作意图。普通聊天、总结、建议和“如何操作”不授予写入权限。

“加上”“你先加”等追加表达可直接授予创建或编辑权限；同句中的“不删除”只限制对应删除操作。短确认（例如“对就这样”）仅能延续最近待确认方案中已有的用户创建／编辑请求，不能从助手建议、摘要、完成回执、取消请求或已切换话题的历史中取得权限，也不继承删除权限。此类确认轮使用相同上下文判定缓存和工具权限并跳过回答缓存。

写入意图拒绝经 MCP 保留安全错误码 `mutation_intent_required`，客户端提示明确操作；其他写入工具错误提示为记录操作失败。错误分类不包含工具参数或私人正文，避免把写入失败显示成记录查询失败。

## 单次设备定位

Web 和 Flutter 的输入框上方提供“使用当前位置”，用户主动操作后获取一次前台定位，可移除或重新获取。点击发送时只附加到当前消息；会话切换、取消或离开界面会丢弃尚未返回的结果。权限拒绝、超时或定位失败不阻止用户输入文字出发地。

`POST /api/v1/ai/conversations/{id}/messages` 可附加 `location`：`latitude`、`longitude`、`accuracyMeters`、`capturedAt`（ISO 8601）及 `coordinateSystem`（`WGS84` 或 `GCJ02`）。服务端先检查本人会话、坐标范围、有效精度及采集时间：最长 5 分钟，允许设备时钟最多超前 1 分钟。无效或过期位置返回 SSE 定位错误，不保存该条消息或调用模型。

浏览器使用 `navigator.geolocation.getCurrentPosition`（需要 HTTPS/localhost 与定位权限），禁止使用缓存位置。WGS84 通过高德官方坐标转换 Web 服务统一为 GCJ02，使用服务端 `Amap:WebServiceKey` / `AMAP_WEB_SERVICE_KEY`，占用现有 LBS 月额度并禁用请求日志。Android 保留高德 SDK 的坐标系；模拟器原生 GPS 标注为 WGS84。坐标转换失败或缺少 Web 服务 Key 时明确报错，不能把未转换坐标当作高德坐标。

定位经 `AssistantLocationContext` 仅注入本轮模型上下文，高德场景可直接使用位置反查地址、搜索周边或规划路线。下一条未附加位置的消息不会继承实时定位；历史地点和服务器 IP 不代替设备位置。带定位的回答跳过缓存读写，原始设备位置不进入数据库、长期记忆或记录。AI 回答与高德查询得到的地点信息仍按现有聊天规则保存。

参考：[高德坐标转换](https://developer.amap.com/api/webservice/guide/api/convert)、[浏览器单次定位](https://developer.mozilla.org/en-US/docs/Web/API/Geolocation/getCurrentPosition)。

## 创建与局部编辑

| 工具 | 行为 |
| --- | --- |
| `CreateMyRecord` | 创建 Trace 经历或 Plan 待执行计划；身份、时区、幂等键由服务端提供 |
| `UpdateMyRecord` | 修改标题、正文、对应时间；未指定字段保留，清空使用 `clearFields`；不修改附件、地点、分类、参与者或类型 |
| `CreateMyStoryline` | 按有序节点引用本人已有记录或创建新计划，由服务端生成 Sequence 连线 |
| `UpdateMyStoryline` | 基础信息、添加记录／计划、节点阶段和顺序、同步固定修订、移除节点；沿用图关系校验，移除节点保留记录 |
| `RequestDeleteMyRecord` | 为本人记录或计划保存待授权请求，不执行删除 |
| `RequestDeleteMyStoryline` | 为本人故事线保存待授权请求，不删除节点记录或计划 |

编辑和删除先搜索、读取目标；同名或指代有歧义时先澄清。共同记录或好友分享的可读权限不授予修改权限。复杂分支图编辑仍使用现有人工编辑界面。

成功写入发送 SSE `mutation-result`，包含 `operationId`、`operation`、`state`、真实 `targets`（类型、ID、标题、修订）以及已经保存的 `message`（ID、正文、证据）。Web 和 Flutter 按消息 ID 去重，独立显示“已创建／已更新”及普通记录／故事线链接；故事线内新计划也包含链接，不依赖模型再次引用。回执消息与操作日志持久化，重新打开聊天可恢复。后续模型上下文包含回执的目标 ID 和标题。

## 删除授权

1. 请求绑定当前用户、会话、来源消息、目标 ID、目标版本和标题，有效期 15 分钟。
2. SSE `approval-request` 返回请求 ID、会话、目标类型／ID、标题、说明及过期时间，然后结束本轮模型连接。
3. Web 和 Flutter 在输入框上方逐个显示“确定／取消”；执行中禁用按钮，失败保留重试提示，目标标题可打开详情。
4. `GET /api/v1/ai/conversations/{id}/approvals` 返回当前本人会话中尚未过期的待授权请求。
5. `POST /api/v1/ai/conversations/{id}/approvals/{approvalId}/decision` 的 JSON 仅接受 `{"decision":"confirm"}` 或 `{"decision":"cancel"}`。目标来自服务端保存的请求，客户端不能替换目标，聊天文字不能代替按钮。
6. 服务端重查用户、会话、状态、有效期和当前目标版本；确认时锁定目标行。软删除和授权完成回执在同一事务提交。取消不删除；过期返回 `Expired`，目标变更或已删除返回 `Conflict`，均需重新申请。
7. 同一授权重复或并发决定返回已有结果，只提交一次删除。删除故事线保留其中的记录与计划。

写入和授权轮跳过回答缓存；成功变更更新数据版本。即使随后模型失败或网络断开，已经提交的回执仍可在聊天历史恢复。同轮相同操作与参数使用服务端生成的幂等键；后续消息的重试应先检查已有回执，避免再建一份。

## 验证

使用官方 SDK 进程内管道完成真实协议往返，测试 schema、参数拒绝、纠正上限、身份/快照隔离、取消释放与假模型工具调用。`AiMutationTests` 使用真实 PostgreSQL 验证回执、局部编辑、故事线节点、事务回滚、并发确认、用户隔离、取消／过期／版本冲突和模型随后失败。两端测试覆盖授权按钮、回执去重、会话恢复与切换及小屏键盘布局。此类测试不需要将个人记录发送给真实模型，也不访问高德。

参考：[官方进程内示例](https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0/samples/InMemoryTransport)、[MCP 工具规范](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/server/tools.mdx)、[JSON Schema 验证库](https://docs.json-everything.net/schema/basics/)。
