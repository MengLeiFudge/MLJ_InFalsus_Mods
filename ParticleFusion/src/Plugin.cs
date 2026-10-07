using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace InFalsusMod;

/// <summary>为 Windows IL2CPP 版 In Falsus 注册粒子融合。</summary>
[BepInPlugin("menglei.infalsus.potency999", "In Falsus Particle Fusion", "1.0.1")]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>供 Unity 回调记录修改结果及异常。</summary>
    internal static ManualLogSource Logger { get; private set; } = null!;

    /// <inheritdoc />
    public override void Load()
    {
        Logger = Log;
        WindowsConsoleEncoding.EnsureUtf8(Log);
        AddComponent<InventoryButtonBehaviour>();
        Log.LogInfo("加载完成。");
    }
}
