namespace InFalsusMod.Potential;

/// <summary>谱面详情与完整历史共用分数曲线；所有坐标都在输出位图内。</summary>
internal static class PlayHistoryImage
{
    internal const int Width = 1200, Height = 760;
    internal const int PlotLeft = 100, PlotRight = 1160, PlotTop = 240, PlotBottom = 550;
    internal const uint Background = 0x10232d, Foreground = 0xeaf5f7, Muted = 0x91adb9, Accent = 0xa5f0ed;

    /// <summary>生成完整记录页；选中项按实际序号索引，未知时间保持未知。</summary>
    internal static PixelImage Full(RankedPlay play, IReadOnlyList<ChartPlay> history, int selected)
    {
        using var image = new WindowsBitmap(Width, Height);
        image.Fill(0, 0, Width, Height, Background);
        Header(image, play, 900);
        image.Text($"{history.Count} 次记录", 910, 24, 260, 40, 26, Foreground, true, true);
        if (history.Count == 0)
        {
            image.Text("该谱面没有可读取的逐局记录", 40, 290, 1100, 60, 30, Muted);
            return image.ToPixels();
        }
        var current = history[selected];
        image.Text("所选成绩", 30, 115, 230, 30, 21, Muted);
        image.Text(PotentialPresentation.Score(current.Score), 25, 151, 720, 70, 59, Foreground, true);
        if (current.NewBest) image.Text("新高", 710, 166, 100, 40, 24, Accent, true);
        image.Text($"最高分  {PotentialPresentation.Score(history.Max(p => p.Score))}", 800, 124, 370, 35, 23, Muted, right: true);
        DrawCurve(image, history, (PlotLeft, PlotTop, PlotRight - PlotLeft, PlotBottom - PlotTop), selected);
        image.Text("1", PlotLeft - 8, 558, 60, 30, 18, Muted);
        image.Text(history.Count.ToString(), PlotRight - 65, 558, 70, 30, 18, Muted, right: true);
        image.Text(Time(history[0]), PlotLeft, 591, 450, 30, 18, Muted);
        image.Text(Time(history[^1]), PlotRight - 450, 591, 450, 30, 18, Muted, right: true);
        image.RoundedFill((25, 642, 1150, 100), 8, 0x1a3440);
        image.Text($"第 {selected + 1} 次 · {Time(current)}", 45, 653, 510, 32, 21, Muted);
        string ptt = play.Chart == null ? "--.---" : PotentialCalculator.Format(PotentialCalculator.Play(play.Chart.Constant,
            current.Score, PotentialCalculator.CompletionLamp(current.Clear)));
        image.Text($"PTT {ptt}（当前定数）", 45, 695, 510, 33, 24, Foreground, true);
        image.Text("最大连击", 580, 653, 170, 30, 20, Muted);
        image.Text(current.Combo.ToString("N0"), 580, 692, 170, 40, 29, Foreground, true);
        image.Text("MISS", 790, 653, 110, 30, 20, Muted);
        image.Text(current.Miss.ToString(), 790, 692, 110, 40, 29, Foreground, true);
        image.Text("通关状态", 950, 653, 210, 30, 20, Muted);
        image.Text(PotentialPresentation.Completion(current.Clear), 950, 692, 210, 40, 23, Accent, true);
        return image.ToPixels();
    }

    /// <summary>右侧只显示摘要与小曲线；展开按钮由原生页面负责。</summary>
    internal static PixelImage Summary(RankedPlay play, IReadOnlyList<ChartPlay> history, string error)
    {
        using var image = new WindowsBitmap(380, 700);
        image.Fill(0, 0, 380, 700, Background);
        Header(image, play, 380);
        image.Text("最高分", 25, 150, 330, 35, 21, Muted);
        image.Text(history.Count == 0 ? "--" : PotentialPresentation.Score(history.Max(p => p.Score)),
            22, 194, 338, 55, 39, Foreground, true);
        image.Text($"游玩记录    {history.Count} 次", 25, 280, 330, 40, 23, Muted);
        image.Text("最近成绩", 25, 335, 330, 35, 21, Muted);
        image.Text(history.Count == 0 ? "--" : PotentialPresentation.Score(history[^1].Score), 25, 375, 330, 44, 30, Foreground);
        if (history.Count > 0)
        {
            DrawCurve(image, history, (25, 450, 330, 160), history.Count - 1);
            image.Text(Time(history[^1]), 25, 638, 330, 32, 19, Muted);
        }
        else
            image.Text(string.IsNullOrEmpty(error) ? "没有可读取的逐局记录" : error, 25, 460, 330, 70, 22, Muted);
        return image.ToPixels();
    }

