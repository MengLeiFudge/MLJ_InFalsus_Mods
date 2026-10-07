// 此程序集只提供离线数据容器，让正式融合源码直接参与用户要求的模拟；不加载 IL2CPP 或游戏。
using System.Runtime.InteropServices;

namespace ifapp.Game.Data
{
    /// <summary>粒子打包字段的离线副本，字段偏移与游戏 interop 一致；低两位保留游戏的反向技能数量编码。</summary>
    [StructLayout(LayoutKind.Explicit, Size = 16)]
    public struct IotaInstance
    {
        /// <summary>具体形状、效能及反向技能数量。</summary>
        [FieldOffset(0)] public int BaseIotaIdPotency;
        /// <summary>第一个技能编号。</summary>
        [FieldOffset(4)] public short TraitId1;
        /// <summary>第二个技能编号。</summary>
        [FieldOffset(6)] public short TraitId2;
        /// <summary>第三个技能编号。</summary>
        [FieldOffset(8)] public short TraitId3;
        /// <summary>用户标记；制卡会话也借用它作逐颗编号。</summary>
        [FieldOffset(10)] public ushort UserData;
        /// <summary>本堆叠包含的粒子颗数。</summary>
        [FieldOffset(12)] public ushort Count;
        /// <summary>原结构保留字段。</summary>
        [FieldOffset(14)] public ushort _Unused;
        /// <summary>形状、效能及前两个技能的整体视图。</summary>
        [FieldOffset(0)] public ulong DataLowerHalf;
        /// <summary>第三技能及用户标记的整体视图。</summary>
        [FieldOffset(8)] public uint DataUpperHalf;
    }

    /// <summary>原资源中的具体粒子编号。</summary>
    public record struct IotaId(ushort Value);
    /// <summary>原资源中的技能编号，零代表未抽到技能。</summary>
    public record struct TraitId(short Value);
    /// <summary>可重复挑战的回想编号。</summary>
    public record struct EncounterId(int Value);

    /// <summary>直接从游戏资源提取的粒子掉落条目。</summary>
    public sealed class LootIotaTableRow
    {
        /// <summary>未进入同阶轮换排除时的抽取权重。</summary>
        public int Weight { get; set; }
        /// <summary>具体粒子编号。</summary>
        public IotaId IotaId { get; set; }
        /// <summary>没有最低效能加成时的下限。</summary>
        public int MinPotency { get; set; }
        /// <summary>来源允许的最高效能。</summary>
        public int MaxPotency { get; set; }
        /// <summary>技能表固定抽取次数，重复行与空技能也消耗次数。</summary>
        public int TraitRolls { get; set; }
    }

    /// <summary>一个回想的粒子掉落表。</summary>
    public sealed class LootIotaTable
    {
        /// <summary>初始权重总和。</summary>
        public int TotalWeight { get; set; }
        /// <summary>保持原资源次序的独立掉落条目。</summary>
        public LootIotaTableRow[] Rows { get; set; } = Array.Empty<LootIotaTableRow>();
    }

    /// <summary>技能表中的独立行，同一技能可能占多行。</summary>
    public sealed class LootTraitTableRow
    {
        /// <summary>本行的抽取权重。</summary>
        public int Weight { get; set; }
        /// <summary>零为空技能。</summary>
        public TraitId TraitId { get; set; }
    }

    /// <summary>按回想掉落表编号索引的技能表。</summary>
    public sealed class LootTraitTable
    {
        /// <summary>包含空技能行的总权重。</summary>
        public int TotalWeight { get; set; }
        /// <summary>原始行次序与重复行。</summary>
        public LootTraitTableRow[] Rows { get; set; } = Array.Empty<LootTraitTableRow>();
    }

    /// <summary>正式身份校验所需的技能效果字段。</summary>
    public sealed class TraitEffectDefinition
    {
        /// <summary>效果种类编号。</summary>
        public int TraitEffect { get; set; }
        /// <summary>触发条件编号。</summary>
        public int TraitActivationCondition { get; set; }
    }

    /// <summary>正式 TraitCatalog.Validate 使用的原始技能身份。</summary>
    public sealed class TraitDefinition
    {
        /// <summary>技能编号。</summary>
        public TraitId Id { get; set; }
        /// <summary>技能等级。</summary>
        public int Tier { get; set; }
        /// <summary>有效效果数。</summary>
        public int EffectCount { get; set; }
        /// <summary>第一项效果。</summary>
        public TraitEffectDefinition Effect0 { get; set; } = new();
        /// <summary>第二项效果。</summary>
        public TraitEffectDefinition Effect1 { get; set; } = new();
    }

    /// <summary>正式可用来源读取路径需要的游戏静态表。</summary>
    public sealed class GameData
    {
        /// <summary>粒子掉落表。</summary>
        public LootIotaTable[] lootIotaTables { get; set; } = Array.Empty<LootIotaTable>();
        /// <summary>使用相同回想索引的技能掉落表。</summary>
        public LootTraitTable[] lootTraitTables { get; set; } = Array.Empty<LootTraitTable>();
        /// <summary>按技能编号排列的技能身份。</summary>
        public TraitDefinition[] traits { get; set; } = Array.Empty<TraitDefinition>();
    }

    /// <summary>本次模拟需要的回想掉落信息。</summary>
    public sealed class EncounterDetail
    {
        /// <summary>回想编号。</summary>
        public EncounterId Id { get; set; }
        /// <summary>同时索引粒子表和技能表。</summary>
        public int LootTableIndex { get; set; }
        /// <summary>未计技能和通关星级的抽取次数。</summary>
        public int BaseRolls { get; set; }
    }

    /// <summary>保持正式访问链的回想集合。</summary>
    public sealed class EncounterDefinitions
    {
        /// <summary>全部可重复回想。</summary>
        public EncounterDetail[] encounterDetails { get; set; } = Array.Empty<EncounterDetail>();
    }

    /// <summary>离线来源集合，不提供任何存档读写能力。</summary>
    public class Definitions
    {
        /// <summary>粒子、技能掉落和技能身份。</summary>
        public GameData GameData { get; set; } = new();
        /// <summary>已导出的可重复回想。</summary>
        public EncounterDefinitions EncounterDetails { get; set; } = new();
    }
}

namespace Il2CppSystem.Collections.Generic
{
    /// <summary>只用于让正式来源读取代码运行在托管集合上。</summary>
    public sealed class List<T> : System.Collections.Generic.List<T> { }
}

namespace _K
{
    /// <summary>正式存档入口的离线替身，仅返回固定静态表和指定解锁列表。</summary>
    public sealed class _NH
    {
        /// <summary>本次场景输入。</summary>
        private readonly ifapp.Game.Data.Definitions definitions;

        /// <summary>创建全回想已解锁的离线读取入口。</summary>
        /// <param name="definitions">本次静态资源。</param>
        public _NH(ifapp.Game.Data.Definitions definitions) => this.definitions = definitions;

        /// <summary>取得正式来源读取代码要求的数据集合。</summary>
        /// <returns>当前场景定义。</returns>
        public ifapp.Game.Data.Definitions _VNA() => definitions;

        /// <summary>填充场景已解锁的可重复回想。</summary>
        /// <param name="destination">正式调用方提供的列表。</param>
        /// <param name="filter">正式路径传入的不限角色标记。</param>
        public void _BOA(Il2CppSystem.Collections.Generic.List<ifapp.Game.Data.EncounterId> destination, int filter)
        {
            if (filter != -1)
                throw new ArgumentOutOfRangeException(nameof(filter));
            destination.AddRange(definitions.EncounterDetails.encounterDetails.Select(item => item.Id));
        }
    }
}
