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
    private readonly BrowserGraphic background, badge, line, pip;
    private readonly NativeLabel title, account, summary, status, searchText, empty;
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
            badge = widgets.Graphic(parent, "Results.Badge", Texture2D.whiteTexture, Color.white);
            title = widgets.Label(parent, "Results.Title", 5f);
            account = widgets.Label(parent, "Results.Account", 3.6f);
            summary = widgets.Label(parent, "Results.Summary", 3f);
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
            close = widgets.Button(parent, "Results.Close", "关闭", closePage);
            export = widgets.Button(parent, "Results.Export", "导出 B50 到剪贴板", exportImage);
            search = widgets.Button(parent, "Results.Search", " ", focusSearch);
            searchText = widgets.Label(search.transform, "Results.SearchText", 3.2f);
            track = widgets.Button(parent, "Results.Scrollbar", " ", () => { });
            track.flags |= UIButton.Flags.CanContinuedDragOutsideOfBounds | UIButton.Flags.CanSelectedButtonBePressed;
            line = widgets.Graphic(track.transform, "Results.Scrollbar.Line", Texture2D.whiteTexture, PotentialPresentation.Color(0x152339));
            pip = widgets.Graphic(track.transform, "Results.Scrollbar.Thumb", Texture2D.whiteTexture, PotentialPresentation.Color(0xa3bedc));
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
        PlaceLabel(title, Center(180f * scale, 26f * scale, size.x - 204f * scale, 60f * scale), size.x - 204f * scale, 5f * scale);
        PlaceLabel(account, Center(180f * scale, 92f * scale, size.x - 204f * scale, 40f * scale), size.x - 204f * scale, 3.6f * scale);
        PlaceLabel(summary, Center(180f * scale, 142f * scale, size.x - 204f * scale, 35f * scale), size.x - 204f * scale, 3f * scale);
        BrowserWidgets.PlaceButton(close, Center(size.x - padding - 95f * scale, 193f * scale, 95f * scale, 60f * scale), new Vector2(95f, 60f) * scale, scale);
        BrowserWidgets.PlaceButton(export, Center(size.x - padding - 400f * scale, 193f * scale, 285f * scale, 60f * scale), new Vector2(285f, 60f) * scale, scale);
        BrowserWidgets.PlaceButton(search, Center(padding, 193f * scale, 420f * scale, 50f * scale), new Vector2(420f, 50f) * scale, scale);
        PlaceLabel(searchText, Vector2.zero, 395f * scale, 3.2f * scale);
        PlaceLabel(status, Center(padding, size.y - 52f * scale, size.x - padding * 2, 40f * scale), size.x - padding * 2, 2.8f * scale);
        float top = 265f * scale;
        viewportHeight = size.y - top - 64f * scale;
        float viewportWidth = size.x - padding * 2 - 46f * scale;
        BrowserWidgets.Place(viewport, Center(padding, top, viewportWidth, viewportHeight), new Vector2(viewportWidth, viewportHeight));
        BrowserWidgets.Place(content, Vector2.zero, new Vector2(viewportWidth, viewportHeight));
        columns = B50Image.Columns;
        cardScale = viewportWidth / (columns * B50Image.CardWidth + (columns - 1) * B50Image.Gap);
        cardWidth = B50Image.CardWidth * cardScale;
        BrowserWidgets.PlaceButton(track, Center(size.x - padding - 30f * scale, top + 30f * scale, 30f * scale, viewportHeight - 60f * scale),
            new Vector2(30f * scale, viewportHeight - 60f * scale), scale);
        line.Show(Vector2.zero, new Vector2(10f * scale, viewportHeight - 60f * scale), Texture2D.whiteTexture);
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

    /// <summary>滚轮仍由原生输入头接收，实际移动使用游戏滚动接口。</summary>
    internal void Wheel(Vector2 delta) => scroll._TTA(new Vector2(0f, Mathf.Clamp(delta.y, -120f, 120f) * 0.75f * scale));

    /// <summary>垂直滑块跳转后停止惯性，避免鼠标拖动与之前的滚动相互覆盖。</summary>
    private void SetScroll(float position)
    {
        content._wQA(0f, Mathf.Clamp(position, 0f, maximum));
        scroll._gkb = Vector3.zero;
    }

    /// <summary>刷新信息和可见卡片；未显示的封面不启动加载。</summary>
    internal void Render(PotentialSnapshot snapshot, PotentialBadges badges, CoverCache covers, string query, string message, bool focused, bool exporting)
    {
        Layout();
        badge.Show(Center(30f * scale, 27f * scale, 135f * scale, 135f * scale), new Vector2(135f, 135f) * scale,
            badges.Texture(PotentialBadges.Tier(snapshot.Potential)));
        SetLabel(title, "潜力值 · 全部最佳成绩", size.x - 204f * scale, 5f * scale);
        SetLabel(account, $"PTT {PotentialCalculator.Format(snapshot.Potential)}  /  MAX {PotentialCalculator.Format(snapshot.Maximum)}", size.x - 204f * scale, 3.6f * scale);
        SetLabel(summary, $"B50 {PotentialCalculator.Format(snapshot.B50Average)}   B10 {PotentialCalculator.Format(snapshot.B10Average)}   已游玩 {snapshot.Plays.Count} 谱面", size.x - 204f * scale, 3f * scale);
        SetLabel(status, message, size.x - 48f * scale, 2.8f * scale);
        SetLabel(searchText, string.IsNullOrEmpty(query) && !focused ? "搜索歌名、作者或难度" : "搜索：" + query, 395f * scale, 3.2f * scale);
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
        finally { widgets.Dispose(); }
    }
}

