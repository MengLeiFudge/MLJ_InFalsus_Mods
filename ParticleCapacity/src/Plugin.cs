using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ifapp.Game.Data;

namespace InFalsusMod.ParticleCapacity;

/// <summary>解除原版9999颗门槛，在原生16位材料编号范围内扩展库存。</summary>
[BepInPlugin("menglei.infalsus.particlecapacity", "In Falsus Particle Capacity", "1.0.0")]
[BepInProcess("infalsus.exe")]
public sealed class Plugin : BasePlugin
{
    /// <summary>原生制卡逐颗分配ushort编号，库存总数保持在该范围内。</summary>
    internal const ushort Capacity = ushort.MaxValue;
    private ushort originalCapacity;
    private Harmony? harmony;

    /// <inheritdoc />
    public override void Load()
    {
        InFalsusMod.WindowsConsoleEncoding.EnsureUtf8(Log);
        originalCapacity = IotaInventory.MaxTotalIotaCount;
        harmony = new Harmony("menglei.infalsus.particlecapacity");
        try
        {
            harmony.PatchAll(typeof(Plugin).Assembly);
            IotaInventory.MaxTotalIotaCount = Capacity;
        }
        catch
        {
            harmony.UnpatchSelf();
            throw;
        }
        Log.LogInfo($"粒子容量已从 {originalCapacity} 提高至 {Capacity}；超过9999颗后仍可获得粒子。");
    }

    /// <inheritdoc />
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        harmony = null;
        if (IotaInventory.MaxTotalIotaCount == Capacity)
            IotaInventory.MaxTotalIotaCount = originalCapacity;
        return true;
    }
}

/// <summary>原生结算只在发奖前检查容量；逐次入库保护同批奖励跨越16位边界的情况。</summary>
[HarmonyPatch(typeof(IotaInventory), nameof(IotaInventory.Add))]
internal static class InventoryAddPatch
{
    /// <summary>仅限制无法安全编号的超额数量，其余仍走原生合并和脏标记流程。</summary>
    /// <param name="__instance">本次奖励的目标库存。</param>
    /// <param name="__0">原生准备入库的粒子堆叠。</param>
    [HarmonyPrefix]
    private static bool Prefix(IotaInventory __instance, ref IotaInstance __0)
    {
        var items = __instance.allIota;
        if (items == null)
            return true;
        long total = 0;
        var buffer = items.RawBuffer;
        for (int i = 0; i < items.Length; i++)
            total += buffer[i].Count;
        long remaining = Plugin.Capacity - total;
        if (remaining <= 0)
            return false;
        __0.Count = (ushort)Math.Min(__0.Count, remaining);
        return __0.Count != 0;
    }
}
