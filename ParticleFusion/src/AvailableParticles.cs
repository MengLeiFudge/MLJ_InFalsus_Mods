extern alias GameData;

using ifapp.Game.Data;
using SaveOwner = GameData::_K._NH;

namespace InFalsusMod;

/// <summary>本次操作中，已解锁回想能够生成的完整粒子组合。</summary>
internal sealed class AvailableParticles
{
    /// <summary>按具体粒子编号分组的独立来源；不同来源的技能池不合并。</summary>
    private readonly Dictionary<ushort, List<Source>> sources = new();

    /// <summary>读取当前存档可重复挑战的回想及其实际掉落表。</summary>
    /// <param name="owner">当前存档访问入口。</param>
    internal AvailableParticles(SaveOwner owner)
    {
        var data = owner._VNA();
        var game = data?.GameData;
        if (game == null || data!.EncounterDetails == null)
            throw new InvalidOperationException("回想掉落定义尚未就绪。");

        TraitCatalog.Validate(game);
        var unlocked = new Il2CppSystem.Collections.Generic.List<EncounterId>();
        owner._BOA(unlocked, -1);
        var ids = new HashSet<int>();
        foreach (var id in unlocked)
            ids.Add(id.Value);

        foreach (var encounter in data.EncounterDetails.encounterDetails)
        {
            if (!ids.Contains(encounter.Id.Value))
                continue;
            int index = encounter.LootTableIndex;
            if (index < 0 || index >= game.lootIotaTables.Length)
                throw new InvalidOperationException($"回想 {encounter.Id.Value} 的掉落表无效。");
            foreach (var row in game.lootIotaTables[index].Rows)
            {
                if (row.Weight <= 0)
                    continue;
                if (row.IotaId.Value is < 1 or > 40 || row.MinPotency < 1
                    || row.MaxPotency > 999 || row.MinPotency > row.MaxPotency || row.TraitRolls < 0)
                    throw new InvalidOperationException("粒子掉落定义已改变，需要适配。");

                var traits = new Dictionary<short, int>();
                bool canBeEmpty = row.TraitRolls == 0;
                if (row.TraitRolls > 0)
                {
                    // 游戏按回想的掉落表编号选择技能池；行内 TraitTableIndex 是未使用的旧字段。
                    if (index >= game.lootTraitTables.Length)
                        throw new InvalidOperationException("技能掉落表编号无效。");
                    foreach (var trait in game.lootTraitTables[index].Rows)
                    {
                        if (trait.Weight <= 0)
                            continue;
                        short id = trait.TraitId.Value;
                        if (id <= 0)
                        {
                            canBeEmpty = true;
                            continue;
                        }
                        if (id >= game.traits.Length || game.traits[id].Id.Value != id)
                            throw new InvalidOperationException("技能掉落表与技能定义不一致。");
                        // 游戏按掉落表行去重抽取；同一技能出现在多行时才可能重复携带。
                        traits[id] = traits.GetValueOrDefault(id) + 1;
                    }
                }
                ushort shape = row.IotaId.Value;
                if (!sources.TryGetValue(shape, out var list))
                    sources.Add(shape, list = new List<Source>());
                list.Add(new Source(row.MinPotency, row.MaxPotency, Math.Min(3, row.TraitRolls), canBeEmpty, traits));
            }
        }
        if (sources.Count == 0)
            throw new InvalidOperationException("尚无可读取掉落规则的已解锁回想。");
    }

    /// <summary>取得保持当前技能组合时可达到的最高效能，不会降低旧库存数值。</summary>
    /// <param name="item">待检查粒子。</param>
    /// <returns>合法来源的最高效能；组合无法合法生成时为零。</returns>
    internal int MaximumPotency(IotaInstance item)
    {
        int maximum = 0;
        if (sources.TryGetValue(ParticleData.Shape(item), out var list))
            foreach (var source in list)
                if (source.SupportsTraits(item) && ParticleData.Potency(item) >= source.Minimum)
                    maximum = Math.Max(maximum, source.Maximum);
        return maximum;
    }

    /// <summary>检查效能、技能数量及全部技能是否能由同一个已解锁来源生成。</summary>
    /// <param name="item">候选粒子。</param>
    /// <returns>整颗粒子是否合法可得。</returns>
    internal bool Contains(IotaInstance item)
    {
        if (!sources.TryGetValue(ParticleData.Shape(item), out var list))
            return false;
        foreach (var source in list)
            if (ParticleData.Potency(item) >= source.Minimum && ParticleData.Potency(item) <= source.Maximum && source.SupportsTraits(item))
                return true;
        return false;
    }

    /// <summary>取得本种粒子当前来源可能提供的三级技能，用于超额库存的目标比例。</summary>
    /// <param name="shape">具体粒子编号。</param>
    /// <returns>不合并为合法组合，仅列出单项需求集合。</returns>
    internal IEnumerable<short> ThirdTraits(ushort shape) => sources.TryGetValue(shape, out var list)
        ? list.Where(source => source.Capacity > 0).SelectMany(source => source.Traits.Keys)
            .Where(TraitDemand.IsThird).Distinct()
        : Enumerable.Empty<short>();

    /// <summary>检查现有特性是否能在当前效能下，由同一来源补齐到三槽。</summary>
    /// <param name="item">接收者的当前或候选特性前缀。</param>
    /// <returns>是否存在容量、技能表行数和效能均满足要求的完整来源。</returns>
    internal bool CanCompleteThree(IotaInstance item)
    {
        if (!sources.TryGetValue(ParticleData.Shape(item), out var list))
            return false;
        return list.Any(source => source.HasThreeSlots
            && ParticleData.Potency(item) >= source.Minimum && ParticleData.Potency(item) <= source.Maximum
            && source.FitsTraits(item));
    }

    /// <summary>单个掉落条目的效能区间、技能抽取容量及技能表行数。</summary>
    private sealed record Source(int Minimum, int Maximum, int Capacity, bool CanBeEmpty, Dictionary<short, int> Traits)
    {
        /// <summary>来源同时具备三个抽取槽和至少三条非空特性行，允许生成三槽结果。</summary>
        internal bool HasThreeSlots { get; } = Capacity == 3 && Traits.Values.Sum() >= 3;

        /// <summary>检查完整技能组合，不把不同来源的技能拼接起来。</summary>
        /// <param name="item">候选粒子。</param>
        /// <returns>技能数量及每项重复次数是否允许。</returns>
        internal bool SupportsTraits(IotaInstance item) => (ParticleData.TraitCount(item) > 0 || CanBeEmpty) && FitsTraits(item);

        /// <summary>校验特性前缀的容量和重复次数；空前缀可用于判断未来三槽组合。</summary>
        /// <param name="item">当前或候选粒子。</param>
        /// <returns>所有已有特性是否均来自本来源且未超额。</returns>
        internal bool FitsTraits(IotaInstance item)
        {
            if (ParticleData.TraitCount(item) > Capacity)
                return false;
            for (int slot = 0; slot < ParticleData.TraitCount(item); slot++)
            {
                short id = ParticleData.GetTrait(item, slot);
                int count = 1;
                for (int previous = 0; previous < slot; previous++)
                    if (ParticleData.GetTrait(item, previous) == id)
                        count++;
                if (id <= 0 || Traits.GetValueOrDefault(id) < count)
                    return false;
            }
            return true;
        }
    }
}
