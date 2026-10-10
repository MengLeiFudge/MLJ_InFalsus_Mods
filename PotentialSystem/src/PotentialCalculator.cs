using System.Globalization;

namespace InFalsusMod.Potential;

/// <summary>以游戏原始分数计算 Arcaea 新版单次潜力值、B50/B10 与显示精度。</summary>
internal static class PotentialCalculator
{
    /// <summary>满分且成功通关的单次潜力值相对定数增加量。</summary>
    internal const decimal MaximumPerformance = 2.2m;

    /// <summary>按完成等级判定 PTT 通关状态；原生 Lamp 仅看 Pace，可能漏掉未判定音符。</summary>
    /// <param name="resultClear">本次成绩或首次汇总候选的完成等级，取 0～4。</param>
    /// <returns>DiveCleared 及以上为 2，其余为 1。</returns>
    internal static byte CompletionLamp(byte resultClear)
    {
        if (resultClear > 4)
            throw new ArgumentOutOfRangeException(nameof(resultClear));
        return resultClear >= 2 ? (byte)2 : (byte)1;
    }

    /// <summary>计算成绩相对谱面定数的增量；游戏 100,000,000 分对应 Arcaea 10,000,000 分。</summary>
    /// <param name="score">原生 PlayerScore，超过满分时仍按满分处理。</param>
    /// <param name="lamp">用于 PTT 的通关状态：0 未知、1 未通关、2 通关。</param>
    /// <returns>可为负的分数增量，含本次通关奖励；尚未对单次潜力值截零。</returns>
    internal static decimal Performance(ulong score, byte lamp)
    {
        if (lamp > 2)
            throw new ArgumentOutOfRangeException(nameof(lamp), "未知原生通关状态。");
        decimal bonus = lamp == 2 ? 0.2m : 0m;
        if (score >= 100_000_000)
            return 2m + bonus;
        if (score >= 98_000_000)
            return 1m + (score - 98_000_000m) / 2_000_000m + bonus;
        return (score - 95_000_000m) / 3_000_000m + bonus;
    }

    /// <summary>使用当前游戏等级与同一次成绩计算不小于零的单曲潜力值。</summary>
    /// <param name="constant">游戏读取的非负整数 Rating。</param>
    /// <param name="score">原始打击分数。</param>
    /// <param name="lamp">与分数属于同一条成绩的通关状态。</param>
    /// <returns>用于 B50 排序的单曲潜力值。</returns>
    internal static decimal Play(int constant, ulong score, byte lamp)
    {
        if (constant < 0)
            throw new ArgumentOutOfRangeException(nameof(constant));
        return Math.Max(0m, constant + Performance(score, lamp));
    }

    /// <summary>把不同谱面的各自最高单次潜力值按固定 60 项权重合并；缺少的项贡献零。</summary>
    /// <param name="ratings">每个谱面最多一项的非负潜力值。</param>
    /// <returns>B50 与其前十项的总和除以 60。</returns>
    internal static decimal Overall(IEnumerable<decimal> ratings)
    {
        var best = ratings.OrderByDescending(value => value).Take(50).ToArray();
        return (best.Sum() + best.Take(10).Sum()) / 60m;
    }

    /// <summary>按 Wiki 的口径截断到三位小数；只用于显示，不影响排名或保存。</summary>
    /// <param name="value">非负潜力值或非负增加值。</param>
    /// <returns>使用小数点的三位小数字符串。</returns>
    internal static string Format(decimal value) =>
        (decimal.Floor(value * 1000m) / 1000m).ToString("F3", CultureInfo.InvariantCulture);
}
