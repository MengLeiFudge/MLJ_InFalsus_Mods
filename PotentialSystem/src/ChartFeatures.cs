namespace InFalsusMod.Potential;

/// <summary>仅提取 v6 实际使用的特征；窗口和时间基准与研究版 ranking_features/sim 一致。</summary>
internal static class ChartFeatures
{
    internal static Dictionary<string, double> Extract(ChartNote[] notes, byte difficulty, CancellationToken token)
    {
        double start = notes.Min(n => n.Time) / 1000.0;
        double duration = Math.Max(.001, notes.Max(n => n.End) / 1000.0 - start);
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        var events = KeyboardWorkload.Events(notes, token);
        var windows = difficulty == 8 ? ChartMotion.TwoHandWindows(notes, events) : new List<(int Start, int End)>();
        bool Two(int time) => windows.Any(w => w.Start <= time && time < w.End);
        var costs = new List<WorkloadPoint>();
        for (int first = 0; first < events.Length;)
        {
            bool mode = Two(events[first].Time);
            int end = first + 1;
            while (end < events.Length && Two(events[end].Time) == mode) end++;
            var segment = events.Skip(first).Take(end - first);
            costs.AddRange(mode ? KeyboardWorkload.TwoHands(segment, token) : KeyboardWorkload.Solve(segment, token));
            first = end;
        }
        var holds = notes.Where(n => n.Ground && n.Type == 2).ToArray();
        var key = costs.Concat(holds.Select(n => new WorkloadPoint(n.End, .5))).ToList();
        var peaks = ChartMotion.Peaks(key, .5);
        result["sim_key_h0.5_q80"] = ChartMotion.Quantile(peaks, .8);
        result["sim_key_h0.5_q95"] = ChartMotion.Quantile(peaks, .95);
        if (difficulty == 8)
        {
            var all = new List<WorkloadPoint>(key);
            foreach (var e in events)
            {
                token.ThrowIfCancellationRequested();
                int active = holds.Count(h => h.Time < e.Time - 30 && h.End > e.Time + 30);
                if (active > 0 && !Two(e.Time)) all.Add(new WorkloadPoint(e.Time, active * KeyboardWorkload.Count(e.Choices[0].Mask)));
                if (e.Choices.Length > 1) all.Add(new WorkloadPoint(e.Time, .5 * (e.Choices.Length - 1)));
            }
            for (int i = 2; i < events.Length; i++)
            {
                int a = events[i - 1].Time - events[i - 2].Time, b = events[i].Time - events[i - 1].Time;
                if (Math.Max(a, b) >= 1000) continue;
                double ratio = Math.Abs(Math.Log2(b / (double)a));
                double odd = ratio < .05 ? 0 : Math.Abs(ratio - Math.Round(ratio)) < .05 ? .5 : 1;
                if (odd > 0) all.Add(new WorkloadPoint(events[i].Time, odd * Math.Min(4, 250.0 / b)));
            }
            result["sim_keyall_h2.0_q80"] = ChartMotion.Quantile(ChartMotion.Peaks(all, 2), .8);
        }
        if (difficulty == 1)
        {
            var air = ChartMotion.Peaks(ChartMotion.Air(notes, token), .5).OrderByDescending(v => v).ToArray();
            result["sim_air_h0.5_top"] = air.Select((v, i) => v * Math.Pow(.9, i)).Sum();
        }
        var masks = events.Select(e => e.Choices.Select(c => c.Mask).Distinct().OrderBy(m => m).ToArray()).ToArray();
        var press = masks.Select(m => (double)m.Min(KeyboardWorkload.Count)).ToArray();
        var gaps = events.Select((e, i) => i == 0 ? 1 : e.Time / 1000.0 - events[i - 1].Time / 1000.0).ToArray();
        var change = new double[events.Length];
        var rhythm = new double[events.Length];
        var discount = costs.Select(c => c.Cost).ToArray();
        for (int i = 1; i < events.Length; i++)
        {
            token.ThrowIfCancellationRequested();
            if (gaps[i] > 1) continue;
            change[i] = masks[i - 1].Min(a => masks[i].Min(b => KeyboardWorkload.Count(a ^ b)));
            if (i > 1 && gaps[i - 1] <= 1) rhythm[i] = Math.Min(2, Math.Abs(Math.Log2(gaps[i] / gaps[i - 1])));
            bool repeated = Enumerable.Range(1, Math.Min(i, 8)).Any(lag => masks[i - lag].SequenceEqual(masks[i])
                && gaps[i - lag] <= 1 && Math.Abs(gaps[i] - gaps[i - lag]) <= .012);
            if (repeated) discount[i] *= .25;
        }
        double[] times = events.Select(e => e.Time / 1000.0).ToArray();
        result["press_1s_q90"] = Window(times, press, start, duration, 1, .9);
        result["change_1s_q99"] = Window(times, change, start, duration, 1, .99);
        result["rhythm_1s_max"] = Window(times, rhythm, start, duration, 1, 1);
        if (difficulty == 1) result["discount25_1s_q90"] = Window(times, discount, start, duration, 1, .9);
        var opsTimes = times.Concat(holds.Select(n => n.End / 1000.0)).ToArray();
        var ops = press.Concat(holds.Select(_ => 1.0)).ToArray();
        result["ops_1s_q90"] = Window(opsTimes, ops, start, duration, 1, .9);
        result["ops_4s_q90"] = Window(opsTimes, ops, start, duration, 4, .9);
        result["hold_short0.125_4s_q90"] = Window(holds.Select(n => n.Time / 1000.0).ToArray(),
            holds.Select(n => n.End - n.Time <= 125 ? 1.0 : 0).ToArray(), start, duration, 4, .9);
        var flicks = notes.Where(n => n.Type == 4).GroupBy(n => (n.Time, Direction: n.Flags >> 8)).ToArray();
        result["flick_precision_mean"] = flicks.Sum(g => -Math.Log2(Math.Min(1, Math.Max(.01, g.Max(n => n.Width))))) / duration;
        int[] keyTimes = events.Select(e => e.Time).ToArray();
        result["twohand_sync_mean"] = flicks.Sum(g => Math.Max(0, 1 - ChartMotion.Nearest(keyTimes, g.Key.Time, 1000) / 125)) / duration;
        Sky(notes, start, duration, result, token);
        var chords = notes.Where(n => n.Type is 1 or 2).GroupBy(n => n.Time).OrderBy(g => g.Key)
            .Select(g => g.Select(n => (Math.Round(n.X, 3), Math.Round(n.Width, 3))).ToHashSet()).ToArray();
        int repeats = 0;
        for (int i = 1; i < chords.Length; i++) if (chords[i - 1].SetEquals(chords[i])) repeats++;
        result["chord_repeat"] = chords.Length > 1 ? repeats / (double)(chords.Length - 1) : 0;
        return result;
    }

