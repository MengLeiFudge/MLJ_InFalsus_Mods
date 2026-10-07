# MouseKeyBindings

为 In Falsus 的六轨操作设置增加鼠标按键绑定，同时保留原有键盘绑定行为。

## 使用

1. 打开“设置”中的操作设置页。
2. 点击任意一个轨道键位槽。
3. 按下要绑定的鼠标按钮；槽位会显示为 `Mouse0`、`Mouse1` 等。
4. 点击原版“应用”按钮保存。

`Mouse0`、`Mouse1`、`Mouse2` 分别是左键、右键和中键，`Mouse3`、`Mouse4` 是 Input System 的前进键和后退键。模组优先通过这些标准 `ButtonControl` 读取每个原始事件，并同时保留 `MouseState.buttons` 的完整 16 位状态读取，因此同一路径可处理 `Mouse0` 至 `Mouse15`；如果自定义鼠标布局还在 `allControls` 中公开其他 `ButtonControl`，这些控件也会获得稳定的 `MouseN` 并可绑定。实际可用按钮取决于鼠标、驱动和 Unity 所公开的控件。

选择槽位所用的首次点击不会立即成为绑定。再次点击同一个槽位可以绑定 `Mouse0`；也可以在选择槽位后按侧键。原版的重复绑定检查、应用按钮、恢复默认和键盘重新绑定继续生效。

绑定写入游戏原有的 `keybind_BottomLane0` 至 `keybind_BottomLane5` 偏好项，不创建第二份配置。重新打开设置、进入歌曲或重启游戏后，设置槽位和轨道按键提示器都会显示并使用已保存的 `MouseN`。这些保留值需要本模组才能显示和触发；停用模组前可先在操作设置中恢复默认。

## 安装

建议通过 In Falsus 的 Thunderstore/r2modman 安装。依赖中的 BepInExPack_IL2CPP 会由模组管理器处理，本包不携带 BepInEx、Doorstop 或 .NET 运行时。插件 DLL 名为 `MouseKeyBindings.dll`，可与 ParticleFusion 和 SkillSelection 分别启用或停用。

## 输入行为

鼠标按下和释放使用 Input System 原始事件时间。只有 MouseN 当前实际绑定在六轨之一、`GameScene` 已登记在原版始终接收输入栈且暂停层未显示时，事件才会进入现有六轨处理路径；角色选择、设置和其他菜单不会收到 MouseN 合成键。释放事件还会在帧末用对应 `ButtonControl` 的当前物理状态校正，避免事件布局漏报释放后轨道保持点亮。暂停弹窗出现时，已经接受按下的轨道会立即收到配对释放，并在暂停层完全关闭前保持失效。

鼠标绑定不包含 Ctrl、Alt 或 Shift 组合。原有键盘绑定及其修饰键匹配不受修改。

## 兼容范围

当前适配 Steam App ID 3971950、Unity 6000.3.9f1 和 IL2CPP 元数据版本 39。实现依赖游戏当前的 `InputManager`、`LaneKeyBindSettingsContainer`、`GameScene` 和 `KeyPressedIndicator` 互操作接口；游戏更新这些接口后需要同步更新模组。
