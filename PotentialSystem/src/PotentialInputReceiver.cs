using Il2CppInterop.Runtime.Attributes;
using ifapp.Game.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace InFalsusMod.Potential;

/// <summary>原生成绩页的输入头；控件点击交给 UIManager，键盘、文字与滚轮只交给本页。</summary>
public sealed class PotentialInputReceiver : InputReceiver
{
    /// <summary>托管会话只在本页存活期间绑定，不进入 IL2CPP 字段布局。</summary>
    private PotentialBrowser? browser;

    /// <summary>绑定由 IL2CPP 创建的原生组件。</summary>
    /// <param name="pointer">Unity 组件指针。</param>
    public PotentialInputReceiver(IntPtr pointer) : base(pointer) { }

    [HideFromIl2Cpp]
    internal void Bind(PotentialBrowser session) => browser = session;
    [HideFromIl2Cpp]
    internal void Unbind() => browser = null;

    /// <summary>登记和释放输入时始终声明文字监听；搜索焦点由 Text 回调过滤，避免原生监听计数失配。</summary>
    public override bool ReceiveTextEvents => true;

    /// <summary>始终消耗选歌快捷键；页面自己处理 Escape 和搜索编辑。</summary>
    public override bool _OvA(Key key, double time, ref CurrentKeyboardModifiersState modifiers)
    {
        browser?.Key(key, modifiers.IsCtrlPressed, Keyboard.current?.shiftKey.isPressed == true);
        return true;
    }

    /// <summary>UIManager 属于始终接收输入的原生层，必须允许它继续处理本页按钮和拖动。</summary>
    public override bool _uwA(InputReceiverMouseButton button, Vector2 screenPosition, Vector2 normalizedPosition,
        double time, ref Ray ray) => false;

    /// <summary>文本只写入当前页面的搜索模型。</summary>
    public override void OnText(char character) => browser?.Text(character);

    /// <summary>滚轮由本页消耗，实际滚动使用原生 UIScrollView。</summary>
    public override bool _pvA(Vector2 delta, ref CurrentKeyboardModifiersState modifiers)
    {
        browser?.Wheel(delta);
        return true;
    }
}
