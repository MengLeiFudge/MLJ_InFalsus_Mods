extern alias GameData;

using System.Runtime.InteropServices;
using ifapp.Game.Common;
using ifapp.Game.Data;
using ifapp.Game.Scenes.Recipe;
using SaveOwner = GameData::_K._NH;

namespace InFalsusMod;

/// <summary>预览供体范围，并原子提交完成到顶强化后的库存与空棋盘缓存。</summary>
internal static class InventoryEditor
{
    /// <summary>只读当前库存，计算供体数量上界；不抽取特性或执行融合。</summary>
    /// <param name="layer">当前拥有输入的原生库存界面。</param>
    /// <returns>确认窗口使用的快照与供体上界。</returns>
    internal static FusionPreview Preview(IotaInventoryLayer layer)
    {
        var owner = SaveOwner._CEb ?? throw new InvalidOperationException("请先载入游戏存档。");
        EnsureNoOccupiedMaterials();
        EnsureLayout();
        var inventory = owner._UNA();
        var source = inventory?.allIota;
        var save = owner._DEb?._iEb;
        if (inventory == null || source == null || save == null)
            throw new InvalidOperationException("粒子库存尚未就绪，请稍后重试。");

        var snapshot = new List<IotaInstance>(source.Length);
        for (int i = 0; i < source.Length; i++)
        {
            var item = source.RawBuffer[i];
            if (item.BaseIotaId is < 1 or > 40 || item.Potency > 999
                || ParticleData.Shape(item) != item.BaseIotaId
                || ParticleData.Potency(item) != item.Potency
                || ParticleData.TraitCount(item) != item.TraitCount)
                throw new InvalidOperationException("粒子编码或种类已改变，未修改库存。");
            snapshot.Add(item);
        }
        return new FusionPreview(owner.Pointer, inventory.Pointer, layer.Pointer, snapshot, CaptureSelection(layer, snapshot),
            new AvailableParticles(owner));
    }

    /// <summary>从真实原生选择集合读取供体，核对每颗均存在于库存。</summary>
    /// <param name="layer">当前库存界面。</param>
    /// <param name="items">同一时刻的库存快照。</param>
    /// <returns>可用于确认一致性检查的选择。</returns>
    private static InventorySelection CaptureSelection(IotaInventoryLayer layer, IReadOnlyList<IotaInstance> items)
    {
        var selected = new Dictionary<IotaKey, int>();
        // 1.0.6：_Kw切换_fBA；_mw以格子的完整堆叠数据（cell._B）增删_OcA。原生批量删除按每项Count消耗整堆。
        if (layer._fBA && layer._OcA != null)
            foreach (var item in layer._OcA)
            {
                var key = IotaKey.Of(item);
                selected[key] = checked(selected.GetValueOrDefault(key) + item.Count);
            }
        var inventory = items.GroupBy(IotaKey.Of).ToDictionary(group => group.Key, group => group.Sum(item => (int)item.Count));
        if (selected.Any(pair => pair.Value <= 0 || pair.Value > inventory.GetValueOrDefault(pair.Key)))
            throw new InvalidOperationException("库存或选择已经变化，请重新查看数量并确认。");
        return new InventorySelection(layer._fBA, selected);
    }

    /// <summary>按预览快照规划一次融合，得到确认窗口随材料上限显示的预估；不修改库存。</summary>
    /// <param name="preview">刚读取的只读快照。</param>
    /// <returns>可按任意不超过 <see cref="FusionPreview.MaximumDecomposed"/> 的上限查询的预估。</returns>
    /// <remarks>规划不含随机性；确认时库存与选择不变，则 <see cref="Apply"/> 的实际结果与对应上限的预估一致。</remarks>
    internal static FusionForecast Forecast(FusionPreview preview)
    {
        var owner = SaveOwner._CEb ?? throw new InvalidOperationException("请先载入游戏存档。");
        return FusionEngine.Forecast(preview.Items, preview.Selection, preview.MaximumDecomposed, new AvailableParticles(owner));
    }

