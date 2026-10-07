using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>用户指定的最后四个回想各刷二十次，再调用一次正式融合算法的离线场景。</summary>
internal static class Program
{
    /// <summary>按游戏挑战列表顺序排列的最后四个回想。</summary>
    private static readonly int[] Encounters = { 83, 84, 85, 105 };
    /// <summary>掉落随机种子；融合算法本身不含随机性。</summary>
    private const int DropSeed = 20260923;
    /// <summary>用于原始输入和可读输出的序列化设置。</summary>
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>生成一次完整样本，保存原始掉落、融合前后库存和汇总。</summary>
    /// <param name="args">输入 JSON 路径和结果目录。</param>
    /// <returns>成功为零；无效数据通过异常明确终止。</returns>
    private static int Main(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("用法：Simulation <input.json> <output-directory>");
        var input = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(args[0]), Json)
            ?? throw new InvalidOperationException("模拟输入为空。");
        var available = new AvailableParticles(new _K._NH(input));
        var drops = Generate(input);
        var before = Group(drops.Select(drop => drop.Item));
        if (before.Any(item => !available.Contains(item)))
            throw new InvalidOperationException("生成的掉落不符合完整来源规则，不能进行融合。");

        var selection = new InventorySelection(false, new Dictionary<IotaKey, int>());
        // 与智能模式确认窗口的默认材料上限相同：全部允许分解的材料。
        int limit = before.Where(item => FusionRules.IsSmartMaterial(item, available.MaximumPotency(item))).Sum(item => (int)item.Count);
        var forecastWatch = System.Diagnostics.Stopwatch.StartNew();
        var forecast = FusionEngine.Forecast(before, selection, limit, available);
        forecastWatch.Stop();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var result = FusionEngine.Prepare(before, selection, limit, available);
        watch.Stop();
        var after = Group(result.Items);
        var expected = forecast.At(limit);
        // 只核对这次实际模拟的输出守恒、合法性及确认窗口预估一致性，不构造额外用例。
        if (after.Any(item => !available.Contains(item))
            || before.Sum(item => (long)item.Count) - result.Decomposed != after.Sum(item => (long)item.Count)
            || result.Supplied != result.Spent + result.Remainder
            || expected != new FusionProgress(result.Processed, result.Decomposed, result.Strengthened))
            throw new InvalidOperationException("本次模拟结果的数量、来源、效能或预估不一致。");

