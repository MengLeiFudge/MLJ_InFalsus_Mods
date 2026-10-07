using System.Reflection;
using System.Text.Json;

namespace InFalsusMod;

/// <summary>玩家可掉落技能的名称、等级与身份定义，供筛选和兼容性检查共用。</summary>
internal static class TraitCatalog
{
    /// <summary>从与适配游戏同版本的 InFalsusCalc 资源提取的技能定义。</summary>
    internal static readonly TraitInfo[] Entries = Load();

    /// <summary>读取嵌入的静态技能定义，不依赖计算器安装路径。</summary>
    /// <returns>按游戏技能编号排列的玩家技能。</returns>
    private static TraitInfo[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("InFalsusMod.Data.traits.json")
            ?? throw new InvalidOperationException("缺少技能定义资源。");
        return JsonSerializer.Deserialize<TraitInfo[]>(stream)
            ?? throw new InvalidOperationException("技能定义资源为空。");
    }

    /// <summary>核对用于统计和范围化的技能身份；数值参数可随游戏平衡调整变化。</summary>
    /// <param name="game">当前游戏定义。</param>
    internal static void Validate(ifapp.Game.Data.GameData game)
    {
        foreach (var info in Entries)
        {
            if (info.Id <= 0 || info.Id >= game.traits.Length)
                throw new InvalidOperationException("技能编号已改变，需要适配。");
            var trait = game.traits[info.Id];
            if (trait.Id.Value != info.Id || trait.Tier != info.Tier || trait.EffectCount != info.Effects.Length)
                throw new InvalidOperationException($"技能 {info.Id} 的身份或等级已改变。");
            for (int i = 0; i < info.Effects.Length; i++)
            {
                var effect = i == 0 ? trait.Effect0 : trait.Effect1;
                if ((int)effect.TraitEffect != info.Effects[i] || (int)effect.TraitActivationCondition != info.Conditions[i])
                    throw new InvalidOperationException($"技能 {info.Id} 的效果或触发条件已改变。");
            }
        }
    }
}

/// <summary>一个玩家技能的静态身份与筛选分组信息。</summary>
internal sealed class TraitInfo
{
    /// <summary>供 JSON 反序列化创建技能定义。</summary>
    public TraitInfo() { }

    /// <summary>游戏内稳定技能编号。</summary>
    public int Id { get; init; }
    /// <summary>游戏中文显示名称。</summary>
    public string Name { get; init; } = "";
    /// <summary>技能等级，与粒子阶级不同。</summary>
    public int Tier { get; init; }
    /// <summary>效果类别序列，不包括可调整的强度参数。</summary>
    public int[] Effects { get; init; } = Array.Empty<int>();
    /// <summary>与效果一一对应的触发条件。</summary>
    public int[] Conditions { get; init; } = Array.Empty<int>();

    /// <summary>筛选使用的同名技能组，省略 II/III 等级后缀。</summary>
    internal string Family => Name.EndsWith(" III", StringComparison.Ordinal) ? Name[..^4]
        : Name.EndsWith(" II", StringComparison.Ordinal) ? Name[..^3] : Name;
}
