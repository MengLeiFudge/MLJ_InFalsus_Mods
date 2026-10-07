using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>通过合法的等量特性交换减少堆叠；不改变种类、效能、槽数、标记或特性总量。</summary>
internal static class TraitConsolidator
{
    /// <summary>限制一次确认的候选交换搜索量，结果只作改善，不宣称全局最优。</summary>
    private const int CandidateBudget = 1_000_000;

    /// <summary>整理逐颗库存，优先减少条目数，其次减少单颗条目数。</summary>
    /// <param name="items">待原位替换的逐颗副本。</param>
    /// <param name="available">交换两端必须满足的来源。</param>
    /// <returns>免费整理的条目变化。</returns>
    internal static ConsolidationResult Apply(List<IotaInstance> items, AvailableParticles available)
    {
        var initial = CountStacks(items);
        int before = initial.Count;
        int singlesBefore = initial.Values.Count(stack => stack.Count == 1);
        int budget = CandidateBudget;
        var stock = new TraitStock(items, available);
        var output = new List<IotaInstance>(items.Count);
        foreach (var group in items.GroupBy(item => (item.BaseIotaIdPotency, item.UserData, item._Unused)))
        {
            var original = CountStacks(group);
            // 已有非法组合不重排；合法组合可统一槽位顺序，便于识别实际等价的堆叠。
            var normalized = group.Select(item => available.Contains(item) ? Normalize(item) : item);
            var stacks = CountStacks(normalized);
            var working = new TraitStock(stock);
            while (budget > 0 && Improve(stacks, available, working, ref budget)) { }
            bool improved = stacks.Count < original.Count
                || (stacks.Count == original.Count && stacks.Values.Count(stack => stack.Count == 1)
                    < original.Values.Count(stack => stack.Count == 1));
            if (improved)
                stock = working;
            foreach (var stack in (improved ? stacks : original).Values)
                for (int i = 0; i < stack.Count; i++)
                    output.Add(stack.Item);
        }
        var final = CountStacks(output);
        int after = final.Count;
        int singlesAfter = final.Values.Count(stack => stack.Count == 1);
        bool changed = after < before || (after == before && singlesAfter < singlesBefore);
        if (!changed)
            return new ConsolidationResult(before, before, singlesBefore, singlesBefore, false);
        items.Clear();
        items.AddRange(output);
        return new ConsolidationResult(before, after, singlesBefore, singlesAfter, true);
    }

    /// <summary>按原生完整数据键聚合，不把特性相同但标记不同的材料合并。</summary>
    /// <param name="items">Count为1的材料。</param>
    /// <returns>可变堆叠计数。</returns>
    private static Dictionary<(ulong, uint), Stack> CountStacks(IEnumerable<IotaInstance> items)
    {
        var stacks = new Dictionary<(ulong, uint), Stack>();
        foreach (var item in items)
        {
            var key = (item.DataLowerHalf, item.DataUpperHalf);
            if (stacks.TryGetValue(key, out var stack))
                stack.Count++;
            else
                stacks.Add(key, new Stack(item, 1));
        }
        return stacks;
    }

