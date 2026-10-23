using System.Numerics;

namespace InFalsusMod.Potential;

/// <summary>一个键型及按键结束时间；零表示该键没有长按。</summary>
internal sealed record KeyChoice(int Mask, int[] Holds);
internal sealed record KeyEvent(int Time, KeyChoice[] Choices);
internal readonly record struct WorkloadPoint(double Time, double Cost);

/// <summary>v6 的六键指法选择和宽度 32 的束搜索，保持 Python 的稳定排序与同分取先规则。</summary>
internal static class KeyboardWorkload
{
    private static readonly Dictionary<int, double>[] Domains =
    {
        new() { [0] = 0, [1] = 20 }, new() { [1] = 0, [2] = .3 },
        new() { [2] = 0, [3] = 0 }, new() { [3] = 0, [4] = 0 }, new() { [5] = 0, [4] = 20 }
    };
    private sealed record Chain(Chain? Previous, WorkloadPoint Point);
    private sealed record State(double Cost, int[] Position, int[] Last, int[] Held, int Time, int Mask, Chain? Chain);

    internal static int Count(int mask) => BitOperations.PopCount((uint)mask);

    internal static int[] Keys(ChartNote note)
    {
        if (note.Track is 2 or 3) return new[] { note.Track == 2 ? 0 : 5 };
        if (note.Track != 1) return Array.Empty<int>();
        int first = (int)Math.Ceiling(note.X * 4 - 1e-8);
        int last = (int)Math.Ceiling((note.X + note.Width) * 4 - 1e-8) - 1;
        if (first < 1 || last > 4 || first > last)
            throw new InvalidDataException("地面音符范围超出六键语义。");
        return Enumerable.Range(first, last - first + 1).ToArray();
    }

    internal static KeyEvent[] Events(ChartNote[] notes, CancellationToken token)
    {
        var events = new List<KeyEvent>();
        foreach (var group in notes.Where(n => n.Ground).GroupBy(n => n.Time).OrderBy(g => g.Key))
        {
            token.ThrowIfCancellationRequested();
            var choices = new List<KeyChoice> { new(0, new int[6]) };
            foreach (var note in group)
            {
                token.ThrowIfCancellationRequested();
                var next = new Dictionary<string, KeyChoice>(StringComparer.Ordinal);
                foreach (var choice in choices)
                    foreach (int key in Keys(note))
                    {
                        int[] holds = (int[])choice.Holds.Clone();
                        if (note.Type == 2 && note.End > note.Time) holds[key] = Math.Max(holds[key], note.End);
                        var value = new KeyChoice(choice.Mask | (1 << key), holds);
                        next.TryAdd(value.Mask + ":" + string.Join(",", holds), value);
                    }
                if (next.Count > 4096) throw new InvalidDataException("谱面同时键型组合过多。");
                choices = next.Values.OrderBy(c => c, Comparer<KeyChoice>.Create(CompareChoices)).ToList();
            }
            events.Add(new KeyEvent(group.Key, choices.ToArray()));
        }
        return events.ToArray();
    }

    private static int CompareChoices(KeyChoice a, KeyChoice b)
    {
        int result = a.Mask.CompareTo(b.Mask);
        if (result != 0) return result;
        int[] ak = Enumerable.Range(0, 6).Where(k => a.Holds[k] != 0).ToArray();
        int[] bk = Enumerable.Range(0, 6).Where(k => b.Holds[k] != 0).ToArray();
        for (int i = 0; i < Math.Min(ak.Length, bk.Length); i++)
        {
            result = ak[i].CompareTo(bk[i]);
            if (result == 0) result = a.Holds[ak[i]].CompareTo(b.Holds[bk[i]]);
            if (result != 0) return result;
        }
        return ak.Length.CompareTo(bk.Length);
    }

