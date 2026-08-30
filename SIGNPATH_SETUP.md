# SignPath 配置步骤

本文件只说明项目所有者在 SignPath Foundation 批准申请后需要完成的配置。API Token、证书和私钥不得提交到仓库。

1. 确保 GitHub 与 SignPath 账户均已启用多重身份验证。
2. 在 SignPath 中把 `https://github.com/elainasamae/WarThunderUIDGuard` 连接为 GitHub.com 受信任构建源，并安装 SignPath GitHub App。
3. 创建只接受 `WarThunderUIDGuard.exe` 的 Authenticode Artifact Configuration；不得对随包附带的第三方文件签名。
4. 创建需要人工批准的正式签名策略，并限制为本仓库官方 `v*` 标签、GitHub 托管的 Windows 运行器和 `.github/workflows/release.yml`。
5. 在 GitHub 仓库的 `Settings > Secrets and variables > Actions` 中配置：

   - Secret：`SIGNPATH_API_TOKEN`
   - Variable：`SIGNPATH_ORGANIZATION_ID`
   - Variable：`SIGNPATH_PROJECT_SLUG`
   - Variable：`SIGNPATH_SIGNING_POLICY_SLUG`
   - Variable：`SIGNPATH_ARTIFACT_CONFIGURATION_SLUG`

6. 使用新版本标签触发发布。流程会在 SignPath 中等待人工批准，随后验证 Authenticode 发布者与可信时间戳，再生成 ZIP 和 SHA-256。
7. 下载最终 ZIP，运行以下命令复核：

```powershell
Expand-Archive '.\WarThunderUIDGuard-vX.Y.Z-win-x64.zip' -DestinationPath '.\verify'
Get-AuthenticodeSignature '.\verify\WarThunderUIDGuard.exe' |
    Format-List Status, SignerCertificate, TimeStamperCertificate
```

必须得到 `Status: Valid`，且签名者包含 `SignPath Foundation`。在满足这些条件前，不得发布新的未签名版本；当前 v0.6.3 继续作为历史未签名版本保留。
