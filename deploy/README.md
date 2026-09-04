# PassingTrace 单机生产部署

该目录用于把 PassingTrace 从 GitHub 部署到一台 Linux 服务器。GitHub Actions 构建镜像并推送到 GHCR，服务端由 Docker Compose 管理，Caddy 提供反向代理和自动 HTTPS。

## 域名

在域名控制台添加以下记录，全部指向服务器公网 IPv4：

| 主机记录 | 类型 | 值 |
| --- | --- | --- |
| `@` | `A` | `154.36.164.76` |
| `www` | `A` | `154.36.164.76` |
| `auth` | `A` | `154.36.164.76` |

Caddy 需要公网放行 TCP `80`、TCP `443` 和可选的 UDP `443`。PostgreSQL 和 Redis 不应对公网开放。生产附件不经过 Caddy 或云服务器，客户端通过 API 颁发的短效预签名 URL 直传、直读雨云对象存储。

## 雨云 S3 对象存储

本地开发仍由 AppHost 启动 MinIO，生产环境不启动 MinIO。雨云桶保持关闭「公共访问」，当前实例的配置是：

- API Endpoint：`https://cn-nb1.rains3.com`
- Bucket：`passingtrace`
- 虚拟主机访问域名：`https://passingtrace.cn-nb1.rains3.com`
- Region：`us-east-1`（S3 签名兼容值）
- `ForcePathStyle=false`

如果雨云控制台提供 CORS 设置，配置：

- 来源：`https://passingtrace.com`
- 方法：`GET`、`HEAD`、`PUT`
- 允许请求头：`*`
- 暴露响应头：`ETag`

雨云 Access Key / Secret Key 只放在 GitHub production Environment Secrets 中，不进入 App 或仓库。如果密钥曾出现在截图、日志或聊天中，必须立即重新生成。

APK 可以与附件放在同一个私有桶的 `releases/android/` 前缀下，由后端更新接口生成短效下载 URL；不需要开启公共访问，也不需要让 APK 经过云服务器。
Web 产品首页的下载按钮访问 `/api/v1/app-updates/android/latest/download`，Events API 会读取 `releases/android/latest.json` 并重定向到短效预签名地址。发布工作流必须最后更新该清单。

## 首次部署

```bash
cd ~/projects
git clone --branch codex/media-ai-memory https://github.com/wogenbenmeizaiyi/passing-trace.git
cd passing-trace
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

编辑 `deploy/.env`，填入随机密码、雨云 S3 密钥、MiniMax Key、百炼 Key 与高德 Web 服务 Key。`AMAP_MCP_KEY` 可选，留空时 AI 高德工具复用 Web 服务 Key。不要提交该文件。AI 问答和图片/文本语义分析使用 MiniMax-M3；向量仍使用百炼，后续可通过 `AiModels` 配置独立切换各角色 Provider。

```bash
sh deploy/update.sh
```

部署完成后访问：

- Web 与 API：`https://passingtrace.com`
- Identity：`https://auth.passingtrace.com`

附件桶没有公开入口，只能通过 Events API 获取当前用户的短效访问地址。

## 后续更新

代码推送到 GitHub 后，在服务器执行：

```bash
cd ~/projects/passing-trace
sh deploy/update.sh
```

脚本会从部署分支执行 `git pull --ff-only`，重新构建镜像并滚动替换容器。数据库、Identity 证书和 Caddy 证书保存在 Docker Volume 中，附件和 APK 保存在雨云对象存储中，都不会因容器重建而删除。

## GitHub Actions 自动部署

工作流 `.github/workflows/deploy-production.yml` 在 `codex/media-ai-memory` 分支推送时执行：

1. 构建 Caddy/Web、Identity、Events 与 Worker 镜像并推送至 GHCR。
2. 使用 `production` Environment Secrets 建立 SSH 连接。
3. 将本次提交对应的镜像地址和运行时密钥写入服务器 `deploy/.env`。
4. 拉取镜像并执行 Compose 滚动更新。

在 GitHub 仓库 `Settings → Environments → production` 中配置：

- `DEPLOY_SSH_PRIVATE_KEY`
- `POSTGRES_PASSWORD`
- `REDIS_PASSWORD`
- `S3_ACCESS_KEY`
- `S3_SECRET_KEY`
- `CERTIFICATE_PASSWORD`
- `REGISTRATION_BOOTSTRAP_CODE`
- `QWEN_API_KEY`
- `MINIMAX_API_KEY`
- `AMAP_WEB_SERVICE_KEY`
- `AMAP_MCP_KEY`（可选；未配置时复用 `AMAP_WEB_SERVICE_KEY`）

并在 production Environment Variables 中配置：

- `S3_ENDPOINT`：`https://cn-nb1.rains3.com`
- `S3_PUBLIC_ENDPOINT`：`https://cn-nb1.rains3.com`
- `S3_BUCKET`：`passingtrace`
- `S3_REGION`：`us-east-1`

工作流中的服务器地址、用户、部署目录和域名不是密钥，直接保存在工作流中。CI 使用专用 SSH Key，不应使用个人日常 SSH 私钥。

## 常用排查

```bash
docker compose --env-file deploy/.env -f deploy/compose.yml ps
docker compose --env-file deploy/.env -f deploy/compose.yml logs --tail=200 caddy identity events worker
```

备份至少应包含 PostgreSQL 导出、雨云对象存储数据和 Identity 证书卷。不要把 `deploy/.env`、证书或数据库备份上传到 GitHub。
