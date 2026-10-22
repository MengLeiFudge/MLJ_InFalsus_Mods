using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>结算谱面的旧最佳、更新后最佳 max(旧, 本次)、本次表现与定数。</summary>
/// <param name="Previous">本局前的单曲潜力值；首次游玩为 null。</param>
/// <param name="Best">本局后的单曲潜力值。</param>
/// <param name="Play">本局自身的单曲潜力值。</param>
/// <param name="Constant">当前来源的谱面定数。</param>
internal sealed record ResultSummary(decimal? Previous, decimal Best, decimal Play, decimal Constant);

/// <summary>拥有原生 PTT 文字、角色菱形与独立档位徽章；结算视图展示单曲与变化状态。</summary>
internal sealed class PotentialView : IDisposable
{
    /// <summary>角色菱形上角裁至游戏上边缘；小徽章位于头像右下，所有节点均在可见入口内。</summary>
    internal static readonly Vector2 PortraitCenter = new(0f, 75f), BadgeCenter = new(120f, -93f);
    private const float EntryHeight = 330f, PortraitSize = 344f;
    internal const float FrameSize = 360f, BadgeSize = 144f;
    /// <summary>结算信息相对顶部中央布局；左侧显示单曲变化，右侧显示账号变化，避开两端导航按钮。</summary>
    private static readonly (Vector2 Center, float Width, float Font)[] ResultSlots =
    {
        (new Vector2(0f, -140f), 150f, 3.6f), // 0 徽章上的滚动数字
        (new Vector2(310f, -125f), 390f, 3.2f), // 1 增量或 Keep
        (new Vector2(310f, -70f), 390f, 2.4f), // 2 PTT / MAX 或错误状态
        (new Vector2(-360f, -70f), 470f, 2.6f), // 3 单曲 旧 -> max(旧, 本次)
        (new Vector2(-360f, -120f), 470f, 2.3f)  // 4 本次与定数
    };
    private const float ResultBadgeSize = 180f;
    /// <summary>仿 Arcaea：入场稍候后数字滚动到新值，结束时徽章轻微放大回弹并显示增量。</summary>
    internal const float AnimationDelay = 0.6f, AnimationSeconds = 1.4f, PopSeconds = 0.3f;
    /// <summary>本视图创建的文字、图形与材质；嵌入纹理由控制器共享。</summary>
    private readonly List<NativeLabel> labels = new();
    private readonly PotentialBadges badges;
    private readonly bool results;
    private Constrained2D? entryRoot, graphic, portrait, frame, backing;
    private Material? material, portraitMaterial, frameMaterial, backingMaterial;
    /// <summary>按菱形轮廓裁去框外像素的头像副本，由本视图释放。</summary>
    private Texture2D? portraitTexture;
    private int displayedTier = -1;
    private float displayedPop = -1f;
    /// <summary>选歌入口绑定不带渲染缩放的容器，头像、边框、徽章和数字分别布局。</summary>
    internal Constrained2D Entry => entryRoot!;

