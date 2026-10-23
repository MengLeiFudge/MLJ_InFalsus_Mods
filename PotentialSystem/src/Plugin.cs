using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using ifapp.Game.Scenes;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>注册独立潜力值模组和原生场景完成后的观察入口。</summary>
[BepInPlugin("menglei.infalsus.potentialsystem", "In Falsus Potential System", "1.0.1")]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>供主线程控制器记录数据与展示错误。</summary>
    internal static ManualLogSource Logger { get; private set; } = null!;
    /// <summary>当前插件持有的唯一主线程控制器。</summary>
    internal static PotentialController? Controller { get; private set; }
    /// <summary>本模组的社区定数配置及原生设置视图。</summary>
    internal static PotentialSettings? Settings { get; private set; }
    /// <summary>本 profile 的安小璃缓存与一次后台更新。</summary>
    internal static AnXiaoLiConstants? AnXiaoLi { get; private set; }
    /// <summary>本 profile 的萌泪酱 v6 内置表、冻结模型与后台计算。</summary>
    internal static MengLeiConstants? MengLei { get; private set; }
    /// <summary>仅拦截本模组的场景观察、设置接入与选歌排序通知。</summary>
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
            Settings = new PotentialSettings(Config);
            AnXiaoLi = new AnXiaoLiConstants(Paths.ConfigPath);
            MengLei = new MengLeiConstants(Paths.ConfigPath);
            Controller = AddComponent<PotentialController>();
            harmony = new Harmony("menglei.infalsus.potentialsystem");
            harmony.PatchAll(typeof(Plugin).Assembly);
        }
        catch
        {
            harmony?.UnpatchSelf();
            Settings?.Dispose();
            Settings = null;
            AnXiaoLi?.Dispose();
            AnXiaoLi = null;
            MengLei?.Dispose();
            MengLei = null;
            if (Controller != null)
                Object.Destroy(Controller);
            Controller = null;
            throw;
        }
        Log.LogInfo("潜力值系统 1.0.1 已加载，安小璃更新和萌泪酱新谱面计算将在后台完成。");
    }

    /// <inheritdoc />
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        harmony = null;
        Settings?.Dispose();
        Settings = null;
        AnXiaoLi?.Dispose();
        AnXiaoLi = null;
        MengLei?.Dispose();
        MengLei = null;
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

/// <summary>在原版排序按钮的双向循环中加入歌曲定数与单曲潜力值的升降序选项。</summary>
[HarmonyPatch(typeof(SongSelectSortingButton), nameof(SongSelectSortingButton._cO))]
internal static class NativeSortCyclePatch
{
    /// <summary>未绑定的场景由原版处理；模组异常不逃回原生按钮回调。</summary>
    [HarmonyPrefix]
    private static bool Prefix(SongSelectSortingButton __instance, int __0)
    {
        try { return Plugin.Controller?.TryCycleSort(__instance, __0) != true; }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"潜力值排序循环接入失败：{ex}");
            return true;
        }
    }
}

/// <summary>原版排序按钮的回调先退出扩展排序，保留全部原版选项。</summary>
[HarmonyPatch(typeof(SongSelectScene._Jc), nameof(SongSelectScene._Jc._vp))]
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