        var output = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(output);
        WriteInventory(Path.Combine(output, "before.csv"), before, input);
        WriteInventory(Path.Combine(output, "after.csv"), after, input);
        WriteDrops(Path.Combine(output, "drops.csv"), drops, input);
        var report = new
        {
            Conditions = new
            {
                RunsPerEncounter = 20, Stars = 5, AdditionalQuantityMultiplier = 1,
                RetrievalBoost = 3, StackBoost = 5, PotencyMinimumRatio = 0.25,
                DirectMaximumChance = 0.05, InitialInventory = 0, InitialBalance = 0,
                FirstClearRewards = false, AllRepeatableEncountersUnlocked = true,
                InitialDropCycle = "每张回想掉落表从未排除任何行开始，状态跨20局保留",
                DropSeed,
                RandomAlgorithm = "掉落使用 System.Random：同概率规则的可复现样本，不复现Unity某局随机序列；融合不含随机性"
            },
            input.Evidence,
            Farming = Encounters.Select(id => new
            {
                Encounter = id, Name = input.EncounterNames[id.ToString(CultureInfo.InvariantCulture)],
                Runs = 20, DrawsPerRun = input.EncounterDetails.encounterDetails.Single(e => e.Id.Value == id).BaseRolls + 7,
                Draws = drops.Count(drop => drop.Encounter == id),
                Inventory = Summarize(drops.Where(drop => drop.Encounter == id).Select(drop => drop.Item), input)
            }),
            Before = Summarize(before, input),
            Operation = new
            {
                Limit = limit, result.Processed, result.Decomposed, result.Supplied, result.Spent, result.Remainder,
                result.RemainderCount, result.Strengthened, UpgradedBySkills = result.Upgraded,
                DecomposedPerNew999 = result.Strengthened == 0 ? (double?)null
                    : Math.Round(result.Decomposed / (double)result.Strengthened, 2),
                result.Consolidated, PrepareMilliseconds = watch.ElapsedMilliseconds,
                ForecastMilliseconds = forecastWatch.ElapsedMilliseconds
            },
            After = Summarize(after, input),
            ByShape = input.Shapes.OrderBy(shape => shape.Id.Value).Select(shape => new
            {
                Id = shape.Id.Value, Name = shape.IdStr, shape.Tier, shape.Color,
                Before = Metrics(before.Where(item => ParticleData.Shape(item) == shape.Id.Value)),
                After = Metrics(after.Where(item => ParticleData.Shape(item) == shape.Id.Value))
            })
        };
        var text = JsonSerializer.Serialize(report, Json);
        File.WriteAllText(Path.Combine(output, "summary.json"), text + Environment.NewLine);
        Console.WriteLine(JsonSerializer.Serialize(new { report.Before, report.Operation, report.After }, Json));
        return 0;
    }

    /// <summary>按原生加权抽取、同阶轮换、技能行去重和效能曲线生成八十局掉落。</summary>
    /// <param name="input">游戏资源快照。</param>
    /// <returns>每次抽取的一组同属性粒子，保留回想和局数来源。</returns>
    private static List<Drop> Generate(Scenario input)
    {
        var random = new System.Random(DropSeed);
        var tiers = input.Shapes.ToDictionary(shape => shape.Id.Value, shape => shape.Tier);
        var drops = new List<Drop>();
        foreach (int id in Encounters)
        {
            var encounter = input.EncounterDetails.encounterDetails.Single(e => e.Id.Value == id);
            var rows = input.GameData.lootIotaTables[encounter.LootTableIndex].Rows;
            var traits = input.GameData.lootTraitTables[encounter.LootTableIndex];
            var used = new bool[rows.Length];
            for (int run = 1; run <= 20; run++)
            {
                // 五星给出四次追加抽取，满回收加成另给三次；基础值是整数，无随机舍入差异。
                int draws = encounter.BaseRolls + 3 + 4;
                var selected = new List<int>(draws);
                for (int draw = 0; draw < draws; draw++)
                {
                    var candidates = Enumerable.Range(0, rows.Length).Where(i => !used[i]).ToArray();
                    int row = candidates[Weighted(candidates.Select(i => rows[i].Weight).ToArray(), random)];
                    selected.Add(row);
                    used[row] = true;
                    var sameTier = Enumerable.Range(0, rows.Length).Where(i => tiers[rows[i].IotaId.Value] == tiers[rows[row].IotaId.Value]).ToArray();
                    if (sameTier.All(i => used[i]))
                        foreach (int i in sameTier)
                            used[i] = false;
                }
                for (int draw = 0; draw < selected.Count; draw++)
                {
                    var row = rows[selected[draw]];
                    int minimum = Math.Max(1, (int)(row.MaxPotency * 0.25));
                    float roll = MathF.Pow((float)random.NextDouble(), 1f / 3f);
                    int potency = minimum + (int)MathF.Round((row.MaxPotency - minimum) * roll, MidpointRounding.ToEven);
                    if (random.NextDouble() < 0.05)
                        potency = row.MaxPotency;
                    var selectedTraits = new List<short>(3);
                    var usedTraitRows = new HashSet<int>();
                    for (int i = 0; i < row.TraitRolls && selectedTraits.Count < 3; i++)
                    {
                        int index = Weighted(traits.Rows.Select(trait => trait.Weight).ToArray(), random);
                        short trait = traits.Rows[index].TraitId.Value;
                        if (trait > 0 && usedTraitRows.Add(index))
                            selectedTraits.Add(trait);
                    }
                    // 使用原生生成公式独立编码，不通过融合辅助函数制作输入。
                    var item = new IotaInstance
                    {
                        BaseIotaIdPotency = (row.IotaId.Value << 12) | (potency << 2) | (3 - selectedTraits.Count),
                        TraitId1 = selectedTraits.ElementAtOrDefault(0),
                        TraitId2 = selectedTraits.ElementAtOrDefault(1),
                        TraitId3 = selectedTraits.ElementAtOrDefault(2),
                        Count = (ushort)random.Next(1, 11)
                    };
                    drops.Add(new Drop(id, run, draw + 1, item));
                }
            }
        }
        return drops;
    }

    /// <summary>在正整数总权重中选择原始行，保留重复行和空技能行。</summary>
    /// <param name="weights">按原顺序排列的非负权重。</param>
    /// <param name="random">本场景的掉落随机流。</param>
    /// <returns>被抽中的行下标。</returns>
    private static int Weighted(int[] weights, System.Random random)
    {
        int value = random.Next(weights.Sum());
        for (int i = 0; i < weights.Length; i++)
        {
            value -= weights[i];
            if (value < 0)
                return i;
        }
        throw new InvalidOperationException("掉落表权重无效。");
    }

    /// <summary>合并同一完整打包状态，模拟库存快照中的真实堆叠。</summary>
    /// <param name="items">待合并的抽取结果或逐颗输出。</param>
    /// <returns>保留首次出现顺序的库存堆叠。</returns>
    private static List<IotaInstance> Group(IEnumerable<IotaInstance> items) => items
        .GroupBy(item => (item.BaseIotaIdPotency, item.TraitId1, item.TraitId2, item.TraitId3))
        .Select(group =>
        {
            var item = group.First();
            item.Count = checked((ushort)group.Sum(entry => entry.Count));
            return item;
        }).ToList();

    /// <summary>汇总数量、效能分段及技能数量，全部按颗数加权。</summary>
    /// <param name="items">要统计的库存部分。</param>
    /// <returns>零库存也可序列化的可读指标。</returns>
    private static object Metrics(IEnumerable<IotaInstance> items)
    {
        var list = items.ToArray();
        long count = list.Sum(item => (long)item.Count);
        return new
        {
            Count = count,
            MeanPotency = count == 0 ? (double?)null : Math.Round(list.Sum(item => (long)item.Count * ParticleData.Potency(item)) / (double)count, 2),
            MinimumPotency = count == 0 ? (int?)null : list.Min(ParticleData.Potency),
            MaximumPotency = count == 0 ? (int?)null : list.Max(ParticleData.Potency),
            At999 = list.Where(item => ParticleData.Potency(item) == 999).Sum(item => (long)item.Count),
            AtLeast900 = list.Where(item => ParticleData.Potency(item) >= 900).Sum(item => (long)item.Count),
            AtLeast800 = list.Where(item => ParticleData.Potency(item) >= 800).Sum(item => (long)item.Count),
            SkillCounts = Enumerable.Range(0, 4).Select(n => list.Where(item => ParticleData.TraitCount(item) == n).Sum(item => (long)item.Count)).ToArray()
        };
    }

    /// <summary>为整体和三个阶级生成相同口径的统计。</summary>
    /// <param name="items">输入库存。</param>
    /// <param name="input">形状及阶级映射。</param>
    /// <returns>整体、阶级和具体技能装备数量。</returns>
    private static object Summarize(IEnumerable<IotaInstance> items, Scenario input)
    {
        var list = items.ToArray();
        var tiers = input.Shapes.ToDictionary(shape => shape.Id.Value, shape => shape.Tier);
        return new
        {
            Total = Metrics(list),
            Tiers = Enumerable.Range(1, 3).Select(tier => new { Tier = tier, Metrics = Metrics(list.Where(item => tiers[ParticleData.Shape(item)] == tier)) }),
            Skills = TraitCatalog.Entries.Select(trait => new
            {
                trait.Id, trait.Name, trait.Tier,
                Copies = list.Sum(item => (long)item.Count * Enumerable.Range(0, ParticleData.TraitCount(item)).Count(slot => ParticleData.GetTrait(item, slot) == trait.Id))
            }).Where(trait => trait.Copies > 0)
        };
    }

    /// <summary>输出适合表格软件打开的完整库存。</summary>
    /// <param name="path">CSV 文件路径。</param>
    /// <param name="items">已合并的库存堆叠。</param>
    /// <param name="input">名称与阶级映射。</param>
    private static void WriteInventory(string path, IEnumerable<IotaInstance> items, Scenario input)
    {
        var names = TraitCatalog.Entries.ToDictionary(trait => (short)trait.Id, trait => trait.Name);
        var shapes = input.Shapes.ToDictionary(shape => shape.Id.Value);
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("ShapeId,ShapeName,Tier,Color,Count,Potency,SkillCount,TraitIds,TraitNames");
        foreach (var item in items.OrderBy(ParticleData.Shape).ThenByDescending(ParticleData.Potency))
        {
            var shape = shapes[ParticleData.Shape(item)];
            var traits = Enumerable.Range(0, ParticleData.TraitCount(item)).Select(slot => ParticleData.GetTrait(item, slot)).ToArray();
            writer.WriteLine($"{shape.Id.Value},{shape.IdStr},{shape.Tier},{shape.Color},{item.Count},{ParticleData.Potency(item)},{traits.Length},{string.Join(';', traits)},{Csv(string.Join(';', traits.Select(trait => names[trait])))}");
        }
    }

    /// <summary>保存每局每次抽取，保留堆叠关联而非仅保留最终总量。</summary>
    /// <param name="path">CSV 文件路径。</param>
    /// <param name="drops">按实际生成顺序排列的掉落。</param>
    /// <param name="input">回想中文名称。</param>
    private static void WriteDrops(string path, List<Drop> drops, Scenario input)
    {
        using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
        writer.WriteLine("Encounter,Name,Run,Draw,ShapeId,Count,Potency,SkillCount,TraitIds");
        foreach (var drop in drops)
        {
            var item = drop.Item;
            var traits = Enumerable.Range(0, ParticleData.TraitCount(item)).Select(slot => ParticleData.GetTrait(item, slot));
            writer.WriteLine($"{drop.Encounter},{Csv(input.EncounterNames[drop.Encounter.ToString(CultureInfo.InvariantCulture)])},{drop.Run},{drop.Draw},{ParticleData.Shape(item)},{item.Count},{ParticleData.Potency(item)},{ParticleData.TraitCount(item)},{string.Join(';', traits)}");
        }
    }

    /// <summary>转义中文名中的逗号、引号与换行。</summary>
    /// <param name="value">原始显示文字。</param>
    /// <returns>带双引号的 CSV 单元格。</returns>
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}

/// <summary>一次掉落事件，同一 Item.Count 中的粒子共享技能和效能。</summary>
internal sealed record Drop(int Encounter, int Run, int Draw, IotaInstance Item);

/// <summary>静态资源、名称和来源证据组成的离线输入。</summary>
internal sealed class Scenario : Definitions
{
    /// <summary>全部四十种具体形状。</summary>
    public Shape[] Shapes { get; set; } = Array.Empty<Shape>();
    /// <summary>回想编号到中文名。</summary>
    public Dictionary<string, string> EncounterNames { get; set; } = new();
    /// <summary>导出来源的版本和文件哈希。</summary>
    public Dictionary<string, string> Evidence { get; set; } = new();
}

/// <summary>形状与阶级、颜色的原资源映射。</summary>
internal sealed class Shape
{
    /// <summary>具体粒子编号。</summary>
    public IotaId Id { get; set; }
    /// <summary>游戏资源中的稳定形状标记。</summary>
    public string IdStr { get; set; } = "";
    /// <summary>粒子阶级 I、II、III。</summary>
    public int Tier { get; set; }
    /// <summary>游戏颜色编号。</summary>
    public int Color { get; set; }
}
