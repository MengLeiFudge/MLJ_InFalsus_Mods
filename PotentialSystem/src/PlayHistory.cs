using ifapp.Game.Data;

namespace InFalsusMod.Potential;

/// <summary>同一谱面的一局原生记录；顺序来自存档，不按可能缺失的时间重新排序。</summary>
/// <param name="Score">原始打击分数。</param>
/// <param name="Clear">同一局的完成等级。</param>
/// <param name="Combo">同一局最大连击。</param>
/// <param name="Miss">四种音符的 MISS 总和。</param>
/// <param name="Time">原生有效 UTC 时间；缺失时为空。</param>
/// <param name="NewBest">是否严格超过此前记录的最高分，首次记录不标新高。</param>
internal sealed record ChartPlay(ulong Score, byte Clear, int Combo, int Miss, DateTimeOffset? Time, bool NewBest);

/// <summary>仅在选择谱面时复制原生逐局数据；不写游戏存档，不把最佳 PTT 当作最高分。</summary>
internal static class PlayHistory
{
    /// <summary>读取指定歌曲和难度的完整历史。空历史合法，缺失或无效历史明确报错。</summary>
    /// <param name="results">当前账号的原生成绩。</param>
    /// <param name="key">歌曲与难度身份。</param>
    /// <returns>按存档先后排列的托管副本，关闭页面后可直接回收。</returns>
    internal static IReadOnlyList<ChartPlay> Read(GameResultsV4 results, ChartKey key)
    {
        var history = results.history ?? throw new InvalidDataException("游戏逐局记录不可用。");
        var buffer = history.RawBuffer;
        int count = history.Length;
        if (count < 0 || (count > 0 && (buffer == null || count > buffer.Length)))
            throw new InvalidDataException("游戏逐局记录长度无效。");
        var plays = new List<ChartPlay>();
        ulong best = 0;
        for (int i = 0; i < count; i++)
        {
            var result = buffer![i];
            if (result.SongId.Value != key.SongId || (byte)result.Difficulty != key.Difficulty)
                continue;
            DateTimeOffset? time = result.SecondsSinceEpochUtc is > 0 and <= 253402300799UL
                ? DateTimeOffset.FromUnixTimeSeconds((long)result.SecondsSinceEpochUtc) : null;
            int miss = result.TapCounts.Miss + result.HoldCounts.Miss + result.SkyAreaCounts.Miss + result.FlickCounts.Miss;
            plays.Add(new ChartPlay(result.PlayerScore, (byte)result.CalculatedResultClear, result.MaxCombo, miss,
                time, plays.Count > 0 && result.PlayerScore > best));
            best = Math.Max(best, result.PlayerScore);
        }
        return plays.AsReadOnly();
    }
}
