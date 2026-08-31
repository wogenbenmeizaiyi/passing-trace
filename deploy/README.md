# PassingTrace 单机生产部署

该目录用于把 PassingTrace 从 GitHub 部署到一台 Linux 服务器。GitHub Actions 构建镜像并推送到 GHCR，服务端由 Docker Compose 管理，Caddy 提供反向代理和自动 HTTPS。

## 域名

在域名控制台添加以下记录，全部指向服务器公网 IPv4：

| 主机记录 | 类型 | 值 |
| --- | --- | --- |
| `@` | `A` | `154.36.164.76` |
| `www` | `A` | `154.36.164.76` |
| `auth` | `A` | `154.36.164.76` |
| `files` | `A` | `154.36.164.76` |

Caddy 需要公网放行 TCP `80`、TCP `443` 和可选的 UDP `443`。PostgreSQL、Redis、MinIO API 与 MinIO Console 不应对公网开放。

## 首次部署

```bash
cd ~/projects
git clone --branch codex/media-ai-memory https://github.com/wogenbenmeizaiyi/passing-trace.git
cd passing-trace
cp deploy/.env.example deploy/.env
chmod 600 deploy/.env
```

编辑 `deploy/.env`，填入随机密码、百炼 Key 与高德 Web 服务 Key。不要提交该文件。

```bash
sh deploy/update.sh
```

部署完成后访问：

- Web 与 API：`https://passingtrace.com`
- Identity：`https://auth.passingtrace.com`
- 私有对象存储入口：`https://files.passingtrace.com`

## 后续更新

代码推送到 GitHub 后，在服务器执行：

```bash
cd ~/projects/passing-trace
sh deploy/update.sh
```

脚本会从部署分支执行 `git pull --ff-only`，重新构建镜像并滚动替换容器。数据库、附件、Identity 证书和 Caddy 证书都存放在 Docker Volume 中，不会因容器重建而删除。

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
- `MINIO_SECRET_KEY`
- `CERTIFICATE_PASSWORD`
- `REGISTRATION_BOOTSTRAP_CODE`
- `QWEN_API_KEY`
- `AMAP_WEB_SERVICE_KEY`

工作流中的服务器地址、用户、部署目录和域名不是密钥，直接保存在工作流中。CI 使用专用 SSH Key，不应使用个人日常 SSH 私钥。

## 常用排查

```bash
docker compose --env-file deploy/.env -f deploy/compose.yml ps
docker compose --env-file deploy/.env -f deploy/compose.yml logs --tail=200 caddy identity events worker
```

备份至少应包含 PostgreSQL 导出、MinIO 数据卷和 Identity 证书卷。不要把 `deploy/.env`、证书或数据库备份上传到 GitHub。
