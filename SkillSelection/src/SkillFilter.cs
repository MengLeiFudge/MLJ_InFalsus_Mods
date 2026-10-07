using ifapp.Game.Common;
using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>制卡技能筛选条件及其只读列表视图，不删除或占用真实库存。</summary>
internal static class SkillFilter
{
    /// <summary>选中的同名技能组，不区分 I/II/III 后缀。</summary>
    internal static readonly HashSet<string> Families = new(StringComparer.Ordinal);

    /// <summary>选中的精确技能等级 I/II/III；空集合与全选均允许全部等级。</summary>
    internal static readonly HashSet<int> Tiers = new();

    /// <summary>为真时要求每个所选技能组都存在，否则命中任意一组即可。</summary>
    internal static bool MatchAll;

    /// <summary>编号到静态技能定义的查找表。</summary>
    private static readonly Dictionary<int, TraitInfo> Traits = TraitCatalog.Entries.ToDictionary(info => info.Id);

    /// <summary>只选一至两个等级时才限制等级；空集合与三级全选都表示不限。</summary>
    private static bool TierLimited => Tiers.Count is > 0 and < 3;

    /// <summary>是否有任何条件需要过滤材料：选了技能组，或单独限定了等级。</summary>
    internal static bool Active => Families.Count > 0 || TierLimited;

    /// <summary>判断已选技能条件是否应参与当前原版筛选模式。</summary>
    /// <param name="mode">原版特性筛选模式。</param>
    /// <returns>当前模式是否应用额外技能条件。</returns>
    internal static bool AppliesSelection(IotaFilterParameters._bH mode) =>
        mode is IotaFilterParameters._bH._Pdb or IotaFilterParameters._bH._Qdb;

    /// <summary>为已有列表建立筛选视图，保持原来的堆叠数量和排序。</summary>
    /// <param name="source">制卡当前可用材料，不含已消耗的数量。</param>
    /// <returns>未筛选时返回原列表，否则返回独立列表。</returns>
    internal static UnorderedListVal<IotaInstance> View(UnorderedListVal<IotaInstance> source)
    {
        if (!Active)
            return source;
        var filtered = new UnorderedListVal<IotaInstance>(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            var item = source.RawBuffer[i];
            if (Matches(item))
                filtered.Add(item);
        }
        return filtered;
    }

    /// <summary>
    /// 检查一颗粒子当前是否满足筛选条件。未选技能组时，只要携带任一所选等级的技能即命中；
    /// 选了技能组时，按“匹配任意 / 匹配全部”检查各组，且命中的技能须属于所选等级。
    /// </summary>
    /// <param name="item">库存中的实际粒子。</param>
    /// <returns>是否满足全部筛选条件。</returns>
    internal static bool Matches(IotaInstance item)
    {
        if (Families.Count == 0)
        {
            if (!TierLimited)
                return true;
            for (int slot = 0; slot < ParticleData.TraitCount(item); slot++)
                if (Traits.TryGetValue(ParticleData.GetTrait(item, slot), out var trait) && Tiers.Contains(trait.Tier))
                    return true;
            return false;
        }
        foreach (string family in Families)
        {
            bool found = false;
            for (int slot = 0; slot < ParticleData.TraitCount(item); slot++)
                if (Traits.TryGetValue(ParticleData.GetTrait(item, slot), out var trait)
                    && trait.Family == family && (!TierLimited || Tiers.Contains(trait.Tier)))
                {
                    found = true;
                    break;
                }
            if (found && !MatchAll)
                return true;
            if (!found && MatchAll)
                return false;
        }
        return MatchAll;
    }
}
