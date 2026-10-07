using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>
/// 逐笔规划到自身合法上限（满效能）的整颗强化；上限按已解锁回想的真实掉落表逐颗确定，所有消耗和余料只作用于临时库存。
/// 满效能粒子只进不出，技能不在粒子之间迁移。
/// 目标按强化费用从低到高处理。智能模式以距上限 <see cref="FusionRules.TargetWindow"/> 划分：
/// 接近上限的粒子只作目标，其余可分解粒子只作材料，使每颗新成品消耗的材料尽量少。
/// 规划不含随机性，也不依赖材料上限：上限只决定在哪一笔之前停止，
/// 因此确认窗口用一次完整规划即可得出任意上限下的实际结果。
/// </summary>
internal static class FusionEngine
{
    /// <summary>按确认上限规划强化并执行免费整理，返回一次操作的完整待提交结果。</summary>
    /// <param name="source">确认时的原始堆叠。</param>
    /// <param name="selection">原生选择快照。</param>
    /// <param name="limit">最多处理的不同供体颗数，包含最终留下余料的供体。</param>
    /// <param name="available">当前解锁来源。</param>
    /// <returns>包含实体余料的逐颗库存。</returns>
    internal static FusionResult Prepare(IReadOnlyList<IotaInstance> source, InventorySelection selection,
        int limit, AvailableParticles available)
    {
        var session = new Session(source, selection, available);
        session.Run(limit);
        return session.Finish();
    }

    /// <summary>只规划、不整理，记录每笔强化后的累计数量，供确认窗口随上限实时显示预估。</summary>
    /// <param name="source">预览时的原始堆叠。</param>
    /// <param name="selection">原生选择快照。</param>
    /// <param name="limit">可选上限的最大值；更大的上限不会改变结果。</param>
    /// <param name="available">当前解锁来源。</param>
    /// <returns>可按任意不超过 <paramref name="limit"/> 的上限查询的预估。</returns>
    internal static FusionForecast Forecast(IReadOnlyList<IotaInstance> source, InventorySelection selection,
        int limit, AvailableParticles available)
    {
        var session = new Session(source, selection, available);
        session.Run(limit);
        return new FusionForecast(session.Steps);
    }

    /// <summary>一次规划的会话状态；只修改逐颗副本，失败或跳过的目标不改变已提交部分。</summary>
    private sealed class Session
    {
        /// <summary>身份稳定的逐颗材料，保持原库存顺序。</summary>
        private readonly List<Particle> particles = new();
        /// <summary>来源规则。</summary>
        private readonly AvailableParticles available;
        /// <summary>是否只用玩家选中的粒子作供体；此时未选中的粒子都是目标，不受目标效能门槛限制。</summary>
        private readonly bool selectedMode;
        /// <summary>各具体种类（1–40）满效能的颗数，按技能数0–3分列。</summary>
        private readonly int[,] fullCounts = new int[41, 4];
        /// <summary>本次新强化到满效能的颗数，按技能数0–3分列。</summary>
        private readonly int[] upgraded = new int[4];
        /// <summary>强化目标，按强化费用从低到高排列。</summary>
        private readonly List<Particle> targets;
        /// <summary>
        /// 新供体的固定消耗顺序：技能需求权重低者优先，同权重效能高者优先，使同样的效能点由更少的颗数提供。
        /// 供体与目标互不重叠：智能模式按效能门槛划分，选中模式按是否选中划分。
        /// </summary>
        private readonly List<Particle> donors;
        /// <summary><see cref="donors"/> 中第一颗仍未使用的位置；之前的都已供能。</summary>
        private int donorCursor;
        /// <summary>保存余数的原供体本体；已计入供体颗数，下一笔优先使用。</summary>
        private readonly List<Particle> remnants = new();
        /// <summary>尚未使用的新供体和余料的效能总和，单位为效能点。</summary>
        private long freshSum, remnantSum;
        /// <summary>去重后的供体颗数，以及其中已经消失的颗数。</summary>
        private int processed, decomposed;
        /// <summary>首次成为供体时的原始效能总量及强化费用。</summary>
        private long supplied, spent;

