# MLJ_InFalsus_Mods

In Falsus 多模组解决方案。当前包含五个可独立安装的 BepInEx 6 IL2CPP 插件：

| 模组 | DLL | 功能 |
| --- | --- | --- |
| `ParticleFusion` | `ParticleFusion.dll` | 智能融合、选中供体融合与免费技能整理 |
| `SkillSelection` | `SkillSelection.dll` | 制卡界面的技能多选与附加材料筛选 |
| `KeyBindingExtensions`（按键绑定扩展） | `KeyBindingExtensions.dll` | 鼠标按键绑定、同键多轨道绑定 |
| `PotentialSystem`（潜力值系统） | `PotentialSystem.dll` | 动态定数的 B50/B10 PTT、徽章与完整成绩页、潜力值排序、B50 剪贴板图片及结算动画 |
| `ParticleCapacity`（粒子容量扩展） | `ParticleCapacity.dll` | 解除9999颗获得门槛，将库存容量提高至65535颗 |

KeyBindingExtensions 当前版本为1.1.2，ParticleFusion 和 SkillSelection 为1.0.2，PotentialSystem 和 ParticleCapacity 为1.0.0。五个模组均使用游戏1.0.7离线生成的互操作引用通过 Release 编译，ParticleFusion 和 PotentialSystem 已适配新版接口；实际游戏行为尚待用户确认。五个模组互不依赖，所需共享源码分别编入各 DLL，不发布公共运行时程序集。

## 目录

- `ParticleFusion/`：融合代码、数据、离线模拟器、样本报告及发布素材。模拟器链接当前正式融合源码，单独构建运行，不参与主解决方案构建。
- `SkillSelection/`：技能选择代码及发布素材。
- `KeyBindingExtensions/`：KeyBindingExtensions 的按键绑定扩展代码及发布素材。
- `PotentialSystem/`：整数定数潜力值、独立成绩保存、原生选歌展示、完整成绩浏览与 B50 图片导出。
- `ParticleCapacity/`：粒子容量扩展代码及发布素材。
- `docs/chart-constant-estimation-handoff.md`：独立会话研究小数定数估算的交接资料。
- `Shared/`：各项目按需共同编译的粒子字段访问、本地化、技能目录、原生 UI 和控制台工具。
- `AfterBuildEvent/`：关闭游戏、更新本地 r2modman 模组、生成 ZIP、修正 Steam 直启路径，并通过本机 qqbot 推送群文件。
- `tools/prepare-icons.py`：将 CPA 生图接口返回的高分辨率方图归一化为 1024×1024 母图，再用 Lanczos 转换为 256×256 发布图标。
- `Directory.Build.props`：统一输出目录和游戏程序集引用。
- `DefaultPath.props`：本机路径配置，不纳入版本控制；模板为 `DefaultPath.props.example`。

## 构建

使用 Windows .NET 工具链：

```powershell
dotnet build MLJ_InFalsus_Mods.sln -c Release
```

模组输出固定在各项目的 `bin\Release`，不追加目标框架目录。默认编译引用按以下顺序解析：

1. `DefaultPath.props` 指定的 r2modman profile 中的 BepInEx core。
2. 游戏目录或 profile 已生成的 `BepInEx\interop`。
3. 当前工作区保留的 `.dependencies` 作为后备。

新下载的 profile 尚未生成 `interop` 时，省略 `InteropDir` 会使用仓库 `.dependencies\interop` 中的编译快照；也可以在 `DefaultPath.props` 中显式指向其他现有 interop。profile 通过 Steam 直启生成 interop 后，默认优先使用 profile 版本。

游戏更新后，旧 profile 的互操作程序集可能仍对应上个版本。编译前须生成或指定与当前游戏匹配的引用；离线生成时用 `-p:InteropDir=<当前版本互操作目录>` 覆盖，不把离线编译快照复制到 profile。

## 角色立绘提取

构建后运行独立命令，提取当前游戏资源总表中每个角色的默认姿势、服装和表情，合成为透明背景 PNG：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe extract-characters
```

游戏位置来自 `DefaultPath.props` 的 `GameDir`，此命令不需要 profile 或 QQ 群配置。每个角色一张图片，直接输出为 `CharacterPortraits\<角色名>.png`，例如 `CharacterPortraits\Nia.png`。角色名沿用游戏预制体命名，可能与游戏显示名不同。重复执行会替换同名 PNG；游戏目录只读，提取结果不进入模组 ZIP，默认输出目录已加入 `.gitignore`。

提取时按游戏表情图集坐标叠加嘴、眉毛、睁眼和前发，再依据预制体的显示宽高及缩放恢复整张立绘的比例。输出保留身体贴图的高度；当前角色的贴图存储尺寸为 2048×4096，显示画布比例为 5788:8186，因此输出为 2896×4096，避免直接使用存储比例造成纵向拉伸。

需要安装 [uv](https://docs.astral.sh/uv/getting-started/installation/) 并加入 PATH；Windows 可用 `winget install --id astral-sh.uv --exact`。首次提取时 uv 会准备 Python 3.12、UnityPy 1.25.3 和 Pillow 12.3.0，需要能够下载这些依赖；之后复用 uv 缓存。提取脚本随 AfterBuildEvent 构建复制到输出目录的 `scripts` 中。

可用 `--output` 指定目录，相对路径以当前工作目录为基准：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe extract-characters --output "D:\Desktop\角色立绘"
```

