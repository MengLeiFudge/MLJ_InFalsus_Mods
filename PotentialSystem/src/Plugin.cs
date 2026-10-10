using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ifapp.Game.Scenes;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>注册独立潜力值模组和原生场景完成后的观察入口。</summary>
[BepInPlugin("menglei.infalsus.potentialsystem", "In Falsus Potential System", "1.0.0")]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>供主线程控制器记录数据与展示错误。</summary>
    internal static ManualLogSource Logger { get; private set; } = null!;
    /// <summary>当前插件持有的唯一主线程控制器。</summary>
    internal static PotentialController? Controller { get; private set; }
    /// <summary>仅拦截本模组的场景观察与选歌排序通知。</summary>
    private Harmony? harmony;

    /// <inheritdoc />
    public override void Load()
    {
        Logger = Log;
        InFalsusMod.WindowsConsoleEncoding.EnsureUtf8(Log);
        try
        {
            if (!ClassInjector.IsTypeRegisteredInIl2Cpp<PotentialInputReceiver>())
                ClassInjector.RegisterTypeInIl2Cpp<PotentialInputReceiver>();
            Controller = AddComponent<PotentialController>();
            harmony = new Harmony("menglei.infalsus.potentialsystem");
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch
        {
            harmony?.UnpatchSelf();
            if (Controller != null)
                Object.Destroy(Controller);
            Controller = null;
            throw;
        }
        Log.LogInfo("潜力值系统 1.0.0 已加载，包含动态定数、完整排名、Arcaea 徽章与 B50 图片导出。");
    }

    /// <inheritdoc />
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        harmony = null;
        if (Controller != null)
            Object.Destroy(Controller);
        Controller = null;
        return true;
    }
}

/// <summary>选歌场景建立后导入当前用户成绩；不改变原版 Start 流程。</summary>
[HarmonyPatch(typeof(SongSelectScene), nameof(SongSelectScene.Start))]
internal static class SelectionStartPatch
{
    /// <summary>原版完成选歌初始化后通知模组，异常不返回原版。</summary>
    /// <param name="__instance">当前原版选歌场景。</param>
    [HarmonyPostfix]
    private static void Postfix(SongSelectScene __instance)
    {
        var controller = Plugin.Controller;
        if (controller == null || !controller.enabled)
            return;
        try { controller.EnterSelection(__instance); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值选歌接入失败：{ex}"); }
    }
}

/// <summary>结算场景建立后捕获旧、新 PTT；不修改原版游戏结果或动画。</summary>
[HarmonyPatch(typeof(ResultsScene), nameof(ResultsScene.Start))]
internal static class ResultsStartPatch
{
    /// <summary>原版准备完成后记录本次成绩并启动模组数值动画。</summary>
    /// <param name="__instance">当前原版结算场景。</param>
    [HarmonyPostfix]
    private static void Postfix(ResultsScene __instance)
    {
        var controller = Plugin.Controller;
        if (controller == null || !controller.enabled)
            return;
        try { controller.EnterResults(__instance); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值结算接入失败：{ex}"); }
    }
}

/// <summary>原版排序按钮的回调先退出潜力值排序，保留全部原版选项。</summary>
[HarmonyPatch(typeof(SongSelectScene._Jc), nameof(SongSelectScene._Jc._po))]
internal static class NativeSortRequestedPatch
{
    /// <summary>只通知当前选歌视图，异常不阻止原版回调。</summary>
    [HarmonyPrefix]
    private static void Prefix(SongSelectScene._Jc __instance)
    {
        try { Plugin.Controller?.NativeSortRequested(__instance._BR); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值排序入口通知失败：{ex}"); }
    }
}

/// <summary>原版建立当前曲包歌曲行并完成排序后，允许模组调整现有行的顺序。</summary>
[HarmonyPatch(typeof(SongSelectScene), nameof(SongSelectScene._Ao))]
internal static class NativeSortCompletedPatch
{
    /// <summary>重排在原版恢复选中歌曲和刷新滚动列表之前完成。</summary>
    [HarmonyPostfix]
    private static void Postfix(SongSelectScene __instance)
    {
        try { Plugin.Controller?.NativeSortCompleted(__instance); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值排序刷新失败：{ex}"); }
    }
}
