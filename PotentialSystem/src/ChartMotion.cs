namespace InFalsusMod.Potential;

/// <summary>v6 鼠标约束、双手空闲窗口及指数衰减聚合；不访问游戏对象。</summary>
internal static class ChartMotion
{
    private sealed record Constraint(double Time, double Left, double Right, double Grace, bool Flick, double End, double Final);
    private sealed record Flick(int Start, int End, double Left, double Right, double Last, uint Direction, int Sign);

    internal static (double Left, double Right) Bounds(ChartNote note, double u)
    {
        double left = note.X - note.Width / 2, right = note.X + note.Width / 2;
        double a = (note.Flags & 8) != 0 ? Math.Sin(Math.PI * u / 2) : (note.Flags & 16) != 0 ? 1 - Math.Cos(Math.PI * u / 2) : u;
        double b = (note.Flags & 64) != 0 ? Math.Sin(Math.PI * u / 2) : (note.Flags & 128) != 0 ? 1 - Math.Cos(Math.PI * u / 2) : u;
        return (left + (note.EndX - note.EndWidth / 2 - left) * a, right + (note.EndX + note.EndWidth / 2 - right) * b);
    }

    internal static List<(int Start, int End)> TwoHandWindows(ChartNote[] notes, KeyEvent[] events)
    {
        var result = new List<(int, int)>();
        if (events.Length == 0) return result;
        var spans = new List<(int Start, int End)>();
        double? previous = null;
        foreach (var n in notes.OrderBy(n => n.Time))
        {
            if (n.Type == 4) spans.Add((n.Time - 300, n.Time + 200));
            else if (n.Type == 5)
            {
                if (Math.Abs(n.EndX - n.X) > .01) spans.Add((n.Time - 300, n.End + 200));
                else if (previous == null || Math.Abs(n.X - previous.Value) > .01) spans.Add((n.Time - 300, n.Time + 200));
                previous = n.EndX;
            }
        }
        var ordered = spans.OrderBy(s => s.Start).ThenBy(s => s.End).ToList();
        ordered.Add((events[^1].Time + 1, events[^1].Time + 1));
        int cursor = events[0].Time;
        foreach (var span in ordered)
        {
            if (span.Start - cursor >= 3000)
            {
                int count = events.Where(e => e.Time >= cursor && e.Time < span.Start).Sum(e => KeyboardWorkload.Count(e.Choices[0].Mask));
                if (count / ((span.Start - cursor) / 1000.0) >= 6) result.Add((cursor, span.Start));
            }
            cursor = Math.Max(cursor, span.End);
        }
        return result;
    }

