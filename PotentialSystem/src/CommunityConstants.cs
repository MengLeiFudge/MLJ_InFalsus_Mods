using System.Reflection;
using System.Text.Json;

namespace InFalsusMod.Potential;

/// <summary>谱面定数来源；数值顺序与设置页左右选项一致，索引用于 profile 配置。</summary>
internal enum ChartConstantSource
{
    /// <summary>直接使用游戏当前等级。</summary>
    OfficialLevel = 0,
    /// <summary>使用 In Falsus Tools 内置定数，缺项回退等级。</summary>
    AnXiaoLi = 1,
    /// <summary>使用 chart-constants-v4.xlsx 定数，缺项回退等级。</summary>
    MengLei = 2
}

/// <summary>两份固定定数表按稳定谱面 ID 匹配；运行时只读取 DLL 内资源。</summary>
internal static class CommunityConstants
{
    /// <summary>各来源的成绩文件身份；旧社区身份仅用于兼容读取。</summary>
    internal const string NativeRule = "integer-rating-v1", CommunityRule = "community-experimental-v1",
        AnXiaoLiRule = "anxiaoli-v1", MengLeiRule = "menglei-v4";
    /// <summary>安小璃表静态提取自用户指定的 In Falsus Tools JAR，每个谱面只保留明确数值。</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, decimal>> anXiaoLi = new(() => Read("AnXiaoLiConstants"));
    /// <summary>萌泪酱表与 chart-constants-v4.xlsx 逐项一致，沿用已有资源文件名。</summary>
    private static readonly Lazy<IReadOnlyDictionary<string, decimal>> mengLei = new(() => Read("CommunityConstants"));

    /// <summary>返回设置和分享图共用的来源名称。</summary>
    /// <param name="source">已验证的来源。</param>
    /// <returns>用户可见的中文名称。</returns>
    internal static string DisplayName(ChartConstantSource source) => source switch
    {
        ChartConstantSource.OfficialLevel => "官方等级",
        ChartConstantSource.AnXiaoLi => "安小璃",
        ChartConstantSource.MengLei => "萌泪酱",
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    /// <summary>记录本次保存数值的来源，便于外部读取者区分固定表。</summary>
    /// <param name="source">已验证的来源。</param>
    /// <returns>独立成绩文件的规则标识。</returns>
    internal static string Rule(ChartConstantSource source) => source switch
    {
        ChartConstantSource.OfficialLevel => NativeRule,
        ChartConstantSource.AnXiaoLi => AnXiaoLiRule,
        ChartConstantSource.MengLei => MengLeiRule,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    /// <summary>根据当前来源选择定数；语言切换不会改变谱面身份。</summary>
    /// <param name="chartId">当前 SongChartInfo.Id。</param>
    /// <param name="rating">当前游戏中的非负歌曲等级。</param>
    /// <param name="source">当前选择的来源。</param>
    /// <returns>固定表中的定数，或缺项时的原生整数等级。</returns>
    internal static decimal Resolve(string chartId, int rating, ChartConstantSource source)
    {
        var values = source switch
        {
            ChartConstantSource.OfficialLevel => null,
            ChartConstantSource.AnXiaoLi => anXiaoLi.Value,
            ChartConstantSource.MengLei => mengLei.Value,
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };
        return values != null && values.TryGetValue(chartId, out var constant) ? constant : rating;
    }

    /// <summary>读取固定表，非法资源作为数据错误报告，避免静默改用另一来源。</summary>
    /// <param name="name">DLL 中的 JSON 资源名，不含命名空间及扩展名。</param>
    /// <returns>已校验的谱面 ID 与非负一位小数定数映射。</returns>
    private static IReadOnlyDictionary<string, decimal> Read(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"PotentialSystem.{name}.json")
            ?? throw new InvalidDataException($"缺少内置定数表：{name}。");
        var constants = JsonSerializer.Deserialize<Dictionary<string, decimal>>(stream)
            ?? throw new InvalidDataException($"内置定数表为空：{name}。");
        if (constants.Count == 0 || constants.Any(pair => string.IsNullOrWhiteSpace(pair.Key)
            || pair.Value < 0m || decimal.Truncate(pair.Value * 10m) != pair.Value * 10m))
            throw new InvalidDataException($"内置定数表含无效谱面身份或定数：{name}。");
        return constants;
    }
}
