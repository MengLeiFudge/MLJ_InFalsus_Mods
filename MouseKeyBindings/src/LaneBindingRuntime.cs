using Il2CppInterop.Runtime.InteropTypes.Arrays;
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

    /// <summary>发生接口不兼容异常后停止多轨道扩展，避免在输入回调中反复抛错。</summary>
    private static bool disabled;

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

        var keys = _YD._JfA;
        if (keys == null)
            return;

        int count = Math.Min(LaneCount, keys.Length);
        Il2CppStructArray<_zD>? states = null;
        for (int lane = 1; lane < count; lane++)
        {
            if (keys[lane] == Key.None)
                continue;
            for (int first = 0; first < lane; first++)
            {
                if (keys[first] != keys[lane])
                    continue;

                // 原版键盘和鼠标转发均只更新首个匹配槽位；没有重复键时无需读取输入数组。
                states ??= controller._pEA?._BEA;
                if (states == null || lane >= states.Length)
                    return;

                _zD source = states[first];
                _zD target = states[lane];
                target._lfA = source._lfA;
                target._LfA = source._LfA;
                target._nfA = source._nfA;
                // _mfA 和 _MfA 由判定按轨道维护，不能随物理按键状态覆盖。
                states[lane] = target;
                break;
            }
        }
    }

    /// <summary>补齐原版仅更新一条轨道的按键提示，让同键轨道一起点亮和熄灭。</summary>
    /// <param name="track">接收原版轨道按键状态的轨道显示容器。</param>
    /// <param name="lane">原版已经更新的零起始轨道编号。</param>
    /// <param name="pressed">按下为真，松开为假。</param>
    internal static void UpdateSharedIndicators(Track track, int lane, bool pressed)
    {
        if (disabled)
            return;
        var keys = _YD._JfA;
        var indicators = track.keyPressedIndicators;
        if (keys == null || indicators == null || (uint)lane >= (uint)Math.Min(LaneCount, keys.Length))
            return;

        Key key = keys[lane];
        if (key == Key.None)
            return;
        int count = Math.Min(LaneCount, Math.Min(keys.Length, indicators.Length));
        for (int other = 0; other < count; other++)
            if (other != lane && keys[other] == key)
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
