using System.Globalization;

namespace InFalsusMod.Potential;

/// <summary>从冻结的最佳成绩快照生成固定宽度分享图，完全独立于屏幕分辨率和浏览页滚动位置。</summary>
internal static class B50Image
{
    /// <summary>五列成绩卡片，只有现有成绩参与；不伪造缺少的 B50 空位。</summary>
    internal static byte[] Render(PotentialSnapshot snapshot, IReadOnlyDictionary<ChartKey, PixelImage> covers, PixelImage badge)
    {
        const int width = 2400, margin = 48, gap = 18, columns = 5, cardHeight = 300, header = 300;
        int cardWidth = (width - margin * 2 - gap * (columns - 1)) / columns;
        int rows = Math.Max(1, (snapshot.B50.Count + columns - 1) / columns);
        int height = header + rows * (cardHeight + gap) + 96;
        using var image = new WindowsBitmap(width, height);
        image.Fill(0, 0, width, height, 0x0d1220);
        image.Fill(0, 0, width, 250, 0x171f33);
        image.Image(badge, margin, 28, 200, 200);
        image.Text(PotentialCalculator.Format(snapshot.Potential), margin + 34, 102, 134, 70, 43, 0xffffff, true);
        image.Text("IN FALSUS · B50", 285, 30, 1500, 80, 62, 0xffffff, true);
        image.Text($"PTT {PotentialCalculator.Format(snapshot.Potential)}    MAX {PotentialCalculator.Format(snapshot.Maximum)}",
            285, 112, 1500, 54, 33, 0xa6f0d6);
        image.Text($"B50 {PotentialCalculator.Format(snapshot.B50Average)}    B10 {PotentialCalculator.Format(snapshot.B10Average)}    已游玩 {snapshot.Plays.Count} 谱面",
            285, 174, 1740, 50, 29, 0xc4cede);
        image.Text($"{snapshot.B50.Count}/50", width - 300, 65, 250, 65, 44, 0xffffff, true, true);
        image.Text(DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), width - 510, 168, 460, 42, 24, 0xc4cede, right: true);
        for (int i = 0; i < snapshot.B50.Count; i++)
        {
            var play = snapshot.B50[i];
            var chart = play.Chart ?? throw new InvalidDataException("B50 中存在无法匹配的谱面。");
            if (!covers.TryGetValue(play.Play.Key, out var cover))
                throw new InvalidDataException($"缺少分享图封面：{chart.Title}。");
            int x = margin + i % columns * (cardWidth + gap);
            int y = header + i / columns * (cardHeight + gap);
            uint accent = PotentialPresentation.DifficultyRgb(play.Play.Difficulty);
            image.Fill(x, y, cardWidth, cardHeight, 0x1c263b);
            image.Fill(x, y, cardWidth, 5, accent);
            image.Text($"#{play.Rank}", x + 16, y + 10, 120, 52, 34, 0xcbd5e9, true);
            image.Text(PotentialCalculator.Format(play.Potential!.Value), x + 150, y + 7, cardWidth - 168, 59, 42, 0xa6f0d6, true, true);
            image.Image(cover, x + 16, y + 70, 152, 152);
            image.Text(PotentialPresentation.Difficulty(play.Play.Difficulty), x + 184, y + 70, cardWidth - 200, 38, 23, accent, true);
            image.Text($"定数 {chart.Constant.ToString("F1", CultureInfo.InvariantCulture)}", x + 184, y + 114, cardWidth - 200, 38, 23, 0xdce4f3);
            image.Text(PotentialPresentation.Lamp(play.Play.Lamp), x + 184, y + 160, cardWidth - 200, 42, 27, play.Play.Lamp == 2 ? 0xa6f0d6u : 0xcbd5e9u, true);
            image.Text(PotentialPresentation.Score(play.Play.Score), x + 184, y + 199, cardWidth - 200, 38, 24, 0xffffff, true);
            image.Text(chart.Title, x + 16, y + 236, cardWidth - 32, 34, 23, 0xffffff, true);
            image.Text(chart.Artist, x + 16, y + 272, cardWidth - 32, 22, 16, 0xaab7d0);
        }
        if (snapshot.B50.Count == 0)
            image.Text("还没有已游玩的谱面", margin, header + 70, width - margin * 2, 80, 44, 0xc4cede);
        image.Text("PotentialSystem · 整数定数读取自当前游戏 · PTT = (B50 总和 + B10 总和) / 60 · 缺项贡献零",
            margin, height - 74, width - margin * 2, 48, 25, 0x91a0bc);
        return image.ToClipboardDib();
    }
}
