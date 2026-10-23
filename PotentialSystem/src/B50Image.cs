using System.Globalization;

namespace InFalsusMod.Potential;

/// <summary>从冻结的最佳成绩快照生成分享图，独立于屏幕分辨率和浏览页滚动位置。</summary>
internal static class B50Image
{
    /// <summary>浏览页和导出图共用五列、卡片宽高及间距；浏览页按可用宽度等比缩放。</summary>
    internal const int Columns = 5, CardWidth = 833, CardHeight = 278, Gap = 20;

    /// <summary>五列横向成绩卡参考 Player Bests 布局，以曲绘印象色作底，不补造缺少的成绩。</summary>
    internal static byte[] Render(PotentialSnapshot snapshot, IReadOnlyDictionary<ChartKey, PixelImage> covers, PixelImage badge)
    {
        const int margin = 80, gap = Gap, columns = Columns, cardHeight = CardHeight, header = 360;
        const int cardWidth = CardWidth, width = margin * 2 + columns * cardWidth + (columns - 1) * gap;
        int rows = Math.Max(1, (snapshot.B50.Count + columns - 1) / columns);
        int height = header + rows * (cardHeight + gap) - gap + 140;
        using var image = new WindowsBitmap(width, height);
        image.Fill(0, 0, width, height, 0x252a34);
        image.Fill(0, 0, width, 305, 0x202630);
        image.Image(badge, margin, 40, 220, 220);
        image.Text(PotentialCalculator.Format(snapshot.Potential), margin + 38, 130, 150, 70, 43, 0xffffff, true);
        image.Text("IN FALSUS", 350, 35, 1200, 70, 48, 0xffffff, true);
        image.Text("PLAYER BESTS · B50", 350, 104, 1500, 100, 72, 0xffffff, true);
        image.Text($"B50 {PotentialCalculator.Format(snapshot.B50Average)}    B10 {PotentialCalculator.Format(snapshot.B10Average)}    {snapshot.B50.Count}/50",
            350, 225, 1440, 46, 30, 0xc4cede);
        image.Text("UNOFFICIAL POTENTIAL", width - margin - 750, 58, 750, 48, 32, 0xa6f0d6, true, true);
        image.Text(PotentialCalculator.Format(snapshot.Potential), width - margin - 700, 115, 700, 115, 98, 0xffffff, true, true);
        image.Text($"MAX {PotentialCalculator.Format(snapshot.Maximum)}    已游玩 {snapshot.Plays.Count} 谱面",
            width - margin - 750, 238, 750, 42, 28, 0xc4cede, right: true);
        image.Fill(margin, 317, width - margin * 2, 3, 0xa6f0d6);
        for (int i = 0; i < snapshot.B50.Count; i++)
        {
            var play = snapshot.B50[i];
            var chart = play.Chart ?? throw new InvalidDataException("B50 中存在无法匹配的谱面。");
            if (!covers.TryGetValue(play.Play.Key, out var cover))
                throw new InvalidDataException($"缺少分享图封面：{chart.Title}。");
            int x = margin + i % columns * (cardWidth + gap);
            int y = header + i / columns * (cardHeight + gap);
            DrawCard(image, play, cover, (x, y, cardWidth, cardHeight));
        }
        if (snapshot.B50.Count == 0)
            image.Text("还没有已游玩的谱面", margin, header + 90, width - margin * 2, 80, 44, 0xc4cede);
        image.Fill(margin, height - 92, width - margin * 2, 2, 0xa6f0d6);
        string constants = $"谱面定数来源：{CommunityConstants.DisplayName(snapshot.Catalog.Source)}";
        image.Text($"PotentialSystem · PTT = (B50 总和 + B10 总和) / 60 · {constants}",
            margin, height - 69, width - margin * 2 - 450, 44, 25, 0xb2bdcd);
        image.Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), width - margin - 430,
            height - 69, 430, 44, 25, 0xb2bdcd, right: true);
        return image.ToClipboardDib();
    }

    /// <summary>封面在左，难度/定数、歌名、作者及大号分数在右，潜力值与排名靠右对齐。</summary>
    private static void DrawCard(WindowsBitmap image, RankedPlay play, PixelImage? cover,
        (int X, int Y, int Width, int Height) bounds)
    {
        var chart = play.Chart;
        var palette = cover == null ? CoverPalette.Neutral : CoverPalette.From(cover);
        int x = bounds.X, y = bounds.Y, width = bounds.Width;
        image.RoundedFill(bounds, 16, palette.Border);
        image.RoundedFill((x + 2, y + 2, width - 4, bounds.Height - 4), 14, palette.Background);
        if (cover != null)
            image.Image(cover, x + 18, y + 21, 236, 236);
        else
            image.Fill(x + 18, y + 21, 236, 236, 0x17202d);
        int left = x + 274, infoWidth = width - 298;
        uint accent = PotentialPresentation.DifficultyRgb(play.Play.Difficulty);
        image.Text($"{PotentialPresentation.Difficulty(play.Play.Difficulty)}  {(chart == null ? "定数不可用" : PotentialCalculator.FormatConstant(chart.Constant))}",
            left, y + 20, infoWidth - 188, 40, 25, accent, true);
        image.Text(play.Potential.HasValue ? PotentialCalculator.Format(play.Potential.Value) : "--.---", x + width - 197, y + 15, 173, 51, 42, accent, true, true);
        image.Text(chart?.Title ?? play.Play.ChartId, left, y + 73, infoWidth, 42, 33, 0xffffff, true);
        image.Text(chart?.Artist ?? "", left, y + 116, infoWidth, 30, 22, 0xc9d4df);
        image.Text(PotentialPresentation.Score(play.Play.Score), left, y + 153, infoWidth, 68, 56, 0xf0faff, true);
        image.Text(PotentialPresentation.Lamp(play.Play.Lamp), left, y + 231, infoWidth - 100, 32, 25,
            play.Play.Lamp == 2 ? 0xa6f0d6U : 0xd2dce7U, true);
        image.Text(play.Rank.HasValue ? $"#{play.Rank}" : "#--", x + width - 130, y + 222, 106, 41, 33, 0xffffff, true, true);
    }

    /// <summary>浏览卡片直接复用导出绘制，字号、圆角和文字基线只有一份定义。</summary>
    internal static PixelImage RenderCard(RankedPlay play, PixelImage? cover, string? coverStatus)
    {
        using var image = new WindowsBitmap(CardWidth, CardHeight);
        image.Fill(0, 0, CardWidth, CardHeight, 0x080f1c);
        DrawCard(image, play, cover, (0, 0, CardWidth, CardHeight));
        if (cover == null)
            image.Text(coverStatus ?? "封面加载中", 26, 120, 220, 38, 22, 0xc9d4df);
        return image.ToPixels();
    }
}
