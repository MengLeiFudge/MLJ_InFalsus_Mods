extern alias GameData;

using ifapp.Game.Data;
using Str;
using UnityEngine;
using UnityEngine.AddressableAssets;
using SongRow = GameData::_K._SH;

namespace InFalsusMod.Potential;

/// <summary>原生歌曲身份与单个难度标记组成的成绩键，不依赖歌名或数组顺序。</summary>
/// <param name="SongId">原生 SongId.Value。</param>
/// <param name="Difficulty">原生单个难度位标记。</param>
internal readonly record struct ChartKey(ushort SongId, byte Difficulty);

/// <summary>当前游戏中的谱面定义与展示素材；定数始终读取原生 Rating。</summary>
/// <param name="Id">原生谱面 Id，用于校验保存成绩的身份。</param>
/// <param name="Constant">从原生 Rating 直接读取的等级。</param>
/// <param name="Title">原版当前语言下的歌名。</param>
/// <param name="Artist">原版当前语言下的作者。</param>
/// <param name="Jacket">按谱面、歌曲、默认封面的优先级选出的原生素材引用。</param>
internal sealed record ChartDefinition(string Id, int Constant, string Title, string Artist,
    AssetReferenceT<Material>? Jacket);

/// <summary>从游戏当前 SongData 构建索引，并用当前全部可用谱面计算理论上限。</summary>
internal sealed class ChartCatalog
{
    /// <summary>歌曲与难度到谱面定义的动态映射。</summary>
    private readonly Dictionary<ChartKey, ChartDefinition> charts;
    /// <summary>当前可用谱面数，包括未解锁但有正式定义的谱面。</summary>
    internal int Count => charts.Count;
    /// <summary>当前曲库全部取得满分且通关时的理论账号潜力值。</summary>
    internal decimal Maximum { get; }
    /// <summary>读取展示名称时的原生语言，语言切换后重建元数据。</summary>
    internal Strings.Localization Language { get; }

    /// <summary>接收已校验的运行时定义，不携带任何内置歌曲表。</summary>
    private ChartCatalog(Dictionary<ChartKey, ChartDefinition> charts, Strings.Localization language)
    {
        this.charts = charts;
        Language = language;
        Maximum = PotentialCalculator.Overall(charts.Values.Select(chart => chart.Constant + PotentialCalculator.MaximumPerformance));
    }

    /// <summary>读取实际加载的谱面；展示名称或封面故障不阻止等级计算与成绩保存。</summary>
    internal static ChartCatalog Read(SongData data)
    {
        var songs = data.allSongInfo ?? throw new InvalidOperationException("歌曲数据尚未载入。");
        var charts = new Dictionary<ChartKey, ChartDefinition>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var language = NativeLabel.CurrentLanguage();
        var songJackets = new Dictionary<ushort, AssetReferenceT<Material>>();
        var chartJackets = new Dictionary<string, AssetReferenceT<Material>>(StringComparer.Ordinal);
        try
        {
            if (data.songIdJacketMaterials != null)
                foreach (var jacket in data.songIdJacketMaterials)
                    if (jacket?.JacketLargeMaterial != null)
                        songJackets[jacket.SongId.Value] = jacket.JacketLargeMaterial;
            if (data.chartIdJacketMaterials != null)
                foreach (var jacket in data.chartIdJacketMaterials)
                    if (jacket?.JacketLargeMaterial != null && !string.IsNullOrEmpty(jacket.ChartId))
                        chartJackets[jacket.ChartId] = jacket.JacketLargeMaterial;
        }
        catch (Exception ex) { Plugin.Logger.LogWarning($"封面索引读取失败，将显示缺失素材提示：{ex}"); }
        bool namesReported = false;
        foreach (var song in songs)
        {
            if (song == null || song.Id.Value == 0)
                continue;
            var entries = song.ChartInfos ?? throw new InvalidDataException($"歌曲 {song.Id.Value} 缺少谱面定义。");
            foreach (var entry in entries)
            {
                if (entry == null || !entry.Available)
                    continue;
                byte difficulty = (byte)entry.Difficulty;
                if (difficulty == 0 || (difficulty & (difficulty - 1)) != 0
                    || entry.Rating < 0 || string.IsNullOrWhiteSpace(entry.Id))
                    throw new InvalidDataException($"歌曲 {song.Id.Value} 的可用谱面定义无效。");
                string title = song.BaseName, artist = "";
                try
                {
                    // 值传递入口由 interop 正确解箱 SongInfo，避免原生 by-ref 包装的结构体偏移问题。
                    var row = new SongRow(song, entry.Difficulty, language);
                    title = row._b;
                    artist = row._B;
                }
                catch (Exception ex)
                {
                    if (!namesReported)
                        Plugin.Logger.LogWarning($"本地化歌曲名称读取失败，暂用原生歌曲标识：{ex}");
                    namesReported = true;
                }
                if (!chartJackets.TryGetValue(entry.Id, out var jacket)
                    && !songJackets.TryGetValue(song.Id.Value, out jacket))
                    jacket = data.FallbackJacketLargeMaterial;
                var key = new ChartKey(song.Id.Value, difficulty);
                if (!charts.TryAdd(key, new ChartDefinition(entry.Id, entry.Rating, title, artist, jacket)) || !ids.Add(entry.Id))
                    throw new InvalidDataException($"游戏存在重复谱面身份：{entry.Id}。");
            }
        }
        if (charts.Count == 0)
            throw new InvalidOperationException("游戏尚未提供可用谱面。");
        return new ChartCatalog(charts, language);
    }

    /// <summary>匹配当前可用谱面；未知身份不猜测等级。</summary>
    internal bool TryGet(ChartKey key, out ChartDefinition chart) => charts.TryGetValue(key, out chart!);
}