    internal static List<WorkloadPoint> Air(ChartNote[] notes, CancellationToken token)
    {
        var constraints = new List<Constraint>();
        foreach (var n in notes.Where(n => n.Type == 5))
        {
            token.ThrowIfCancellationRequested();
            int steps = Math.Max(1, (n.End - n.Time) / 25);
            if (constraints.Count + steps > 2000000) throw new InvalidDataException("谱面 Sky 采样数量超限。");
            for (int i = 0; i <= steps; i++)
            {
                double u = i / (double)steps;
                var band = Bounds(n, u);
                constraints.Add(new Constraint(n.Time + u * (n.End - n.Time), band.Left - .02, band.Right + .02,
                    i == 0 ? .15 : 0, false, 0, 0));
            }
        }
        var flicks = new List<Flick>();
        foreach (var n in notes.Where(n => n.Type == 4).OrderBy(n => n.Time))
        {
            double center = n.X + n.Width / 2;
            var previous = flicks.LastOrDefault();
            if (previous != null && n.Flags >> 8 == previous.Direction && n.Time - previous.End <= 60)
            {
                double step = center - previous.Last;
                if (previous.Sign == 0 || step * previous.Sign >= 0)
                {
                    flicks[^1] = previous with { End = n.Time, Last = center, Sign = previous.Sign == 0 ? Math.Sign(step) : previous.Sign };
                    continue;
                }
            }
            flicks.Add(new Flick(n.Time, n.Time, n.X, n.X + n.Width, center, n.Flags >> 8, 0));
        }
        foreach (var flick in flicks)
            constraints.Add(new Constraint(flick.Start, flick.Left, flick.Right, .05, true, flick.End, flick.Last));
        double position = .5, sweepEnd = -1;
        double? last = null;
        Constraint? bandAt = null;
        var result = new List<WorkloadPoint>();
        foreach (var c in constraints.OrderBy(c => c.Time))
        {
            token.ThrowIfCancellationRequested();
            double width = Math.Max(.01, c.Right - c.Left), target = Math.Min(Math.Max(position, c.Left), c.Right);
            if (!c.Flick && c.Time <= sweepEnd)
            {
                bandAt = c; position = target; last = c.Time;
                continue;
            }
            double available = Math.Max(.02, (last.HasValue ? (c.Time - last.Value) / 1000 : 1) + c.Grace);
            double cost = Math.Log2(1 + Math.Abs(target - position) / width) / available;
            if (c.Flick)
            {
                cost += 1 + Math.Log2(1 + .05 / width);
                if (bandAt != null && bandAt.Time >= c.Time - 25 && (target < bandAt.Left || target > bandAt.Right)) cost += 2;
                sweepEnd = c.End;
                if (c.End > c.Time) target = Math.Clamp(c.Final, 0, 1);
            }
            else bandAt = c;
            if (cost > 1e-6) result.Add(new WorkloadPoint(c.Time, cost));
            position = target;
            last = c.Flick ? Math.Max(c.Time, c.End) : c.Time;
        }
        var air = new List<int>();
        double? previousX = null;
        foreach (var n in notes.OrderBy(n => n.Time))
        {
            if (n.Type == 4) air.Add(n.Time);
            else if (n.Type == 5)
            {
                if (previousX == null || Math.Abs(n.X - previousX.Value) > .01 || Math.Abs(n.EndX - n.X) > .01) air.Add(n.Time);
                previousX = n.EndX;
            }
        }
        int[] times = air.OrderBy(t => t).ToArray();
        foreach (var n in notes.Where(n => n.Ground && n.Track is 2 or 3))
        {
            double gap = Nearest(times, n.Time, double.PositiveInfinity);
            if (gap > 0 && gap <= 400) result.Add(new WorkloadPoint(n.Time, 250 / Math.Max(gap, 60)));
        }
        return result;
    }

    internal static double Nearest(int[] sorted, int time, double empty)
    {
        if (sorted.Length == 0) return empty;
        int index = Array.BinarySearch(sorted, time);
        if (index >= 0) return 0;
        index = ~index;
        return Math.Min(index > 0 ? time - sorted[index - 1] : double.PositiveInfinity,
            index < sorted.Length ? sorted[index] - time : double.PositiveInfinity);
    }

    internal static double[] Peaks(IEnumerable<WorkloadPoint> source, double halfLife)
    {
        var events = source.OrderBy(e => e.Time).ThenBy(e => e.Cost).ToArray();
        if (events.Length == 0) return new double[1];
        var peaks = new double[(int)(events[^1].Time / 1000 / .4) + 1];
        double strain = 0, previous = events[0].Time / 1000;
        foreach (var e in events)
        {
            double time = e.Time / 1000;
            strain = strain * Math.Pow(.5, (time - previous) / halfLife) + e.Cost;
            previous = time;
            int index = (int)(time / .4);
            peaks[index] = Math.Max(peaks[index], strain);
        }
        return peaks;
    }

    internal static double Quantile(double[] values, double fraction)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        double index = (sorted.Length - 1) * fraction;
        int low = (int)index, high = Math.Min(low + 1, sorted.Length - 1);
        return sorted[low] + (sorted[high] - sorted[low]) * (index - low);
    }
}
