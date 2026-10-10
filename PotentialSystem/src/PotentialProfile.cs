using System.Text.Json.Serialization;
using ifapp.Game.Data;

namespace InFalsusMod.Potential;

/// <summary>一个谱面的真实最佳 PTT、完成等级及取得时间。</summary>
internal sealed record BestPlay
{
    /// <summary>允许 System.Text.Json 创建记录。</summary>
    public BestPlay() { }
    /// <summary>原生歌曲身份。</summary>
    public ushort SongId { get; init; }
    /// <summary>原生单个难度位标记，取 1、2、4 或 8。</summary>
    public byte Difficulty { get; init; }
    /// <summary>记录产生时的谱面 Id，防止歌曲编号复用后串成绩。</summary>
    public string ChartId { get; init; } = "";
    /// <summary>原始打击分数；写为十进制字符串，避免网页读取 ulong 时丢失精度。</summary>
    [JsonNumberHandling(JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString)]
    public ulong Score { get; init; }
    /// <summary>用于 PTT 的通关状态，由同一次成绩的完成等级决定。</summary>
    public byte Lamp { get; init; }
    /// <summary>原生完成等级：None、DiveFailed、DiveCleared、FullLink、PerfectDive。</summary>
    public byte ResultClear { get; init; }
    /// <summary>计算并保存 Potential 时使用的游戏整数定数。</summary>
    public int Constant { get; init; }
    /// <summary>未截断的单曲潜力值，供独立网页与模组使用同一计算依据。</summary>
    public decimal Potential { get; init; }
    /// <summary>取得该纪录的 UTC 时间；原生历史未保存有效时间时为 null。</summary>
    public DateTimeOffset? AchievedAt { get; init; }
    /// <summary>history 为原生历史，play 为模组捕获的结算；加载器只为旧文件兼容识别 summary。</summary>
    public string Source { get; init; } = "";
    /// <summary>用于索引的歌曲与难度。</summary>
    [JsonIgnore]
    internal ChartKey Key => new(SongId, Difficulty);
}

/// <summary>与原生存档并列的最佳纪录和时间文件，规则身份避免与其他定数方案混用。</summary>
internal sealed class PotentialDocument
{
    /// <summary>允许 System.Text.Json 创建文档。</summary>
    public PotentialDocument() { }
    /// <summary>文件格式版本，当前为 1；缺失时的零值必须报错。</summary>
    public int SchemaVersion { get; init; }
    /// <summary>定数计算规则身份。</summary>
    public string Rule { get; init; } = "";
    /// <summary>首次创建独立文件的时间，不代表历史成绩的游玩时间。</summary>
    public DateTimeOffset? ImportedAt { get; init; }
    /// <summary>每个谱面至多一项的最佳表现；空集合合法，缺失集合非法。</summary>
    public List<BestPlay>? BestPlays { get; init; }
}

/// <summary>一项最佳成绩及其完整列表排名；已移除的谱面保留成绩，但不猜测定数或排名。</summary>
/// <param name="Rank">有效谱面按未截断单曲潜力值降序排列的从 1 开始的序号。</param>
/// <param name="Play">该谱面保留的最佳原始表现。</param>
/// <param name="Chart">能匹配身份时的当前原生谱面定义。</param>
/// <param name="Potential">按当前等级计算的单曲值，未知谱面为空。</param>
internal sealed record RankedPlay(int? Rank, BestPlay Play, ChartDefinition? Chart, decimal? Potential);

/// <summary>只读的完整成绩快照，统一供徽章、卡片、排序、浏览与导出使用。</summary>
internal sealed class PotentialSnapshot
{
    /// <summary>账号 B50/B10 潜力值。</summary>
    internal decimal Potential { get; }
    /// <summary>当前动态曲库理论上限。</summary>
    internal decimal Maximum { get; }
    /// <summary>全部已游玩谱面的最佳成绩，包括超过 B50 或暂时失去定义的成绩。</summary>
    internal IReadOnlyList<RankedPlay> Plays { get; }
    /// <summary>前 50 项可计算成绩；没有成绩的空位贡献零。</summary>
    internal IReadOnlyList<RankedPlay> B50 { get; }
    /// <summary>用于原版卡片的常数时间排名查询。</summary>
    private readonly Dictionary<ChartKey, RankedPlay> byChart;
    /// <summary>B50 中已有的有效成绩数量。</summary>
    internal int ChartCount => B50.Count;
    /// <summary>固定分母 50 的 B50 均值。</summary>
    internal decimal B50Average { get; }
    /// <summary>固定分母 10 的 B10 均值。</summary>
    internal decimal B10Average { get; }

