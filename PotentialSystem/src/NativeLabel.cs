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

    /// <summary>复制原生文字并重新绑定渲染引用，使用模组的完整简中字形。</summary>
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
            PotentialFonts.Apply(Text);
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
            layout.sizeInParentNormalized = Vector2.zero;
            layout.overrideMaterialRenderQueueOffset = 0;
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
    internal bool Set(string text, Color tint)
    {
        if (Text == null)
            return false;
        var current = CurrentLanguage();
        if (value == text && color == tint && language == current)
            return false;
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
        return true;
    }
    /// <summary>描边开关和颜色由原生布局回写；同时配置布局属性，避免下次刷新清除描边。</summary>
    internal void SetOutline(Color tint, float size)
    {
        var constrained = Text.GetComponent<Constrained2D>();
        var layout = constrained.constrained2DTransform;
        var extra = layout.typeSpecificExtraScale;
        extra.y = 1f;
        layout.typeSpecificExtraScale = extra;
        layout.typeSpecificTilingRepeatValue = new Vector4(tint.r, tint.g, tint.b, tint.a);
        constrained.constrained2DTransform = layout;
        Text.SetStrokeEnable(true, Text.UpdateType.DontUpdateMesh);
        Text.SetStrokeColor(tint, Text.UpdateType.DontUpdateMesh);
        Text.SetStrokeSize(size, Text.UpdateType.DontUpdateMesh);
        constrained._PQA(0x3f);
    }

    /// <summary>单行文字在布局坐标中的完整边界，保留字形中心相对文字原点的偏移。</summary>
    /// <param name="fontSize">FastText 字号。</param>
    /// <returns>相对文字原点的边界；字形未就绪时为空。</returns>
    internal Rect Measure(float fontSize)
    {
        Text.presention = (Text.presention & ~Text.FastTextPresentation.AutoLineBreak) | Text.FastTextPresentation.IgnoreLinebreak;
        Text.SetFontSize(fontSize, Text.UpdateType.DontUpdateMesh);
        Text.EnsureDirtyFlagsProcessed();
        var size = Text.GetComponent<Constrained2D>()._mrA();
        var bounds = Text.Bounds;
        if (bounds.size.x <= 0f || bounds.size.y <= 0f) return default;
        return new Rect(bounds.min.x * size.x / bounds.size.x, bounds.min.y * size.y / bounds.size.y, size.x, size.y);
    }

    /// <summary>将完整文字边界中的指定位置对齐到布局坐标，不将文字原点误当成字形中心。</summary>
    /// <param name="position">相对原生 parentAlignment 的目标坐标。</param>
    /// <param name="pivot">边界内的归一化位置；(0.5,0.5) 为中心，(0,0) 为左下。</param>
    /// <param name="fontSize">FastText 字号。</param>
    internal void PlaceBounds(Vector2 position, Vector2 pivot, float fontSize)
    {
        var bounds = Measure(fontSize);
        var constrained = Text.GetComponent<Constrained2D>();
        var origin = position - bounds.position - Vector2.Scale(bounds.size, pivot);
        BrowserWidgets.Place(constrained, origin, constrained.constrained2DTransform.sizeInPixels);
    }

    /// <summary>仅销毁本对象的文字副本。</summary>
    public void Dispose()
    {
        if (Text != null)
            Object.Destroy(Text.gameObject);
    }
}
