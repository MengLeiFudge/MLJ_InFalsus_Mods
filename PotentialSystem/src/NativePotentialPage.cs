using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.UI;
using ifapp.Game.UI.Common;
using UnityEngine;

namespace InFalsusMod.Potential;

/// <summary>全部最佳成绩的原生视图；滚动位置由游戏 UIScrollView 与原生垂直滑块共同管理。</summary>
internal sealed class NativePotentialPage : IDisposable
{
    /// <summary>整页资源、原生滚动组件及只为可见行建立的卡片池。</summary>
    private readonly BrowserWidgets widgets;
    private readonly Constrained2D viewport, content;
    private readonly UIScrollView scroll;
    private readonly UiProgressVerticalSliderContainer slider;
    private readonly UIButton close, export, search, track;
    private readonly BrowserGraphic background, line, pip;
    private readonly NativeLabel account, status, searchText, empty;
    private readonly List<BrowserCard> cards = new();
    private Vector2 size;
    private float scale, cardScale, viewportHeight, cardWidth, maximum;
    private int columns;
    private RankedPlay[] plays = Array.Empty<RankedPlay>();

    /// <summary>把所有命中区域登记到成绩页输入头，原版选歌按钮不会参与本页点击。</summary>
    internal NativePotentialPage(SongSelectScene scene, InputReceiver receiver, Action closePage,
        Action exportImage, Action focusSearch)
    {
        widgets = new BrowserWidgets(scene, receiver);
        try
        {
            var parent = widgets.Root.transform;
            background = widgets.Graphic(parent, "Results.Backing", Texture2D.whiteTexture, PotentialPresentation.Color(0x080f1c));
            background.Depth(0.1f);
            account = widgets.Label(parent, "Results.Account", 4f);
            status = widgets.Label(parent, "Results.Status", 2.8f);
            viewport = widgets.Container(parent, "Results.Viewport", Vector2.one, hitArea: true);
            content = widgets.Container(viewport.transform, "Results.ScrollPosition", Vector2.one);
            scroll = viewport.gameObject.AddComponent<UIScrollView>();
            scroll.boxCollider = viewport.GetComponent<BoxCollider>();
            scroll.contentContainer = content;
            scroll.contentContainerTransform = content.transform;
            scroll.scrollDirection = _m._lI._ZJb;
            scroll.weight = 0.22f;
            scroll.maxForce = 1600f;
            scroll.constrainScrollToBounds = true;
            UIManager._A._KtA(scroll, receiver);
            empty = widgets.Label(viewport.transform, "Results.Empty", 3.8f);
            close = widgets.Button(parent, "Results.Close", "×", closePage);
            foreach (var text in close.GetComponentsInChildren<FastText.FastText>(true))
                text.SetTextColor(PotentialPresentation.Color(0xff5964));
            export = widgets.Button(parent, "Results.Export", "导出 B50 到剪贴板", exportImage);
            search = widgets.Button(parent, "Results.Search", " ", focusSearch);
            searchText = widgets.Label(search.transform, "Results.SearchText", 3.2f);
            track = widgets.Button(parent, "Results.Scrollbar", " ", () => { });
            track.flags |= UIButton.Flags.CanContinuedDragOutsideOfBounds | UIButton.Flags.CanSelectedButtonBePressed;
            line = widgets.Graphic(track.transform, "Results.Scrollbar.Line", Texture2D.whiteTexture, PotentialPresentation.Color(0x152339));
            pip = widgets.Graphic(track.transform, "Results.Scrollbar.Thumb", Texture2D.whiteTexture, PotentialPresentation.Color(0xa3bedc));
            line.Depth(-0.04f);
            pip.Depth(-0.06f);
            slider = track.gameObject.AddComponent<UiProgressVerticalSliderContainer>();
            slider.inputLayer = receiver;
            slider.line = line.Layout;
            slider.pipIcon = pip.Layout;
            slider.pipSelector = track;
            slider.pipSelectorC2D = track.constrained2D;
            slider.boxCollider = track.ButtonCollider;
            UiProgressVerticalSliderContainer._ab moved = (Action<UiProgressVerticalSliderContainer, float>)((_, value) => SetScroll((1f - value) * maximum));
            widgets.Keep(moved);
            slider._BE(moved);
            // Start 在 Unity 下一帧绑定原生按下/拖动回调，所需引用在激活后首次更新前已完整设置。
            Layout();
            InputManager.Instance?.RequestLastMouseMoveReplay();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>按游戏投影范围重新布局；16:10 桌面上的上下黑边不进入页面坐标。</summary>
    private void Layout()
    {
        var available = widgets.Size;
        if (available.x <= 0 || available.y <= 0)
            throw new InvalidOperationException("游戏成绩页的原生布局范围不可用。");
        if (size == available)
            return;
        size = available;
        scale = Mathf.Clamp(size.y / 1015f, 0.65f, 2f);
        float padding = 24f * scale;
        background.Show(Vector2.zero, size, Texture2D.whiteTexture);
        PlaceLabel(account, Center(padding, 20f * scale, size.x - 140f * scale, 60f * scale), size.x - 140f * scale, 4f * scale);
        BrowserWidgets.PlaceButton(close, Center(size.x - padding - 56f * scale, 20f * scale, 56f * scale, 56f * scale), new Vector2(56f, 56f) * scale, scale);
        BrowserWidgets.PlaceButton(export, Center(size.x - padding - 285f * scale, 100f * scale, 285f * scale, 54f * scale), new Vector2(285f, 54f) * scale, scale);
        BrowserWidgets.PlaceButton(search, Center(padding, 100f * scale, 520f * scale, 54f * scale), new Vector2(520f, 54f) * scale, scale);
        PlaceLabel(searchText, Vector2.zero, 490f * scale, 3.2f * scale);
        BrowserWidgets.CenterButtonText(search);
        PlaceLabel(status, Center(padding, size.y - 42f * scale, size.x - padding * 2, 32f * scale), size.x - padding * 2, 2.8f * scale);
        float top = 176f * scale;
        viewportHeight = size.y - top - 52f * scale;
        float viewportWidth = size.x - padding * 2 - 46f * scale;
        BrowserWidgets.Place(viewport, Center(padding, top, viewportWidth, viewportHeight), new Vector2(viewportWidth, viewportHeight));
        BrowserWidgets.Place(content, Vector2.zero, new Vector2(viewportWidth, viewportHeight));
        columns = B50Image.Columns;
        cardScale = viewportWidth / (columns * B50Image.CardWidth + (columns - 1) * B50Image.Gap);
        cardWidth = B50Image.CardWidth * cardScale;
        BrowserWidgets.PlaceButton(track, Center(size.x - padding - 30f * scale, top, 30f * scale, viewportHeight),
            new Vector2(30f * scale, viewportHeight), scale);
        line.Show(Vector2.zero, new Vector2(8f * scale, viewportHeight - 54f * scale), Texture2D.whiteTexture);
        pip.Show(Vector2.zero, new Vector2(26f, 54f) * scale, Texture2D.whiteTexture);
        SetBounds();
        foreach (var card in cards)
            card.Invalidate();
    }

    /// <summary>把页面左上角坐标转为原生容器中心偏移。</summary>
    private Vector2 Center(float x, float y, float width, float height) => new(-size.x / 2f + x + width / 2f, size.y / 2f - y - height / 2f);

    /// <summary>单行文字按可用宽度缩小，保持原生字形和布局约束；align 为 -1 左对齐、0 居中、1 右对齐。</summary>
    internal static void PlaceLabel(NativeLabel label, Vector2 center, float width, float fontSize, float align = 0f)
    {
        var text = label.Text;
        text.presention = (text.presention & ~FastText.FastText.FastTextPresentation.AutoLineBreak)
            | FastText.FastText.FastTextPresentation.IgnoreLinebreak;
        text.SetFontSize(fontSize, FastText.FastText.UpdateType.DontUpdateMesh);
        text.EnsureDirtyFlagsProcessed();
        var layout = text.GetComponent<Constrained2D>();
        BrowserWidgets.Place(layout, center, layout.constrained2DTransform.sizeInPixels);
        float measured = layout._mrA().x;
        if (measured > width && measured > 0)
        {
            text.SetFontSize(fontSize * width / measured, FastText.FastText.UpdateType.Default);
            layout._PQA(0x3f);
            measured = width;
        }
        // 原生文字以中心定位；对齐时按实测宽度把中心移到可用区域的左/右边。
        if (align != 0f && measured > 0)
            BrowserWidgets.Place(layout, center + new Vector2(align * (width - measured) / 2f, 0f), layout.constrained2DTransform.sizeInPixels);
    }

    /// <summary>搜索变化后滚动回到首行，保留成绩的全列表排名。</summary>
    internal void SetPlays(RankedPlay[] filtered)
    {
        plays = filtered;
        SetBounds();
        SetScroll(0f);
        foreach (var card in cards)
            card.Invalidate();
    }

    /// <summary>内容边界、滑块状态与虚拟卡片数量共用相同列数和行距。</summary>
    private void SetBounds()
    {
        int rows = (plays.Length + columns - 1) / Math.Max(1, columns);
        maximum = Math.Max(0f, rows * (B50Image.CardHeight + B50Image.Gap) * cardScale - B50Image.Gap * cardScale - viewportHeight);
        scroll._b = new Rect(0f, 0f, 0f, maximum);
        scroll._sTA();
        NativeButtons.SetState(track, maximum > 0);
    }

    /// <summary>滚轮按一格移动约一行；直接设置列表偏移并清掉拖动惯性。</summary>
    internal void Wheel(Vector2 delta)
    {
        if (!float.IsFinite(delta.y) || delta.y == 0f) return;
        float steps = Mathf.Abs(delta.y) >= 120f ? delta.y / 120f : delta.y;
        SetScroll(content.constrained2DTransform.offset.y - Mathf.Clamp(steps, -8f, 8f)
            * (B50Image.CardHeight + B50Image.Gap) * cardScale);
    }

    /// <summary>垂直滑块跳转后停止惯性，避免鼠标拖动与之前的滚动相互覆盖。</summary>
    private void SetScroll(float position)
    {
        content._wQA(0f, Mathf.Clamp(position, 0f, maximum));
        scroll._gkb = Vector3.zero;
    }

    /// <summary>刷新信息和可见卡片；未显示的封面不启动加载。</summary>
    internal void Render(PotentialSnapshot snapshot, CoverCache covers, string query, string message, bool focused, bool exporting)
    {
        Layout();
        SetLabel(account, $"PTT {PotentialCalculator.Format(snapshot.Potential)}    B10 {PotentialCalculator.Format(snapshot.B10Average)}    B50 {PotentialCalculator.Format(snapshot.B50Average)}", size.x - 140f * scale, 4f * scale);
        SetLabel(status, message, size.x - 48f * scale, 2.8f * scale);
        string searchValue = string.IsNullOrEmpty(query) && !focused ? "搜索歌名、作者或难度" : query;
        if (searchText.Set(searchValue, Color.white))
        {
            PlaceLabel(searchText, Vector2.zero, 490f * scale, 3.2f * scale);
            BrowserWidgets.CenterButtonText(search);
        }
        NativeButtons.SetState(search, true, focused);
        NativeButtons.SetState(export, !exporting);
        slider._cE(maximum > 0 ? 1f - content.constrained2DTransform.offset.y / maximum : 1f);
        empty.Text.gameObject.SetActive(plays.Length == 0);
        if (plays.Length == 0)
            empty.Set("没有符合条件的已游玩谱面。", Color.white);
        int startRow = Math.Max(0, (int)(content.constrained2DTransform.offset.y / ((B50Image.CardHeight + B50Image.Gap) * cardScale)));
        int visibleRows = (int)Math.Ceiling(viewportHeight / ((B50Image.CardHeight + B50Image.Gap) * cardScale)) + 1;
        int count = Math.Min(plays.Length - startRow * columns, visibleRows * columns);
        while (cards.Count < count)
            cards.Add(new BrowserCard(widgets, viewport.transform));
        float offset = content.constrained2DTransform.offset.y;
        float width = viewport.constrained2DTransform.sizeInPixels.x;
        for (int i = 0; i < cards.Count; i++)
        {
            if (i >= count)
            { cards[i].Hide(); continue; }
            int index = startRow * columns + i;
            var center = new Vector2(-width / 2f + (index % columns) * (cardWidth + B50Image.Gap * cardScale) + cardWidth / 2f,
                viewportHeight / 2f - (index / columns) * (B50Image.CardHeight + B50Image.Gap) * cardScale - B50Image.CardHeight / 2f * cardScale + offset);
            cards[i].Render(plays[index], covers, center, cardWidth, cardScale, viewportHeight);
        }
        widgets.SyncHitAreas();
    }

    /// <summary>信息改变时重新适配文字宽度，长搜索或错误信息不会越过游戏页面边界。</summary>
    private static void SetLabel(NativeLabel label, string value, float width, float fontSize)
    {
        if (label.Set(value, Color.white))
            PlaceLabel(label, label.Text.GetComponent<Constrained2D>().constrained2DTransform.offset, width, fontSize);
    }

    /// <summary>先注销滚动输入，再由资源工厂销毁整页。</summary>
    public void Dispose()
    {
        try
        {
            if (scroll != null)
                UIManager._A?._ItA(scroll);
        }
        finally
        {
            widgets.Dispose();
            foreach (var card in cards) card.Dispose();
            cards.Clear();
        }
    }
}

/// <summary>仅为可见行保留卡片纹理，与导出共用绘制函数，整张纹理随视口裁切。</summary>
internal sealed class BrowserCard : IDisposable
{
    private readonly BrowserGraphic graphic;
    private Texture2D? texture;
    private RankedPlay? bound;
    private PixelImage? cover;
    private string? coverError;

    internal BrowserCard(BrowserWidgets widgets, Transform parent) =>
        graphic = widgets.Graphic(parent, "Results.Card", Texture2D.whiteTexture, Color.white);

    internal void Invalidate() => bound = null;
    internal void Hide() => graphic.Layout.gameObject.SetActive(false);

    /// <summary>成绩或异步封面改变时重画，滚动帧只调整位置和裁切 UV。</summary>
    internal void Render(RankedPlay play, CoverCache covers, Vector2 center, float width, float scale, float height)
    {
        var pixels = covers.GetPixels(play.Chart, out var error);
        if (bound != play || !ReferenceEquals(cover, pixels) || coverError != error)
        {
            var card = B50Image.RenderCard(play, pixels, error == null ? "封面加载中" : "封面不可用");
            if (texture == null)
                texture = new Texture2D(card.Width, card.Height, TextureFormat.RGBA32, false)
                {
                    name = "PotentialSystem.ResultCard", hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear
                };
            texture.LoadRawTextureData(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(card.Rgba));
            texture.Apply(false, false);
            bound = play;
            cover = pixels;
            coverError = error;
        }
        graphic.Show(center, new Vector2(width, B50Image.CardHeight * scale), texture!, -height / 2f, height / 2f);
    }

    public void Dispose()
    {
        if (texture != null) UnityEngine.Object.Destroy(texture);
        texture = null;
    }
}
