# 星期八高德 AI 工具包技术方案 v1.0

## 目标与边界

高德 AI 工具包为问答提供实时地点、地理编码、天气、路线、距离、导航和专属地图能力。它与个人记录工具包并列注册，失败时只降级地图能力，不影响记录、历史地点和故事线问答。

第一版不接入 IP 定位、打车、订票、下单和支付，也不把高德查询结果自动保存成记录、地点或计划。记录表单原有地点搜索继续使用高德 REST API，不依赖 MCP。

## 结构

```text
AssistantService
  ├─ personal-records / IAiCapabilityPackage
  └─ amap / IAiCapabilityPackage
       ├─ AmapAiTools（稳定的内部工具合同）
       ├─ AmapMcpGateway（官方 Streamable HTTP MCP）
       └─ RedisAmapQuotaGuard（月度保护计数）
```

`IAiCapabilityPackage` 负责声明工具包的可用状态、能力名称和模型工具。供应商 MCP 的工具名与响应先在服务端适配，客户端只读取星期八自己的 `AmapPlaceEvidence` 和 `AssistantAction` 合同。

高德工具每轮最多调用 6 次。搜索类和 LBS 类的 Redis 月度计数 Key 只包含年月和类型，不包含关键词或坐标。默认保护上限分别为 4,500 和 135,000 次。

## 地点与会话规则

历史地点优先查询 `SearchMyPlaces`。只有名称或地址、没有可信坐标时，可以临时使用高德解析，但不得更新记录数据库。外部地点最多保存 3 个归一化候选到会话证据快照，供下一轮理解“第二个”或“刚才那个地方”。完整 MCP 原始响应不落库。

“附近”查询必须有用户明确提供的起点坐标；服务端地址和 IP 不可作为默认位置。高德证据统一标记 `source=amap-live`，个人记录地点生成的动作标记 `source=personal-record`。

地图、天气、路线和位置问题跳过 24 小时问答缓存。无网络搜索工具时，AI 不得把高德 POI 当作网友评价、套餐价格或攻略来源。

## 安全动作合同

SSE `action` 事件、会话证据和历史恢复都使用同一个类型化合同：

```json
{
  "type": "amap-navigation",
  "provider": "amap",
  "label": "导航到人民广场地铁站",
  "placeName": "人民广场地铁站",
  "address": "上海市黄浦区",
  "latitude": 31.232,
  "longitude": 121.475,
  "coordinateSystem": "GCJ02",
  "poiId": "p1",
  "source": "amap-live"
}
```

模型不能提供可执行 URL。Web 使用固定的 `https://uri.amap.com/navigation` 模板生成导航地址；Android 先生成固定 `amapuri://route/plan` 模板，无法唤起 App 时再使用固定 Web 模板。坐标系、经纬度范围、Provider 和动作类型均由客户端再次校验。专属地图只接受 HTTPS 且属于 `amap.com` 的链接。

## 配置

后端优先读取环境变量 `AMAP_MCP_KEY`，为空时使用 `Amap:WebServiceKey` 或 `AMAP_WEB_SERVICE_KEY`。真实 Key 只能进入 AppHost User Secrets、部署环境变量或 Secret，不得写入客户端、日志、Trace、错误消息或仓库配置。

```powershell
dotnet user-secrets set --project AppHost "Parameters:amap-web-service-key" "your-web-service-key"
dotnet user-secrets set --project AppHost "AMAP_MCP_KEY" "your-optional-mcp-key"
```

授权用户可调用 `GET /api/v1/ai/capabilities` 查看高德当前是否可用以及服务端允许的能力。接口不会返回 Key、MCP URL、额度余量或供应商原始合同。

## 验证重点

- 同名地点只返回最多 3 个候选，不擅自导航。
- 导航只接受本轮或近期证据中已有的候选，不接受模型自造坐标。
- 恶意 URI、非高德域名、无效坐标和非 GCJ02 动作被拒绝。
- 缺 Key、超时、额度耗尽或 MCP 工具变化时，个人记录问答仍能继续。
- 缓存命中和重新打开会话时仍能恢复相同的类型化动作卡片。
