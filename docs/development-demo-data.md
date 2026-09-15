# 本地开发示例数据

## 使用

开启 Docker Desktop 后，在仓库根目录启动：

```powershell
dotnet run --project AppHost/AppHost.csproj
```

Aspire 的 `development-demo-data` 一次性任务等待 Identity 和 Events API 就绪，使用 Web 客户端相同的 OIDC + PKCE 自动开发登录，给 `dev` 账号补齐数据。初次运行完成后刷新网页即可查看；以后每次启动只检查并补齐缺项。仅重新编译不会触碰数据库，已有数据仍然保留。

需要 Python 3.10+，无第三方 Python 依赖。Windows 使用 `python`，其他系统使用 `python3`。如果缺少 Python，其他本地服务仍可启动，安装后手动执行即可。

## 数据内容

- 30 条记录和计划，带统一的“开发示例”标签，包含分类、行为标签、日期和部分确认地点。
- 日期覆盖今年、去年、上个月、近期和未来，适合检查年份/月分组、分页和筛选。日期按首次创建时计算，此后不会随启动日期移动。
- 5 条故事线：西湖周末、朋友野餐、个人网站改版、十公里挑战、街角探店。
- 包含进行中/已完成状态、阶段、分支、并行与汇合，并保存初始 Web 布局；手机读取同一套语义节点。
- 全部是内置文字示例，不下载外部图片，不调用高德。通过正常应用服务生成修订、标签和搜索索引，并进入现有 AI 后台处理流程；如果本地 Worker 配置了真实模型 Key，首次分析仍会产生正常模型用量。

## 重复执行和保护

```powershell
python tools/seed_development_demo.py
# 自定义本地端口（仍只允许 loopback 地址）
python tools/seed_development_demo.py --identity-url http://localhost:56229 --api-url http://localhost:54934
```

- 使用版本化、用户隔离的固定幂等键，不通过标题判断，因此修改名称不会新增重复示例。
- 已有记录、正文修订、计划状态和故事线图均不重置；归档及软删除也保留。
- 手动新增的数据不受影响，脚本没有清空、覆盖或重置操作。
- 中途失败可以重新运行，已成功的部分不会重复写入。单条记录及其初始计划状态在同一事务创建，每条故事线通过现有事务服务保存。
- 如果某故事线依赖的示例记录被软删除，缺失的故事线不会强行创建，也不会恢复该记录。
- 本地停止 AppHost 不删除 PostgreSQL 持久卷。主动清理开发数据库或卷后，下一次启动会重新准备示例；本脚本不会主动清库。

## 开关与隔离

- AppHost：`DevelopmentDemo:Enabled=false` 禁用自动运行任务，例如 `dotnet run --project AppHost/AppHost.csproj -- --DevelopmentDemo:Enabled=false`。
- Events API：`DevelopmentDemo:Enabled=false` 关闭补齐接口。
- 默认账号：Identity 的 `DevelopmentAutoLogin:Username` 和 Events API 的 `DevelopmentDemo:Username` 必须一致，默认为 `dev`；禁用自动开发登录后脚本会提示无法登录，不会绕过认证。
- 接口为 `POST /api/v1/development/demo-data`，只在 `Development` 注册，必须使用已验证的 JWT，且账号必须匹配默认开发账号。生产环境即使误配启用选项也不注册路由。
- 脚本只接受本机地址，不输出密码、授权码或 Token；生产部署不包含这个 AppHost 任务。

## 验证

```powershell
python -m unittest discover -s tools/tests -p test_seed_development_demo.py
dotnet test tests/PassingTrace.Events.IntegrationTests --filter FullyQualifiedName~DevelopmentDemo
```

后端集成测试使用独立 PostgreSQL 测试容器，需要 Docker；不会操作正在运行的开发数据库。
