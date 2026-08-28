# War Thunder UID Guard v0.4.1 Safe

Windows 桌面伴侣程序，用 UID 保存本地黑名单，并通过玩家昵称历史监听 War Thunder 的公开本地接口 `127.0.0.1:8111`。

## 能做什么

- UID 作为记录主键，保存多个当前/历史昵称与备注。
- 每约 0.9 秒轮询 `/gamechat` 与 `/hudmsg`。
- 首次连接超过 10 秒仍未成功时自动停止监控，并明确显示“连接失败”。
- 支持简体中文和英文，可在运行时切换并记住语言选择；首次运行跟随 Windows 显示语言。
- 可选 OneDrive 手动同步：其他用户通过只读共享链接拉取，管理员通过 Windows 已登录的 OneDrive 文件夹上传。
- 可对选中的 UID 打开可见的战争雷霆官网查询页；官网返回唯一昵称时自动追加到昵称历史，不需要第二次确认。
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

## OneDrive 同步

勾选程序顶部的“OneDrive 手动同步”只会启用两个按钮，不会自动上传或拉取。

“拉取同步”从项目内置的只读 OneDrive 文件共享链接下载并合并黑名单。其他用户不需要登录管理员的 OneDrive；程序在独立的匿名 WebView2 环境中打开官方 OneDrive 页面并触发其“下载”操作，不会读取浏览器登录状态或账号密码。共享链接必须允许“任何人、无需登录、可查看”。

管理员先在 Windows 中登录并正常运行 OneDrive。“管理员上传”会安全合并并写入：

`OneDrive\WarThunderUIDGuard\blacklist.json`

OneDrive 文件写入成功后由 OneDrive 客户端负责上传。其他用户的“管理员上传”不会修改管理员的共享文件，因为他们没有写权限。多台电脑合并时以 UID 为主键、保留昵称历史，并以更新时间较新的备注或删除操作为准。远程页面与下载仅允许 HTTPS 的 Microsoft OneDrive 域名，文件上限为 1 MB；JSON 验证失败时不会覆盖本地数据。拉取功能需要 Microsoft Edge WebView2 Runtime，Windows 10/11 通常已随 Edge 安装。

## 官网昵称同步

在黑名单表格中选择一名玩家并点击“同步昵称”。程序使用 Microsoft Edge WebView2 打开 `warthunder.com` 官方查询页面并自动填入 UID。官网返回唯一结果时自动追加昵称；旧昵称不会删除。页面始终可见，程序不会绕过验证码或 Cloudflare 验证，查询失败时也不会修改本地数据。昵称同步需要 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已随 Edge 安装。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## 合规说明

本项目是非官方工具，与 Gaijin Entertainment 无关联。Gaijin 的条款禁止未经授权、干扰游戏或提供不公平优势的第三方软件；即使这里只读取本地接口，也应由使用者自行确认当前规则并承担使用风险。