    private static void Sky(ChartNote[] notes, double start, double duration, Dictionary<string, double> result, CancellationToken token)
    {
        var segments = notes.Where(n => n.Type == 5).OrderBy(n => n.Time).ThenBy(n => n.End).ToArray();
        var jumpTimes = new List<double>();
        var jumps = new List<double>();
        double forced = 0;
        for (int i = 1; i < segments.Length; i++)
        {
            var previous = segments[i - 1];
            var n = segments[i];
            double gap = (n.Time - previous.End) / 1000.0;
            if (gap < 0 || gap >= .05) continue;
            double distance = Math.Abs(n.X - previous.EndX), width = Math.Max(.03, n.Width);
            forced += Math.Max(0, distance - (previous.EndWidth + n.Width) / 2) / width;
            jumpTimes.Add(n.Time / 1000.0);
            jumps.Add(Math.Log2(1 + distance / width));
        }
        result["sky_forced_jump"] = forced / duration;
        result["sky_jump_local_4s_max"] = Window(jumpTimes.ToArray(), jumps.ToArray(), start, duration, 4, 1);
        var times = new List<double>();
        var movements = new List<double>();
        (double Left, double Right)? current = null;
        int? previousEnd = null;
        foreach (var n in segments)
        {
            token.ThrowIfCancellationRequested();
            if (previousEnd == null || n.Time - previousEnd < 0 || n.Time - previousEnd >= 50) current = null;
            double dt = Math.Max(.001, (n.End - n.Time) / 1000.0);
            for (int i = 0; i <= 32; i++)
            {
                double u = i / 32.0;
                var band = ChartMotion.Bounds(n, u);
                double movement = 0;
                if (current.HasValue)
                {
                    var old = current.Value;
                    movement = Math.Max(0, Math.Max(band.Left - old.Right, old.Left - band.Right));
                    current = old.Right < band.Left ? (band.Left, band.Left) : old.Left > band.Right ? (band.Right, band.Right)
                        : (Math.Max(old.Left, band.Left), Math.Min(old.Right, band.Right));
                }
                else current = band;
                times.Add(n.Time / 1000.0 + dt * u);
                movements.Add(movement / Math.Max(.01, band.Right - band.Left));
            }
            previousEnd = n.End;
        }
        result["sky_required_1s_q90"] = Window(times.ToArray(), movements.ToArray(), start, duration, 1, .9);
    }

    /// <summary>0.25 秒箱的有效卷积；按整曲有效时长起算，不补尾窗。</summary>
    private static double Window(double[] times, double[] values, double start, double duration, int seconds, double quantile)
    {
        var bins = new double[Math.Max(1, (int)Math.Ceiling(duration / .25))];
        for (int i = 0; i < times.Length; i++) bins[Math.Clamp((int)((times[i] - start) / .25), 0, bins.Length - 1)] += values[i];
        int count = Math.Min(bins.Length, seconds * 4);
        var roll = new double[bins.Length - count + 1];
        for (int i = 0; i < roll.Length; i++)
        {
            double sum = 0;
            for (int k = 0; k < count; k++) sum += bins[i + k];
            roll[i] = sum / (count * .25);
        }
        return ChartMotion.Quantile(roll, quantile);
    }
}