    /// <summary>寻找一次严格改善的等量交换；批量交换较小堆叠的全部颗数，避免人为制造新单颗。</summary>
    /// <param name="stacks">同形状、效能、槽数及标记的堆叠。</param>
    /// <param name="available">掉落合法性。</param>
    /// <param name="stock">当前三级技能携带者计数。</param>
    /// <param name="budget">全次操作共享的剩余候选数。</param>
    /// <returns>是否接受了一次改善。</returns>
    private static bool Improve(Dictionary<(ulong, uint), Stack> stacks, AvailableParticles available, TraitStock stock, ref int budget)
    {
        var entries = stacks.OrderBy(pair => pair.Value.Count).ToArray();
        for (int i = 0; i < entries.Length; i++)
            for (int j = i + 1; j < entries.Length; j++)
            {
                if (--budget < 0)
                    return false;
                var a = entries[i];
                var b = entries[j];
                if (!available.Contains(a.Value.Item) || !available.Contains(b.Value.Item))
                    continue;
                int slots = ParticleData.TraitCount(a.Value.Item);
                int quantity = Math.Min(a.Value.Count, b.Value.Count);
                for (int x = 0; x < slots; x++)
                    for (int y = 0; y < slots; y++)
                    {
                        if (--budget < 0)
                            return false;
                        short first = ParticleData.GetTrait(a.Value.Item, x);
                        short second = ParticleData.GetTrait(b.Value.Item, y);
                        if (first == second)
                            continue;
                        var nextA = a.Value.Item;
                        var nextB = b.Value.Item;
                        ParticleData.SetTrait(ref nextA, x, second);
                        ParticleData.SetTrait(ref nextB, y, first);
                        nextA = Normalize(nextA);
                        nextB = Normalize(nextB);
                        var keyA = (nextA.DataLowerHalf, nextA.DataUpperHalf);
                        var keyB = (nextB.DataLowerHalf, nextB.DataUpperHalf);
                        if (keyA != keyB && !stacks.ContainsKey(keyA) && !stacks.ContainsKey(keyB))
                            continue;
                        var deltas = new Dictionary<(ulong, uint), int>();
                        deltas[a.Key] = -quantity;
                        deltas[b.Key] = -quantity;
                        deltas[keyA] = deltas.GetValueOrDefault(keyA) + quantity;
                        deltas[keyB] = deltas.GetValueOrDefault(keyB) + quantity;
                        int stackDelta = 0, singletonDelta = 0;
                        foreach (var delta in deltas)
                        {
                            int oldCount = stacks.TryGetValue(delta.Key, out var old) ? old.Count : 0;
                            int newCount = oldCount + delta.Value;
                            stackDelta += (newCount > 0 ? 1 : 0) - (oldCount > 0 ? 1 : 0);
                            singletonDelta += (newCount == 1 ? 1 : 0) - (oldCount == 1 ? 1 : 0);
                        }
                        if (stackDelta > 0 || (stackDelta == 0 && singletonDelta >= 0)
                            || !available.Contains(nextA) || !available.Contains(nextB)
                            || !stock.CanExchange(new[] { a.Value.Item, b.Value.Item }, new[] { nextA, nextB }, quantity))
                            continue;
                        stock.Change(a.Value.Item, -quantity);
                        stock.Change(b.Value.Item, -quantity);
                        stock.Change(nextA, quantity);
                        stock.Change(nextB, quantity);
                        foreach (var delta in deltas)
                        {
                            if (!stacks.TryGetValue(delta.Key, out var stack))
                            {
                                stack = new Stack(delta.Key == keyA ? nextA : nextB, 0);
                                stacks.Add(delta.Key, stack);
                            }
                            stack.Count += delta.Value;
                            if (stack.Count == 0)
                                stacks.Remove(delta.Key);
                        }
                        return true;
                    }
            }
        return false;
    }

    /// <summary>按具体ID排列已有槽位；数量、等级和每项重复次数均保持不变。</summary>
    /// <param name="item">合法材料副本。</param>
    /// <returns>槽位顺序统一的同一特性组合。</returns>
    private static IotaInstance Normalize(IotaInstance item)
    {
        var traits = Enumerable.Range(0, ParticleData.TraitCount(item)).Select(slot => ParticleData.GetTrait(item, slot)).ToArray();
        Array.Sort(traits);
        for (int slot = 0; slot < traits.Length; slot++)
            ParticleData.SetTrait(ref item, slot, traits[slot]);
        return item;
    }

    /// <summary>搜索中的一个完整数据堆叠，数量以颗为单位。</summary>
    private sealed class Stack
    {
        /// <summary>Count保持1的代表材料。</summary>
        internal readonly IotaInstance Item;
        /// <summary>该完整数据当前有多少颗。</summary>
        internal int Count;

        /// <summary>创建搜索计数项。</summary>
        /// <param name="item">代表材料。</param>
        /// <param name="count">颗数。</param>
        internal Stack(IotaInstance item, int count) { Item = item; Count = count; }
    }
}

/// <summary>一次免费整理的完整数据条目统计。</summary>
/// <param name="Before">整理前条目数。</param>
/// <param name="After">整理后条目数。</param>
/// <param name="SinglesBefore">整理前单颗条目数。</param>
/// <param name="SinglesAfter">整理后单颗条目数。</param>
/// <param name="Changed">是否接受过实际改善。</param>
internal readonly record struct ConsolidationResult(int Before, int After, int SinglesBefore, int SinglesAfter, bool Changed);