    /// <summary>选歌头像采用顶部裁切的大菱形及右下 PTT；结算信息放在顶部中央。</summary>
    internal PotentialView(Text template, Transform parent, Constrained2D graphicTemplate,
        PotentialBadges badges, bool results = false)
    {
        this.badges = badges;
        this.results = results;
        try
        {
            var anchor = new Vector2(0.5f, 0.5f);
            var alignment = Constrained2DAlignment.Top;
            var center = new Vector2(0f, -EntryHeight / 2f);
            if (!results)
            {
                var root = new GameObject("InFalsus.PotentialSystem.Account");
                root.SetActive(false);
                root.transform.SetParent(parent, false);
                entryRoot = root.AddComponent<Constrained2D>();
                var container = entryRoot.constrained2DTransform;
                container.transform = root.transform;
                container.type = Constrained2DType.Container;
                container.anchor = anchor;
                container.parentAlignment = alignment;
                container.offset = center;
                container.sizeInPixels = new Vector2(440f, EntryHeight);
                container.sizeInParentNormalized = Vector2.zero;
                container.renderableScale = Vector2.one;
                container.color = Color.white;
                container.zPosition = -0.2f;
                container.overrideMaterialRenderQueueOffset = 20;
                entryRoot.constrained2DTransform = container;
                root.SetActive(true);
                entryRoot._rQA();
                entryRoot._RQA();
                entryRoot._PQA(0x3f);
                BrowserWidgets.Place(entryRoot, center, container.sizeInPixels);
            }
            var graphicsParent = results ? parent : entryRoot!.transform;
            graphic = CloneGraphic(graphicTemplate, graphicsParent, "Badge");
            var layout = graphic.constrained2DTransform;
            var sourceMaterial = layout.renderableTargetMaterial
                ?? throw new InvalidOperationException("徽章模板缺少图形材质。");
            layout.parentAlignment = results ? alignment : Constrained2DAlignment.Center;
            layout.offset = results ? ResultSlots[0].Center : BadgeCenter;
            layout.zPosition = results ? -0.2f : -0.08f;
            layout.sizeInPixels = results ? new Vector2(ResultBadgeSize, ResultBadgeSize) : Vector2.one * BadgeSize;
            graphic.constrained2DTransform = layout;
            material = new Material(sourceMaterial)
            {
                name = "PotentialSystem.Badge", hideFlags = HideFlags.HideAndDontSave,
                mainTexture = badges.Texture(8), mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
            };
            graphic._irA(material);
            displayedTier = 8;
            graphic._PQA(0x3f);
            if (results)
            {
                for (int i = 0; i < ResultSlots.Length; i++)
                    labels.Add(new NativeLabel(template, parent, "Result" + i, anchor, alignment, ResultSlots[i].Center, ResultSlots[i].Font));
            }
            else
            {
                backing = CloneGraphic(graphicTemplate, graphicsParent, "AvatarBacking");
                var backingLayout = backing.constrained2DTransform;
                backingLayout.zPosition = -0.01f;
                backing.constrained2DTransform = backingLayout;
                backingMaterial = new Material(sourceMaterial)
                {
                    name = "PotentialSystem.AvatarBacking", hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = badges.AvatarBacking, mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
                };
                CropAtGameTop(backing, backingMaterial, FrameSize);
                // Sprite 的 Transform 随网格尺寸缩放；全部子控件挂在独立容器下，保持彼此字号与坐标。
                portrait = CloneGraphic(graphicTemplate, graphicsParent, "Portrait");
                var portraitLayout = portrait.constrained2DTransform;
                portraitLayout.zPosition = -0.03f;
                portrait.constrained2DTransform = portraitLayout;
                portrait.gameObject.SetActive(false);
                frame = CloneGraphic(graphicTemplate, graphicsParent, "AvatarFrame");
                var frameLayout = frame.constrained2DTransform;
                frameLayout.zPosition = -0.05f;
                frame.constrained2DTransform = frameLayout;
                frameMaterial = new Material(sourceMaterial)
                {
                    name = "PotentialSystem.AvatarFrame", hideFlags = HideFlags.HideAndDontSave,
                    mainTexture = badges.AvatarFrame, mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
                };
                CropAtGameTop(frame, frameMaterial, FrameSize);
                labels.Add(new NativeLabel(template, graphicsParent, "Account", anchor,
                    Constrained2DAlignment.Center, BadgeCenter, 3.6f));
                labels.Add(new NativeLabel(template, parent, "Status", anchor, alignment,
                    center + new Vector2(0f, -EntryHeight / 2f - 25f), 2.4f));
            }
        }
        catch { Dispose(); throw; }
    }

    /// <summary>只裁掉菱形的上角，并在原材质 UV 内组合裁切；调用方必须传入尚未提交的新材质。</summary>
    private static void CropAtGameTop(Constrained2D target, Material ownedMaterial, float size)
    {
        float bottom = PortraitCenter.y - size / 2f;
        float top = Math.Min(PortraitCenter.y + size / 2f, EntryHeight / 2f);
        var scale = ownedMaterial.mainTextureScale;
        ownedMaterial.mainTextureScale = new Vector2(scale.x, scale.y * (top - bottom) / size);
        target._irA(ownedMaterial);
        BrowserWidgets.Place(target, new Vector2(PortraitCenter.x, (top + bottom) / 2f), new Vector2(size, top - bottom));
        target._PQA(0x3f);
    }