    /// <summary>冻结一次计算的完整排名，避免 UI 每帧重新排序或只保留前 50 项。</summary>
    internal PotentialSnapshot(decimal maximum, IEnumerable<RankedPlay> plays)
    {
        Maximum = maximum;
        Plays = Array.AsReadOnly(plays.ToArray());
        B50 = Array.AsReadOnly(Plays.Where(play => play.Potential.HasValue).Take(50).ToArray());
        decimal sum50 = B50.Sum(play => play.Potential!.Value);
        decimal sum10 = B50.Take(10).Sum(play => play.Potential!.Value);
        Potential = (sum50 + sum10) / 60m;
        B50Average = sum50 / 50m;
        B10Average = sum10 / 10m;
        byChart = Plays.ToDictionary(play => play.Play.Key);
    }

    /// <summary>未游玩或身份无效的谱面没有可展示排名。</summary>
    internal RankedPlay? Find(ChartKey key) => byChart.TryGetValue(key, out var play) && play.Rank.HasValue ? play : null;
}

/// <summary>从原生逐局历史计算最佳表现，以独立文件保留对应真实成绩的已知时间。</summary>
internal sealed class PotentialProfile
{
    /// <summary>每个歌曲与难度的最佳成绩，包括暂时不在当前曲库中的身份。</summary>
    private readonly Dictionary<ChartKey, BestPlay> best = new();
    /// <summary>首次创建独立文件的时间，后续同步保留。</summary>
    private readonly DateTimeOffset importedAt;
    /// <summary>存在尚未成功写入磁盘的成绩变更。</summary>
    internal bool Dirty { get; set; }

    /// <summary>验证独立文档并恢复真实纪录的时间；旧组合条目在历史校准前丢弃。</summary>
    /// <param name="document">当前账号的独立 PTT 文档。</param>
    internal PotentialProfile(PotentialDocument document)
    {
        if (document.SchemaVersion != 1 || document.Rule != "integer-rating-v1"
            || document.ImportedAt == null || document.ImportedAt.Value.Offset != TimeSpan.Zero
            || document.BestPlays == null || document.BestPlays.Count > 10_000)
            throw new InvalidDataException("潜力值文件格式或定数规则不受支持。");
        importedAt = document.ImportedAt.Value;
        var keys = new HashSet<ChartKey>();
        foreach (var play in document.BestPlays)
        {
            if (play == null || play.SongId == 0 || play.Difficulty is not (1 or 2 or 4 or 8)
                || play.Lamp > 2 || play.ResultClear > 4 || (play.ResultClear >= 2) != (play.Lamp == 2)
                || play.Constant < 0 || string.IsNullOrWhiteSpace(play.ChartId)
                || play.Source is not ("history" or "summary" or "play")
                || (play.AchievedAt.HasValue && play.AchievedAt.Value.Offset != TimeSpan.Zero)
                || (play.Source == "summary" && play.AchievedAt.HasValue)
                || (play.Source == "play" && !play.AchievedAt.HasValue)
                || play.Potential != PotentialCalculator.Play(play.Constant, play.Score, play.Lamp)
                || !keys.Add(play.Key))
                throw new InvalidDataException("潜力值文件含无效、计算不一致或重复的谱面成绩。");
            if (play.Source == "summary")
                Dirty = true; // 兼容旧 v1 文件的退役记录；数值不能参与真实历史计算。
            else
                best.Add(play.Key, play);
        }
    }

    /// <summary>创建新账号的独立状态，成绩由随后读取的历史填入。</summary>
    /// <returns>需要首次保存的当前格式状态。</returns>
    internal static PotentialProfile Create() => new(new PotentialDocument
    {
        SchemaVersion = 1,
        Rule = "integer-rating-v1",
        ImportedAt = DateTimeOffset.UtcNow,
        BestPlays = new List<BestPlay>()
    }) { Dirty = true };

