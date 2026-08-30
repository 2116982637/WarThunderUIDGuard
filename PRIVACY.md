# 隐私说明

War Thunder UID Guard 不收集遥测、分析数据、广告标识符或设备指纹，也不会把黑名单和检测记录自动上传给项目维护者。

程序仅在用户主动操作或明确启用相应功能时进行以下网络通信：

- 监控功能只读取本机 `127.0.0.1:8111` 的 `/gamechat` 与 `/hudmsg`。
- “拉取同步”从项目专用只读服务器或公开 GitHub/CDN 后备源下载黑名单，并在本机验证数字签名或来源限制。
- “管理员上传”只写入当前 Windows 用户已经登录的 OneDrive 文件夹，随后由 Microsoft OneDrive 客户端处理上传。
- “同步昵称”访问 War Thunder 官方玩家查询页面，仅提交用户选择的 UID。
- “检查更新”访问项目专用更新服务器或本项目 GitHub Release。
- “申请添加”只调用用户的默认邮件程序生成邮件草稿；是否发送由用户决定。

本地黑名单、远程缓存和备份保存在 `%LOCALAPPDATA%\WarThunderUIDGuard`。删除该目录即可清除这些本地数据。游戏画面、游戏进程、内存、输入和游戏文件不在读取或修改范围内。

相关第三方服务分别适用其自身的隐私政策：Microsoft OneDrive、GitHub、jsDelivr、Gcore、Fastly 和 War Thunder 官方网站。
