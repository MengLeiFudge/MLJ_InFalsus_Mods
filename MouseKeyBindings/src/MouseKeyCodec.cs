using UnityEngine.InputSystem;

namespace InFalsusMod;

/// <summary>在原版六轨 <see cref="Key"/> 数组中编码和识别鼠标按钮。</summary>
internal static class MouseKeyCodec
{
    /// <summary>Unity <see cref="Key"/> 当前最大值为 126；该区间专供鼠标按钮持久化。</summary>
    private const int FirstMouseKey = 1000;

    /// <summary>保留值在有符号整数范围内可表达的最大 MouseN。</summary>
    internal const int MaximumMouseButton = int.MaxValue - FirstMouseKey;

    /// <summary>把鼠标按钮号转换为原版可保存但键盘不会产生的按键值。</summary>
    /// <param name="button">从零开始的鼠标按钮号。</param>
    /// <returns>写入原版六轨绑定数组的保留按键值。</returns>
    /// <exception cref="ArgumentOutOfRangeException">按钮号为负数或与保留值相加后溢出。</exception>
    internal static Key Encode(int button)
    {
        if (button < 0 || button > MaximumMouseButton)
            throw new ArgumentOutOfRangeException(nameof(button));
        return (Key)(FirstMouseKey + button);
    }

    /// <summary>尝试从原版按键值还原鼠标按钮号。</summary>
    /// <param name="key">原版六轨绑定中的按键值。</param>
    /// <param name="button">成功时为从零开始的鼠标按钮号。</param>
    /// <returns>该值是否属于本模组的鼠标保留区间。</returns>
    internal static bool TryDecode(Key key, out int button)
    {
        int value = (int)key;
        if (value < FirstMouseKey)
        {
            button = -1;
            return false;
        }
        button = value - FirstMouseKey;
        return true;
    }
}
