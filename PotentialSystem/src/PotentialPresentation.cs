using System.Globalization;
using UnityEngine;

namespace InFalsusMod.Potential;

/// <summary>原生卡片、浏览页与分享图共用的成绩文案，不参与潜力值计算。</summary>
internal static class PotentialPresentation
{
    /// <summary>保留原生难度名，不把难度位当作数组下标。</summary>
    internal static string Difficulty(byte difficulty) => difficulty switch
    {
        1 => "MINIMAL", 2 => "EVOLVED", 4 => "ULTIMATE", 8 => "FORBIDDEN", _ => "UNKNOWN"
    };
    /// <summary>与原版难度大致对应的辨识色，用于网页式卡片和分享图。</summary>
    internal static uint DifficultyRgb(byte difficulty) => difficulty switch
    {
        1 => 0x70d6c7, 2 => 0xf1c76a, 4 => 0xfa8496, 8 => 0xbe9cff, _ => 0xb5bfd3
    };
    /// <summary>为 Unity GUI 转换 RGB 颜色。</summary>
    internal static Color Color(uint rgb) => new((rgb >> 16) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);
    /// <summary>原始打击分数使用三位分组，不误显示为战斗分数。</summary>
    internal static string Score(ulong score) => score.ToString("N0", CultureInfo.InvariantCulture);
    /// <summary>与该最佳表现同一次游玩的通关状态。</summary>
    internal static string Lamp(byte lamp) => lamp switch { 2 => "CLEAR", 1 => "DIVE", _ => "FAILED" };
    /// <summary>选歌卡片所需的严格单行格式；尚未游玩的谱面没有排名。</summary>
    internal static string RankLine(RankedPlay? play) => play?.Rank != null && play.Potential.HasValue
        ? $"#{play.Rank.Value} {PotentialCalculator.Format(play.Potential.Value)}" : "";
}