        /// <summary>每笔强化提交后的累计进度，按提交顺序排列。</summary>
        internal List<FusionProgress> Steps { get; } = new();

        /// <summary>展开库存，标记明确选择的供体，统计现有满效能成品并一次排好目标和供体顺序。</summary>
        /// <param name="source">原堆叠。</param>
        /// <param name="selection">供体模式与数量。</param>
        /// <param name="available">来源规则。</param>
        internal Session(IReadOnlyList<IotaInstance> source, InventorySelection selection, AvailableParticles available)
        {
            this.available = available;
            selectedMode = selection.SelectedMode;
            var remaining = new Dictionary<IotaKey, int>(selection.Quantities);
            foreach (var stack in source)
            {
                var key = IotaKey.Of(stack);
                int selected = Math.Min(stack.Count, remaining.GetValueOrDefault(key));
                remaining[key] = remaining.GetValueOrDefault(key) - selected;
                var item = stack;
                item.Count = 1;
                int cap = available.MaximumPotency(item);
                for (int i = 0; i < stack.Count; i++)
                {
                    bool chosen = i < selected;
                    int potency = ParticleData.Potency(item);
                    bool donor = selectedMode ? chosen && FusionRules.CanDonate(item, cap) : FusionRules.IsSmartMaterial(item, cap);
                    bool target = selectedMode ? !chosen && FusionRules.CanTarget(item, cap) : FusionRules.IsSmartTarget(item, cap);
                    var particle = new Particle(item, donor, target, cap);
                    particles.Add(particle);
                    if (particle.IsFull)
                        fullCounts[particle.Shape, particle.Skills]++;
                }
            }
            // OrderBy 是稳定排序：同键粒子保持库存顺序，同一输入总得到同一结果。
            targets = particles.Where(particle => particle.Target)
                .OrderBy(particle => particle.Cost).ToList();
            donors = particles.Where(particle => particle.FreshDonor)
                .OrderBy(particle => particle.DemandWeight)
                .ThenByDescending(particle => particle.Potency)
                .ToList();
            freshSum = donors.Sum(particle => (long)particle.Potency);
        }

        /// <summary>
        /// 按费用从低到高逐个强化目标。下一笔会让不同供体颗数超过上限时停止；
        /// 规划本身与上限无关，所以任意上限的结果都是不限上限时提交序列的前缀。
        /// </summary>
        /// <param name="limit">最多处理的不同供体颗数。</param>
        internal void Run(int limit)
        {
            if (limit < 0)
                throw new ArgumentOutOfRangeException(nameof(limit));
            int next = 0;
            while (true)
            {
                while (next < targets.Count && !targets[next].IsOpenTarget)
                    next++;
                if (next == targets.Count)
                    return;
                var target = PickTarget(next);
                int cost = target.Cost;
                // 剩余材料总量已不足以完成这一颗；后续目标只会更贵，可以直接结束。
                if (freshSum + remnantSum < cost)
                    return;
                var plan = Plan(cost);
                if (plan == null)
                {
                    // 没有供体能合法保存余数；换一颗费用不同的目标仍可能可行。
                    target.Skipped = true;
                    continue;
                }
                if (processed + plan.Donors.Count(donor => !donor.Donor) > limit)
                    return;
                Commit(target, cost, plan);
            }
        }

        /// <summary>在与首个可强化目标费用相同的目标中，选择满效能成品最少的种类，再选该种类离技能数目标占比缺口最大的一颗。</summary>
        /// <param name="start">目标列表中第一颗仍可强化的位置。</param>
        /// <returns>本笔要强化的目标；同效能目标费用相同，这一步只影响成品分布，不影响材料消耗。</returns>
        private Particle PickTarget(int start)
        {
            var best = targets[start];
            // 按会话开始时的费用分组：已强化的同组目标效能已变，但仍需跳过而不是截断扫描。
            for (int i = start + 1; i < targets.Count && targets[i].Cost == best.Cost; i++)
            {
                var candidate = targets[i];
                if (!candidate.IsOpenTarget)
                    continue;
                int total = FullTotal(candidate.Shape), bestTotal = FullTotal(best.Shape);
                if (total < bestTotal || total == bestTotal
                    && Deficit(candidate.Shape, candidate.Skills) > Deficit(best.Shape, best.Skills))
                    best = candidate;
            }
            return best;
        }

