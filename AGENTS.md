# MLJ_InFalsus_Mods 项目约定

## 项目与构建

- 本地目录：`D:\project\in falsus\MLJ_InFalsus_Mods`；WSL 路径：`/mnt/d/project/in falsus/MLJ_InFalsus_Mods`。命令中的路径必须正确处理空格。
- 解决方案：`MLJ_InFalsus_Mods.sln`。项目名称与三个独立模组的包名分开管理；包名、DLL 名和 BepInEx 插件 GUID 保持稳定。
- GitHub 目标：`https://github.com/MengLeiFudge/MLJ_InFalsus_Mods`。三个模组的 `Assets/manifest.json` 中 `website_url` 均指向该仓库。
- 使用 Windows .NET SDK：Windows 执行 `dotnet build MLJ_InFalsus_Mods.sln -c Release`；WSL 在项目根目录执行 `dotnet.exe build MLJ_InFalsus_Mods.sln -c Release`。
- `DefaultPath.props` 保存本机游戏和 r2modman profile 路径；只提交 `DefaultPath.props.example`。构建依赖配置见 README。
- 默认仅做源码、配置静态检查与编译，不运行测试、模拟器或游戏来验收修改。

## GitHub 推送

- 用户明确要求推送后才执行；GitHub 推送和 Thunderstore 发布是独立操作。
- 首次推送前核实 GitHub 账号、仓库名称和可见性。保留已有远端历史，不强制覆盖。
- 不提交 `.dependencies/`、`.codex/`、`bin/`、`obj/`、`ModZips/`、本机配置或任何 token；图标、manifest、README 和 CHANGELOG 属于项目发布素材。
- 仓库改名不要求同步改动代码内部命名空间和嵌入资源标识。

## 常规本地交付与 QQ 群文件

- 每次修改本项目完成后，必须先用 Windows .NET SDK 编译 Release；编译成功后运行 `AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1`，完成本地更新、打包和配置 QQ 群的文件推送，不得仅交付源码或 DLL 后结束。此流程属于用户常驻授权，无需再次确认；用户当轮明确要求跳过时除外。
- 模式 1 的交付顺序：关闭游戏 → 更新本地 r2modman 模组并打包 → 修正游戏目录 Doorstop 路径 → 向配置的 QQ 群推送文件。完成后不自动启动游戏。
- 用户在本项目中说“推到 R2”指发布到 Thunderstore（雷霆商店），必须明确授权、单独执行；本地 r2modman profile 的模组替换属于已授权的常规交付，不额外请求确认。
- Windows Release 编译通过后，运行 `AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1` 默认更新全部模组并处理 QQ 推送；普通 `dotnet build` 不执行交付工具。只有本次授权明确限于单个模组时才传入包名，以免部署其他已有改动。内容未变的群文件仍去重跳过。
- 游戏目录的 `target_assembly` 指向 profile 中 BepInEx.Unity.IL2CPP.dll 的绝对路径；`coreclr_path`、`corlib_dir` 保留原配置，按游戏目录验证。路径修正在本地模组更新之后执行，不改 profile 的 Doorstop 原件。
- QQ 接收群来自本机 `DefaultPath.props` 的 `QqGroupIds`；`QqFolderId` 指定已有群文件夹 ID，省略、空白或 `/` 为最外层。没有配置群号时询问目标群，不推断其他项目的目标。上传、同名文件清理、成功缓存与回执都区分群和目录；切换目录不复用原目录的成功记录，不自动移动或删除原目录文件。
- 群文件通过本机 qqbot 的 `publish-local` 接口发送，客户端及服务端分别按内容哈希去重。目录 ID 从该群的 `get_group_root_files` 响应 `folders[].folder_id` 取得，不能把显示名称当 ID；目标目录不存在时失败，不降级到根目录。
- `--message-file <UTF-8文件>` 可覆盖发布说明。未指定时，干净工作区使用当前提交正文，未提交工作区自动生成包清单并标记构建来源和基准提交；不能为缺少手工说明阻断普通模式 1，也不自动提交无关 dirty。
- 最终核对本地安装文件、ZIP、游戏引导路径和 QQ 回执。QQ 失败不能视为交付完成；超时先核对群文件和服务端状态，不盲目重传。Thunderstore 上传不包含在此命令中。

## Thunderstore 发布