    /// <summary>从完整逐局历史重建当前谱面的最佳表现，并保留对应真实记录的已知时间。</summary>
    /// <param name="results">当前原生存档的成绩集合，只读取实际 history。</param>
    /// <param name="catalog">当前游戏实际可用的谱面定义。</param>
    /// <returns>成绩、时间或当前定数是否发生需要保存的变化。</returns>
    internal bool SynchronizeHistory(GameResultsV4 results, ChartCatalog catalog)
    {
        var history = results.history ?? throw new InvalidDataException("原生成绩缺少游玩历史。");
        var buffer = history.RawBuffer;
        int count = history.Length;
        if (buffer == null || count < 0 || count > buffer.Length)
            throw new InvalidDataException("原生成绩历史缓冲区无效。");
        var next = new Dictionary<ChartKey, BestPlay>();
        for (int i = 0; i < count; i++)
        {
            var result = buffer[i];
            var key = new ChartKey(result.SongId.Value, (byte)result.Difficulty);
            if (!catalog.TryGet(key, out var chart))
                continue;
            var play = CreatePlay(result, chart, "history", HistoryTime(result.SecondsSinceEpochUtc));
            // 同分但完成等级不同仍是不同记录，不能借用另一局的日期。
            if (best.TryGetValue(key, out var saved) && saved.ChartId == play.ChartId
                && saved.Score == play.Score && saved.ResultClear == play.ResultClear && saved.AchievedAt.HasValue
                && (!play.AchievedAt.HasValue || saved.AchievedAt.Value < play.AchievedAt.Value))
                play = play with { AchievedAt = saved.AchievedAt, Source = saved.Source };
            if (!next.TryGetValue(key, out var previous) || Prefer(play, previous))
                next[key] = play;
        }
        // 已移除的真实谱面留作归档；当前有效谱面的数值始终由历史重建。
        foreach (var play in best.Values)
            if (!catalog.TryGet(play.Key, out var chart) || chart.Id != play.ChartId)
                next.TryAdd(play.Key, play);
        if (next.Count == best.Count && next.All(entry => best.TryGetValue(entry.Key, out var play) && play == entry.Value))
            return false;
        best.Clear();
        foreach (var entry in next)
            best.Add(entry.Key, entry.Value);
        Dirty = true;
        return true;
    }

    /// <summary>按当前游戏定义计算排名，并同步文件中的定数与未截断 PTT。</summary>
    /// <param name="catalog">游戏实际加载的当前可用谱面。</param>
    /// <returns>固定分母 60 的账号潜力值和当前理论上限。</returns>
    internal PotentialSnapshot Calculate(ChartCatalog catalog)
    {
        var values = new List<RankedPlay>();
        foreach (var original in best.Values.ToArray())
        {
            var play = original;
            bool matched = catalog.TryGet(play.Key, out var chart) && chart.Id == play.ChartId;
            decimal? potential = matched ? PotentialCalculator.Play(chart.Constant, play.Score, play.Lamp) : null;
            if (matched && (play.Constant != chart.Constant || play.Potential != potential))
            {
                play = play with { Constant = chart.Constant, Potential = potential!.Value };
                best[play.Key] = play;
                Dirty = true;
            }
            values.Add(new RankedPlay(null, play, matched ? chart : null, potential));
        }
        var ordered = values.OrderByDescending(play => play.Potential.HasValue)
            .ThenByDescending(play => play.Potential).ThenBy(play => play.Play.SongId)
            .ThenBy(play => play.Play.Difficulty).ToArray();
        for (int i = 0; i < ordered.Length; i++)
            if (ordered[i].Potential.HasValue)
                ordered[i] = ordered[i] with { Rank = i + 1 };
        return new PotentialSnapshot(catalog.Maximum, ordered);
    }