        /// <summary>某具体种类当前满效能的总颗数。</summary>
        /// <param name="shape">具体粒子编号。</param>
        /// <returns>各技能数之和。</returns>
        private int FullTotal(ushort shape)
        {
            int total = 0;
            for (int skills = 0; skills < 4; skills++)
                total += fullCounts[shape, skills];
            return total;
        }

        /// <summary>
        /// 该种类满效能成品中某技能数类别距目标比例的缺口，单位为颗；负值表示超出比例。
        /// 按“再增加一颗”后的总数计算，使空种类也会先补占比最高的类别。
        /// </summary>
        /// <param name="shape">具体粒子编号。</param>
        /// <param name="skills">技能数0–3。</param>
        /// <returns>目标颗数减去现有颗数。</returns>
        private double Deficit(ushort shape, int skills)
            => FusionRules.SkillRatios[skills] * (FullTotal(shape) + 1) - fullCounts[shape, skills];

        /// <summary>
        /// 按固定顺序收集足额供体：先用已有余料（不占新名额），再按 <see cref="donors"/> 的顺序取新供体。
        /// 余数保存在某个供体本体中；使累计超额的候选没有合法余料时换用后续候选。
        /// </summary>
        /// <param name="cost">强化到合法上限的完整费用。</param>
        /// <returns>可完整执行的计划；余数无法保存时为空。调用方已确认材料总量足够。</returns>
        private UpgradePlan? Plan(int cost)
        {
            var chosen = new List<Particle>();
            long gathered = 0;
            foreach (var candidate in Candidates())
            {
                long total = gathered + candidate.Potency;
                if (total < cost)
                {
                    chosen.Add(candidate);
                    gathered = total;
                    continue;
                }
                int excess = checked((int)(total - cost));
                Particle? holder = null;
                if (excess > 0 && (holder = FindHolder(candidate, chosen, excess)) == null)
                    continue;
                chosen.Add(candidate);
                return new UpgradePlan(chosen, holder, excess);
            }
            return null;
        }

        /// <summary>按消耗优先级列出当前可用供体：余料在前，其后为尚未使用的新供体。</summary>
        /// <returns>候选序列；供体与目标互不重叠，无需排除接收者。</returns>
        private IEnumerable<Particle> Candidates()
        {
            foreach (var remnant in remnants)
                yield return remnant;
            for (int i = donorCursor; i < donors.Count; i++)
                if (!donors[i].Donor)
                    yield return donors[i];
        }

        /// <summary>优先用最后一颗供体保存余数，其次逆序尝试已选供体。</summary>
        /// <param name="last">使累计效能达到费用的候选。</param>
        /// <param name="chosen">本笔已选供体。</param>
        /// <param name="excess">正整数余数。</param>
        /// <returns>能成为已解锁来源合法无技能余料的本体，或无。</returns>
        private Particle? FindHolder(Particle last, List<Particle> chosen, int excess)
        {
            if (Holds(last, excess))
                return last;
            for (int i = chosen.Count - 1; i >= 0; i--)
                if (Holds(chosen[i], excess))
                    return chosen[i];
            return null;
        }

        /// <summary>判断供体本体能否作为合法的无技能余料保存指定效能。</summary>
        /// <param name="holder">候选本体。</param>
        /// <param name="excess">余数效能。</param>
        /// <returns>余数不超过其原效能且该无技能组合可由已解锁来源生成。</returns>
        private bool Holds(Particle holder, int excess)
            => excess <= holder.Potency && available.Contains(FusionRules.Remainder(holder.Item, excess));

