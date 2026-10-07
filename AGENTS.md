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

## Thunderstore 发布

- 团队／namespace：`MengLei`；社区：`in-falsus`；分类同时选择 `mods` 和 `tools`。
- 独立包名：`ParticleFusion`、`SkillSelection`、`MouseKeyBindings`。
- 同一个已发布版本不能用于上传更新。发布前核对对应 csproj 的 `Version`、代码中的 BepInEx 插件版本、manifest 的 `version_number` 和 CHANGELOG；只发布用户指定的模组和版本。
- 先构建，再打包。已有工具命令为 `AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 <包名>`；省略包名会处理全部模组。该工具还会写入本地 r2modman profile 和游戏目录中的 Doorstop 配置，运行前必须核实这些本地部署操作属于用户授权范围。
- ZIP 位于 `ModZips\MengLei-<包名>-<版本>.zip`，只包含对应 DLL、manifest.json、icon.png、README.md、CHANGELOG.md。上传前比较包内内容和当前构建产物、发布素材，不能仅凭文件名判断 ZIP 是否最新。
- 已使用 Thunderstore CLI `tcli` 0.2.4 成功发布。项目本地安装位置为 `.codex\tools\tcli\tcli.exe`；缺失时运行 `dotnet tool install tcli --version 0.2.4 --tool-path .codex\tools\tcli`。
- 认证源为 Windows 环境变量 `THUNDERSTORE_API_TOKEN`；现有 token 保存在 User 范围。上传进程临时映射到 CLI 读取的 `TCLI_AUTH_TOKEN`。不要输出值，不写入配置文件或命令参数。
- CLI 上传现成 ZIP 时使用 `--file`，不能同时使用 `--package-name`；包信息放在 TOML 配置中。

以下 PowerShell 在项目根目录执行；仅在用户明确授权 Thunderstore 发布后运行。修改 `$package` 选择模组，版本从 manifest 读取：

```powershell
$ErrorActionPreference = 'Stop'
$package = 'MouseKeyBindings'
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
