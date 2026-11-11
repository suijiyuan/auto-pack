# 文件传输打包

Windows 桌面工具：选择一组文件，自动整理到 `文件传输-当前日期-序号` 文件夹，再生成加密的 7z 分卷。

## 使用

1. 将整个 `AutoPack-win-x64.zip` 解压到本地文件夹，运行其中的 `AutoPack.exe`。请保留旁边的 `tools` 文件夹。
2. 首次运行，在“设置”中选好固定输出目录，输入并确认加密密码，保存即可。勾选“显示输入的密码”后，确认密码框会隐藏，只需输入一次。
3. 日常点击“添加文件”，或将文件拖入列表，然后点击“开始打包”。输出目录和密码自动沿用，无需重复填写。
4. 等待复制、压缩和完整性校验完成，通过“打开输出目录”查看结果。

之后可通过“设置”修改目录或密码。修改密码时将密码输入框留空可保留原密码；隐藏密码时，确认密码框也需留空。新密码只用于之后的任务，旧压缩包仍需要旧密码。

## 输出

```text
所选输出目录/
  文件传输-20261111-001/
    合同.pdf
    资料.xlsx
  文件传输-20261111-001.7z.001
  文件传输-20261111-001.7z.002
```

- 日期采用 Windows 本地日期，格式为 `yyyyMMdd`；同一天在当前输出目录内自动递增序号，避免覆盖已有任务。
- 每卷上限 700 MiB（734003200 字节），最后一卷可以更小。较小的任务也生成 `.7z.001`。
- 压缩包采用 7z AES-256 加密，并加密内部文件名。传输时发送全部 `.7z.001`、`.7z.002` 等分卷；收件人将其放在同一目录，用 7-Zip 从 `.001` 解压并输入密码。
- 原始文件保留；整理文件夹也是普通文件副本，**只有压缩分卷经过加密**。
- 只支持普通文件。不同位置同名（包括仅大小写不同）的文件需要先移除冲突项。
- 支持取消任务；取消或失败会清理本次未完成产物，保留源文件和其他任务。输出目录的 `.autopack.lock` 是并发协调文件，无需手动处理。

## 设置保存

配置保存在 `%LOCALAPPDATA%\AutoPack\settings.json`，不受程序解压位置影响。输出目录按普通配置保存；密码通过 Windows DPAPI 按当前用户加密保存，配置文件没有明文密码。密码不会作为压缩进程的命令行参数传递。

换电脑或换 Windows 用户后，可能无法恢复已保存的密码，需要重新配置。请自行保管密码，程序不能恢复遗忘的密码。密码不支持换行及空字符。

## 构建与验证

使用 .NET 10 SDK 和 PowerShell 7。发布目标为 Windows x64，建议 Windows 11。自包含发布包无需用户另装 .NET 或 7-Zip。

在 Windows 项目根目录运行：

```powershell
./scripts/publish.ps1
```

脚本校验随项目提供的官方 7-Zip 26.06 引擎，执行测试，再生成 `artifacts/AutoPack-win-x64.zip`。

GitHub Actions 分为两个工作流：

- `ci.yml`：推送到 `master` 或向 `master` 提交 PR 时，在 Windows 上构建应用并运行测试。
- `release.yml`：仅在推送 `v*` 标签时执行测试、打包、生成 SHA256 校验文件，并创建同名 GitHub Release，上传 ZIP 和 `SHA256SUMS.txt` 附件。

例如，提交代码后发布标签：

```bash
git tag v1.0.0
git push origin v1.0.0
```

标签对应的提交需包含工作流及完整项目文件。成功后，可在仓库 Releases 对应版本下载 ZIP 和 `SHA256SUMS.txt`，也可在 Actions 对应运行记录的 Artifacts 中下载 `AutoPack-win-x64` 制品。工作流使用 GitHub 自动提供的 `GITHUB_TOKEN`，无需配置个人访问令牌。

只运行测试：

```powershell
$env:AUTO_PACK_7ZIP = (Resolve-Path ./tools/7zip/7za.exe).Path
dotnet run --project tests/AutoPack.Tests -c Release
```

macOS/Linux 可将 `AUTO_PACK_7ZIP` 指向本机官方 7-Zip 控制台程序，运行核心与实际归档测试；Windows DPAPI 测试会明确跳过。之后可用 `pwsh ./scripts/publish.ps1 -SkipTests` 交叉发布 Windows 包。此方式不能验证 Windows 界面或真实 DPAPI，仍需 Windows 验证。

测试覆盖命名和序号、同名冲突、显示密码时的单次输入、隐藏密码时的两次确认、设置恢复及保存失败保护、加密归档恢复和内容比对、错误密码、文件名加密、分卷上限、取消清理、发布冲突和并发任务。归档集成测试使用较小分卷以节省时间，桌面应用固定为 700 MiB。

本次验证（2026-11-11）：macOS 上 13 项测试通过，使用真实官方 7-Zip 引擎执行归档测试；Windows 项目交叉编译通过。Windows 专属 DPAPI 测试已明确跳过，Windows 界面实机及 Windows 工作流尚未运行。
