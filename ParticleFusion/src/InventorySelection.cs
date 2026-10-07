using ifapp.Game.Data;

namespace InFalsusMod;

/// <summary>完整材料身份，排除堆叠数量但保留用户标记与保留字段。</summary>
/// <param name="Lower">形状、效能及前两个技能。</param>
/// <param name="Upper">第三技能及用户标记。</param>
/// <param name="Unused">原结构保留字段。</param>
internal readonly record struct IotaKey(ulong Lower, uint Upper, ushort Unused)
{
    /// <summary>从原生材料复制可比较的完整身份。</summary>
    /// <param name="item">库存或格子中的材料。</param>
    /// <returns>不包含Count的键。</returns>
    internal static IotaKey Of(IotaInstance item) => new(item.DataLowerHalf, item.DataUpperHalf, item._Unused);
}

/// <summary>原生批量选择状态及精确的供体颗数；与颜色筛选无关，读取原生界面由 InventoryEditor 负责。</summary>
internal sealed class InventorySelection
{
    /// <summary>是否处于批量选择模式。</summary>
    internal bool SelectedMode { get; }
    /// <summary>被实际勾选的完整材料及各自颗数。</summary>
    internal Dictionary<IotaKey, int> Quantities { get; }
    /// <summary>已选颗数，而非UI选中条目数。</summary>
    internal int Count => Quantities.Values.Sum();

    /// <summary>保存只读选择快照。</summary>
    /// <param name="selectedMode">原生模式。</param>
    /// <param name="quantities">完整数据到已选颗数的映射；调用方已核对每项都存在于库存。</param>
    internal InventorySelection(bool selectedMode, Dictionary<IotaKey, int> quantities)
    {
        SelectedMode = selectedMode;
        Quantities = quantities;
    }

    /// <summary>选择顺序不影响身份，数量或模式变化则必须重新确认。</summary>
    /// <param name="other">执行前重新读取的选择。</param>
    /// <returns>是否仍是同一批材料。</returns>
    internal bool Matches(InventorySelection other) => SelectedMode == other.SelectedMode
        && Quantities.Count == other.Quantities.Count
        && Quantities.All(pair => other.Quantities.GetValueOrDefault(pair.Key) == pair.Value);
}
