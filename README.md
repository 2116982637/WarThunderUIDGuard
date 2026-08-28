# War Thunder UID Guard v0.1.2 Safe

Windows 桌面伴侣程序，用 UID 保存本地黑名单，并通过玩家昵称历史监听 War Thunder 的公开本地接口 `127.0.0.1:8111`。

## 能做什么

- UID 作为记录主键，保存多个当前/历史昵称与备注。
- 每约 0.9 秒轮询 `/gamechat` 与 `/hudmsg`。
- 黑名单昵称出现在聊天或 HUD 战斗事件时，通过 Windows 通知区域提醒并播放系统提示音。
- 数据只保存在 `%LOCALAPPDATA%\WarThunderUIDGuard\blacklist.json`。
- 不读取游戏画面、进程或内存，不注入进程，不监听或模拟输入，不修改游戏文件。
- HTTP 客户端强制只允许 `127.0.0.1:8111` 的 `/gamechat` 与 `/hudmsg`，并禁用代理和重定向。

## 重要限制

War Thunder 的 8111 接口不会提供对局完整名单，也不会提供参与者账号 UID。聊天与 HUD 事件仅提供昵称。因此本工具无法仅凭 UID 在玩家刚进入对局时立刻识别，也无法自动知道对方刚改过的新昵称。

为了避免误导，添加记录时必须同时填写至少一个已知昵称。程序会匹配该 UID 的所有昵称历史。得到对方新昵称后，再用同一 UID 添加即可合并。

## 运行

运行发布目录中的 `WarThunderUIDGuard.exe`，添加记录，然后点击“开始监控”。游戏尚未运行或不在对局时，状态会显示“等待游戏进入对局”。

## 构建

需要 .NET 8 SDK：

```powershell
dotnet publish -c Release -r win-x64 --self-contained false
```

## 合规说明

本项目是非官方工具，与 Gaijin Entertainment 无关联。Gaijin 的条款禁止未经授权、干扰游戏或提供不公平优势的第三方软件；即使这里只读取本地接口，也应由使用者自行确认当前规则并承担使用风险。
