# 用户中心与个人资料

Web 顶部头像、App 菜单头像和设置入口进入用户中心；编辑头像、昵称、简介，登录名和加入时间只读。资料默认仅当前登录用户可见，不进入记录、故事线或 AI 记忆。

## 合同

- Identity `GET /api/v1/account/profile`：username、nickname、bio、createdAt、version、hasAvatar。
- Identity `PUT /api/v1/account/profile`：认证 multipart，nickname、bio、version；可选 avatar（二进制）或 removeAvatar，不能同时指定。保存前校验版本，冲突 409，缺少版本 428。
- Identity `GET /api/v1/account/avatar`：认证读取当前用户的 PNG，不支持传入其他用户 ID 或对象键。仅返回私有/no-store 响应，客户端使用内存缓存，退出/换账号时清理。

两端裁剪生成 PNG，再由服务端实际解码、限制像素、重绘为 512×512 PNG，移除原始 EXIF/GPS。支持 JPEG、PNG、WebP，最大 5MB；不接收任意外链。头像先成功存入 S3 再更新资料，明确的并发失败清理新对象，数据库成功后清理旧对象。提交期间连接中断时结果可能不确定，保留新对象避免误删已经生效的头像；重新加载资料可确认结果。清理失败仅记录不含对象键的警告，需运维清理未引用头像；不影响保存成功的资料。

## 部署

新增 Identity 数据库迁移 `AddAccountProfile`。旧账号昵称回退登录用户名，头像回退主题默认图案。

Identity 新增复用现有 ObjectStorage 配置（Endpoint、AccessKey、SecretKey、Bucket、Region、ForcePathStyle、CreateBucketIfMissing），AppHost 和 deploy/compose.yml 已接线。无需新账户或新密钥。生产 S3 策略需允许 Identity 在 `identity/avatars/` 下读写和删除；不要公开整个桶。头像通过 Identity API 中转，不依赖浏览器到 S3 的 CORS。

只更新前端、未更新 Identity 或未执行迁移时不能保存。发布时需要同时更新 Identity、Web 和 App。资料不会写进 ID Token；客户端登录、打开资料页、返回前台时重新获取，编辑时保留最初的版本以检测跨端冲突。

## 验证

Identity 集成测试覆盖真实令牌、旧账号回退、更新、冲突、头像隔离、图片校验、存储失败回滚。Web/Flutter 测试覆盖协议、昵称校验、保存、冲突、返回及裁剪计算。可在 Flutter 测试中添加 `--dart-define=PROFILE_SCREENSHOTS=true --update-goldens`，将小屏主题检查截图生成到根目录 artifacts，文件不提交。中文截图可另外传入 `--dart-define=PROFILE_SCREENSHOT_FONT=<本地中文字体路径>`，仅测试时加载，不复制或打包该字体。
