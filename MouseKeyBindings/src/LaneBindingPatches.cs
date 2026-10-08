using HarmonyLib;
using UnityEngine.InputSystem;
using _E;
using ifapp.Game.Scenes.Game;
using ifapp.Game.UI.Settings;

namespace InFalsusMod;

/// <summary>保留设置页绑定流程，允许多个轨道的临时键位使用相同按键。</summary>
[HarmonyPatch(typeof(LaneKeyBindSettingsContainer), nameof(LaneKeyBindSettingsContainer._HE))]
internal static class LaneBindingCapturePatch
{
    /// <summary>原版关闭等待弹窗后，仅更新当前轨道，不自动替换其他轨道的重复键位。</summary>
    /// <param name="__instance">正在处理键位设置的原版容器。</param>
    /// <param name="__0">原方法收到的键盘按键或鼠标保留值。</param>
    /// <param name="__result">接管后向原版返回输入已处理。</param>
    /// <returns>未接管时继续原版方法，否则跳过原版唯一性处理。</returns>
    [HarmonyPrefix]
    private static bool Prefix(LaneKeyBindSettingsContainer __instance, Key __0, ref bool __result)
    {
        try
        {
            if (!LaneBindingRuntime.TryStoreBinding(__instance, __0))
                return true;
            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            LaneBindingRuntime.Disable(ex);
            return true;
        }
    }
}

/// <summary>在原版处理音符判定前，为同键轨道补齐本次真实输入状态。</summary>
[HarmonyPatch(typeof(_VD), nameof(_VD._Oz))]
internal static class SharedLaneInputPatch
{
    /// <summary>复用原版首个匹配轨道的物理按键状态和事件时间，供本次判定读取。</summary>
    /// <param name="__instance">当前游玩判定控制器。</param>
    [HarmonyPrefix]
    private static void Prefix(_VD __instance)
    {
        try
        {
            LaneBindingRuntime.SynchronizeInput(__instance);
        }
        catch (Exception ex)
        {
            LaneBindingRuntime.Disable(ex);
        }
    }
}

/// <summary>让同键轨道的按键提示同步显示原版收到的按下和释放。</summary>
[HarmonyPatch(typeof(Track), nameof(Track._GBA))]
internal static class SharedLaneIndicatorPatch
{
    /// <summary>首个轨道完成显示更新后，为其他共用按键的轨道补齐相同状态。</summary>
    /// <param name="__instance">当前歌曲的轨道显示容器。</param>
    /// <param name="__0">原版本次更新的零起始轨道编号。</param>
    /// <param name="__1">原版本次收到的按下状态。</param>
    [HarmonyPostfix]
    private static void Postfix(Track __instance, int __0, bool __1)
    {
        try
        {
            LaneBindingRuntime.UpdateSharedIndicators(__instance, __0, __1);
        }
        catch (Exception ex)
        {
            LaneBindingRuntime.Disable(ex);
        }
    }
}