/// <summary>五列横向成绩卡，与分享图共用曲绘印象色；图形与文字限定在滚动视口内。</summary>
internal sealed class BrowserCard
{
    /// <summary>原生布局单位的卡片高度；列表边界与虚拟行复用同一尺寸。</summary>
    private const float Height = B50Image.CardHeight;
    private readonly Constrained2D root;
    private readonly BrowserGraphic border, background, cover;
    private readonly NativeLabel[] labels;
    private RankedPlay? bound;

    internal BrowserCard(BrowserWidgets widgets, Transform parent)
    {
        root = widgets.Container(parent, "Results.Card", Vector2.one);
        border = widgets.Graphic(root.transform, "Results.Card.Border", Texture2D.whiteTexture, Color.white);
        background = widgets.Graphic(root.transform, "Results.Card.Backing", Texture2D.whiteTexture, Color.white);
        cover = widgets.Graphic(root.transform, "Results.Card.Cover", Texture2D.whiteTexture, Color.white);
        var layout = border.Layout.constrained2DTransform;
        layout.zPosition = 0f;
        border.Layout.constrained2DTransform = layout;
        border.Layout._PQA(0x3f);
        layout = cover.Layout.constrained2DTransform;
        layout.zPosition = -0.04f;
        cover.Layout.constrained2DTransform = layout;
        cover.Layout._PQA(0x3f);
        labels = Enumerable.Range(0, 8).Select(i => widgets.Label(root.transform, "Results.Card.Text" + i, 3f)).ToArray();
    }

    internal void Invalidate() => bound = null;
    internal void Hide() => root.gameObject.SetActive(false);

