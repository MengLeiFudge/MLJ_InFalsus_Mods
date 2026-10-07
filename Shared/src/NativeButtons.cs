using ifapp.Game.Input;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod;

/// <summary>为模组界面复制原版按钮外观，并登记游戏输入归属。</summary>
internal static class NativeButtons
{
    /// <summary>沿用模板字体及样式，并用原版非本地化入口设置已经翻译完成的模组文字。</summary>
    /// <param name="button">已复制的模组按钮。</param>
    /// <param name="template">当前语言下的原生样式来源。</param>
    /// <param name="text">模组本地化后的文字。</param>
    internal static void UpdateText(UIButton button, UIButton template, string text)
    {
        var labels = button.GetComponentsInChildren<FastText.FastText>(true);
        var originals = template.GetComponentsInChildren<FastText.FastText>(true);
        if (labels.Length != originals.Length)
            throw new InvalidOperationException("原版按钮文字层级已改变。");
        for (int i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            var original = originals[i];
            label.style = original.style;
            label.SetFontFamilyAsset(original.FontFamilyAsset);
            label.SetFontSize(original.FontSize, FastText.FastText.UpdateType.DontUpdateMesh);
            // 当前 interop 的 by-ref TextParameters 包装会把值类型对象地址当作结构体数据；
            // 字符串重载由原生侧创建 DirectString，可在按钮状态切换后继续正确刷新。
            label.SetTextNonLocalized(text, FastText.FastText.UpdateType.Default);
            label.targetLocalization = original.targetLocalization;
            // 字符串重载首次固定按日文目标生成网格；恢复模板语言后必须立即重建，
            // 否则首次显示和下一次 OnEnable 的字形度量不同。
            label.dirtyFlags |= FastText.FastText.DirtyFlags.FontChanged
                | FastText.FastText.DirtyFlags.TextChanged;
            label.EnsureDirtyFlagsProcessed();
        }
    }

    /// <summary>建立独立按钮；返回时保持隐藏，由所属界面控制可见性。</summary>
    /// <param name="template">提供布局、碰撞体及动画的原生按钮。</param>
    /// <param name="owner">负责该按钮输入权限的游戏界面。</param>
    /// <param name="name">模组对象的稳定名称后缀。</param>
    /// <param name="text">按钮文字，支持原生换行。</param>
    /// <param name="clicked">完成点击时执行的回调。</param>
    /// <returns>已登记输入归属的原生按钮。</returns>
    internal static UIButton Create(UIButton template, InputReceiver owner, string name, string text, Action<UIButton> clicked)
    {
        var manager = UIManager._A;
        if (manager == null || template.constrained2D == null)
            throw new InvalidOperationException("原生按钮输入或布局尚未就绪。");
        var root = Object.Instantiate(template.gameObject, template.transform.parent, false);
        root.name = "InFalsus." + name;
        root.SetActive(false);
        try
        {
            var button = root.GetComponent<UIButton>();
            var labels = root.GetComponentsInChildren<FastText.FastText>(true);
            if (button == null || button.constrained2D == null || button.ButtonCollider == null || labels.Length == 0)
                throw new InvalidOperationException("原生按钮缺少布局、碰撞体或文字组件。");
            button._Gjb = null;
            button._hjb = null;
            button._Hjb = null;
            button._ijb = null;
            button._Ijb = null;
            button.UserComponent = null;
            button._DSA(clicked);
            button._dSA(_m._dI._YIb);
            UpdateText(button, template, text);
            // OnEnable只更新外观；原生界面还需登记碰撞体和InputReceiver，才能收到悬停/点击。
            manager._mtA(button, owner);
            return button;
        }
        catch
        {
            // UIButton.OnDestroy同时注销已登记的输入信息。
            Object.Destroy(root);
            throw;
        }
    }

    /// <summary>把克隆按钮放入指定原版容器，并以容器中心为基准设置像素位置与尺寸。</summary>
    /// <param name="button">待定位的克隆按钮。</param>
    /// <param name="parent">拥有原版布局与渲染层级的父节点。</param>
    /// <param name="offset">相对父容器中心的像素偏移，正 X 向右、正 Y 向上。</param>
    /// <param name="size">按钮的像素宽高。</param>
    internal static void Place(UIButton button, Transform parent, Vector2 offset, Vector2 size)
    {
        button.transform.SetParent(parent, false);
        var layout = button.constrained2D.constrained2DTransform;
        layout.anchor = new Vector2(0.5f, 0.5f);
        layout.offset = offset;
        layout.sizeInPixels = size;
        layout.parentAlignment = Constrained2DAlignment.Center;
        button.constrained2D.constrained2DTransform = layout;
        foreach (var label in button.GetComponentsInChildren<FastText.FastText>(true))
        {
            label.presention = (label.presention | FastText.FastText.FastTextPresentation.AutoLineBreak)
                & ~FastText.FastText.FastTextPresentation.IgnoreLinebreak;
            label.SetMaxAutoLineBreakPixelWidth((int)Math.Max(24f, size.x - 24f),
                FastText.FastText.UpdateType.DontUpdateMesh);
            label.dirtyFlags |= FastText.FastText.DirtyFlags.TextChanged;
            label.EnsureDirtyFlagsProcessed();
        }
        button.gameObject.SetActive(true);
        button.constrained2D._rQA();
        button.constrained2D._PQA(0x3f);
    }

    /// <summary>切换原版按钮的可用与选中外观。</summary>
    /// <param name="button">目标原版按钮。</param>
    /// <param name="enabled">是否允许点击。</param>
    /// <param name="selected">可用时是否使用原版选中状态。</param>
    internal static void SetState(UIButton button, bool enabled, bool selected = false)
    {
        button._dSA(!enabled ? _m._dI._yIb : selected ? _m._dI._zIb : _m._dI._YIb);
    }
}
