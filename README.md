# War Thunder UID Guard v0.6.3 Safe

Windows 桌面伴侣程序，用 UID 保存本地黑名单，并通过玩家昵称历史监听 War Thunder 的公开本地接口 `127.0.0.1:8111`。

本项目采用 [MIT License](LICENSE) 开源。隐私与网络访问范围见 [PRIVACY.md](PRIVACY.md)。

## 能做什么

- UID 作为记录主键，保存玩家昵称与备注；手动编辑时可填写多个已知昵称。
- 每约 0.9 秒轮询 `/gamechat` 与 `/hudmsg`。
- 首次连接超过 10 秒仍未成功时自动停止监控，并明确显示“连接失败”。
- 支持简体中文和英文，可在运行时切换并记住语言选择；首次运行跟随 Windows 显示语言。
- 可选远程手动同步：其他用户优先从专用只读同步服务器拉取经过 RSA-3072 数字签名的数据；获授权的管理员可输入共享的高强度密码上传。
- 其他用户可以通过“申请添加”生成一封发送到 `elainasamae@outlook.com` 的邮件草稿，由管理员审核 UID、昵称和备注。
- 可在后台通过战争雷霆官网查询选中 UID 的当前昵称；官网返回唯一昵称时自动替换旧昵称，不弹出网页，也不需要第二次确认。
- 内置应用更新：点击“检查更新”后优先从专用服务器下载，服务器不可用时自动回退到本项目 GitHub Release；校验、替换并重启均自动完成，无需手动解压覆盖。
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

“拉取同步”会优先尝试专用只读服务器 `http://39.105.200.142:8443/blacklist.json`，客户端必须使用内置 RSA-3072 公钥验证配套签名，验签失败的数据一律拒绝。随后并发尝试 GitHub Raw、Gcore、Fastly 和 jsDelivr 四条 HTTPS 后备线路，每条线路自动重试，任意一条成功就立即继续；全部网络线路暂时不可用时使用上次成功下载的本地缓存。拉取时服务器对远程 UID 的存在状态具有优先权：即使本机曾删除该 UID，只要服务器仍保留它，再次拉取就会恢复；服务器发布的删除记录仍会生效。服务器黑名单是权威副本，定时任务只维护签名和更新镜像，不会再用 GitHub 的旧数据覆盖管理员上传；仅在服务器文件缺失时才从公开仓库引导恢复。

“管理员上传”会先拉取并验证服务器当前数据，再按 UID 和更新时间合并。管理员密码只在本次操作的遮罩输入框中使用，不会保存或通过网络发送；客户端使用 PBKDF2-SHA256 派生认证密钥，再用 HMAC-SHA256 绑定请求时间戳、随机数、服务器基础版本和完整文件哈希。服务器拒绝过期请求、重复随机数、旧版本覆盖、错误密码、超限频率和无效 JSON，并在原子替换前保存备份和生成新的 RSA-3072 数据签名。

服务器是公共黑名单的权威来源。GitHub/CDN 镜像仍作为拉取后备，但管理员上传不会依赖 OneDrive。多台电脑合并时以 UID 为主键，以更新时间较新的昵称列表、备注或删除操作为准。远程文件上限为 1 MB；签名或 JSON 验证失败时不会覆盖本地数据。

## 申请添加

在输入区填写 UID，并可选填写昵称和备注，然后点击“申请添加”。程序只会调用 Windows 默认邮件程序并生成邮件草稿，收件人为 `elainasamae@outlook.com`；申请人仍需检查内容并亲自点击发送。程序不会保存邮箱密码，也不会在后台静默发送邮件。

## 官网昵称同步

在黑名单表格中选择一名玩家并点击“同步昵称”。程序使用屏幕外且不会抢焦点的 Microsoft Edge WebView2 访问 `warthunder.com` 官方查询页面并自动填入 UID，进度与结果只显示在主窗口。官网返回唯一结果时，会用当前昵称替换全部旧昵称。程序不会绕过验证码或 Cloudflare 验证，查询失败或超时时也不会修改本地数据。昵称同步需要 Microsoft Edge WebView2 Runtime；Windows 10/11 通常已随 Edge 安装。

## 应用内更新

点击“检查更新”后，程序先读取专用服务器上经过 RSA-3072 签名的版本元数据；服务器存在有效新版本时立即开始服务器下载，不再等待 GitHub 查询。服务器不可用、没有新版本或文件下载失败时，才访问 `elainasamae/WarThunderUIDGuard` 的 GitHub 正式发布接口。GitHub 下载后备仍被服务器签名元数据中的 SHA-256 约束。服务器每 5 分钟同步最新正式版本、校验 GitHub 发布页提供的 SHA-256 后再生成签名元数据。更新校验无误后程序会退出、替换文件并自动重启；失败则保留或恢复当前版本。黑名单位于 `%LOCALAPPDATA%`，更新不会删除它。v0.6.0 及更早版本首次升级到 v0.6.1 仍会使用 GitHub；从 v0.6.1 升级时已经优先使用专用服务器。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## Code signing policy

项目正在申请 SignPath Foundation 的免费开源代码签名。v0.6.3 及以前版本没有 Authenticode 签名，因此 Windows 可能显示“未知发布者”。批准后的正式 Windows 版本只允许由 GitHub 托管运行器从公开标签构建，经 SignPath 审批签名并验证成功后发布；发布流程不会在签名缺失时静默回退到未签名包。

Free code signing provided by SignPath.io, certificate by SignPath Foundation.

维护者、审核者和签名批准人均为 [@elainasamae](https://github.com/elainasamae)。完整规则见 [CODE_SIGNING_POLICY.md](CODE_SIGNING_POLICY.md)，批准后的配置步骤见 [SIGNPATH_SETUP.md](SIGNPATH_SETUP.md)。

## 同步服务器

仓库中的 `server` 目录保存当前镜像服务器、管理员上传端点及刷新脚本的公开配置，便于重建及审计。服务器私钥和管理员认证密钥不会提交到仓库；它们使用 Windows DPAPI 的 `LocalMachine` 范围加密，并限制为 `SYSTEM` 与服务器管理员读取。上传服务作为无执行时限、失败自动重启的 `SYSTEM` 计划任务运行。服务器数据位于 `C:\ProgramData\WarThunderUIDGuardSync\www\blacklist.json`，上传前备份位于 `C:\ProgramData\WarThunderUIDGuardSync\upload-backups`，不记录密码的审计日志位于 `C:\ProgramData\WarThunderUIDGuardSync\admin-upload.log`。

## 合规说明

本项目是非官方工具，与 Gaijin Entertainment 无关联。Gaijin 的条款禁止未经授权、干扰游戏或提供不公平优势的第三方软件；即使这里只读取本地接口，也应由使用者自行确认当前规则并承担使用风险。
