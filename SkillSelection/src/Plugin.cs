using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace InFalsusMod;

/// <summary>为 Windows IL2CPP 版 In Falsus 注册制卡技能选择。</summary>
[BepInPlugin("menglei.infalsus.skillselection", "In Falsus Skill Selection", "1.0.1")]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>供 Unity 回调记录筛选状态及异常。</summary>
    internal static ManualLogSource Logger { get; private set; } = null!;

    /// <summary>持有制卡原生钩子与委托，生命周期与插件一致。</summary>
    private SkillFilterNativeHooks? filterHooks;

    /// <inheritdoc />
    public override void Load()
    {
        Logger = Log;
        WindowsConsoleEncoding.EnsureUtf8(Log);
        try
        {
            filterHooks = new SkillFilterNativeHooks();
            AddComponent<SkillFilterPanel>();
        }
        catch
        {
            filterHooks?.Dispose();
            filterHooks = null;
            throw;
        }
        Log.LogInfo("加载完成。");
    }
}
