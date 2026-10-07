using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>读取和修改游戏粒子的紧凑字段编码，不包含融合或筛选策略。</summary>
internal static class ParticleData
{
    /// <summary>读取颜色、阶级与形状共同确定的具体粒子编号。</summary>
    /// <param name="item">粒子数据副本。</param>
    /// <returns>具体粒子编号。</returns>
    internal static ushort Shape(IotaInstance item) => (ushort)((item.BaseIotaIdPotency >> 12) & 0x3f);

    /// <summary>读取粒子的当前效能。</summary>
    /// <param name="item">粒子数据副本。</param>
    /// <returns>范围为 1–999 的效能。</returns>
    internal static int Potency(IotaInstance item) => (item.BaseIotaIdPotency >> 2) & 0x3ff;

    /// <summary>读取实际技能数量；游戏将低两位编码为 3 减去实际数量。</summary>
    /// <param name="item">粒子数据副本。</param>
    /// <returns>0–3 个技能。</returns>
    internal static int TraitCount(IotaInstance item) => 3 - (item.BaseIotaIdPotency & 3);

    /// <summary>读取粒子的指定技能槽。</summary>
    /// <param name="item">粒子数据副本。</param>
    /// <param name="slot">从零开始的技能槽编号。</param>
    /// <returns>技能编号。</returns>
    internal static short GetTrait(IotaInstance item, int slot) => slot switch
    {
        0 => item.TraitId1,
        1 => item.TraitId2,
        2 => item.TraitId3,
        _ => throw new ArgumentOutOfRangeException(nameof(slot))
    };

    /// <summary>修改一个技能槽，并在追加技能时增加实际技能数。</summary>
    /// <param name="item">要修改的临时粒子副本。</param>
    /// <param name="slot">要替换或追加的槽编号，不得跳过空位。</param>
    /// <param name="trait">正的技能编号。</param>
    internal static void SetTrait(ref IotaInstance item, int slot, short trait)
    {
        if (slot < 0 || slot > TraitCount(item) || slot >= 3 || trait <= 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        switch (slot)
        {
            case 0: item.TraitId1 = trait; break;
            case 1: item.TraitId2 = trait; break;
            case 2: item.TraitId3 = trait; break;
        }
        if (slot == TraitCount(item))
            item.BaseIotaIdPotency = (item.BaseIotaIdPotency & ~3) | (3 - (slot + 1));
    }

    /// <summary>仅改变效能编码，保留形状、技能槽数和其他数据。</summary>
    /// <param name="item">要修改的临时粒子副本。</param>
    /// <param name="potency">目标效能，范围 1–999。</param>
    internal static void SetPotency(ref IotaInstance item, int potency)
    {
        if (potency is < 1 or > 999)
            throw new ArgumentOutOfRangeException(nameof(potency));
        item.BaseIotaIdPotency = (item.BaseIotaIdPotency & ~(0x3ff << 2)) | (potency << 2);
    }
}
