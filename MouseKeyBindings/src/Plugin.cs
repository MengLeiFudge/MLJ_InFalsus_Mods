using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace InFalsusMod;

/// <summary>为 Windows IL2CPP 版 In Falsus 注册按键绑定扩展。</summary>
[BepInPlugin(PluginGuid, "KeyBindingExtensions", PluginVersion)]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>BepInEx 插件标识，同时用作 Harmony 补丁所有者。</summary>
    internal const string PluginGuid = "menglei.infalsus.mousekeybindings";

    /// <summary>插件及 Thunderstore 包版本。</summary>
    internal const string PluginVersion = "1.1.0";

    /// <summary>供输入补丁记录初始化和运行错误。</summary>
    internal static ManualLogSource Logger { get; private set; } = null!;

    /// <summary>持有本插件注册的 Harmony 补丁。</summary>
    private Harmony? harmony;

    /// <inheritdoc />
    public override void Load()
    {
        Logger = Log;
        WindowsConsoleEncoding.EnsureUtf8(Log);
        harmony = new Harmony(PluginGuid);
        harmony.PatchAll(typeof(Plugin).Assembly);
        Log.LogInfo("加载完成。");
    }
}