    internal static List<WorkloadPoint> Solve(IEnumerable<KeyEvent> events, CancellationToken token)
    {
        var beams = new[] { new State(0, new[] { 0, 1, 2, 4, 5 }, Enumerable.Repeat(-1000000000, 5).ToArray(),
            Enumerable.Repeat(-1000000000, 5).ToArray(), -1000000000, 0, null) };
        foreach (var e in events)
        {
            token.ThrowIfCancellationRequested();
            var next = new Dictionary<int, State>();
            foreach (var state in beams)
            {
                int busy = 0;
                for (int f = 0; f < 5; f++) if (state.Held[f] > e.Time) busy |= 1 << f;
                foreach (var choice in e.Choices)
                {
                    token.ThrowIfCancellationRequested();
                    int[] keys = Enumerable.Range(0, 6).Where(k => (choice.Mask & (1 << k)) != 0).ToArray();
                    bool assigned = false;
                    foreach (var fingers in Assign(keys, busy, 0, new int[keys.Length]))
                    {
                        assigned = true;
                        double cost = .5 * keys.Length;
                        int[] position = (int[])state.Position.Clone(), last = (int[])state.Last.Clone(), held = (int[])state.Held.Clone();
                        for (int i = 0; i < keys.Length; i++)
                        {
                            int key = keys[i], finger = fingers[i];
                            int gap = Math.Max(1, e.Time - Math.Max(last[finger], held[finger] < e.Time ? held[finger] : -1000000000));
                            cost += Domains[finger][key];
                            cost += position[finger] != key ? Move(gap) : Jack(gap);
                            position[finger] = key;
                            last[finger] = e.Time;
                            if (choice.Holds[key] != 0) held[finger] = choice.Holds[key];
                        }
                        if ((state.Mask & choice.Mask) != 0 && state.Mask != choice.Mask) cost += Shape(e.Time - state.Time);
                        if (busy != 0) cost += .3 * keys.Length;
                        Keep(new State(state.Cost + cost, position, last, held, e.Time, choice.Mask,
                            new Chain(state.Chain, new WorkloadPoint(e.Time, cost))));
                    }
                    if (!assigned)
                    {
                        double cost = .5 * keys.Length + 20;
                        Keep(new State(state.Cost + cost, state.Position, state.Last, state.Held, e.Time, choice.Mask,
                            new Chain(state.Chain, new WorkloadPoint(e.Time, cost))));
                    }
                }
            }
            beams = next.Values.OrderBy(s => s.Cost).Take(32).ToArray();
            if (beams.Length == 0) throw new InvalidDataException("谱面没有可用键型。");
            void Keep(State state)
            {
                int key = 0;
                foreach (int p in state.Position) key = key * 6 + p;
                if (!next.TryGetValue(key, out var previous) || state.Cost < previous.Cost) next[key] = state;
            }
        }
        var result = new List<WorkloadPoint>();
        for (var chain = beams[0].Chain; chain != null; chain = chain.Previous) result.Add(chain.Point);
        result.Reverse();
        return result;
    }

    private static IEnumerable<int[]> Assign(int[] keys, int busy, int index, int[] fingers)
    {
        if (index == keys.Length) { yield return fingers; yield break; }
        for (int finger = 0; finger < 5; finger++)
            if ((busy & (1 << finger)) == 0 && Domains[finger].ContainsKey(keys[index]))
            {
                fingers[index] = finger;
                foreach (var assignment in Assign(keys, busy | (1 << finger), index + 1, fingers)) yield return assignment;
            }
    }

    internal static List<WorkloadPoint> TwoHands(IEnumerable<KeyEvent> events, CancellationToken token)
    {
        var result = new List<WorkloadPoint>();
        var last = new Dictionary<int, int>();
        var held = new Dictionary<int, int>();
        foreach (var e in events)
        {
            token.ThrowIfCancellationRequested();
            KeyChoice? best = null;
            double cost = double.PositiveInfinity;
            foreach (var choice in e.Choices)
            {
                double value = 0;
                for (int key = 0; key < 6; key++)
                    if ((choice.Mask & (1 << key)) != 0)
                        value += .5 + (last.TryGetValue(key, out int t) ? Jack(Math.Max(1, e.Time - t)) : 0)
                            + (held.TryGetValue(key, out int end) && end > e.Time ? 5 : 0);
                if (value < cost) { best = choice; cost = value; }
            }
            if (best == null) throw new InvalidDataException("谱面没有可用双手键型。");
            for (int key = 0; key < 6; key++)
            {
                if ((best.Mask & (1 << key)) != 0) last[key] = e.Time;
                if (best.Holds[key] != 0) held[key] = best.Holds[key];
            }
            result.Add(new WorkloadPoint(e.Time, cost));
        }
        return result;
    }

    private static double Move(int gap) => gap <= 160 ? 20 : gap < 250 ? Math.Min(20, 135.0 / (gap - 160)) : Math.Max(0, 1.5 * (600 - gap) / 350);
    private static double Shape(int gap) => gap >= 600 ? 0 : Math.Min(1, (600 - gap) / 350.0) * Math.Max(1, 250.0 / gap);
    internal static double Jack(int gap) => gap >= 250 ? 0 : .5 * (250.0 / gap - 1);
}
