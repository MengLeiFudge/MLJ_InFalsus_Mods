using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>融合的固定费用与粒子策略，不依赖玩家库存。</summary>
internal static class FusionRules
{
    /// <summary>
    /// 每种具体粒子的满效能成品按技能数（下标0–3）的目标占比。
    /// 只在效能相同（费用相同）的目标之间决定先强化哪一颗，不影响材料消耗；三技能份额受掉落供给限制，实际通常达不到。
    /// </summary>
    internal static readonly IReadOnlyList<double> SkillRatios = new[] { 0.4, 0.1, 0.2, 0.3 };

    /// <summary>
    /// 智能模式的目标与材料分界：效能距离自身合法上限不超过此值的粒子只作强化目标、从不分解，更低的只作材料。
    /// 上限为999时分界为900。到上限的费用随距离增大而陡增（999时，900约3762点，800约7162点，600约12762点），
    /// 只强化接近上限的粒子、且不拿它们互相供能，能显著降低每颗新成品消耗的材料颗数；代价是低效能粒子不会被补满。
    /// 选中模式由玩家指定材料，未选中的粒子都可作为目标，不受此分界限制。
    /// </summary>
    internal const int TargetWindow = 99;

    /// <summary>判断粒子是否已达到自身合法上限，即满效能成品；成品永不作为供体。</summary>
    /// <param name="item">粒子副本。</param>
    /// <param name="cap">该组合在已解锁回想中的合法最高效能；无法合法生成时为零。</param>
    /// <returns>存在合法来源且效能不低于其最高效能。</returns>
    internal static bool IsComplete(IotaInstance item, int cap) => cap > 0 && ParticleData.Potency(item) >= cap;

    /// <summary>判断粒子本身是否允许被分解为供体，不考虑用户选择或本次限额。</summary>
    /// <param name="item">粒子副本。</param>
    /// <param name="cap">该组合的合法最高效能。</param>
    /// <returns>未满效能且技能少于三个时为真；三技能的未满粒子作为高价值粒子保留。</returns>
    internal static bool CanDonate(IotaInstance item, int cap) => ParticleData.Potency(item) > 0
        && !IsComplete(item, cap) && ParticleData.TraitCount(item) < 3;

    /// <summary>判断粒子能否作为强化目标：存在合法来源且尚未达到其最高效能。</summary>
    /// <param name="item">粒子副本。</param>
    /// <param name="cap">该组合的合法最高效能。</param>
    /// <returns>强化到 <paramref name="cap"/> 后仍是合法组合。</returns>
    internal static bool CanTarget(IotaInstance item, int cap) => cap > 0 && ParticleData.Potency(item) < cap;

    /// <summary>判断粒子在智能模式下是否作为强化目标：未满效能且距离自身上限不超过 <see cref="TargetWindow"/>。</summary>
    /// <param name="item">粒子副本。</param>
    /// <param name="cap">该组合的合法最高效能。</param>
    /// <returns>是否只强化、不分解。</returns>
    internal static bool IsSmartTarget(IotaInstance item, int cap) => CanTarget(item, cap)
        && ParticleData.Potency(item) >= cap - TargetWindow;

    /// <summary>判断粒子在智能模式下是否作为材料：本身可分解且不属于智能目标。</summary>
    /// <param name="item">粒子副本。</param>
    /// <param name="cap">该组合的合法最高效能。</param>
    /// <returns>是否计入智能模式的材料与材料上限。</returns>
    internal static bool IsSmartMaterial(IotaInstance item, int cap) => CanDonate(item, cap) && !IsSmartTarget(item, cap);

    /// <summary>按提升后的效能计算这一点的整数费用。</summary>
    /// <param name="potency">提升后的效能，范围 1–999。</param>
    /// <returns>本次提升一点所需的效能。</returns>
    internal static int PointCost(int potency)
    {
        if (potency is < 1 or > 999)
            throw new ArgumentOutOfRangeException(nameof(potency));
        return potency <= 200 ? 6 : 6 + ((potency - 101) / 100) * 4;
    }

    /// <summary>计算从当前效能一次提升到合法上限的完整费用。</summary>
    /// <param name="potency">原效能。</param>
    /// <param name="maximum">目标合法上限。</param>
    /// <returns>整数效能费用。</returns>
    internal static int TotalCost(int potency, int maximum)
    {
        int cost = 0;
        for (int value = potency + 1; value <= maximum; value++)
            cost += PointCost(value);
        return cost;
    }

    /// <summary>制作原供体本体的无特性余料，不改变颜色、阶级、形状或标记。</summary>
    /// <param name="item">原供体。</param>
    /// <param name="potency">正整数余数。</param>
    /// <returns>Count为1的无特性余料。</returns>
    internal static IotaInstance Remainder(IotaInstance item, int potency)
    {
        item.BaseIotaIdPotency |= 3;
        item.TraitId1 = item.TraitId2 = item.TraitId3 = 0;
        item.Count = 1;
        ParticleData.SetPotency(ref item, potency);
        return item;
    }
}
