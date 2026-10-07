using Il2CppInterop.Runtime;
using Str;
using ifapp.Game.Scenes;
using ifapp.Game.UI.Common;
using _m;

namespace InFalsusMod;

/// <summary>通过当前场景已完整绑定的原版 Modal 显示模组文本，并持有托管回调对应的 IL2CPP 委托。</summary>
internal static class NativeModal
{
    /// <summary>显示一个带原版确定按钮的消息窗口。</summary>
    /// <param name="title">窗口标题的直接文本。</param>
    /// <param name="body">窗口正文的直接文本。</param>
    /// <param name="session">成功时返回需要由调用方持有到窗口关闭的会话。</param>
    /// <param name="closed">点击确定按钮关闭后执行的可选动作。</param>
    /// <returns>原版 Modal 存在且当前未被其他流程占用时为 <see langword="true"/>。</returns>
    internal static bool TryShowMessage(string title, string body, out NativeModalSession? session, System.Action? closed = null)
    {
        session = null;
        var modal = GetAvailable();
        if (modal == null)
            return false;

        var action = Convert(new System.Action<UiModal>(current =>
        {
            current._VtA();
            closed?.Invoke();
        }));
        try
        {
            modal._TtA(Direct(title), Direct(body), _yi._bIb, Strings.Key.Modal_ButtonTextOk, action, true);
        }
        catch (Exception openException)
        {
            try { if (modal._A) modal._VtA(); }
            catch (Exception closeException) { openException.Data["ModalCleanup"] = closeException.ToString(); }
            throw;
        }
        session = new NativeModalSession(modal, action, null);
        return true;
    }

    /// <summary>显示使用原版取消与确认按钮的双按钮窗口。</summary>
    /// <param name="title">窗口标题的直接文本。</param>
    /// <param name="body">窗口正文的直接文本。</param>
    /// <param name="cancelled">点击左侧取消按钮并关闭后执行的动作。</param>
    /// <param name="confirmed">点击右侧确认按钮并关闭后执行的动作。</param>
    /// <param name="session">成功时返回需要由调用方持有到窗口关闭的会话。</param>
    /// <returns>原版 Modal 存在且当前未被其他流程占用时为 <see langword="true"/>。</returns>
    internal static bool TryShowConfirmation(string title, string body, System.Action cancelled, System.Action confirmed,
        out NativeModalSession? session)
    {
        session = null;
        var modal = GetAvailable();
        if (modal == null)
            return false;

        var cancelAction = Convert(new System.Action<UiModal>(current =>
        {
            current._VtA();
            cancelled();
        }));
        var confirmAction = Convert(new System.Action<UiModal>(current =>
        {
            current._VtA();
            confirmed();
        }));
        try
        {
            modal._vtA(Direct(title), Direct(body), _yi._bIb, _yi._bIb,
                Strings.Key.Cancel, Strings.Key.Modal_ButtonTextConfirmUpper, cancelAction, confirmAction, true);
        }
        catch (Exception openException)
        {
            try { if (modal._A) modal._VtA(); }
            catch (Exception closeException) { openException.Data["ModalCleanup"] = closeException.ToString(); }
            throw;
        }
        session = new NativeModalSession(modal, cancelAction, confirmAction);
        return true;
    }

    /// <summary>优先使用 UiModal 在 Awake 登记的单例，并以 CoreScene 序列化引用作为后备。</summary>
    /// <returns>当前未显示内容的原版实例；不存在或正被游戏占用时为空。</returns>
    private static UiModal? GetAvailable()
    {
        var modal = UiModal._BJb;
        if (modal == null)
        {
            var core = CoreScene._Xl ?? CoreScene._c;
            modal = core?.UiModal;
        }
        return modal != null && !modal._A ? modal : null;
    }

    /// <summary>把模组已完成本地化的字符串包装为原版文本参数。</summary>
    /// <param name="text">不再经过游戏字符串表查找的最终文本。</param>
    /// <returns>直接字符串布局的原版参数。</returns>
    private static TextParameters Direct(string text) => TextParameters.CreateDirectString(text);

    /// <summary>建立可由 IL2CPP 原生按钮调用的委托；会话负责延长其生命周期。</summary>
    /// <param name="action">签名与原版 Modal 回调一致的托管动作。</param>
    /// <returns>原生可调用的委托包装。</returns>
    private static Il2CppSystem.Action<UiModal> Convert(System.Action<UiModal> action) =>
        DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UiModal>>(action)
        ?? throw new InvalidOperationException("无法建立原版 Modal 回调。");
}

/// <summary>表示一次由模组占用的原版 Modal；只在窗口存续期间持有原生回调。</summary>
internal sealed class NativeModalSession
{
    /// <summary>左侧或单按钮回调的强引用。</summary>
    private readonly Il2CppSystem.Action<UiModal> primaryAction;
    /// <summary>双按钮窗口右侧回调的强引用；单按钮窗口为空。</summary>
    private readonly Il2CppSystem.Action<UiModal>? secondaryAction;

    /// <summary>当前会话使用的原版实例，供同一窗口内附加原版控件。</summary>
    internal UiModal Modal { get; }

    /// <summary>检查原版对象时发生的异常；拥有者在释放会话时负责写入对应模组日志。</summary>
    internal Exception? Failure { get; private set; }

    /// <summary>原版对象仍处于显示状态时为真；对象失效按已经关闭处理并保留异常。</summary>
    internal bool IsOpen
    {
        get
        {
            try
            {
                bool open = Modal != null && Modal._A;
                GC.KeepAlive(primaryAction);
                GC.KeepAlive(secondaryAction);
                return open;
            }
            catch (Exception ex)
            {
                Failure ??= ex;
                return false;
            }
        }
    }

    /// <summary>保存实例及回调，防止原生按钮尚可触发时托管委托被回收。</summary>
    /// <param name="modal">本次占用的场景 Modal。</param>
    /// <param name="primaryAction">单按钮或左侧按钮回调。</param>
    /// <param name="secondaryAction">可选的右侧按钮回调。</param>
    internal NativeModalSession(UiModal modal, Il2CppSystem.Action<UiModal> primaryAction,
        Il2CppSystem.Action<UiModal>? secondaryAction)
    {
        Modal = modal;
        this.primaryAction = primaryAction;
        this.secondaryAction = secondaryAction;
    }

    /// <summary>在窗口保持打开时替换正文；用于更新智能融合的供体限制。</summary>
    /// <param name="body">当前语言的完整正文。</param>
    internal void UpdateBody(string body)
    {
        if (IsOpen)
            Modal._ZtA(TextParameters.CreateDirectString(body));
    }

    /// <summary>通过原版关闭路径结束当前窗口；关闭失败保存在 <see cref="Failure"/>。</summary>
    internal void Close()
    {
        try
        {
            if (IsOpen)
                Modal._VtA();
        }
        catch (Exception ex)
        {
            Failure ??= ex;
        }
    }
}
