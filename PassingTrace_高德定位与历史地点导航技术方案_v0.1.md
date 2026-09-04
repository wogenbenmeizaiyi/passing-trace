# PassingTrace 高德定位与历史地点导航技术方案

> 版本：v0.1
> 状态：技术决策稿，尚未进入实现
> 适用端：Android Flutter 主客户端、Events API、AI Worker 与 AI 问答
> 目标：保存可信地点，让 AI 按用户检索历史地点，并在用户确认后导航回曾经去过的地方

---

## 1. 目标与非目标

本功能不是持续追踪用户行程，而是为一条 Event 保存用户主动确认的发生地点。

V1 目标：

- 创建或编辑记录时获取一次当前位置。
- 用户可以选择高德 POI，也可以手动修改地点名称。
- 地点随 SourceRevision 保存，AI 不能覆盖用户确认的位置。
- 地点名称、地址、行政区和 POI 信息参与检索、聚合与 AI 问答。
- AI 可以回答“我上次去的那家店在哪里”等问题。
- AI 找到历史地点后返回结构化导航操作；只有用户点击“导航”才调起高德地图。
- 所有查询严格按 JWT `sub` 隔离，模型不能指定 `userId`。

V1 不做：

- 后台持续定位、轨迹采集和围栏监控。
- 在 PassingTrace 内实现完整的逐向导航。
- 自动推断并保存家庭、公司等敏感地点。
- 让大模型生成经纬度、导航 URI 或直接启动外部应用。
- 根据图片 EXIF 静默提取位置。
- 海外地图坐标体系的完整兼容。

---

## 2. 核心原则

### 2.1 用户确认的位置属于 Source

位置存在三种可信等级：

1. **用户确认的定位或 POI**：属于 Source，可用于导航。
2. **用户手动输入的地点**：属于 Source，但没有坐标时只能搜索，不能直接导航。
3. **AI 从正文或图片中提取的地点**：属于 Semantic，只能辅助检索，不能自动成为导航目标。

AI 不得用“正文里提到了杭州”覆盖用户选择的“西湖文化广场”，也不得把推测地点升级为精确坐标。

### 2.2 导航目标必须可追溯

每一个导航目标必须能够追溯到：

```text
当前用户
  → 已检索到的 Event
  → 对应 SourceRevision
  → 用户确认的 EventLocation
  → 高德 POI ID 或确认过的 GCJ-02 坐标
```

没有 Source 证据、已经删除、已经过期或属于其他用户的位置，一律不能生成导航操作。

### 2.3 模型只选择证据，不构造坐标

大模型可以表达“应该导航到哪条历史记录中的地点”，但不能自行填写经纬度。服务端工具根据受控的 `locationId` 读取导航目标，客户端再构造高德 URI。

---

## 3. 用户体验

### 3.1 创建或编辑记录

地点区域提供：

- “获取当前位置”按钮。
- 当前定位状态、精度和失败原因。
- 当前位置附近的 POI 候选。
- 搜索地点和地图选点入口。
- 已选地点名称、简短地址和“清除”操作。
- 手动输入地点名称的降级能力。

默认只做一次前台定位。离开页面、定位完成或用户取消后立即停止并销毁定位客户端。

V1 每条 Event 只展示一个主要地点；底层采用可扩展的 `EventLocation` 集合模型，后续可增加出发地、目的地和途经点。

### 3.2 记录详情

存在可信坐标时展示地点名称、简短地址、“在地图中查看”和“导航到这里”。只有地点文字而没有可信坐标时，展示“在高德搜索”，不展示直接导航。

### 3.3 AI 问答

示例：

```text
用户：我上次和朋友吃烤肉的地方在哪里？

AI：你最近一次相关记录是“涩谷烤肉”，地点为涩谷某某店。
    [查看记录] [导航到这里]
```

导航按钮不是 Markdown 链接，而是服务端返回的结构化 `navigation` action。用户点击后，客户端再次校验目标仍有效，再调起高德地图。

