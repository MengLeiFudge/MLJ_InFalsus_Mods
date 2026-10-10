using ifapp.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InFalsusMod.Potential;

/// <summary>成绩页的原生输入头，消耗原版事件避免快捷键或鼠标操作穿透；页面交互由 Unity GUI 处理。</summary>
public sealed class PotentialInputReceiver : InputReceiver
{
    /// <summary>绑定由 IL2CPP 创建的原生组件。</summary>
    /// <param name="pointer">Unity 组件指针。</param>
    public PotentialInputReceiver(IntPtr pointer) : base(pointer) { }

    /// <summary>原版键盘事件标记为已处理，GUI 仍接收自己的文本输入与 Escape 事件。</summary>
    public override bool _OvA(Key key, double time, ref CurrentKeyboardModifiersState modifiers) => true;

    /// <summary>原版鼠标按下不继续交给场景按钮，避免页面背后的选歌操作。</summary>
    public override bool _uwA(InputReceiverMouseButton button, Vector2 screenPosition, Vector2 normalizedPosition,
        double time, ref Ray ray) => true;

    /// <summary>滚轮只用于 GUI 成绩列表，不滚动原版的歌曲列表。</summary>
    public override bool _pvA(Vector2 delta, ref CurrentKeyboardModifiersState modifiers) => true;
}
