# AI 工具协议与执行边界

## 当前调用链

```text
用户消息 → ChatClientAgent（模型原生 tools / tool_calls）
  ├─ 个人记录包 → MCP client → 进程内 JSON-RPC 管道 → MCP server → 参数校验 → 只读应用工具
  └─ 高德适配包 → 官方高德 Streamable HTTP MCP
```

内部 MCP 使用现有官方 `ModelContextProtocol 2.2.0` 客户端、服务端和 Stream transports，采用双方 SDK 支持的 `2026-07-28` 协议，实际执行工具发现、`tools/list`、`tools/call`。该版本支持直接返回数组等结构化内容，与个人记录工具的列表结果一致。不是从聊天正文中提取 JSON，也不是用提示词模拟 MCP。

内置工具没有新增 HTTP 路由、端口、命令执行器或第三方进程。每轮回答创建独立 MCP 会话；结束、失败或取消后释放客户端、服务端和管道。任一工具调用取消也会结束本轮连接，确保服务端查询不在后台继续运行。此实现暂不提供给外部 MCP 客户端连接。高德仍沿用现有外部 MCP 配置、协议协商和额度保护。

## 参数与错误

- 工具名、参数类型、必填、枚举以及校验注解从 .NET 工具定义生成标准 JSON Schema。
- `ValidatedMcpServerTool` 使用 JsonSchema.Net（Draft 2020-12）在执行前校验；拒绝未声明的根参数并启用格式校验。
- 模型不能传 `userId`、SQL、服务实例或取消令牌。无效参数不会执行查询，也不加入证据快照。
- 格式失败以标准 MCP `isError` 返回，只允许一次纠正；第二次无效或业务执行失败终止本轮，向用户返回中文提示。
- 可能查不到单条结果的工具使用非空 `{ result: 值或 null }` 容器，明确区分“未找到”和协议失败；数组与非空对象仍保留自然结构。
- 不把错误解释为零金额、空证据或成功，不自动重复执行成功调用。
- 参数通过校验不代表模型理解一定正确。时间范围解释、记录所有权、已检索证据限制等仍由应用服务执行，提示词只负责说明工具用途和回答方式。

## 身份、状态与隐私

MCP 服务端复用当前请求内已绑定的 `PersonalRecordTools`，而非创建新的 DbContext 或全局工具实例。用户身份来自已认证请求，不进入模型参数。预检索和工具调用共享同一证据快照，不同用户/请求之间不共享 MCP 会话。

同一会话工具调用串行执行，以保护 DbContext 和快照集合。内部协议不配置帧日志，错误不包含参数原值或底层异常。模型不获得任意 CLI/shell 执行权限。

## 扩展

`IAiCapabilityPackage.UsesInternalMcp` 默认为 `true`。新增内部只读工具包注册后走相同 MCP 会话、校验、错误和释放边界。高德因已经有外部 MCP 适配与配额策略，显式设为 `false`，避免重复包裹。

当前内置包只支持只读查询。未来涉及创建记录、订单或支付的工具，必须另行设计权限、确认、幂等与审计，不能直接当作只读工具加入。后端的确定性预检索、证据整理等应用逻辑仍可直接调用服务；模型自主选择的个人工具调用均经过 MCP。

## 验证

使用官方 SDK 进程内管道完成真实协议往返，测试 schema、参数拒绝、纠正上限、身份/快照隔离、取消释放与假模型工具调用。此类测试不需要将个人记录发送给真实模型，也不访问高德。

参考：[官方进程内示例](https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0/samples/InMemoryTransport)、[MCP 工具规范](https://github.com/modelcontextprotocol/modelcontextprotocol/blob/main/docs/specification/2026-07-28/server/tools.mdx)、[JSON Schema 验证库](https://docs.json-everything.net/schema/basics/)。
