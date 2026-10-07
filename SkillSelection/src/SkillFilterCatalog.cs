namespace InFalsusMod;

/// <summary>定义特性筛选的展示分组与顺序；材料匹配仍使用 TraitCatalog 的真实身份。</summary>
internal static class SkillFilterCatalog
{
    /// <summary>按伤害、回复、增益、减益排列的四行；每个编号代表该族最低等级。</summary>
    internal static readonly SkillFilterGroup[] Groups =
    {
        new("DamageTraits", new[] { 2, 5, 8, 11, 12, 13, 63 }),
        new("HealingTraits", new[] { 22, 25, 28, 31, 32, 33 }),
        // 内在增强、精神强化、集中强化及两种减益的范围版均紧邻非范围版。
        new("BuffTraits", new[] { 36, 39, 37, 38, 40, 43, 46, 49 }),
        new("DebuffTraits", new[] { 16, 18, 19, 20, 21 })
    };

    /// <summary>按分组展开的完整技能族，与面板按钮和悬浮目标使用相同索引。</summary>
    internal static readonly TraitInfo[] Families = LoadFamilies();

    /// <summary>把展示编号解析为静态定义，并在资源边界检查遗漏或重复的技能族。</summary>
    /// <returns>每族恰好一个、按行排列的代表定义。</returns>
    private static TraitInfo[] LoadFamilies()
    {
        var entries = TraitCatalog.Entries.ToDictionary(info => info.Id);
        var families = Groups.SelectMany(group => group.TraitIds).Select(id => entries[id]).ToArray();
        var expected = TraitCatalog.Entries.Select(info => info.Family).ToHashSet(StringComparer.Ordinal);
        if (families.Select(info => info.Family).Distinct(StringComparer.Ordinal).Count() != families.Length
            || !expected.SetEquals(families.Select(info => info.Family)))
            throw new InvalidOperationException("特性展示分组与游戏技能目录不一致。");
        return families;
    }
}

/// <summary>一行同类特性的本地化标题和稳定展示编号。</summary>
/// <param name="TitleKey">模组五语言分类标题键。</param>
/// <param name="TraitIds">各族最低等级的原版编号；范围版紧邻非范围版。</param>
internal sealed record SkillFilterGroup(string TitleKey, int[] TraitIds);