如果检索到多个可能地点，AI 必须列出候选记录供用户选择，不能默认选一个并直接导航。

---

## 4. 高德接入边界

### 4.1 V1 使用能力

- 高德 Android 定位能力：单次前台定位。
- 逆地理编码与附近 POI：把坐标转换为用户可确认的地点。
- 地图/POI 选择：第二阶段加入，不阻塞第一阶段单次定位。
- 高德地图手机版 URI：查看地点或发起路线规划。

V1 不集成高德导航 SDK。路线规划与导航交给已安装的高德地图 App；未安装时降级到高德 Web URI。这样可以减少包体、权限、合规和维护成本。

### 4.2 Key 与签名

Android Key 必须绑定：

- 应用包名：`com.passingtrace.passingtrace_mobile`。
- Debug APK 的 SHA1。
- Release APK 的 SHA1。

Debug 与 Release 使用不同签名，发布前必须验证 Release Key。Android SDK Key 不放入 AppHost User Secrets；它通过 `android/local.properties` 的 `AMAP_ANDROID_KEY` 和 Manifest Placeholder 注入，仓库只保留占位符。后端 POI 搜索使用独立 Web 服务 Key，通过 AppHost 参数 `amap-web-service-key` 注入 `Amap__WebServiceKey`，并进入 User Secrets/K8s Secret。

### 4.3 隐私合规与权限

- 用户同意包含高德 SDK 说明的隐私政策后，才能初始化 SDK。
- 只在用户点击定位或地图选点时申请前台定位权限。
- 拒绝权限时保留手动填写和地点搜索能力。
- V1 不申请后台定位权限。
- 定位完成后停止并销毁客户端，不在后台继续监听 Wi-Fi、基站或 GPS。
- 日志、Trace、错误上报和 AI Prompt 不记录精确经纬度、完整地址、Key 或原始定位响应。

官方参考：

