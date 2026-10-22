using HarmonyLib;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.UI.Settings;
using GameInputManager = ifapp.Game.Input.InputManager;

namespace InFalsusMod;

/// <summary>在游戏现有 Input System 订阅中旁路读取完整鼠标按钮位。</summary>
[HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.OnInputSystemEvent))]
internal static class MouseInputEventPatch
{
    /// <summary>原版改写或压入事件前保存鼠标按钮变化和精确时间。</summary>
    /// <param name="__instance">接收原始事件的游戏输入管理器。</param>
    /// <param name="__0">原方法第一个参数：Input System 事件。</param>
    /// <param name="__1">原方法第二个参数：事件所属设备。</param>
    [HarmonyPrefix]
    private static void Prefix(GameInputManager __instance, InputEventPtr __0, InputDevice __1) =>
        MouseInputEvents.Observe(__instance, __0, __1);
}

/// <summary>在原版输入事件完成分发后处理鼠标设置绑定和轨道事件。</summary>
[HarmonyPatch(typeof(GameInputManager), nameof(GameInputManager.Update))]
internal static class MouseInputUpdatePatch
{
    /// <summary>保持原版事件顺序，并确保设置槽点击已在鼠标捕获前完成。</summary>
    /// <param name="__instance">当前游戏输入管理器。</param>
    [HarmonyPostfix]
    private static void Postfix(GameInputManager __instance)
    {
        try
        {
            MouseBindingRuntime.Process(__instance);
        }
        catch (Exception ex)
        {
            MouseBindingRuntime.Disable(ex);
        }
    }
}

/// <summary>让歌曲场景的原版按键提示器识别鼠标保留值。</summary>
[HarmonyPatch(typeof(KeyPressedIndicator), nameof(KeyPressedIndicator._wl))]
internal static class MouseKeyIndicatorPatch
{
    /// <summary>鼠标绑定直接显示为 MouseN，键盘绑定仍由原版查询显示名称。</summary>
    /// <param name="__instance">当前轨道的按键提示器。</param>
    /// <param name="__0">原方法接收的轨道绑定值。</param>
    /// <returns>键盘绑定返回 true 继续原方法；鼠标绑定返回 false 跳过 Keyboard 索引。</returns>
    [HarmonyPrefix]
    private static bool Prefix(KeyPressedIndicator __instance, Key __0)
    {
        if (!MouseKeyCodec.TryDecode(__0, out int button))
            return true;
        if (__instance._ip == __0)
            return false;

        __instance._ip = __0;
        __instance.text?.SetTextNonLocalized(
            $"Mouse{button}",
            FastText.FastText.UpdateType.Default);
        return false;
    }
}

/// <summary>在原版设置数据刷新后注册当前容器并替换鼠标保留值文本。</summary>
[HarmonyPatch(typeof(LaneKeyBindSettingsContainer), nameof(LaneKeyBindSettingsContainer._gE))]
internal static class MouseBindingSettingsPatch
{
    /// <summary>原版已复制当前六轨绑定后刷新 MouseN 标签。</summary>
    /// <param name="__instance">刚初始化的六轨绑定容器。</param>
    [HarmonyPostfix]
    private static void Postfix(LaneKeyBindSettingsContainer __instance)
    {
        try
        {
            MouseBindingRuntime.RegisterSettings(__instance);
        }
        catch (Exception ex)
        {
            MouseBindingRuntime.Disable(ex);
        }
    }
}

/// <summary>
/// 1.0.6 起 _GE 用 Keyboard.current[key].displayName 刷新全部轨道文字，鼠标保留值会越界抛错。
/// 有鼠标绑定时改由本补丁按原版相同逻辑刷新文字、轨道按钮和应用按钮状态；纯键盘绑定仍走原版。
/// </summary>
[HarmonyPatch(typeof(LaneKeyBindSettingsContainer), nameof(LaneKeyBindSettingsContainer._GE))]
internal static class MouseBindingRefreshPatch
{
    /// <summary>检测到鼠标保留值时接管刷新。</summary>
    /// <param name="__instance">六轨绑定设置容器。</param>
    /// <returns>是否继续执行原版方法。</returns>
    [HarmonyPrefix]
    private static bool Prefix(LaneKeyBindSettingsContainer __instance)
    {
        try
        {
            return !MouseBindingRuntime.RefreshBindingView(__instance);
        }
        catch (Exception ex)
        {
            MouseBindingRuntime.Disable(ex);
            return true;
        }
    }
}