- 团队／namespace：`MengLei`；社区：`in-falsus`；分类同时选择 `mods` 和 `tools`。
- 独立包名：`ParticleFusion`、`SkillSelection`、`KeyBindingExtensions`。
- 同一个已发布版本不能用于上传更新。发布前核对对应 csproj 的 `Version`、代码中的 BepInEx 插件版本、manifest 的 `version_number` 和 CHANGELOG；只发布用户指定的模组和版本。
- 雷霆商店发布必须有用户明确授权，不能由构建、本地 r2modman 更新或 QQ 群文件推送自动触发。先按常规交付流程构建、打包并核对 ZIP，再单独上传用户指定的模组和版本。
- ZIP 位于 `ModZips\MengLei-<包名>-<版本>.zip`，只包含对应 DLL、manifest.json、icon.png、README.md、CHANGELOG.md。上传前比较包内内容和当前构建产物、发布素材，不能仅凭文件名判断 ZIP 是否最新。
- 已使用 Thunderstore CLI `tcli` 0.2.4 成功发布。项目本地安装位置为 `.codex\tools\tcli\tcli.exe`；缺失时运行 `dotnet tool install tcli --version 0.2.4 --tool-path .codex\tools\tcli`。
- 认证源为 Windows 环境变量 `THUNDERSTORE_API_TOKEN`；现有 token 保存在 User 范围。上传进程临时映射到 CLI 读取的 `TCLI_AUTH_TOKEN`。不要输出值，不写入配置文件或命令参数。
- CLI 上传现成 ZIP 时使用 `--file`，不能同时使用 `--package-name`；包信息放在 TOML 配置中。

以下 PowerShell 在项目根目录执行；仅在用户明确授权 Thunderstore 发布后运行。修改 `$package` 选择模组，版本从 manifest 读取：

```powershell
$ErrorActionPreference = 'Stop'
$package = 'KeyBindingExtensions'
$manifest = Get-Content "$package\Assets\manifest.json" -Raw -Encoding UTF8 | ConvertFrom-Json
$version = $manifest.version_number
$zip = "ModZips\MengLei-$package-$version.zip"
if (-not (Test-Path $zip)) { throw "找不到发布包：$zip" }
$env:TCLI_AUTH_TOKEN = [Environment]::GetEnvironmentVariable('THUNDERSTORE_API_TOKEN', 'User')
if ([string]::IsNullOrWhiteSpace($env:TCLI_AUTH_TOKEN)) {
    $env:TCLI_AUTH_TOKEN = [Environment]::GetEnvironmentVariable('THUNDERSTORE_API_TOKEN', 'Machine')
}
if ([string]::IsNullOrWhiteSpace($env:TCLI_AUTH_TOKEN)) { throw '缺少 Thunderstore token' }
New-Item -ItemType Directory -Force '.codex\publish' | Out-Null
$config = @"
[config]
schemaVersion = "0.0.1"
[package]
namespace = "MengLei"
name = "$package"
versionNumber = "$version"
[build]
outdir = "../../ModZips"
[publish]
repository = "https://thunderstore.io"
communities = ["in-falsus"]
[publish.categories]
in-falsus = ["mods", "tools"]
"@
[IO.File]::WriteAllText((Join-Path $PWD '.codex\publish\thunderstore.toml'), $config)
try {
    & .\.codex\tools\tcli\tcli.exe publish --config-path .\.codex\publish\thunderstore.toml --file $zip
    if ($LASTEXITCODE -ne 0) { throw "Thunderstore 发布失败：$LASTEXITCODE" }
} finally {
    Remove-Item Env:\TCLI_AUTH_TOKEN -ErrorAction SilentlyContinue
}
```

- 上传命令必须有有限超时；网络中断或超时后先查询远端状态，不盲目重传或递增版本。
- 发布成功后查询 `https://thunderstore.io/api/experimental/package/MengLei/<包名>/`，核实版本、`is_active`、社区与分类。社区 `review_status` 与上传成功是不同状态，按接口结果报告。
- Windows PowerShell 5.1 读取接口中文时，显式按 UTF-8 解码 `RawContentStream`；不要把终端或默认解码产生的乱码当成包内容损坏。
- 包页面为 `https://thunderstore.io/c/in-falsus/p/MengLei/<包名>/`。
