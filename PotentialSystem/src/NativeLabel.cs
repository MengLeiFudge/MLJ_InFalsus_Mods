using FastText.Data;
using ifapp.Game.UI.Common;
using Str;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>拥有一个原生文字副本，保留 FastText 的材质与字形缓存生命周期。</summary>
internal sealed class NativeLabel : IDisposable
{
    /// <summary>自有的原生字体及布局组件。</summary>
    internal Text Text { get; }
    /// <summary>已提交的内容、颜色及语言，避免重复生成网格。</summary>
    private string? value;
    private Color color;
    private Strings.Localization language;

    /// <summary>复制原版字体，重新绑定副本的渲染引用，并按父容器定位。</summary>
    internal NativeLabel(Text template, Transform parent, string name, Vector2 anchor,
        Constrained2DAlignment alignment, Vector2 offset, float fontSize)
    {
        var root = Object.Instantiate(template.gameObject, parent, false);
        root.name = "InFalsus.PotentialSystem." + name;
        try
        {
            Text = root.GetComponent<Text>() ?? throw new InvalidOperationException("文字模板缺少 FastText。");
            var constrained = root.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("文字模板缺少 Constrained2D。");
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            Text.meshRenderer = root.GetComponent<MeshRenderer>();
            Text.meshFilter = root.GetComponent<MeshFilter>();
            // 渲染及 OnDestroy 都依赖原生初始化的缓存；不清空 charBuffer 或材质数组。
            var layout = constrained.constrained2DTransform;
            layout.transform = root.transform;
            layout.fastText = Text;
            layout.meshRenderer = Text.meshRenderer;
            layout.meshFilter = Text.meshFilter;
            layout.anchor = anchor;
            layout.parentAlignment = alignment;
            layout.offset = offset;
            layout.zPosition = -0.25f;
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            constrained.constrained2DTransform = layout;
            Text.SetFontSize(fontSize, Text.UpdateType.DontUpdateMesh);
            Text.SetAlignment(AlignmentOptions.Center, Text.UpdateType.DontUpdateMesh);
            root.SetActive(true);
            constrained._rQA();
            constrained._PQA(0x3f);
        }
        catch { Object.Destroy(root); throw; }
    }

    /// <summary>取得有效的原生语言；游戏未选择语言时使用系统映射。</summary>
    internal static Strings.Localization CurrentLanguage()
    {
        var current = Strings.GetCurrentLocalization();
        return current == Strings.Localization.Unset ? Strings.GetLocalizationForSystemLanguage() : current;
    }

    /// <summary>只在文本、颜色或语言改变时刷新字形；空字符串用于隐藏未游玩的排名。</summary>
    internal void Set(string text, Color tint)
    {
        if (Text == null)
            return;
        var current = CurrentLanguage();
        if (value == text && color == tint && language == current)
            return;
        Text.SetTextNonLocalized(text, Text.UpdateType.DontUpdateMesh);
        // 字符串入口会选择日文；必须在调用后恢复当前语言，而不能在调用前设置。
        Text.targetLocalization = current;
        Text.SetTextColor(tint, Text.UpdateType.DontUpdateMesh);
        Text.dirtyFlags |= Text.DirtyFlags.FontChanged | Text.DirtyFlags.TextChanged;
        Text.EnsureDirtyFlagsProcessed();
        Text.GetComponent<Constrained2D>()._PQA(0x3f);
        value = text;
        color = tint;
        language = current;
    }

    /// <summary>仅销毁本对象的文字副本。</summary>
    public void Dispose()
    {
        if (Text != null)
            Object.Destroy(Text.gameObject);
    }
}
