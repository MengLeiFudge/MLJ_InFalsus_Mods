namespace InFalsusMod.Potential;

/// <summary>谱面定数来源；数值顺序与设置页左右选项一致，索引用于 profile 配置。</summary>
internal enum ChartConstantSource
{
    /// <summary>直接使用游戏当前等级。</summary>
    OfficialLevel = 0,
    /// <summary>使用 In Falsus Tools 定数及启动时更新的缓存，缺项回退等级。</summary>
    AnXiaoLi = 1,
    /// <summary>使用内置 v6 结果及后台计算的新谱面缓存。</summary>
    MengLei = 2
}

/// <summary>统一解析定数来源及其主线程版本，切换和更新时共用相同重算入口。</summary>
internal static class CommunityConstants
{
    /// <summary>各来源的成绩文件身份；旧社区身份仅用于兼容读取。</summary>
    internal const string NativeRule = "integer-rating-v1", CommunityRule = "community-experimental-v1",
        AnXiaoLiRule = "anxiaoli-v1", MengLeiRule = "menglei-v6", LegacyMengLeiRule = "menglei-v4";

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

    /// <summary>记录本次保存数值的来源，便于外部读取者区分定数来源。</summary>
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
    /// <param name="key">原生歌曲与难度身份。</param>
    /// <param name="chartId">当前 SongChartInfo.Id。</param>
    /// <param name="rating">当前游戏中的非负歌曲等级。</param>
    /// <param name="source">当前选择的来源。</param>
    /// <returns>所选来源中的定数，或缺项时的原生整数等级。</returns>
    internal static decimal Resolve(ChartKey key, string chartId, int rating, ChartConstantSource source) => source switch
    {
        ChartConstantSource.OfficialLevel => rating,
        ChartConstantSource.AnXiaoLi => Plugin.AnXiaoLi?.Resolve(key, rating) ?? rating,
        ChartConstantSource.MengLei => Plugin.MengLei?.Resolve(chartId, key.Difficulty, rating) ?? rating,
        _ => throw new ArgumentOutOfRangeException(nameof(source))
    };

    /// <summary>当前来源已采用的表版本；后台计算中的结果不参与 UI 读取。</summary>
    internal static int Revision(ChartConstantSource source) => source switch
    {
        ChartConstantSource.AnXiaoLi => Plugin.AnXiaoLi?.Revision ?? 0,
        ChartConstantSource.MengLei => Plugin.MengLei?.Revision ?? 0,
        _ => 0
    };
}
