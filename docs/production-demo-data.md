# 正式环境演示数据

仓库提供 `tools/seed_production_demo.py`，用于给一个独立演示账号写入最多 36 条经过整理的中文记录，其中包含分类、行为标签、确认地点和 6 张演示图片。

脚本只调用公网 Identity、Events 和媒体上传 API，不直接连接生产数据库，也不持有 S3 密钥。图片在执行时从 Wikimedia Commons 下载，再通过 PassingTrace 的预签名上传流程复制到正式环境私有对象存储；记录正文会保留图片来源和许可页面。

## GitHub Actions 执行

1. 在 GitHub `production` Environment 增加 Secret：`DEMO_ACCOUNT_PASSWORD`。密码至少 8 位，不要与主账号或初始化注册码相同。
2. 先部署包含本功能的版本，使生产 Identity 用户上限变为 2。
3. 打开 Actions，手动运行 `Seed Production Demo Data`。
4. 默认用户名为 `passingtrace-demo`，记录数为 `36`。
5. 在 confirmation 输入 `seed-passingtrace-production-demo`。

脚本先用演示账号登录；账号不存在时才使用 production Environment 中已有的 `REGISTRATION_BOOTSTRAP_CODE` 注册。生产用户上限为 2，因此主账号和演示账号存在后，普通注册会继续保持关闭。

## 本地预览与执行

仅查看将要创建的数据，不访问网络：

```powershell
python tools/seed_production_demo.py --dry-run --count 36
```

需要直接从本机执行时，通过环境变量传入密码和初始化注册码，最后显式确认：

```powershell
$env:DEMO_ACCOUNT_PASSWORD = '<demo-password>'
$env:REGISTRATION_BOOTSTRAP_CODE = '<production-bootstrap-code>'
python tools/seed_production_demo.py --confirm seed-passingtrace-production-demo
```

重复执行不会不断追加同一批记录：脚本会先读取演示账号已有标题，并跳过同名的种子记录；服务端创建请求还带有固定幂等键。脚本不会清空、修改或删除任何已有记录。

## 图片来源

当前图片全部来自 Wikimedia Commons。脚本通过 MediaWiki API 读取实际下载地址和许可元数据：

- `Chinese food in Harbin.jpg`（CC0）
- `Chinese Food in Street Market of Hong Kong.jpg`（CC0）
- `Kiosk of rental bikes.jpg`（CC0）
- `Design Museum interior (30764512834).jpg`（CC0）
- `DC Coffee Shop.jpg`（CC0）
- `West lake twilight.jpg`（以 Commons 返回的实时许可元数据为准）

若 Commons 文件被删除、改名或 MIME 变为非 JPEG/PNG/WebP，脚本会终止，不会绕过媒体校验。