    /// <summary>重新核对已确认的快照，再执行融合和库存整理。</summary>
    /// <param name="approved">玩家确认的库存及数量预览。</param>
    /// <param name="layer">执行时的库存界面。</param>
    /// <param name="limit">玩家确认的供体数量上限。</param>
    /// <returns>可跟随游戏语言呈现的结果；失败通过异常返回明确原因。</returns>
    internal static UiMessage Apply(FusionPreview approved, IotaInventoryLayer layer, int limit)
    {
        var current = Preview(layer);
        if (!approved.Matches(current))
            throw new InvalidOperationException("库存或选择已经变化，请重新查看数量并确认。");
        if (limit < 0 || limit > approved.MaximumDecomposed)
            throw new InvalidOperationException("供体限额超出确认范围。");
        if (current.Items.Count == 0)
            return new UiMessage("NoInventory");
        var owner = SaveOwner._CEb!;
        var inventory = owner._UNA();
        var available = new AvailableParticles(owner);
        var result = FusionEngine.Prepare(current.Items, current.Selection, limit, available);
        if (result.Items.Count + result.Decomposed != current.Total
            || result.Supplied != result.Spent + result.Remainder)
            throw new InvalidOperationException("融合数量或效能总量不守恒，未提交库存。");
        var originalKeys = current.Items.GroupBy(IotaKey.Of)
            .ToDictionary(group => group.Key, group => group.Sum(item => (int)item.Count));
        foreach (var group in result.Items.GroupBy(IotaKey.Of))
            if (group.Count() > originalKeys.GetValueOrDefault(group.Key) && !available.Contains(group.First()))
                throw new InvalidOperationException("融合产生了无法从单一已解锁来源取得的粒子。");
        // 满效能成品与三技能未满粒子不能作为供体，因此两者在结果中的颗数只增不减；上限按已解锁回想逐颗确定。
        int protectedBefore = current.Items.Where(item => !FusionRules.CanDonate(item, available.MaximumPotency(item)))
            .Sum(item => (int)item.Count);
        if (result.Items.Count(item => !FusionRules.CanDonate(item, available.MaximumPotency(item))) < protectedBefore)
            throw new InvalidOperationException("融合结果分解了满效能或三技能的粒子，未提交库存。");
        if (result.Processed == 0 && result.Strengthened == 0 && !result.Consolidated)
            return new UiMessage("NoAction");
        if (result.Processed > limit)
            throw new InvalidOperationException("融合消耗超过确认上界，未提交库存。");

        var grouped = new Dictionary<(ulong, uint), IotaInstance>();
        foreach (var item in result.Items)
        {
            var key = (item.DataLowerHalf, item.DataUpperHalf);
            if (grouped.TryGetValue(key, out var stack))
            {
                stack.Count = checked((ushort)(stack.Count + 1));
                grouped[key] = stack;
            }
            else
                grouped.Add(key, item);
        }
        var prepared = new IotaInventory { allIota = new UnorderedListVal<IotaInstance>(grouped.Count) };
        foreach (var item in grouped.Values)
            prepared.allIota.Add(item);
        prepared.OnDeserialized();
        prepared.Initialize();

        // 原生制卡可用材料逐颗展开，并以UserData作为本会话唯一编号；不能直接共享库存堆叠。
        var crafting = new List<(CraftingLayer Layer, UnorderedListVal<IotaInstance> Old, UnorderedListVal<IotaInstance> Next)>();
        foreach (var craftingLayer in UnityEngine.Object.FindObjectsOfType<CraftingLayer>())
        {
            if (!craftingLayer._a)
                continue;
            var next = new UnorderedListVal<IotaInstance>(result.Items.Count);
            for (int i = 0; i < result.Items.Count; i++)
            {
                var item = result.Items[i];
                item.UserData = checked((ushort)i);
                item.Count = 1;
                next.Add(item);
            }
            crafting.Add((craftingLayer, craftingLayer._dZ, next));
        }
        var oldItems = inventory.allIota;
        var oldLookup = inventory.instanceExistsBaseIotaIdPotency;
        bool oldDirty = inventory.dirty;
        try
        {
            inventory.allIota = prepared.allIota;
            inventory.instanceExistsBaseIotaIdPotency = prepared.instanceExistsBaseIotaIdPotency;
            inventory.dirty = false;
            foreach (var view in crafting)
                view.Layer._dZ = view.Next;
        }
        catch
        {
            inventory.allIota = oldItems;
            inventory.instanceExistsBaseIotaIdPotency = oldLookup;
            inventory.dirty = oldDirty;
            foreach (var view in crafting)
                view.Layer._dZ = view.Old;
            throw;
        }

        var message = new UiMessage("Result", current.Total, result.Items.Count, result.Strengthened);
        Plugin.Logger.LogDebug(message);
        try
        {
            foreach (var view in crafting)
            {
                var detail = view.Layer.bottomInsetIotaScrollContainer?._a;
                view.Layer._ku();
                if (detail != null && detail.Id.Value is >= 1 and <= 40)
                    view.Layer._ju(ref detail, true);
            }
            var filter = owner._ioA();
            foreach (var inventoryLayer in UnityEngine.Object.FindObjectsOfType<IotaInventoryLayer>())
                if (inventoryLayer.isActiveAndEnabled)
                {
                    // 原生批量删除也结束选择模式；使用原生退出路径清理格子高亮和旧选择集合。
                    if (inventoryLayer._fBA)
                        inventoryLayer._Kw();
                    // 1.0.6：_ww是带筛选参数的完整重建（选择模式时先_Kw退出，再_kw/_ow/_Iw）。
                    inventoryLayer._ww(ref filter);
                }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogWarning($"融合已提交，界面刷新失败：{ex}");
            message.Warning = "RefreshWarning";
        }
        return message;
    }