        /// <summary>在临时会话里一次完成分解、余料和到合法上限的强化；目标保留原技能。</summary>
        /// <param name="target">已规划的接收者。</param>
        /// <param name="cost">完整强化费用。</param>
        /// <param name="plan">完整且足额的供体计划。</param>
        private void Commit(Particle target, int cost, UpgradePlan plan)
        {
            foreach (var donor in plan.Donors)
            {
                if (donor.Donor)
                    remnantSum -= donor.Potency;
                else
                {
                    donor.Donor = true;
                    processed++;
                    supplied = checked(supplied + donor.Potency);
                    freshSum -= donor.Potency;
                }
                if (donor != plan.Holder)
                {
                    donor.Alive = false;
                    decomposed++;
                }
            }
            remnants.RemoveAll(remnant => !remnant.Alive);
            if (plan.Holder != null)
            {
                plan.Holder.Item = FusionRules.Remainder(plan.Holder.Item, plan.Remainder);
                remnantSum += plan.Remainder;
                if (!remnants.Contains(plan.Holder))
                    remnants.Add(plan.Holder);
            }
            ParticleData.SetPotency(ref target.Item, target.Cap);
            if (!available.Contains(target.Item))
                throw new InvalidOperationException("强化结果不是单一已解锁来源的合法组合。");
            fullCounts[target.Shape, target.Skills]++;
            upgraded[target.Skills]++;
            spent = checked(spent + cost);
            while (donorCursor < donors.Count && donors[donorCursor].Donor)
                donorCursor++;
            Steps.Add(new FusionProgress(processed, decomposed, Steps.Count + 1));
        }

        /// <summary>核对效能守恒并执行免费整理，生成待提交结果。</summary>
        /// <returns>本次操作的完整结果。</returns>
        internal FusionResult Finish()
        {
            if (supplied != spent + remnantSum)
                throw new InvalidOperationException("融合供体效能与强化费用、实体余料不守恒。");
            var items = particles.Where(particle => particle.Alive).Select(particle => particle.Item).ToList();
            var consolidation = TraitConsolidator.Apply(items, available);
            return new FusionResult
            {
                Items = items, Processed = processed, Decomposed = decomposed,
                Supplied = supplied, Spent = spent, Remainder = remnantSum, RemainderCount = remnants.Count,
                Upgraded = upgraded,
                StacksBefore = consolidation.Before, StacksAfter = consolidation.After,
                SingleStacksBefore = consolidation.SinglesBefore, SingleStacksAfter = consolidation.SinglesAfter,
                Consolidated = consolidation.Changed
            };
        }
    }

    /// <summary>逐颗材料身份；原供体留下的余料可继续供能，但不会成为本轮接收者。</summary>
    private sealed class Particle
    {
        /// <summary>当前完整数据。</summary>
        internal IotaInstance Item;
        /// <summary>会话开始时是否允许作为新供体；与 <see cref="Target"/> 互斥。</summary>
        internal readonly bool FreshDonor;
        /// <summary>会话开始时是否列为强化目标。</summary>
        internal readonly bool Target;
        /// <summary>是否仍有实体。</summary>
        internal bool Alive = true;
        /// <summary>是否曾在本次操作中作为供体，供体上限按不同实体计算。</summary>
        internal bool Donor;
        /// <summary>作为目标时没有供体能合法保存余数，本次不再尝试。</summary>
        internal bool Skipped;
        /// <summary>会话开始时组合在已解锁回想中的合法最高效能；无法合法生成时为零。余料不会成为目标。</summary>
        internal readonly int Cap;
        /// <summary>会话开始时强化到 <see cref="Cap"/> 的完整费用，决定目标顺序；非目标为零。</summary>
        internal readonly int Cost;
        /// <summary>具体种类。</summary>
        internal ushort Shape => ParticleData.Shape(Item);
        /// <summary>当前效能。</summary>
        internal int Potency => ParticleData.Potency(Item);
        /// <summary>实际技能数0–3。</summary>
        internal int Skills => ParticleData.TraitCount(Item);
        /// <summary>是否已达到会话开始时组合的合法上限；只对目标和原有成品有意义，余料不会被统计。</summary>
        internal bool IsFull => Cap > 0 && Potency >= Cap;
        /// <summary>仍可尝试强化的目标。</summary>
        internal bool IsOpenTarget => Target && !IsFull && !Skipped;
        /// <summary>所带技能的需求权重之和；作为供体时越低越先消耗，使常用技能留在库存中。</summary>
        internal int DemandWeight
        {
            get
            {
                int weight = 0;
                for (int slot = 0; slot < Skills; slot++)
                    weight += TraitDemand.Weight(ParticleData.GetTrait(Item, slot));
                return weight;
            }
        }

