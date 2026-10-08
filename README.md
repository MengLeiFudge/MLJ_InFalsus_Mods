# MLJ_InFalsus_Mods

In Falsus 多模组解决方案。当前包含三个可独立安装的 BepInEx 6 IL2CPP 插件：

| 模组 | DLL | 功能 |
| --- | --- | --- |
| `ParticleFusion` | `ParticleFusion.dll` | 智能融合、选中供体融合与免费技能整理 |
| `SkillSelection` | `SkillSelection.dll` | 制卡界面的技能多选与附加材料筛选 |
| `KeyBindingExtensions`（按键绑定扩展） | `MouseKeyBindings.dll` | 鼠标按键绑定、同键多轨道绑定 |

KeyBindingExtensions 当前版本为 1.1.0，ParticleFusion 和 SkillSelection 为 1.0.1；三个模组均适配游戏 1.0.6，互不依赖。所需共享源码会分别编入各 DLL，不发布公共运行时程序集。

## 目录

- `ParticleFusion/`：融合代码、数据、离线模拟器、样本报告及发布素材。模拟器链接当前正式融合源码，单独构建运行，不参与主解决方案构建。
- `SkillSelection/`：技能选择代码及发布素材。
- `MouseKeyBindings/`：KeyBindingExtensions 的按键绑定扩展代码及发布素材。
- `Shared/`：各项目按需共同编译的粒子字段访问、本地化、技能目录、原生 UI 和控制台工具。
- `AfterBuildEvent/`：发现模组项目、更新本地 r2modman profile、生成 Thunderstore ZIP，并把 Steam 直启绑定到该 profile。
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

## 本地部署与打包

完整构建后运行：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1
```

只处理一个模组：

```powershell
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 ParticleFusion
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 SkillSelection
.\AfterBuildEvent\bin\Release\AfterBuildEvent.exe 1 MouseKeyBindings
```

工具会：

1. 扫描根目录下一层所有 `IsModProject=true` 的 csproj。
2. 校验版本、Thunderstore manifest、BepInEx 依赖和 256×256 PNG 图标。
3. 将 DLL、manifest、icon、README 和 CHANGELOG 更新到 `ProfileDir\BepInEx\plugins\MengLei-<PackageId>`。
4. 把旧合并版 `InFalsus.Potency999.dll` 改名为 `.old`，避免重复加载。
5. 在根目录 `ModZips\` 生成每个模组独立的确定性 ZIP。
6. 将 Default profile 的 `winhttp.dll` 和 `doorstop_config.ini` 更新到游戏根目录；配置中的 `target_assembly`、`coreclr_path` 和 `corlib_dir` 使用该 profile 的绝对路径。

这样从 Steam 直接启动游戏时，会使用 `DefaultPath.props` 中 `ProfileDir` 指向的 r2modman profile。游戏根目录只保留 Doorstop 的引导文件，不需要 `BepInEx` 或 `dotnet` 目录；完整运行时仍由 profile 和 Thunderstore 依赖管理。

发布 ZIP 只包含对应模组的五个文件，不包含 BepInEx、Doorstop、.NET 运行时、游戏程序集、PDB 或 1024×1024 图标母图。Thunderstore manifest 依赖 `BepInEx-BepInExPack_IL2CPP-6.0.755`。

若需增加模组，建立与项目名同级的目录和 csproj，设置 `IsModProject`、`PackageId`、`AssemblyName`、`Version`、`BepInExPluginGuid` 与 `ThunderstoreNamespace`，并提供 `Assets/manifest.json`、`Assets/icon.png`、`README.md` 和 `CHANGELOG.md`。AfterBuildEvent 无需增加项目专用分支。
