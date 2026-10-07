using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>跟踪每种粒子的数量和三级技能携带颗数，供免费交换检查保有量与分布。</summary>
internal sealed class TraitStock
{
    /// <summary>各具体种类颗数。</summary>
    private readonly Dictionary<ushort, int> quantities = new();
    /// <summary>每种粒子、每项三级技能的实际携带者数量。</summary>
    private readonly Dictionary<(ushort Shape, short Trait), int> carriers = new();
    /// <summary>本轮每种粒子的合法需求技能集合。</summary>
    private readonly Dictionary<ushort, short[]> traits;

    /// <summary>从库存和当前解锁来源建立计数；旧库存中已有技能也参与保护。</summary>
    /// <param name="items">堆叠或逐颗库存。</param>
    /// <param name="available">当前合法来源。</param>
    internal TraitStock(IEnumerable<IotaInstance> items, AvailableParticles available)
    {
        foreach (var item in items)
            Change(item, item.Count);
        traits = quantities.Keys.ToDictionary(shape => shape, shape => available.ThirdTraits(shape)
            .Concat(carriers.Keys.Where(key => key.Shape == shape).Select(key => key.Trait)).Distinct().ToArray());
    }

    /// <summary>复制事务计数，失败规划不污染真实会话状态。</summary>
    /// <param name="source">当前已提交的会话计数。</param>
    internal TraitStock(TraitStock source)
    {
        quantities = new(source.quantities);
        carriers = new(source.carriers);
        traits = source.traits;
    }

    /// <summary>取得某项技能的携带者颗数。</summary>
    /// <param name="shape">具体粒子编号。</param>
    /// <param name="trait">三级技能编号。</param>
    /// <returns>相同技能在同一颗上只计一次。</returns>
    internal int Count(ushort shape, short trait) => carriers.GetValueOrDefault((shape, trait));

    /// <summary>列出一颗粒子携带的不同三级技能。</summary>
    /// <param name="item">完整粒子数据。</param>
    /// <returns>最多三个不同编号。</returns>
    internal static IEnumerable<short> ThirdTraits(IotaInstance item)
    {
        for (int slot = 0; slot < ParticleData.TraitCount(item); slot++)
        {
            short id = ParticleData.GetTrait(item, slot);
            if (!TraitDemand.IsThird(id))
                continue;
            bool duplicate = false;
            for (int previous = 0; previous < slot; previous++)
                duplicate |= ParticleData.GetTrait(item, previous) == id;
            if (!duplicate)
                yield return id;
        }
    }

    /// <summary>增减同一完整数据的若干颗；不使用item.Count以免重复乘堆叠。</summary>
    /// <param name="item">代表材料。</param>
    /// <param name="copies">有符号颗数。</param>
    internal void Change(IotaInstance item, int copies)
    {
        ushort shape = ParticleData.Shape(item);
        quantities[shape] = checked(quantities.GetValueOrDefault(shape) + copies);
        foreach (short id in ThirdTraits(item))
            carriers[(shape, id)] = checked(Count(shape, id) + copies);
    }

    /// <summary>检查一次批量交换不会减少不足3颗的技能或跌破3颗。</summary>
    /// <param name="before">交换前两端，各含一颗。</param>
    /// <param name="after">交换后两端，各含一颗。</param>
    /// <param name="copies">两端同时交换的颗数。</param>
    /// <returns>是否可以提交交换。</returns>
    internal bool CanExchange(IotaInstance[] before, IotaInstance[] after, int copies)
    {
        var delta = new Dictionary<(ushort Shape, short Trait), int>();
        foreach (var item in before)
            foreach (short id in ThirdTraits(item))
            {
                var key = (ParticleData.Shape(item), id);
                delta[key] = delta.GetValueOrDefault(key) - copies;
            }
        foreach (var item in after)
            foreach (short id in ThirdTraits(item))
            {
                var key = (ParticleData.Shape(item), id);
                delta[key] = delta.GetValueOrDefault(key) + copies;
            }
        if (delta.Any(entry => Count(entry.Key.Shape, entry.Key.Trait) + entry.Value
            < Math.Min(3, Count(entry.Key.Shape, entry.Key.Trait))))
            return false;
        foreach (ushort shape in delta.Keys.Select(key => key.Shape).Distinct())
        {
            var supported = traits.GetValueOrDefault(shape, Array.Empty<short>());
            int beforeMissing = supported.Sum(id => Math.Max(0, 3 - Count(shape, id)));
            int afterMissing = supported.Sum(id => Math.Max(0, 3 - Count(shape, id) - delta.GetValueOrDefault((shape, id))));
            if (afterMissing == beforeMissing && Mismatch(shape, delta) > Mismatch(shape, null) + 1e-10)
                return false;
        }
        return true;
    }

    /// <summary>计算超额库存相对于正权重目标的归一化偏差，供免费交换判断是否破坏分布。</summary>
    /// <param name="shape">具体种类。</param>
    /// <param name="delta">交换后的携带数变化；空值表示当前分布。</param>
    /// <returns>加权平方偏差；没有超额库存时为零。</returns>
    private double Mismatch(ushort shape, Dictionary<(ushort Shape, short Trait), int>? delta)
    {
        var supported = traits.GetValueOrDefault(shape, Array.Empty<short>());
        var excess = supported.Select(id => Math.Max(0, Count(shape, id) + (delta?.GetValueOrDefault((shape, id)) ?? 0) - 3)).ToArray();
        double total = excess.Sum();
        double weights = supported.Sum(TraitDemand.Weight);
        if (total == 0 || weights == 0)
            return 0;
        double error = 0;
        for (int i = 0; i < supported.Length; i++)
        {
            double expected = TraitDemand.Weight(supported[i]) / weights;
            double difference = excess[i] / total - expected;
            error += difference * difference / expected;
        }
        return error;
    }
}
