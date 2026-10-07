using FastText.Data;
using ifapp.Game.Scenes.Recipe;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod;

/// <summary>处理技能筛选专用的原生布局和文字绑定；筛选状态及借用生命周期由面板管理。</summary>
internal static class SkillFilterVisuals
{
    /// <summary>按原版背景的真实矩形计算根节点偏移，使完整浮窗以父界面中心为基准显示。</summary>
    /// <param name="layer">已初始化的原版配方筛选浮窗。</param>
    /// <returns>传给原版打开函数的布局像素偏移，正 X 向右、正 Y 向上。</returns>
    internal static Vector2 CenterOffset(RecipeFilterLayer layer)
    {
        var backing = layer.backing.constrained2DTransform;
        // 展开动画会改变 renderableScale；定位使用完整设计尺寸，避免关闭态的零缩放影响中心。
        var center = backing.offset + Vector2.Scale(new Vector2(0.5f, 0.5f) - backing.anchor,
            backing.sizeInPixels);
        return -center;
    }

    /// <summary>把复制按钮放到原版内容左上角容器，保持按钮内部图形与碰撞体的原始尺寸。</summary>
    /// <param name="button">模组持有的按钮副本。</param>
    /// <param name="parent">原版 ContentContainerTopLeft。</param>
    /// <param name="offset">从容器原点开始的像素位置，正 X 向右、负 Y 向下。</param>
    /// <param name="activate">图标复制完成前可保持宿主隐藏。</param>
    internal static void Place(UIButton button, Transform parent, Vector2 offset, bool activate = true)
    {
        button.transform.SetParent(parent, false);
        var layout = button.constrained2D.constrained2DTransform;
        layout.anchor = new Vector2(0.5f, 0.5f);
        layout.parentAlignment = Constrained2DAlignment.Center;
        layout.offset = offset;
        layout.zPosition = -0.05f;
        button.constrained2D.constrained2DTransform = layout;
        button.gameObject.SetActive(activate);
        button.constrained2D._rQA();
        button.constrained2D._PQA(0x3f);
    }

    /// <summary>使用模板字体和当前语言更新文字；入口保留原版图文错开的布局。</summary>
    /// <param name="button">模组按钮。</param>
    /// <param name="template">未挂模组回调的原版字体来源。</param>
    /// <param name="text">当前语言的完整文案。</param>
    /// <param name="centered">纯文字匹配按钮需要居中；带左侧图案的入口保持模板对齐。</param>
    internal static void UpdateButtonText(UIButton button, UIButton template, string text, bool centered = false)
    {
        NativeButtons.UpdateText(button, template, text);
        foreach (var label in button.GetComponentsInChildren<Text>(true))
        {
            var constrained = label.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("原版按钮文字缺少布局组件。");
            var layout = constrained.constrained2DTransform;
            if (centered)
            {
                layout.anchor = new Vector2(0.5f, 0.5f);
                layout.parentAlignment = Constrained2DAlignment.Center;
                layout.offset = new Vector2(0f, -8f);
                label.SetAlignment(AlignmentOptions.Center, Text.UpdateType.DontUpdateMesh);
            }
            layout.fastText = label;
            layout.meshRenderer = label.GetComponent<MeshRenderer>();
            constrained.constrained2DTransform = layout;
            RefreshTextMesh(label);
            constrained._PQA(0x3f);
        }
    }

    /// <summary>入口文字改用原版“全部”状态文字的布局，使入口与左侧特性筛选按钮的文字高度一致。</summary>
    /// <param name="button">只显示 TraitsOnly 图文层的入口按钮；其隐藏的 AllPresent 层保留原版布局。</param>
    internal static void AlignEntryLabel(UIButton button)
    {
        var reference = button.transform.Find("AllPresent")?.GetComponentInChildren<Text>(true)?.GetComponent<Constrained2D>()
            ?? throw new InvalidOperationException("原版特性入口缺少“全部”文字布局。");
        var content = button.transform.Find("TraitsOnly")
            ?? throw new InvalidOperationException("原版特性入口缺少有特性图文层。");
        var target = reference.constrained2DTransform;
        foreach (var label in content.GetComponentsInChildren<Text>(true))
        {
            var constrained = label.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("原版按钮文字缺少布局组件。");
            var layout = constrained.constrained2DTransform;
            layout.anchor = target.anchor;
            layout.parentAlignment = target.parentAlignment;
            layout.offset = target.offset;
            constrained.constrained2DTransform = layout;
            constrained._PQA(0x3f);
        }
    }

