# Linux 同步服务器

此目录是当前 `39.105.200.142:8443` 同步服务器的公开实现，不包含任何私钥、管理员密码或 HMAC 密钥。

## 运行结构

- Nginx 只公开 `/health`、`/blacklist.json`、`/blacklist.sig`、`/signing-public.xml`、`/updates/*`，并把 `POST /admin/upload` 转发到回环地址。`/health` 仅返回无正文的 204，且不写访问日志，供客户端每秒显示实际同步服务器延迟。
- 管理员上传服务仅监听 `127.0.0.1:8090`，执行 HMAC 鉴权、时间窗、Nonce 防重放、限流、基础版本冲突检测、严格 JSON 校验、备份和 RSA 重新签名。
- 刷新服务每 5 分钟重新签名权威黑名单，并镜像最新稳定 GitHub Release。已有 `blacklist.json` 永远不会被 GitHub 数据覆盖；只有文件缺失时才会从固定公开源初始化。
- 上传和刷新服务通过 `/var/lib/wtuidguard/data.lock` 共用同一把 `flock` 写锁。

## 生产路径

```text
/srv/wtuidguard/www
/var/lib/wtuidguard
/var/log/wtuidguard
/etc/wtuidguard/secrets/signing-private.pem
/etc/wtuidguard/secrets/admin-upload-hmac.key
/opt/wtuidguard
```

`admin-upload-hmac.key` 必须是客户端管理员密码经既定 PBKDF2 参数派生后的原始 32 字节密钥。两个密钥文件应为 `root:wtuidguard`、权限 `0640`；不得提交到 Git。

## 测试

```bash
python3 -m py_compile admin_upload_server.py wtuidguard_common.py refresh_content.py
python3 -m unittest discover -s tests -v
```

## 安装

在已恢复数据、密钥并创建 `wtuidguard` 系统用户的服务器上执行：

```bash
sudo bash install.sh
```

安装脚本会先检查必要文件和密钥格式，保存现有配置副本，验证 Python 与 Nginx 配置，然后启用上传服务、刷新定时器和 Nginx。配置备份保存在 `/var/lib/wtuidguard/deploy-backups/`。

服务器继续使用 HTTP 8443 是为了兼容已经发布的客户端；内容真实性由客户端内置 RSA 公钥和 SHA-256 强制验证。管理员密码不会通过网络发送，上传正文也不会写入审计日志。