    /// <summary>选歌页面的透明趋势图；复用逐局曲线，黑白两次绘制仅用于恢复 GDI 抗锯齿透明度。</summary>
    internal static PixelImage Selection(PotentialSnapshot snapshot, ChartKey key, IReadOnlyList<ChartPlay> history, string error)
    {
        if (!snapshot.Catalog.TryGet(key, out var chart)) throw new ArgumentException("选中谱面不在当前曲库中。", nameof(key));
        var play = snapshot.Find(key);
        using var black = new WindowsBitmap(860, 430);
        using var white = new WindowsBitmap(860, 430);
        black.Fill(0, 0, 860, 430, 0);
        white.Fill(0, 0, 860, 430, 0xffffff);
        Draw(black);
        Draw(white);
        return black.ToPixels(white);

        void Draw(WindowsBitmap image)
        {
            image.Text($"{PotentialPresentation.Difficulty(key.Difficulty)} · 定数 {PotentialCalculator.FormatConstant(chart.Constant)}",
                10, 4, 490, 38, 28, Foreground, true);
            image.Text($"{history.Count} 次游玩", 580, 4, 270, 38, 26, Foreground, right: true);
            string ptt = play?.Potential is decimal value ? $"PTT {PotentialCalculator.Format(value)}  #{play.Rank}" : "尚未游玩";
            image.Text(ptt, 10, 48, 490, 46, 34, Accent, true);
            image.Text($"定数来源：{CommunityConstants.DisplayName(snapshot.Catalog.Source)}", 510, 48, 340, 46, 23, Foreground, right: true);
            if (history.Count == 0)
            {
                image.Text(string.IsNullOrEmpty(error) ? "没有可读取的逐局记录" : error, 10, 180, 820, 60, 30, Foreground);
                return;
            }
            image.Text($"最高 {PotentialPresentation.Score(history.Max(p => p.Score))}", 10, 104, 450, 40, 27, Foreground, true);
            image.Text($"最近 {PotentialPresentation.Score(history[^1].Score)}", 450, 104, 400, 40, 27, Foreground, right: true);
            DrawCurve(image, history, (100, 175, 740, 175), history.Count - 1);
            image.Text("1", 95, 358, 80, 30, 22, Foreground);
            image.Text(history.Count.ToString(), 750, 358, 90, 30, 22, Foreground, right: true);
            image.Text(Time(history[^1]), 380, 397, 460, 30, 23, Foreground, right: true);
        }
    }

    /// <summary>复用标题、难度和定数含义，缺失谱面定义时不猜测。</summary>
    private static void Header(WindowsBitmap image, RankedPlay play, int width)
    {
        image.Text(play.Chart?.Title ?? play.Play.ChartId, 25, 20, width - 55, 54, 32, Foreground, true);
        image.Text($"{PotentialPresentation.Difficulty(play.Play.Difficulty)} · 定数 {(play.Chart == null ? "--" : PotentialCalculator.FormatConstant(play.Chart.Constant))}",
            25, 78, width - 55, 30, 21, PotentialPresentation.DifficultyRgb(play.Play.Difficulty));
    }

    /// <summary>曲线按每局分数绘制，纵轴包含全部数据并留边；基准线仅在量程内显示。</summary>
    private static void DrawCurve(WindowsBitmap image, IReadOnlyList<ChartPlay> history,
        (int X, int Y, int Width, int Height) box, int selected)
    {
        double min = history.Min(p => (double)p.Score), max = history.Max(p => (double)p.Score);
        double pad = Math.Max(100000d, (max - min) * 0.1d);
        min = Math.Max(0, min - pad); max += pad;
        bool full = box.Width > 400;
        int Y(double score) => box.Y + box.Height - (int)Math.Round((score - min) / (max - min) * box.Height);
        for (int i = 0; i <= 3; i++)
        {
            double score = min + (max - min) * i / 3;
            int y = Y(score);
            image.Fill(box.X, y, box.Width, 1, 0x2a424e);
            if (full) image.Text((score / 1000000d).ToString("F2") + "M", 8, y - 14, 85, 28, 17, Muted, right: true);
        }
        if (full)
            foreach (var (score, label) in new[] { (98000000d, "EX"), (99500000d, "EX+") })
                if (score >= min && score <= max)
                {
                    int y = Y(score);
                    for (int x = box.X; x < box.X + box.Width; x += 16) image.Fill(x, y, Math.Min(7, box.X + box.Width - x), 1, 0x617d88);
                    image.Text(label, box.X + box.Width - 60, y - 30, 58, 27, 18, Accent, right: true);
                }
        var points = history.Select((p, i) => (X: box.X + (history.Count == 1 ? box.Width / 2 : (int)Math.Round(i * (double)box.Width / (history.Count - 1))), Y: Y(p.Score))).ToArray();
        image.Stroke(points, Accent, full ? 3 : 2);
        // 密集历史仍逐局连线，只有点标记按像素间距节流，不丢弃峰值或选中项。
        int stride = Math.Max(1, (int)((long)history.Count * 7 / box.Width));
        for (int i = 0; i < points.Length; i += stride)
            image.RoundedFill((points[i].X - 3, points[i].Y - 3, 6, 6), 3, history[i].NewBest ? 0xf5d795U : Accent);
        var point = points[selected];
        image.RoundedFill((point.X - 7, point.Y - 7, 14, 14), 7, Foreground);
    }

    /// <summary>仅将真实 UTC 时间转换为本地显示。</summary>
    private static string Time(ChartPlay play) => play.Time?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "时间未知";
}
