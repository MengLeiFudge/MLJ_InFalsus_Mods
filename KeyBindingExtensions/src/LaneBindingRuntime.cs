using UnityEngine.InputSystem;
using _E;
using ifapp.Game.Scenes.Game;
using ifapp.Game.UI.Settings;

namespace InFalsusMod;

/// <summary>允许六轨共用按键，并在原版判定前同步同键轨道的真实输入状态。</summary>
internal static class LaneBindingRuntime
{
    /// <summary>原版下方轨道与偏好键位槽的数量。</summary>
    private const int LaneCount = 6;

    /// <summary>最近一次读取的原版键位；缺失槽位以 None 表示，不持有原生数组。</summary>
    private static readonly Key[] Keys = new Key[LaneCount];

    /// <summary>每条轨道对应的同键首轨；自身就是首轨时无需同步。</summary>
    private static readonly int[] FirstLanes = { 0, 1, 2, 3, 4, 5 };

    /// <summary>当前键位中是否有需要补齐输入和显示的同键轨道。</summary>
    private static bool hasSharedBindings;

    /// <summary>发生接口不兼容异常后停止多轨道扩展，避免在输入回调中反复抛错。</summary>
    private static bool disabled;

    /// <summary>最近读取的六轨键位是否需要鼠标事件采集；由主线程刷新和消费。</summary>
    internal static bool HasMouseBindings { get; private set; }

    /// <summary>读取六轨键位，仅在内容变化时重建托管映射，兼容原数组替换和原地修改。</summary>
    internal static void RefreshBindings()
    {
        var keys = _YD._JfA;
        int count = keys == null ? 0 : Math.Min(LaneCount, keys.Length);
        bool changed = false;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            Key key = lane < count ? keys![lane] : Key.None;
            changed |= Keys[lane] != key;
            Keys[lane] = key;
        }
        if (!changed)
            return;

        HasMouseBindings = false;
        hasSharedBindings = false;
        for (int lane = 0; lane < LaneCount; lane++)
        {
            Key key = Keys[lane];
            HasMouseBindings |= MouseKeyCodec.TryDecode(key, out _);
            FirstLanes[lane] = lane;
            if (key == Key.None)
                continue;
            for (int first = 0; first < lane; first++)
            {
                if (Keys[first] != key)
                    continue;
                FirstLanes[lane] = first;
                hasSharedBindings = true;
                break;
            }
        }
    }

    /// <summary>在本次已刷新的托管快照中查询鼠标合成键是否绑定六轨。</summary>
    /// <param name="key">MouseN 对应的保留 Key 值。</param>
    /// <returns>至少一条轨道使用该键时为真。</returns>
    internal static bool IsLaneBinding(Key key) => Array.IndexOf(Keys, key) >= 0;

    /// <summary>只写入正在等待绑定的轨道，允许其他轨道保留相同键值。</summary>
    /// <param name="container">持有临时键位和等待槽位的原版设置容器。</param>
    /// <param name="key">键盘按键或 MouseN 对应的保留键值。</param>
    /// <returns>是否已完成绑定；未接管时由原版继续处理。</returns>
    internal static bool TryStoreBinding(LaneKeyBindSettingsContainer container, Key key)
    {
        // Escape 和方向键继续遵守原版 _HE 的限制及弹窗取消行为。
        if (disabled || key is Key.Escape or Key.LeftArrow or Key.RightArrow or Key.UpArrow or Key.DownArrow)
            return false;

        int lane = container._Hh;
        var keys = container._hh;
        if (keys == null || (uint)lane >= (uint)Math.Min(LaneCount, keys.Length))
            return false;

        keys[lane] = key;
        container._Hh = -1;
        container._GE();
        return true;
    }

    /// <summary>在判定读取输入前，把每组同键轨道的按下、保持和事件时间同步到整组。</summary>
    /// <param name="controller">当前原版游玩判定控制器。</param>
    internal static void SynchronizeInput(_VD controller)
    {
        // _Oz 仅在 _afA 模式执行实际判定；其他模式的自动输入保持原版行为。
        if (disabled || _VD._a != _VD._WD._afA)
            return;

        RefreshBindings();
        if (!hasSharedBindings)
            return;
        var states = controller._pEA?._BEA;
        if (states == null)
            return;

        int count = Math.Min(LaneCount, states.Length);
        for (int lane = 1; lane < count; lane++)
        {
            int first = FirstLanes[lane];
            if (first == lane)
                continue;
            _zD source = states[first];
            _zD target = states[lane];
            target._lfA = source._lfA;
            target._LfA = source._LfA;
            target._nfA = source._nfA;
            // _mfA 和 _MfA 由判定按轨道维护，不能随物理按键状态覆盖。
            states[lane] = target;
        }
    }

    /// <summary>补齐原版仅更新一条轨道的按键提示，让同键轨道一起点亮和熄灭。</summary>
    /// <param name="track">接收原版轨道按键状态的轨道显示容器。</param>
    /// <param name="lane">原版已经更新的零起始轨道编号。</param>
    /// <param name="pressed">按下为真，松开为假。</param>
    internal static void UpdateSharedIndicators(Track track, int lane, bool pressed)
    {
        if (disabled || (uint)lane >= LaneCount)
            return;
        RefreshBindings();
        if (!hasSharedBindings || Keys[lane] == Key.None)
            return;
        var indicators = track.keyPressedIndicators;
        if (indicators == null)
            return;
        int count = Math.Min(LaneCount, indicators.Length);
        for (int other = 0; other < count; other++)
            if (other != lane && Keys[other] == Keys[lane])
                indicators[other]?._Wl(pressed);
    }

    /// <summary>记录一次不兼容异常，并停止本运行层的后续设置和输入扩展。</summary>
    /// <param name="exception">导致多轨道扩展无法安全继续的异常。</param>
    internal static void Disable(Exception exception)
    {
        if (disabled)
            return;
        disabled = true;
        Plugin.Logger.LogError($"同键多轨道绑定已停用：{exception}");
    }
}
