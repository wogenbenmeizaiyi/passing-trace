# PassingTrace 单机生产部署

该目录用于把 PassingTrace 从 GitHub 部署到一台 Linux 服务器。服务由 Docker Compose 管理，Caddy 提供反向代理和自动 HTTPS。

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

## 常用排查

```bash
docker compose --env-file deploy/.env -f deploy/compose.yml ps
docker compose --env-file deploy/.env -f deploy/compose.yml logs --tail=200 caddy identity events worker
```

备份至少应包含 PostgreSQL 导出、MinIO 数据卷和 Identity 证书卷。不要把 `deploy/.env`、证书或数据库备份上传到 GitHub。