    /// <summary>复制原生图形并重新绑定渲染引用；布局以独立父容器为基准。</summary>
    private static Constrained2D CloneGraphic(Constrained2D template, Transform parent, string name)
    {
        var root = Object.Instantiate(template.gameObject, parent, false);
        try
        {
            var clone = root.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("徽章模板缺少布局。");
            root.name = "InFalsus.PotentialSystem." + name;
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var layout = clone.constrained2DTransform;
            layout.transform = root.transform;
            layout.meshRenderer = root.GetComponent<MeshRenderer>();
            layout.meshFilter = root.GetComponent<MeshFilter>();
            layout.type = Constrained2DType.Sprite;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Center;
            layout.sizeInParentNormalized = Vector2.zero;
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            layout.overrideMaterialRenderQueueOffset = 0;
            layout.spriteBlendEffect = default;
            clone.constrained2DTransform = layout;
            root.SetActive(true);
            clone._rQA();
            clone._PQA(0x3f);
            return clone;
        }
        catch { Object.Destroy(root); throw; }
    }

    /// <summary>复制所选角色头像的图集区域，按菱形轮廓清除框外像素，再交给原生图形显示。</summary>
    internal void SetPortrait(Material source)
    {
        if (portrait == null || source == null || source.mainTexture == null)
            throw new InvalidOperationException("所选角色的原生头像不可用。");
        var masked = DiamondMask(source.mainTexture, source.mainTextureScale, source.mainTextureOffset);
        var next = new Material(source)
        {
            name = "PotentialSystem.Portrait", hideFlags = HideFlags.HideAndDontSave,
            mainTexture = masked, mainTextureScale = Vector2.one, mainTextureOffset = Vector2.zero
        };
        try { CropAtGameTop(portrait, next, PortraitSize); }
        catch { Object.Destroy(next); Object.Destroy(masked); throw; }
        if (portraitMaterial != null) Object.Destroy(portraitMaterial);
        if (portraitTexture != null) Object.Destroy(portraitTexture);
        portraitMaterial = next;
        portraitTexture = masked;
        portrait.gameObject.SetActive(true);
        portrait._rQA();
        portrait._PQA(0x3f);
    }