    /// <summary>仅阻止真实制卡会话占用材料，后台启用的界面组件不构成占用。</summary>
    private static void EnsureNoOccupiedMaterials()
    {
        foreach (var layer in UnityEngine.Object.FindObjectsOfType<CraftingLayer>())
            if (layer._a && ((layer._DZ?.Length ?? 0) > 0 || layer._Uz != null || layer._vz != null))
                throw new InvalidOperationException("当前棋盘或拖拽操作仍占用粒子。请先收回这些材料，再确认融合；本次未修改库存。");
    }

    /// <summary>核对融合直接读取和改写的打包字段布局。</summary>
    private static void EnsureLayout()
    {
        if (Marshal.SizeOf<IotaInstance>() != 16
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.BaseIotaIdPotency)).ToInt32() != 0
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.TraitId1)).ToInt32() != 4
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.TraitId2)).ToInt32() != 6
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.TraitId3)).ToInt32() != 8
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.UserData)).ToInt32() != 10
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.Count)).ToInt32() != 12
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.DataLowerHalf)).ToInt32() != 0
            || Marshal.OffsetOf<IotaInstance>(nameof(IotaInstance.DataUpperHalf)).ToInt32() != 8)
            throw new InvalidOperationException("游戏粒子结构已改变，未修改库存。");
    }
}

/// <summary>玩家确认所依据的只读库存快照，不包含任何预先抽取的融合结果。</summary>
internal sealed class FusionPreview
{
    /// <summary>预览时的存档入口身份。</summary>
    private readonly IntPtr owner;
    /// <summary>预览时的库存对象身份。</summary>
    private readonly IntPtr inventory;
    /// <summary>预览所属原生库存界面。</summary>
    private readonly IntPtr layer;
    /// <summary>原始堆叠快照。</summary>
    internal IReadOnlyList<IotaInstance> Items { get; }
    /// <summary>确认时的原生模式与明确选择。</summary>
    internal InventorySelection Selection { get; }
    /// <summary>库存颗数，不是堆叠数。</summary>
    internal long Total { get; }
    /// <summary>最多可参与的不同供体颗数，含可能保留为余料的本体；不是一定消耗的数量。智能模式只计 <see cref="FusionRules.IsSmartMaterial"/> 的材料。</summary>
    internal int MaximumDecomposed { get; }

    /// <summary>保存当前状态并计算不涉及随机性的数量预览。</summary>
    /// <param name="owner">存档入口指针。</param>
    /// <param name="inventory">库存对象指针。</param>
    /// <param name="layer">原生库存界面指针。</param>
    /// <param name="items">库存堆叠快照。</param>
    /// <param name="selection">原生选择快照。</param>
    /// <param name="available">当前解锁来源，用于确定每颗粒子的合法上限。</param>
    internal FusionPreview(IntPtr owner, IntPtr inventory, IntPtr layer, IReadOnlyList<IotaInstance> items, InventorySelection selection,
        AvailableParticles available)
    {
        this.owner = owner;
        this.inventory = inventory;
        this.layer = layer;
        Items = items;
        Selection = selection;
        Total = items.Sum(item => (long)item.Count);
        // 余料本体可能保留，供体颗数与净删除数不同；满效能成品和三技能粒子从不参与供能，
        // 智能模式另外不使用接近自身上限的粒子作材料。
        MaximumDecomposed = selection.SelectedMode ? selection.Count
            : items.Where(item => FusionRules.IsSmartMaterial(item, available.MaximumPotency(item))).Sum(item => (int)item.Count);
    }

    /// <summary>确认期间存档、库存或选择变化必须重新获得玩家确认。</summary>
    /// <param name="other">确认时重新读取的状态。</param>
    /// <returns>是否仍是玩家看到的同一批库存。</returns>
    internal bool Matches(FusionPreview other)
    {
        if (owner != other.owner || inventory != other.inventory || layer != other.layer
            || Items.Count != other.Items.Count || !Selection.Matches(other.Selection))
            return false;
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].DataLowerHalf != other.Items[i].DataLowerHalf
                || Items[i].DataUpperHalf != other.Items[i].DataUpperHalf || Items[i].Count != other.Items[i].Count
                || Items[i]._Unused != other.Items[i]._Unused)
                return false;
        return true;
    }
}
