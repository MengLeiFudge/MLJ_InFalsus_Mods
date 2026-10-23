using FastText.Data;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.UI.Common;
using Lowiro.UI.Projection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>成绩页的原生控件工厂；拥有副本、材质、按钮登记及跨 IL2CPP 回调。</summary>
internal sealed class BrowserWidgets : IDisposable
{
    /// <summary>模板只提供字体和图形资源；输入、布局及碰撞体全部属于本页。</summary>
    private readonly SongSelectScene scene;
    private readonly InputReceiver owner;
    private readonly List<BrowserGraphic> graphics = new();
    private readonly List<UIButton> buttons = new();
    private readonly List<Il2CppSystem.Delegate> callbacks = new();
    /// <summary>保持每个原生点击或拖动区域与其已计算的显示范围一致。</summary>
    private readonly List<(Constrained2D Layout, BoxCollider Collider)> hitAreas = new();
    internal Constrained2D Root { get; }

    /// <summary>使用原生选歌区域为根节点，随游戏的投影和黑边范围变化。</summary>
    internal BrowserWidgets(SongSelectScene scene, InputReceiver owner)
    {
        this.scene = scene;
        this.owner = owner;
        Root = Container(scene.scene.transform, "Results", Vector2.zero);
        try
        {
            var layout = Root.constrained2DTransform;
            layout.sizeInParentNormalized = new Vector2(0.96f, 0.94f);
            layout.zPosition = -1f;
            layout.overrideMaterialRenderQueueOffset = 100;
            Root.constrained2DTransform = layout;
            Root._PQA(0x3f);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>页面可用范围以子控件的原生设计像素表示，随投影和继承缩放变化。</summary>
    internal Vector2 Size => LayoutSize(Root);

    /// <summary>按原生缓存分区读取已登记控件的布局快照，避免 IL2CPP 按引用返回结构体的封送问题。</summary>
    /// <param name="constrained">已完成原生布局刷新的控件。</param>
    /// <returns>当前控件的尺寸、继承缩放和投影深度；不持有原生缓存引用。</returns>
    internal static ChildAccessCalculated ReadLayout(Constrained2D constrained)
    {
        // 原生 _lQA 使用 Container 分区 _Pib，其余类型使用 _qib，索引均为控件 Id。
        // _f 的生成包装直接解释 runtime_invoke 返回地址，不能用它读取结构体字段。
        var cache = constrained.constrained2DTransform.type == Constrained2DType.Container
            ? _m._wi._Pib : _m._wi._qib;
        int id = constrained.Id;
        if (id == ushort.MaxValue || cache == null || id >= cache.Length)
            throw new InvalidOperationException($"原生 UI 控件尚未登记布局：{constrained.name} (Id={id})。");
        return cache[id];
    }

    /// <summary>将屏幕比例换算为子控件使用的设计像素；先处理父级待刷新的布局。</summary>
    /// <param name="constrained">提供可用范围及子控件继承缩放的原生容器。</param>
    /// <returns>扣除继承缩放后的宽高；无有效布局时明确报错，避免生成零尺寸控件。</returns>
    private static Vector2 LayoutSize(Constrained2D constrained)
    {
        _m._wi._ERA(false);
        var calculated = ReadLayout(constrained);
        var pixels = ProjectionValues.CameraPixelSize;
        var scale = calculated.CachedWorldScale;
        var size = new Vector2(calculated.CalculatedNormalizedSizeOfScreen.x * pixels.x / scale.x,
            calculated.CalculatedNormalizedSizeOfScreen.y * pixels.y / scale.y);
        if (!float.IsFinite(size.x) || !float.IsFinite(size.y) || size.x <= 0f || size.y <= 0f)
            throw new InvalidOperationException("原生 UI 容器的布局范围尚未就绪。");
        return size;
    }

    /// <summary>创建原生布局容器；独立碰撞体在布局完成后通过游戏投影换算尺寸。</summary>
    internal Constrained2D Container(Transform parent, string name, Vector2 size, bool hitArea = false)
    {
        var root = new GameObject("InFalsus.PotentialSystem." + name);
        root.SetActive(false);
        root.layer = scene.backButton.gameObject.layer;
        root.transform.SetParent(parent, false);
        try
        {
            BoxCollider? collider = null;
            if (hitArea)
            {
                collider = root.AddComponent<BoxCollider>();
                collider.enabled = false;
            }
            var constrained = root.AddComponent<Constrained2D>();
            var layout = constrained.constrained2DTransform;
            layout.transform = root.transform;
            layout.type = Constrained2DType.Container;
            layout.sizeInPixels = size;
            layout.sizeInParentNormalized = Vector2.zero;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Center;
            layout.offset = Vector2.zero;
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            constrained.constrained2DTransform = layout;
            root.SetActive(true);
            constrained._rQA();
            constrained._RQA();
            constrained._PQA(0x3f);
            Place(constrained, Vector2.zero, size);
            if (collider != null)
                hitAreas.Add((constrained, collider));
            return constrained;
        }
        catch { Object.Destroy(root); throw; }
    }

    /// <summary>设置原生偏移和设计像素尺寸；自有容器保持 renderableScale 为一，不缩放子控件。</summary>
    /// <param name="constrained">本模组拥有的容器、图形或文字。</param>
    /// <param name="center">相对原生 parentAlignment 的中心偏移。</param>
    /// <param name="size">设计像素宽高；零尺寸容器保留自身已有的父级比例。</param>
    internal static void Place(Constrained2D constrained, Vector2 center, Vector2 size)
    {
        var layout = constrained.constrained2DTransform;
        var normalized = layout.sizeInParentNormalized;
        if (layout.type == Constrained2DType.Container && size != Vector2.zero)
        {
            // Container 只读取父级比例，忽略 sizeInPixels；比例必须按最新父布局换算。
            var parent = constrained.transform.parent.GetComponentInParent<Constrained2D>()
                ?? throw new InvalidOperationException("原生 UI 控件缺少父布局容器。");
            var bounds = LayoutSize(parent);
            normalized = new Vector2(size.x / bounds.x, size.y / bounds.y);
        }
        if (layout.offset == center && layout.sizeInPixels == size && layout.sizeInParentNormalized == normalized)
            return;
        layout.offset = center;
        layout.sizeInPixels = size;
        layout.sizeInParentNormalized = normalized;
        constrained.constrained2DTransform = layout;
        constrained._PQA(0x3f);
    }

    /// <summary>复制游戏图形并重新绑定渲染引用，自有材质允许逐卡更新封面。</summary>
    internal BrowserGraphic Graphic(Transform parent, string name, Texture texture, Color color)
    {
        var template = scene.leftSongCardContainer.songCardPrefab.jacket;
        var root = Object.Instantiate(template.gameObject, parent, false);
        root.name = "InFalsus.PotentialSystem." + name;
        try
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var constrained = root.GetComponent<Constrained2D>();
            var layout = constrained.constrained2DTransform;
            var source = layout.renderableTargetMaterial
                ?? throw new InvalidOperationException("原生图形缺少材质。");
            layout.transform = root.transform;
            layout.meshRenderer = root.GetComponent<MeshRenderer>();
            layout.meshFilter = root.GetComponent<MeshFilter>();
            layout.type = Constrained2DType.Sprite;
            layout.sizeInParentNormalized = Vector2.zero;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Center;
            layout.renderableScale = Vector2.one;
            layout.color = color;
            layout.zPosition = -0.02f;
            layout.overrideMaterialRenderQueueOffset = 0;
            layout.spriteBlendEffect = default;
            constrained.constrained2DTransform = layout;
            var material = new Material(source) { name = root.name, hideFlags = HideFlags.HideAndDontSave, mainTexture = texture };
            constrained._irA(material);
            var graphic = new BrowserGraphic(constrained, material);
            graphics.Add(graphic);
            root.SetActive(true);
            constrained._rQA();
            constrained._PQA(0x3f);
            return graphic;
        }
        catch { Object.Destroy(root); throw; }
    }

    /// <summary>保留 FastText 字形缓存和当前语言，并限制自动换行宽度。</summary>
    internal NativeLabel Label(Transform parent, string name, float fontSize)
    {
        var label = new NativeLabel(scene.topBar.text, parent, name, new Vector2(0.5f, 0.5f),
            Constrained2DAlignment.Center, Vector2.zero, fontSize);
        label.Text.SetAlignment(AlignmentOptions.Center, FastText.FastText.UpdateType.DontUpdateMesh);
        var constrained = label.Text.GetComponent<Constrained2D>();
        var layout = constrained.constrained2DTransform;
        layout.overrideMaterialRenderQueueOffset = 0;
        layout.sizeInParentNormalized = Vector2.zero;
        constrained.constrained2DTransform = layout;
        constrained._PQA(0x3f);
        return label;
    }

    /// <summary>创建原生 UIButton，不携带模板的碰撞体、动画目标或原版点击回调。</summary>
    internal UIButton Button(Transform parent, string name, string text, Action clicked)
    {
        var constrained = Container(parent, name, new Vector2(100f, 60f), hitArea: true);
        var button = constrained.gameObject.AddComponent<UIButton>();
        buttons.Add(button);
        button.constrained2D = constrained;
        button.ButtonCollider = constrained.GetComponent<BoxCollider>();
        button.button = scene.backButton.button;
        button.sfxSetType = scene.backButton.sfxSetType;
        button.flags = UIButton.Flags.DoesConsumeTouchOnPress | UIButton.Flags.CanCauseCursorHover;
        var background = Graphic(button.transform, name + ".Backing", Texture2D.whiteTexture, PotentialPresentation.Color(0x283951));
        var label = Label(button.transform, name + ".Text", 3.4f);
        label.Set(text, Color.white);
        UIButton._EI click = (Action<UIButton>)(_ => clicked());
        UIButton._fI hover = (Action<UIButton, bool>)((_, hovered) => background.Tint(
            PotentialPresentation.Color(hovered ? 0x425b7cU : 0x283951U)));
        callbacks.Add(click);
        callbacks.Add(hover);
        button._DSA(click);
        button._eSA(hover);
        button._dSA(_m._dI._YIb);
        UIManager._A._mtA(button, owner);
        return button;
    }

    /// <summary>按钮图形和命中区域使用相同布局；文字随可用宽度换行。</summary>
    internal static void PlaceButton(UIButton button, Vector2 center, Vector2 size, float scale)
    {
        Place(button.constrained2D, center, size);
        foreach (var graphic in button.GetComponentsInChildren<Constrained2D>(true))
        {
            if (graphic == button.constrained2D)
                continue;
            if (graphic.constrained2DTransform.type == Constrained2DType.Sprite)
                Place(graphic, Vector2.zero, size);
        }
        foreach (var text in button.GetComponentsInChildren<FastText.FastText>(true))
        {
            text.SetFontSize(3.4f * scale, FastText.FastText.UpdateType.DontUpdateMesh);
            text.SetMaxAutoLineBreakPixelWidth((int)(size.x - 24f * scale), FastText.FastText.UpdateType.DontUpdateMesh);
            text.EnsureDirtyFlagsProcessed();
        }
        CenterButtonText(button);
    }

    /// <summary>按实际字形包围盒居中，消除字体模板基线与按钮中心之间的偏移。</summary>
    internal static void CenterButtonText(UIButton button)
    {
        foreach (var text in button.GetComponentsInChildren<FastText.FastText>(true))
        {
            var layout = text.GetComponent<Constrained2D>();
            Place(layout, Vector2.zero, layout.constrained2DTransform.sizeInPixels);
            text.EnsureDirtyFlagsProcessed();
            _m._wi._ERA(false);
            var bounds = text.meshRenderer.bounds;
            if (bounds.size.x <= 0f || bounds.size.y <= 0f) continue;
            var calculated = ReadLayout(layout);
            var unit = ProjectionValues.GetWorldSize(calculated.CalculatedZFromCamera, Vector2.one);
            var inherited = calculated.CachedWorldScale;
            float x = unit.x * inherited.x, y = unit.y * inherited.y;
            if (Mathf.Abs(x) <= 0f || Mathf.Abs(y) <= 0f) continue;
            var delta = button.transform.position - bounds.center;
            Place(layout, new Vector2(delta.x / x, delta.y / y), layout.constrained2DTransform.sizeInPixels);
        }
    }

    /// <summary>原生 _irA 遇到同一材质对象会直接返回；每次换纹理或 UV 都提交一份新材质并释放旧的。</summary>
    /// <param name="target">本模组拥有的原生图形。</param>
    /// <param name="current">当前已提交、由调用方拥有的材质。</param>
    /// <param name="texture">新的主纹理。</param>
    /// <param name="scale">主纹理 UV 缩放。</param>
    /// <param name="offset">主纹理 UV 偏移。</param>
    /// <returns>已提交的新材质，调用方负责后续释放。</returns>
    internal static Material SwapMaterial(Constrained2D target, Material current, Texture texture, Vector2 scale, Vector2 offset)
    {
        var next = new Material(current)
        {
            name = current.name, hideFlags = HideFlags.HideAndDontSave,
            mainTexture = texture, mainTextureScale = scale, mainTextureOffset = offset
        };
        try { target._irA(next); }
        catch { Object.Destroy(next); throw; }
        Object.Destroy(current);
        return next;
    }

    /// <summary>使用游戏投影把显示尺寸换成碰撞体局部单位，禁止把 UI 像素当作世界尺寸。</summary>
    internal static void SyncHitArea(Constrained2D constrained, BoxCollider collider)
    {
        var calculated = ReadLayout(constrained);
        var pixels = ProjectionValues.CameraPixelSize;
        var size = Vector2.Scale(calculated.CalculatedNormalizedSizeOfScreen, new Vector2(pixels.x, pixels.y));
        if (size.x <= 0f || size.y <= 0f)
        { collider.enabled = false; return; }
        // 与原生布局引擎一致，投影使用累计的相机深度，不能传 Transform 的世界 Z。
        var world = ProjectionValues.GetWorldSize(calculated.CalculatedZFromCamera, size);
        var scale = collider.transform.lossyScale;
        if (Mathf.Abs(scale.x) <= 0f || Mathf.Abs(scale.y) <= 0f)
        { collider.enabled = false; return; }
        collider.center = Vector3.zero;
        collider.size = new Vector3(world.x / Mathf.Abs(scale.x), world.y / Mathf.Abs(scale.y), 0.01f);
        collider.enabled = true;
    }

    /// <summary>分辨率或布局变化后，按钮与列表视口都按最新原生显示区域更新命中范围。</summary>
    internal void SyncHitAreas()
    {
        _m._wi._ERA(false);
        foreach (var (layout, collider) in hitAreas)
            SyncHitArea(layout, collider);
    }

    /// <summary>保留传入的原生委托，直到控件注销并失去输入权限。</summary>
    internal void Keep(Il2CppSystem.Delegate callback) => callbacks.Add(callback);

    /// <summary>先注销点击区域并隐藏整页，再释放副本和自有材质。</summary>
    public void Dispose()
    {
        if (Root != null)
            Root.gameObject.SetActive(false);
        foreach (var (_, collider) in hitAreas)
            if (collider != null) collider.enabled = false;
        hitAreas.Clear();
        foreach (var button in buttons)
            if (button != null && button.ButtonCollider != null)
            {
                try { UIManager._A?._ktA(button); }
                catch (Exception ex) { Plugin.Logger.LogError($"注销成绩页按钮失败：{ex}"); }
            }
        buttons.Clear();
        if (Root != null)
            Object.Destroy(Root.gameObject);
        foreach (var graphic in graphics)
            graphic.Dispose();
        graphics.Clear();
        callbacks.Clear();
    }
}

/// <summary>一个原生图形及其自有材质，支持封面替换和视口边缘裁切。</summary>
internal sealed class BrowserGraphic : IDisposable
{
    internal Constrained2D Layout { get; }
    /// <summary>当前提交给原生图形的材质；每次换纹理都会被新副本替换。</summary>
    private Material? material;
    /// <summary>只在纹理或裁切 UV 改变时提交材质，避免每帧重复建立原生材质副本。</summary>
    private Texture? displayedTexture;
    private Vector2 displayedScale = Vector2.one, displayedOffset;