    /// <summary>复制原版文字并重绑布局与网格；副本由面板或宿主按钮负责销毁。</summary>
    /// <param name="template">具有原版字体和颜色的文字。</param>
    /// <param name="parent">原版内容容器或技能按钮。</param>
    /// <param name="name">副本的稳定名称后缀。</param>
    /// <param name="text">当前语言的完整文案。</param>
    /// <param name="offset">相对父容器原点的布局像素位置。</param>
    /// <returns>已显示的文字副本。</returns>
    internal static Text CreateLabel(Text template, Transform parent, string name, string text, Vector2 offset)
    {
        var root = Object.Instantiate(template.gameObject, parent, false);
        root.name = "InFalsus.SkillSelection." + name;
        try
        {
            var label = root.GetComponent<Text>();
            var constrained = root.GetComponent<Constrained2D>();
            if (label == null || constrained == null)
                throw new InvalidOperationException("原版分区标题缺少文字或布局。");
            var layout = constrained.constrained2DTransform;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Center;
            layout.offset = offset;
            layout.zPosition = -0.08f;
            layout.fastText = label;
            layout.meshRenderer = root.GetComponent<MeshRenderer>();
            constrained.constrained2DTransform = layout;
            root.SetActive(true);
            SetText(label, text);
            constrained._rQA();
            constrained._PQA(0x3f);
            return label;
        }
        catch
        {
            Object.Destroy(root);
            throw;
        }
    }

    /// <summary>在完整特性图标下方常显名称；扩大字体并为长译名保留两行空间。</summary>
    /// <param name="template">原版特性分区的文字模板。</param>
    /// <param name="host">拥有文字生命周期的技能按钮。</param>
    /// <param name="index">从零开始的技能族索引。</param>
    /// <param name="text">游戏当前语言的技能族名称。</param>
    /// <returns>与图标一同显示的名称。</returns>
    internal static Text CreateTraitName(Text template, UIButton host, int index, string text)
    {
        var label = CreateLabel(template, host.transform, $"TraitName{index + 1:00}", text,
            new Vector2(0f, -72f));
        var constrained = label.GetComponent<Constrained2D>();
        var layout = constrained.constrained2DTransform;
        layout.color = Color.white;
        constrained.constrained2DTransform = layout;
        label.SetFontSize(3f, Text.UpdateType.DontUpdateMesh);
        label.SetAlignment(AlignmentOptions.Top, Text.UpdateType.DontUpdateMesh);
        label.SetTextColor(Color.white, Text.UpdateType.DontUpdateMesh);
        label.presention = (label.presention | Text.FastTextPresentation.AutoLineBreak)
            & ~Text.FastTextPresentation.IgnoreLinebreak;
        label.SetMaxAutoLineBreakPixelWidth(280, Text.UpdateType.DontUpdateMesh);
        RefreshTextMesh(label);
        constrained._PQA(0x3f);
        return label;
    }

    /// <summary>经原版字符串入口设置模组文字，避免 by-ref TextParameters 的互操作问题。</summary>
    /// <param name="label">模组拥有的标题或按钮文字。</param>
    /// <param name="text">已经翻译的文案。</param>
    internal static void SetText(Text label, string text)
    {
        label.SetTextNonLocalized(text, Text.UpdateType.DontUpdateMesh);
        RefreshTextMesh(label);
    }

    /// <summary>重建当前游戏语言的字形网格，不使用隐藏模板尚未初始化的语言缓存。</summary>
    /// <param name="label">需要刷新语言和渲染绑定的文字。</param>
    internal static void RefreshTextMesh(Text label)
    {
        label.targetLocalization = ModText.Language;
        label.meshRenderer = label.GetComponent<MeshRenderer>();
        label.meshFilter = label.GetComponent<MeshFilter>();
        label.dirtyFlags |= Text.DirtyFlags.FontChanged | Text.DirtyFlags.TextChanged;
        label.EnsureDirtyFlagsProcessed();
    }

