using System.Reflection;
using System.Text.Json;

namespace InFalsusMod;

/// <summary>网页惩罚2配队的实际成品需求，只影响保留优先级，不生成或转换技能。</summary>
internal static class TraitDemand
{
    /// <summary>三级玩家技能的去重复用频次，按稳定技能编号索引。</summary>
    private static readonly Dictionary<short, int> counts = Load();

    /// <summary>读取嵌入资源并核对玩家技能范围及权重公式。</summary>
    /// <returns>实际技能槽出现次数。</returns>
    private static Dictionary<short, int> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("InFalsusMod.Data.trait-demand.json")
            ?? throw new InvalidOperationException("Missing trait demand resource.");
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<short, int>();
        foreach (var row in document.RootElement.GetProperty("Skills").EnumerateArray())
        {
            short id = row.GetProperty("Id").GetInt16();
            int count = row.GetProperty("Count").GetInt32();
            if (count < 0 || row.GetProperty("Weight").GetInt32() != count / 4 + 1)
                throw new InvalidOperationException("Invalid trait demand weight.");
            result.Add(id, count);
        }
        if (!result.Keys.ToHashSet().SetEquals(TraitCatalog.Entries.Where(info => info.Tier == 3).Select(info => (short)info.Id)))
            throw new InvalidOperationException("Trait demand catalog mismatch.");
        return result;
    }

    /// <summary>判断技能是否属于本次需求统计覆盖的玩家三级技能。</summary>
    /// <param name="trait">实际技能编号。</param>
    /// <returns>是否需要最低携带量保护。</returns>
    internal static bool IsThird(short trait) => counts.ContainsKey(trait);

    /// <summary>按floor(出现次数/4)+1计算权重，任何技能都不返回零。</summary>
    /// <param name="trait">实际技能编号。</param>
    /// <returns>正整数权重，未统计的低级技能为1。</returns>
    internal static int Weight(short trait) => counts.GetValueOrDefault(trait) / 4 + 1;
}
