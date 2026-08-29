# War Thunder UID Guard v0.5.0 Safe

Windows 桌面伴侣程序，用 UID 保存本地黑名单，并通过玩家昵称历史监听 War Thunder 的公开本地接口 `127.0.0.1:8111`。

## 能做什么

- UID 作为记录主键，保存玩家昵称与备注；手动编辑时可填写多个已知昵称。
- 每约 0.9 秒轮询 `/gamechat` 与 `/hudmsg`。
- 首次连接超过 10 秒仍未成功时自动停止监控，并明确显示“连接失败”。
- 支持简体中文和英文，可在运行时切换并记住语言选择；首次运行跟随 Windows 显示语言。
- 可选远程手动同步：其他用户优先从公开 GitHub 镜像匿名拉取，管理员仍可通过 Windows 已登录的 OneDrive 文件夹上传。
- 其他用户可以通过“申请添加”生成一封发送到 `elainasamae@outlook.com` 的邮件草稿，由管理员审核 UID、昵称和备注。
- 可在后台通过战争雷霆官网查询选中 UID 的当前昵称；官网返回唯一昵称时自动替换旧昵称，不弹出网页，也不需要第二次确认。
- 内置应用更新：点击“检查更新”即可从本项目 GitHub Release 自动下载、校验、替换并重启，无需手动解压覆盖。
- 始终保留 `%LOCALAPPDATA%\WarThunderUIDGuard\blacklist.json` 本地副本，并保存最近 10 个自动备份。
- 黑名单昵称出现在聊天或 HUD 战斗事件时，通过 Windows 通知区域提醒并播放系统提示音。
- OneDrive 同步关闭时，数据只保存在本机；同步失败时自动回退到本地副本。
- 不读取游戏画面、进程或内存，不注入进程，不监听或模拟输入，不修改游戏文件。
- HTTP 客户端强制只允许 `127.0.0.1:8111` 的 `/gamechat` 与 `/hudmsg`，并禁用代理和重定向。

## 重要限制

War Thunder 的 8111 接口不会提供对局完整名单，也不会提供参与者账号 UID。聊天与 HUD 事件仅提供昵称。因此本工具无法仅凭 UID 在玩家刚进入对局时立刻识别，也无法自动知道对方刚改过的新昵称。

为了避免误导，添加记录时必须同时填写至少一个已知昵称。程序会匹配该 UID 的所有昵称历史。得到对方新昵称后，再用同一 UID 添加即可合并。

## 运行

运行发布目录中的 `WarThunderUIDGuard.exe`，添加记录，然后点击“开始监控”。游戏尚未运行或不在对局时，状态会显示“等待游戏进入对局”。

## 远程数据同步

勾选程序顶部的“远程手动同步”只会启用两个按钮，不会自动上传或拉取。

“拉取同步”首先从本项目的公开 GitHub `data/blacklist.json` 下载，失败时自动尝试 jsDelivr CDN，最后才回退到 OneDrive 网页下载。GitHub 和 CDN 路径不需要登录、浏览器账户或 WebView2，避免 OneDrive 共享权限与页面结构导致其他电脑拉取失败。

管理员先在 Windows 中登录并正常运行 OneDrive。“管理员上传”会安全合并并写入：

`OneDrive\WarThunderUIDGuard\blacklist.json`

OneDrive 文件写入成功后由 OneDrive 客户端负责上传；公开 GitHub 镜像作为面向其他用户的稳定只读副本，随版本发布或数据更新提交同步。多台电脑合并时以 UID 为主键，以更新时间较新的昵称列表、备注或删除操作为准，不会把旧昵称重新合并回来。程序只允许 HTTPS 的精确 GitHub/CDN 镜像路径和 Microsoft OneDrive 域名，文件上限为 1 MB；JSON 验证失败时不会覆盖本地数据。只有回退到 OneDrive 网页时才需要 Microsoft Edge WebView2 Runtime。

## 申请添加

在输入区填写 UID，并可选填写昵称和备注，然后点击“申请添加”。程序只会调用 Windows 默认邮件程序并生成邮件草稿，收件人为 `elainasamae@outlook.com`；申请人仍需检查内容并亲自点击发送。程序不会保存邮箱密码，也不会在后台静默发送邮件。

## 官网昵称同步

在黑名单表格中选择一名玩家并点击“同步昵称”。程序使用屏幕外且不会抢焦点的 Microsoft Edge WebView2 访问 `warthunder.com` 官方查询页面并自动填入 UID，进度与结果只显示在主窗口。官网返回唯一结果时，会用当前昵称替换全部旧昵称。程序不会绕过验证码或 Cloudflare 验证，查询失败或超时时也不会修改本地数据。昵称同步需要 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已随 Edge 安装。

## 应用内更新

点击“检查更新”后，程序只访问 `elainasamae/WarThunderUIDGuard` 的 GitHub 正式发布接口。发现新版本时会自动下载免安装 ZIP 和对应 SHA-256 文件，校验无误后退出、替换程序文件并自动重启；失败则保留或恢复当前版本。黑名单位于 `%LOCALAPPDATA%`，更新不会删除它。首次获得带有该功能的 v0.5.0 仍需手动下载一次，此后可以直接在程序内更新。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## 合规说明

本项目是非官方工具，与 Gaijin Entertainment 无关联。Gaijin 的条款禁止未经授权、干扰游戏或提供不公平优势的第三方软件；即使这里只读取本地接口，也应由使用者自行确认当前规则并承担使用风险。