    /// <summary>准备只负责展示的资源副本，统一渲染层并断开来源按钮输入和文字网格缓存。</summary>
    /// <param name="root">完整原版图标或特性说明组件的副本。</param>
    /// <param name="layer">宿主筛选浮窗使用的 Unity 渲染层。</param>
    internal static void PrepareDisplayClone(GameObject root, int layer)
    {
        foreach (var child in root.GetComponentsInChildren<Transform>(true))
            child.gameObject.layer = layer;
        foreach (var button in root.GetComponentsInChildren<UIButton>(true))
        {
            button._Gjb = null;
            button._hjb = null;
            button._Hjb = null;
            button._ijb = null;
            button._Ijb = null;
            button.UserComponent = null;
            button.enabled = false;
        }
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (var label in root.GetComponentsInChildren<Text>(true))
        {
            var constrained = label.GetComponent<Constrained2D>();
            if (constrained == null)
                throw new InvalidOperationException("原版特性说明文字缺少布局组件。");
            var layout = constrained.constrained2DTransform;
            layout.fastText = label;
            layout.meshRenderer = label.GetComponent<MeshRenderer>();
            constrained.constrained2DTransform = layout;
            RefreshTextMesh(label);
        }
    }

    /// <summary>缩小技能按钮的方形状态框和碰撞体，名称仍使用独立字号。</summary>
    /// <param name="button">尚未添加图标和名称的等级按钮副本。</param>
    internal static void ResizeTraitButton(UIButton button)
    {
        for (int i = 0; i < button.transform.childCount; i++)
        {
            var child = button.transform.GetChild(i);
            if (child.name.StartsWith("icon-tier-", StringComparison.Ordinal))
            {
                child.gameObject.SetActive(false);
                continue;
            }
            var constrained = child.GetComponent<Constrained2D>();
            if (constrained == null)
                continue;
            var layout = constrained.constrained2DTransform;
            layout.sizeInPixels = new Vector2(128f, 128f);
            constrained.constrained2DTransform = layout;
            constrained._PQA(0x3f);
        }
        var collider = button.ButtonCollider.TryCast<BoxCollider>()
            ?? throw new InvalidOperationException("原版特性筛选按钮的碰撞体类型已改变。");
        var size = collider.size;
        collider.size = new Vector3(size.x * 128f / 180f, size.y * 128f / 180f, size.z);
    }

    /// <summary>呈现完整的条件外框和效果图像，清除来源容器的缩放、偏移和运行时淡化。</summary>
    /// <param name="icon">方形按钮或悬浮说明中的独立 TraitIcon 副本。</param>
    /// <param name="frameSize">六边形外框边长，单位为原版布局像素。</param>
    internal static void CenterTraitIcon(TraitIcon icon, float frameSize = 120f)
    {
        var layout = icon.self.constrained2DTransform;
        layout.anchor = new Vector2(0.5f, 0.5f);
        layout.parentAlignment = Constrained2DAlignment.Center;
        layout.offset = Vector2.zero;
        layout.zPosition = -0.05f;
        layout.renderableScale = Vector2.one;
        layout.color = Color.white;
        icon.self.constrained2DTransform = layout;
        icon.self._PQA(0x3f);

        // 原版完整图标以 600 像素外框搭配 400 像素中心图像，保持这个比例才能看到触发标记。
        foreach (var (part, size, z) in new[] { (icon.frame, frameSize, 0f), (icon.icon, frameSize * 2f / 3f, -0.01f) })
        {
            var image = part.constrained2DTransform;
            image.anchor = new Vector2(0.5f, 0.5f);
            image.parentAlignment = Constrained2DAlignment.Center;
            image.offset = Vector2.zero;
            image.zPosition = z;
            image.sizeInPixels = new Vector2(size, size);
            image.renderableScale = Vector2.one;
            image.color = Color.white;
            image.spriteBlendEffect = default;
            part.constrained2DTransform = image;
            part._PQA(0x3f);
        }
    }
}