        /// <summary>建立逐颗身份。</summary>
        /// <param name="item">Count为1的材料。</param>
        /// <param name="freshDonor">是否可作新供体。</param>
        /// <param name="target">是否列为强化目标。</param>
        /// <param name="cap">合法上限。</param>
        internal Particle(IotaInstance item, bool freshDonor, bool target, int cap)
        {
            Item = item;
            FreshDonor = freshDonor;
            Target = target;
            Cap = cap;
            Cost = target ? FusionRules.TotalCost(ParticleData.Potency(item), cap) : 0;
        }
    }

    /// <summary>一笔完整强化的供体计划。</summary>
    /// <param name="Donors">供体身份，含余料本体。</param>
    /// <param name="Holder">保留余数的供体本体；余数为零时为空。</param>
    /// <param name="Remainder">余料效能，零表示无余料。</param>
    private sealed record UpgradePlan(List<Particle> Donors, Particle? Holder, int Remainder);
}

/// <summary>某笔强化提交后的累计数量。</summary>
/// <param name="Processed">不同供体颗数，与材料上限同一口径。</param>
/// <param name="Decomposed">最终消失的粒子颗数。</param>
/// <param name="Strengthened">新增满效能的颗数。</param>
internal readonly record struct FusionProgress(int Processed, int Decomposed, int Strengthened);

/// <summary>一次完整规划的逐笔累计进度；任意上限的实际结果都是这一序列的前缀。</summary>
internal sealed class FusionForecast
{
    /// <summary>按提交顺序排列，Processed 单调不减。</summary>
    private readonly IReadOnlyList<FusionProgress> steps;

    /// <summary>保存一次完整规划的进度。</summary>
    /// <param name="steps">按提交顺序排列的累计数量。</param>
    internal FusionForecast(IReadOnlyList<FusionProgress> steps) => this.steps = steps;

    /// <summary>取得指定上限下的结果：最后一笔供体颗数不超过上限的累计进度。</summary>
    /// <param name="limit">玩家设置的材料上限。</param>
    /// <returns>没有可完成的强化时为全零。</returns>
    internal FusionProgress At(int limit)
    {
        int low = 0, high = steps.Count;
        while (low < high)
        {
            int middle = (low + high) / 2;
            if (steps[middle].Processed <= limit)
                low = middle + 1;
            else
                high = middle;
        }
        return low == 0 ? default : steps[low - 1];
    }
}

/// <summary>一次操作的完整结果。</summary>
internal sealed class FusionResult
{
    /// <summary>每项Count为1的完整结果库存。</summary>
    internal List<IotaInstance> Items { get; init; } = new();
    /// <summary>实际参与的不同供体颗数，包含保留为余料的本体。</summary>
    internal int Processed { get; init; }
    /// <summary>最终消失的粒子颗数。</summary>
    internal int Decomposed { get; init; }
    /// <summary>这些供体最初携带的效能总数。</summary>
    internal long Supplied { get; init; }
    /// <summary>整颗强化费用之和。</summary>
    internal long Spent { get; init; }
    /// <summary>仍在实体余料中的效能之和。</summary>
    internal long Remainder { get; init; }
    /// <summary>最终保留的余料颗数。</summary>
    internal int RemainderCount { get; init; }
    /// <summary>新强化到满效能的颗数，按技能数0–3分列。</summary>
    internal IReadOnlyList<int> Upgraded { get; init; } = new int[4];
    /// <summary>新强化到满效能的总颗数。</summary>
    internal int Strengthened => Upgraded.Sum();
    /// <summary>免费整理前的不同条目数。</summary>
    internal int StacksBefore { get; init; }
    /// <summary>整理后的条目数。</summary>
    internal int StacksAfter { get; init; }
    /// <summary>整理前的单颗条目数。</summary>
    internal int SingleStacksBefore { get; init; }
    /// <summary>整理后的单颗条目数。</summary>
    internal int SingleStacksAfter { get; init; }
    /// <summary>是否发生了有收益的免费交换。</summary>
    internal bool Consolidated { get; init; }
}