提取命令不关闭或启动游戏，也不执行部署、打包和群文件推送。可按 Ctrl+C 取消；最长执行 10 分钟。资源缺失、结构不兼容或合成失败会返回非零退出码，并指出失败角色；已完成的 PNG 保留，单张图片写完后才替换目标文件。当前不导出全部服装或表情组合。


## 本地更新与群文件交付

在本机 `DefaultPath.props` 中设置 `QqGroupIds` 为接收群号；多个群号用逗号或分号分隔。可选的 `QqFolderId` 指定该群内已有文件夹的真实 ID，省略、留空或 `/` 表示群文件最外层。完整构建后运行以下命令，默认更新全部模组并处理 QQ 推送：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1
```

只处理一个模组：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 ParticleFusion
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 SkillSelection
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 KeyBindingExtensions
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 PotentialSystem
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 ParticleCapacity
```

工具会：

1. 扫描 `IsModProject=true` 的项目并按指定包名筛选，关闭 `GameDir` 下的 `infalsus.exe`；请求窗口关闭超时后结束该游戏进程。
2. 校验版本、manifest、BepInEx 依赖和 256×256 PNG 图标。
3. 将 DLL、manifest、icon、README 和 CHANGELOG 更新到 `ProfileDir\BepInEx\plugins\MengLei-<PackageId>`，停用对应旧合并 DLL。
4. 在根目录 `ModZips\` 生成每个模组独立的确定性 ZIP。
5. 从 profile 复制 Doorstop 引导文件到游戏目录，启用 Doorstop，将 `target_assembly` 修正为 profile 的绝对路径；`coreclr_path=dotnet\coreclr.dll`、`corlib_dir=dotnet` 等运行时配置保持原值，并按游戏目录验证其存在。
6. 将有变化的包交给 `http://127.0.0.1:8080/admin/api/artifacts/publish-local`，由 qqbot 上传到配置的群及目录。客户端和服务端分别核对内容哈希；成功缓存按群、目录和包名隔离，不向相同位置重复发送未变内容。

这样从 Steam 直接启动游戏时，会使用 `ProfileDir` 指向的本地模组。游戏目录保留 Doorstop 引导文件和配置相对引用的 `dotnet` 运行时；profile 的 Doorstop 原件不改写。r2modman 同步根文件后可能覆盖游戏配置，后续运行本工具会在更新模组之后重新修正路径。自动交付完成后不启动游戏。

需要具体发布说明时，可用 UTF-8 文件覆盖自动说明：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 PotentialSystem --message-file .codex\publish\message.txt
```

省略 `--message-file` 时，干净工作区使用当前提交正文，未提交工作区自动列出本次更新包，并明确标记“未提交工作区构建”和基准提交。普通 `AfterBuildEvent.exe 1` 不因缺少手工说明而停止 QQ 阶段，也不会自动提交 Git。QQ 回执保存在 `ModZips\publish-receipts\`；根目录成功缓存沿用 `ModZips\publish-content-sha256\<群号>\`，子目录缓存在其 `folders\<目录ID哈希>\` 下。群号缺失、显式说明文件无效或上传失败会返回非零；此前完成的本地更新和路径修正保留。上传超时先核对群文件和 qqbot 状态，不自动重传。

目录配置示例（把占位值替换为该群的实际 ID，留空即可恢复最外层）：

```xml
<QqFolderId>实际的群文件夹ID</QqFolderId>
```

ID 可从 NapCat `get_group_root_files` 的 `folders[].folder_id` 取得，文件夹名称和 ID 不是同一个值。qqbot 会先确认文件夹存在；不存在或不可访问时失败，不回退根目录。只清理目标目录中的同名旧群文件，切换目录不会搬迁或删除原目录文件。两个模组仓库分别配置自己的 `QqFolderId`，互不影响。

本地 r2modman 更新、QQ 群文件推送与 **Thunderstore（雷霆商店）发布** 是不同操作。`AfterBuildEvent.exe 1` 不调用 Thunderstore；向雷霆商店发布必须另行获得用户明确授权，再按项目发布流程执行。

发布 ZIP 只包含对应模组的五个文件，不包含 BepInEx、Doorstop、.NET 运行时、游戏程序集、PDB 或 1024×1024 图标母图。Thunderstore manifest 依赖 `BepInEx-BepInExPack_IL2CPP-6.0.755`。

若需增加模组，建立与项目名同级的目录和 csproj，设置 `IsModProject`、`PackageId`、`AssemblyName`、`Version`、`BepInExPluginGuid` 与 `ThunderstoreNamespace`，并提供 `Assets/manifest.json`、`Assets/icon.png`、`README.md` 和 `CHANGELOG.md`。AfterBuildEvent 无需增加项目专用分支。