    /// <summary>用 GPU 复制图集中的头像区域，在 CPU 上把 |x|+|y| 超出菱形的像素设为透明并羽化 1.5 像素。</summary>
    private static Texture2D DiamondMask(Texture source, Vector2 scale, Vector2 offset)
    {
        const int size = 256;
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        Texture2D? texture = null;
        try
        {
            Graphics.Blit(source, target, scale, offset);
            RenderTexture.active = target;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "PotentialSystem.Portrait", hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
            };
            texture.ReadPixels(new Rect(0f, 0f, size, size), 0, 0, false);
            Il2CppStructArray<byte> raw = texture.GetRawTextureData();
            var pixels = raw.ToArray();
            if (pixels.Length != size * size * 4)
                throw new InvalidDataException("头像像素长度无效。");
            float half = size / 2f;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    // 到菱形边缘的像素距离；内侧为正。菱形与 FrameSize 中的白框内沿一致。
                    float inside = (half - Math.Abs(x + 0.5f - half) - Math.Abs(y + 0.5f - half)) * 0.7071f;
                    float coverage = Mathf.Clamp01(inside / 1.5f + 0.5f);
                    int alpha = y * size * 4 + x * 4 + 3;
                    pixels[alpha] = (byte)(pixels[alpha] * coverage);
                }
            texture.LoadRawTextureData(new Il2CppStructArray<byte>(pixels));
            texture.Apply(false, true);
            return texture;
        }
        catch
        {
            if (texture != null) Object.Destroy(texture);
            throw;
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }

    /// <summary>换徽章档位时提交新材质；原生 _irA 对同一材质对象不会重新应用。</summary>
    private void ShowTier(int tier)
    {
        if (tier == displayedTier || graphic == null || material == null)
            return;
        material = BrowserWidgets.SwapMaterial(graphic, material, badges.Texture(tier), Vector2.one, Vector2.zero);
        displayedTier = tier;
    }

    /// <summary>同步选歌入口尺寸并刷新徽章数值，保持分辨率变化后的点击范围与显示一致。</summary>
    internal void Render(PotentialSnapshot? snapshot, string status)
    {
        if (entryRoot != null)
        {
            var layout = entryRoot.constrained2DTransform;
            BrowserWidgets.Place(entryRoot, layout.offset, layout.sizeInPixels);
        }
        ShowTier(snapshot == null ? 8 : PotentialBadges.Tier(snapshot.Potential));
        labels[0].Set(snapshot == null ? "--.---" : PotentialCalculator.Format(snapshot.Potential), Color.white);
        labels[1].Set(snapshot == null || status.StartsWith("Unsaved", StringComparison.Ordinal) ? status : "",
            new Color(0.7f, 1f, 0.85f));
    }

    /// <summary>仿 Arcaea 的结算 PTT：延迟后用三次缓出从旧值滚动到新值，结束时徽章回弹并显示增量。</summary>
    /// <param name="snapshot">当前快照；不可用时只显示状态。</param>
    /// <param name="before">本局前账号潜力值。</param>
    /// <param name="after">本局后账号潜力值。</param>
    /// <param name="summary">本局谱面的单曲变化；不在曲库中为 null。</param>
    /// <param name="elapsed">观察到结算后经过的非缩放秒数。</param>
    /// <param name="error">存储或数据错误；为 null 时显示 PTT / MAX。</param>
    internal void RenderResult(PotentialSnapshot? snapshot, decimal before, decimal after, ResultSummary? summary,
        float elapsed, string? error)
    {
        float progress = Mathf.Clamp01((elapsed - AnimationDelay) / AnimationSeconds);
        decimal blend = (decimal)(1f - Mathf.Pow(1f - progress, 3f));
        decimal shown = before + (after - before) * blend;
        ShowTier(snapshot == null ? 8 : PotentialBadges.Tier(shown));
        decimal delta = (decimal.Floor(after * 1000m) - decimal.Floor(before * 1000m)) / 1000m;
        // 只有数值上升才回弹；回弹期间按正弦放大至多 12%。
        float popTime = elapsed - AnimationDelay - AnimationSeconds;
        float pop = delta > 0m && popTime is > 0f and < PopSeconds ? 1f + 0.12f * Mathf.Sin(Mathf.PI * popTime / PopSeconds) : 1f;
        if (graphic != null && pop != displayedPop)
        {
            BrowserWidgets.Place(graphic, ResultSlots[0].Center, Vector2.one * ResultBadgeSize * pop);
            displayedPop = pop;
        }
        bool done = progress >= 1f;
        var accent = new Color(0.7f, 1f, 0.85f);
        SetResult(0, snapshot == null ? "--.---" : PotentialCalculator.Format(shown), Color.white);
        SetResult(1, snapshot == null || !done ? "" : delta > 0m ? "+" + PotentialCalculator.Format(delta) : "Keep",
            delta > 0m ? accent : new Color(0.8f, 0.85f, 0.92f));
        SetResult(2, error ?? (snapshot == null ? "PTT 数据不可用" : $"PTT / MAX {PotentialCalculator.Format(snapshot.Maximum)}"),
            error == null ? Color.white : new Color(1f, 0.6f, 0.6f));
        if (summary == null)
        {
            SetResult(3, snapshot == null ? "" : "该谱面未计入潜力值", Color.white);
            SetResult(4, "", Color.white);
            return;
        }
        string old = summary.Previous.HasValue ? PotentialCalculator.Format(summary.Previous.Value) : "--.---";
        bool improved = !summary.Previous.HasValue || summary.Best > summary.Previous.Value;
        SetResult(3, $"单曲 {old} -> {PotentialCalculator.Format(summary.Best)}", improved ? accent : Color.white);
        SetResult(4, $"本次 {PotentialCalculator.Format(summary.Play)} / 定数 {PotentialCalculator.FormatConstant(summary.Constant)}", Color.white);
    }

    /// <summary>文字变化时按槽位宽度缩放，避免长文案越过右侧菜单列。</summary>
    private void SetResult(int slot, string text, Color color)
    {
        var (center, width, font) = ResultSlots[slot];
        if (labels[slot].Set(text, color))
            NativePotentialPage.PlaceLabel(labels[slot], center, width, font);
    }

    /// <summary>先销毁自有节点，再释放视图材质与头像副本；共享纹理留给控制器回收。</summary>
    public void Dispose()
    {
        foreach (var label in labels)
            label.Dispose();
        labels.Clear();
        if (entryRoot != null)
            Object.Destroy(entryRoot.gameObject);
        else if (graphic != null)
            Object.Destroy(graphic.gameObject);
        entryRoot = graphic = portrait = frame = backing = null;
        if (material != null) Object.Destroy(material);
        if (portraitMaterial != null) Object.Destroy(portraitMaterial);
        if (frameMaterial != null) Object.Destroy(frameMaterial);
        if (backingMaterial != null) Object.Destroy(backingMaterial);
        if (portraitTexture != null) Object.Destroy(portraitTexture);
        material = portraitMaterial = frameMaterial = backingMaterial = null;
        portraitTexture = null;
    }
}