    /// <summary>捕获真实结算及 UTC 时间，让接下来的历史同步保留该局的时间。</summary>
    /// <param name="result">本次游戏结算的成绩副本。</param>
    /// <param name="catalog">当前谱面定义。</param>
    /// <param name="achievedAt">模组观察到结算时的 UTC 时间。</param>
    /// <returns>最佳表现或已知时间是否发生变化。</returns>
    internal bool Observe(GameResultV4 result, ChartCatalog catalog, DateTimeOffset achievedAt)
    {
        var key = new ChartKey(result.SongId.Value, (byte)result.Difficulty);
        if (!catalog.TryGet(key, out var chart))
            return false;
        var play = CreatePlay(result, chart, "play", achievedAt.ToUniversalTime());
        if (best.TryGetValue(key, out var previous) && previous.ChartId == chart.Id && !Prefer(play, previous))
            return false;
        best[key] = play;
        Dirty = true;
        return true;
    }

    /// <summary>把同一次游玩的分数、完成等级与时间形成不可变的真实候选。</summary>
    /// <param name="result">原生逐局成绩或本次结算副本。</param>
    /// <param name="chart">与该成绩身份匹配的当前谱面。</param>
    /// <param name="source">history 或 play。</param>
    /// <param name="achievedAt">该成绩的已知 UTC 时间，未知时为 null。</param>
    /// <returns>按当前定数计算且未截断的单曲纪录。</returns>
    private static BestPlay CreatePlay(GameResultV4 result, ChartDefinition chart, string source, DateTimeOffset? achievedAt)
    {
        byte lamp = PotentialCalculator.CompletionLamp((byte)result.CalculatedResultClear);
        return new BestPlay
        {
            SongId = result.SongId.Value,
            Difficulty = (byte)result.Difficulty,
            ChartId = chart.Id,
            Score = result.PlayerScore,
            Lamp = lamp,
            ResultClear = (byte)result.CalculatedResultClear,
            Constant = chart.Constant,
            Potential = PotentialCalculator.Play(chart.Constant, result.PlayerScore, lamp),
            AchievedAt = achievedAt,
            Source = source
        };
    }

    /// <summary>先比较未截零的表现，同表现优先已知的较早时间。</summary>
    /// <param name="candidate">当前考虑的真实纪录。</param>
    /// <param name="previous">同谱面已经选出的真实纪录。</param>
    /// <returns>是否应使用候选替换已有纪录。</returns>
    private static bool Prefer(BestPlay candidate, BestPlay previous)
    {
        decimal value = PotentialCalculator.Performance(candidate.Score, candidate.Lamp);
        decimal old = PotentialCalculator.Performance(previous.Score, previous.Lamp);
        if (value != old)
            return value > old;
        return candidate.AchievedAt.HasValue
            && (!previous.AchievedAt.HasValue || candidate.AchievedAt.Value < previous.AchievedAt.Value);
    }

    /// <summary>零值或越界的原生时间不冒充游玩日期。</summary>
    /// <param name="seconds">原生 UTC Unix 秒值。</param>
    /// <returns>有效的 UTC 时间或 null。</returns>
    private static DateTimeOffset? HistoryTime(ulong seconds) => seconds is > 0 and <= 253_402_300_799
        ? DateTimeOffset.FromUnixTimeSeconds((long)seconds) : null;

    /// <summary>查询当前谱面最高潜力值；尚未游玩的谱面为零。</summary>
    /// <param name="key">当前选中的歌曲与难度。</param>
    /// <param name="catalog">动态曲库。</param>
    /// <returns>当前定数下该谱面的最高单次潜力值。</returns>
    internal decimal Best(ChartKey key, ChartCatalog catalog)
    {
        if (!best.TryGetValue(key, out var play) || !catalog.TryGet(key, out var chart) || chart.Id != play.ChartId)
            return 0m;
        return PotentialCalculator.Play(chart.Constant, play.Score, play.Lamp);
    }

    /// <summary>输出稳定排序的真实最佳纪录及时间，供模组和网页读取。</summary>
    /// <returns>包含来源与时间的整数规则最佳成绩文档。</returns>
    internal PotentialDocument ToDocument() => new()
    {
        SchemaVersion = 1,
        Rule = "integer-rating-v1",
        ImportedAt = importedAt,
        BestPlays = best.Values.OrderBy(play => play.SongId).ThenBy(play => play.Difficulty).ToList()
    };
}