- [Flutter 定位插件接口](https://lbs.amap.com/api/flutter/guide/positioning-flutter-plug-in/interface-info)
- [Android 定位 Key 与签名](https://lbs.amap.com/api/android-location-sdk/guide/create-project/get-key/)
- [高德 SDK 合规使用方案](https://lbs.amap.com/api/compliance-center/check-and-reference/sdkhgsy)
- [Android 路径规划 URI](https://lbs.amap.com/api/amap-mobile/guide/android/route)
- [URI API 概述](https://lbs.amap.com/api/uri-api/summary)

---

## 5. 坐标与精度

### 5.1 坐标系

所有坐标必须显式记录坐标系：

```text
GCJ02
WGS84
UNKNOWN
```

高德返回的国内坐标保存为 `GCJ02`。调起高德地图时，已是高德坐标的目标按“不需要再次偏移”处理。禁止仅凭字段名猜测坐标系，也禁止对同一坐标重复转换。

### 5.2 数值与精度

- 数据库存储使用足够精度的 decimal/numeric，不使用字符串。
- 纬度范围必须为 `[-90, 90]`，经度范围必须为 `[-180, 180]`。
- 同时保存定位精度 `accuracyMeters`。
- 精度较差时 UI 明确提示用户确认，不自动吸附到某个 POI。
- AI Prompt 默认只接收地点名称与行政区，不接收精确坐标。

---

## 6. 数据模型

### 6.1 EventLocation：不可变位置事实快照

`EventLocation` 绑定特定 SourceRevision。Event 修改地点后创建新修订，旧位置快照保留用于审计，但不进入当前搜索。

建议字段：

| 字段 | 类型 | 说明 |
|---|---|---|
| `id` | long | 位置快照 ID |
| `user_id` | long | 强制用户隔离 |
| `event_id` | long | 所属 Event |
| `source_revision` | int | 所属 Source 修订 |
| `role` | enum | V1 固定 `primary`，预留 origin/destination/waypoint |
| `source` | enum | `amap_location`、`amap_poi`、`manual` |
| `name` | string | 用户确认的地点名称 |
| `formatted_address` | string? | 展示地址 |
| `province/city/district/township` | string? | 行政区文本 |
| `ad_code` | string? | 高德行政区划编码 |
| `provider` | string? | `amap` |
| `provider_poi_id` | string? | 高德 POI ID |
| `provider_poi_type` | string? | POI 类型 |
| `latitude/longitude` | decimal? | 坐标 |
| `coordinate_system` | enum | `GCJ02/WGS84/UNKNOWN` |
| `accuracy_meters` | decimal? | 定位精度 |
| `captured_at` | timestamptz? | 定位发生时间 |
| `confirmed_at` | timestamptz | 用户确认时间 |
| `is_user_confirmed` | bool | V1 导航目标必须为 true |
| `metadata` | jsonb | 经过白名单清洗的低频扩展字段 |

约束：

- `user_id + event_id + source_revision` 必须一致。
- 经纬度必须同时存在或同时为空。
- 有坐标时必须有坐标系。
- `provider_poi_id` 不能作为全局唯一键。
- 公共响应不返回未经筛选的高德原始响应。

### 6.2 UserPlace：可重建的用户地点索引

`UserPlace` 是用户范围内的派生索引，用于“去过哪些地方”“第一次去”“上次去”等查询，不是 Source。

建议字段：

```text
id
user_id
canonical_name
provider
provider_poi_id
latitude / longitude / coordinate_system
ad_code
visit_count
first_visited_at
last_visited_at
search_text
embedding(1024)
updated_at
```

去重规则：

1. 同一用户、同一 provider + POI ID，视为同一地点。
2. 没有 POI ID 时，使用标准化名称、行政区和小范围空间距离生成候选。
3. 仅名称相同或仅 AI 推断相近时不能自动合并。
4. 用户纠正或拆分优先于自动归一化。

`UserPlace` 可从当前有效的 `EventLocation` 重建；删除 Event 后必须同步移除引用并刷新次数和首末访问时间。

### 6.3 AI 推断地点

AI 从正文或图片识别的地点继续保存为 `SemanticMention(category=location)`，并记录断言类型、置信度和证据。它可以进入检索文本，但不能写入 `EventLocation.latitude/longitude`，也不能单独生成导航按钮。

---

## 7. Event 与接口契约

### 7.1 Event 写入

Event 创建和修改请求增加可选 `locations`。V1 最多一个 `primary`：

```json
{
  "title": "西湖散步",
  "rawContent": "沿湖走了一圈。",
  "happenedAt": "2026-08-23T18:30:00+08:00",
  "timezone": "Asia/Shanghai",
  "locations": [
    {
      "clientLocationId": "11e5a5d2-13e0-46cf-b7fd-d16a848df000",
      "role": "primary",
      "source": "amapPoi",
      "name": "西湖风景名胜区",
      "formattedAddress": "浙江省杭州市西湖区",
      "adCode": "330106",
      "provider": "amap",
      "providerPoiId": "...",
      "latitude": 30.249,
      "longitude": 120.143,
      "coordinateSystem": "GCJ02",
      "accuracyMeters": 18.2,
      "capturedAt": "2026-08-23T18:29:40+08:00",
      "isUserConfirmed": true
    }
  ]
}
```

服务端必须忽略客户端提供的用户及修订身份，校验坐标、坐标系、长度、数量和字段组合，使用 `clientLocationId` 保障重试幂等，并将位置、Event、SourceRevision 与 Outbox 在同一事务提交。地点变化必须生成新的 SourceRevision 和 AI 重算任务。

### 7.2 Event 响应

Event 响应增加 `locations[]`，只返回当前 SourceRevision 的位置。历史修订位置不通过普通详情接口暴露。

### 7.3 导航目标

新增只读接口：

```http
GET /api/v1/events/{eventId}/locations/{locationId}/navigation-target
Authorization: Bearer <access_token>
```

只允许读取当前用户、当前有效 Event 中用户确认且含可信坐标的位置。响应为结构化数据，不直接返回任意 URI：

```json
{
  "eventId": 42,
  "locationId": 7,
  "name": "西湖风景名胜区",
  "latitude": 30.249,
  "longitude": 120.143,
  "coordinateSystem": "GCJ02",
  "providerPoiId": "..."
}
```

Flutter 客户端使用专门的 `NavigationLauncher` 生成高德 Intent/URI；不可把模型输出直接传给系统 URI。

---

## 8. AI 分析与搜索

### 8.1 Worker 处理

Event 创建或地点修订后，Worker：

1. 读取当前 SourceRevision 的 EventLocation。
2. 把用户确认地点作为确定性事实发布，不要求 Qwen 再猜一次。
3. 将地点名称、地址、行政区、POI 类型加入 `EventSearchIndex.RetrievalText`。
4. 将地点文本加入当前 Event 的 embedding 输入。
5. 更新用户范围的 `UserPlace` 和访问次数。
6. 更新用户数据水位，使旧问答缓存失效。

模型提取结果与 Source 冲突时，以 Source 为准，并保留冲突信息供调试，不修改位置事实。

### 8.2 搜索能力

`SearchMyRecords` 扩展以下内部过滤参数：

```text
placeQuery
adCode
centerLatitude / centerLongitude / coordinateSystem
radiusMeters
```

地点候选排序合并 POI ID 精确匹配、行政区与标准化地点匹配、`pg_trgm` 模糊匹配、EventSearchIndex 向量召回，以及经纬度包围盒预筛选后的 Haversine 距离排序。

V1 不强制引入 PostGIS。数据量和空间查询复杂度明显增长后，再评估 PostGIS geography 索引。

### 8.3 新增只读 Agent Tools

在现有工具基础上增加：

- `SearchMyPlaces`：按自然语言、行政区、时间和访问次数搜索当前用户的历史地点。
- `GetMyPlaceEvidence`：读取地点关联的 Event 标题、时间和 Source 证据。
- `GetNavigationTarget`：只接受本轮已经检索并授权的 `locationId`，返回结构化导航目标。

这些工具不接受 `userId`，统一从服务端 `CurrentUserContext` 获取当前用户。

### 8.4 回答与导航 Action

AI 回答仍以文本和 Event 引用为主。导航使用单独的 SSE 事件：

```text
delta
evidence
action
done
error
```

`action` 示例：

```json
{
  "type": "navigation",
  "label": "导航到西湖风景名胜区",
  "eventId": 42,
  "locationId": 7
}
```

`action` 中不包含模型提供的经纬度。用户点击后，App 调用导航目标接口取得当前有效坐标，再启动高德。

### 8.5 典型问答链路

```text
“导航到我上次吃烤肉的地方”
  → CurrentUserContext 取得当前用户
  → SearchMyRecords / SearchMyPlaces 找到候选
  → GetMyPlaceEvidence 核实记录与时间
  → 候选唯一：返回说明 + navigation action
  → 候选多个：要求用户选择，不生成默认导航
  → 用户点击导航
  → API 重新校验 locationId 与用户归属
  → App 调起高德地图
```

---

## 9. 安全与隐私

- 所有 Location Repository 查询必须把 `user_id` 作为必要条件。
- 跨用户访问统一返回 404，避免枚举位置 ID。
- 精确位置不写入日志、OpenTelemetry Attribute、异常文本和 Redis Key。
- Redis 问答缓存只保存受控的地点展示文本和位置 ID，不缓存导航 URI。
- 发送给 Qwen 的默认上下文不包含精确经纬度；导航坐标由确定性工具在模型外处理。
- 用户删除 Event 后，其位置索引、向量、UserPlace 引用和缓存必须失效。
- 用户可以清除单条记录的位置，而不必删除整条 Event。
- 家庭、公司、学校等敏感标签不能由 AI 自动确认为长期记忆。
- App 调起外部高德地图前必须有明确的用户点击操作。

---

## 10. 异常与降级

| 场景 | 行为 |
|---|---|
| 用户拒绝定位权限 | 允许手动输入或搜索地点 |
| 定位超时/无网络 | 保留表单内容，支持重试和手动输入 |
| 精度过低 | 显示精度提示，由用户确认，不自动选择 POI |
| 高德 Key/SHA1 不匹配 | 显示定位服务配置错误，日志只记录错误码 |
| 高德地图未安装 | 使用高德 HTTPS URI 或提示复制地址 |
| 只有地点文字、没有坐标 | 可参与 AI 搜索，但不提供直接导航 |
| AI 只推断出地点 | 标记为语义候选，不提供导航 |
| 历史位置已删除或修订 | 点击 action 时提示重新检索 |
| 多个历史地点相似 | 让用户选择记录，不自动导航 |

---

## 11. 测试计划

### 11.1 Flutter

当前工程为 Android-only，定位层使用 Kotlin `MethodChannel` 直接接入高德 Android 定位 SDK `11.2.100`，不依赖旧版 Flutter 地图组件。Flutter 只负责隐私确认、前台权限请求、单次定位状态和地点候选交互。

- 首次授权、拒绝、永久拒绝和重新授权。
- 单次定位成功、超时、低精度和取消。
- POI 选择、手动地点和清除地点。
- Debug/Release Key 与签名验证。
- 高德已安装、未安装和 URI 调起失败。
- AI `navigation` action 展示、点击确认和过期目标处理。
- App 生命周期切换后定位客户端已停止并销毁。

### 11.2 API/数据库

- 坐标范围、坐标系、字段组合和长度校验。
- Event 与 EventLocation 同事务及幂等重试。
- 修改地点生成新 SourceRevision，旧位置不进入当前搜索。
- 删除、归档和用户隔离。
- 两个用户交叉访问 Event、Location、Place 与导航目标全部失败。
- 无坐标位置不能获取导航目标。

### 11.3 AI/搜索

- POI ID、地点名、行政区、模糊词、向量和半径搜索。
- Source 地点优先于冲突的 AI 推断地点。
- “上次/第一次/最常去”按用户时区和发生时间计算。
- 多候选时不自动生成导航。
- 模型伪造的 locationId、坐标或跨用户请求被工具拒绝。
- 删除/修订位置后数据水位使旧缓存失效。

---

## 12. 分阶段实施

### 阶段一：位置事实

- 高德 Android Key、隐私合规和前台单次定位。
- EventLocation、SourceRevision 快照和 Event 接口。
- 记录创建/编辑/详情页的位置交互。

### 阶段二：地点检索

- EventSearchIndex 地点字段与 embedding。
- UserPlace 派生索引。
- SearchMyRecords 地点过滤和 SearchMyPlaces。
- 按地点回答 AI 问题并返回 Event 证据。

### 阶段三：历史导航

- 导航目标接口与 SSE `action`。
- Flutter 导航确认卡片和 NavigationLauncher。
- 高德 App URI 与 Web URI 降级。

### 阶段四：增强体验

- 地图选点、附近 POI、多个地点角色。
- 地点纠错、合并与拆分。
- 足迹地图、首次到访和地点趋势洞察。
- 数据规模达到需求后评估 PostGIS。

---

## 13. 验收标准

- 用户不授权后台定位也能完整使用记录功能。
- 定位只在用户主动操作时运行，并在结束后释放。
- 用户确认地点随 SourceRevision 保存，AI 无法修改。
- 地点可以按名称、行政区、语义和距离参与当前用户的 AI 搜索。
- AI 回答历史地点时提供可点击的 Event 证据。
- 导航目标唯一且有 Source 坐标时才展示导航按钮。
- 用户点击后才调起高德；模型不能构造或替换坐标。
- 删除或修改位置后，搜索、记忆、缓存和导航目标及时失效。
- 任意跨用户位置读取和导航请求均不可成功。