    internal BrowserGraphic(Constrained2D layout, Material material)
    { Layout = layout; this.material = material; displayedTexture = material.mainTexture; }

    /// <summary>只释放自有材质；节点随页面根节点一起销毁。</summary>
    public void Dispose()
    {
        if (material != null)
            Object.Destroy(material);
        material = null;
    }

    /// <summary>区分底板、轨道和滑块的深度，避免同平面互相覆盖。</summary>
    internal void Depth(float value)
    {
        var layout = Layout.constrained2DTransform;
        layout.zPosition = value;
        Layout.constrained2DTransform = layout;
        Layout._PQA(0x3f);
    }

    /// <summary>覆盖图形颜色，不改变共享纹理。</summary>
    internal void Tint(Color color)
    {
        var layout = Layout.constrained2DTransform;
        if (layout.color == color)
            return;
        layout.color = color;
        Layout.constrained2DTransform = layout;
        Layout._PQA(0x3f);
    }

    /// <summary>原生图形在列表边缘按可见矩形及对应 UV 裁切，防止伸出游戏区域。</summary>
    internal void Show(Vector2 center, Vector2 size, Texture texture, float lower = float.NegativeInfinity, float upper = float.PositiveInfinity)
    {
        float bottom = Math.Max(center.y - size.y / 2f, lower);
        float top = Math.Min(center.y + size.y / 2f, upper);
        bool visible = top > bottom;
        Layout.gameObject.SetActive(visible);
        if (!visible)
            return;
        var uvScale = new Vector2(1f, (top - bottom) / size.y);
        var uvOffset = new Vector2(0f, (bottom - center.y + size.y / 2f) / size.y);
        if (material != null && (displayedTexture != texture || displayedScale != uvScale || displayedOffset != uvOffset))
        {
            material = BrowserWidgets.SwapMaterial(Layout, material, texture, uvScale, uvOffset);
            displayedTexture = texture;
            displayedScale = uvScale;
            displayedOffset = uvOffset;
        }
        BrowserWidgets.Place(Layout, new Vector2(center.x, (top + bottom) / 2f), new Vector2(size.x, top - bottom));
    }
}