    /// <summary>封面在左，成绩信息在右；颜色随封面异步就绪更新，搜索保留原始排名。</summary>
    internal void Render(RankedPlay play, CoverCache covers, Vector2 center, float width, float scale, float height)
    {
        root.gameObject.SetActive(true);
        BrowserWidgets.Place(root, center, new Vector2(width, Height * scale));
        float lower = -height / 2f - center.y, upper = height / 2f - center.y;
        var texture = covers.GetTexture(play.Chart, out var palette, out var error);
        border.Tint(PotentialPresentation.Color(palette.Border));
        border.Show(Vector2.zero, new Vector2(width, Height * scale), Texture2D.whiteTexture, lower, upper);
        background.Tint(PotentialPresentation.Color(palette.Background));
        background.Show(Vector2.zero, new Vector2(width - 4f * scale, (Height - 4f) * scale), Texture2D.whiteTexture, lower, upper);
        // 封面与导出图一致：左边距 18、上边距 21、边长 236（导出像素），垂直居中。
        float coverSize = 236f * Height / 278f * scale;
        cover.Show(new Vector2(-width / 2f + 136f * Height / 278f * scale, 0f), new Vector2(coverSize, coverSize),
            texture ?? Texture2D.whiteTexture, lower, upper);
        cover.Tint(texture == null ? PotentialPresentation.Color(0x17202d) : Color.white);
        if (bound != play)
        {
            var accent = PotentialPresentation.Color(PotentialPresentation.DifficultyRgb(play.Play.Difficulty));
            labels[0].Set(play.Rank.HasValue ? $"#{play.Rank}" : "#--", Color.white);
            labels[1].Set(play.Potential.HasValue ? PotentialCalculator.Format(play.Potential.Value) : "--.---", accent);
            labels[2].Set(play.Chart == null ? $"{PotentialPresentation.Difficulty(play.Play.Difficulty)}  定数不可用"
                : $"{PotentialPresentation.Difficulty(play.Play.Difficulty)}  {PotentialCalculator.FormatConstant(play.Chart.Constant)}", accent);
            labels[3].Set(play.Chart?.Title ?? play.Play.ChartId, Color.white);
            labels[4].Set(PotentialPresentation.Score(play.Play.Score), Color.white);
            labels[5].Set(PotentialPresentation.Lamp(play.Play.Lamp), play.Play.Lamp == 2 ? new Color(0.65f, 0.95f, 0.84f) : Color.white);
            labels[6].Set(play.Chart?.Artist ?? "", PotentialPresentation.Color(0xc9d4df));
            // 宽高、封面、留白及字号均按同一个比例缩放，窄列不会压缩文字区域或挤占封面。
            float k = Height / 278f * scale;
            float F(float px) => px * 0.125f * Height / 278f * scale;
            float left = -width / 2f + 274f * k, infoWidth = width - 298f * k;
            float top = Height / 2f * scale;
            NativePotentialPage.PlaceLabel(labels[2], new Vector2(left + (infoWidth - 188f * k) / 2f, top - 40f * k), infoWidth - 188f * k, F(25f), -1f);
            NativePotentialPage.PlaceLabel(labels[1], new Vector2(width / 2f - 110.5f * k, top - 40.5f * k), 173f * k, F(42f), 1f);
            NativePotentialPage.PlaceLabel(labels[3], new Vector2(left + infoWidth / 2f, top - 94f * k), infoWidth, F(33f), -1f);
            NativePotentialPage.PlaceLabel(labels[6], new Vector2(left + infoWidth / 2f, top - 131f * k), infoWidth, F(22f), -1f);
            NativePotentialPage.PlaceLabel(labels[4], new Vector2(left + infoWidth / 2f, top - 187f * k), infoWidth, F(56f), -1f);
            NativePotentialPage.PlaceLabel(labels[5], new Vector2(left + (infoWidth - 100f * k) / 2f, top - 247f * k), infoWidth - 100f * k, F(25f), -1f);
            NativePotentialPage.PlaceLabel(labels[0], new Vector2(width / 2f - 77f * k, top - 242.5f * k), 106f * k, F(33f), 1f);
            NativePotentialPage.PlaceLabel(labels[7], new Vector2(-width / 2f + 136f * k, 0f), 220f * k, F(22f));
            bound = play;
        }
        labels[7].Set(error != null ? "封面不可用" : "封面加载中", PotentialPresentation.Color(0xc9d4df));
        foreach (var label in labels)
        {
            float y = label.Text.GetComponent<Constrained2D>().constrained2DTransform.offset.y;
            bool visible = y - 19f * scale >= lower && y + 19f * scale <= upper;
            label.Text.gameObject.SetActive(visible && (label != labels[7] || texture == null));
        }
    }
}
