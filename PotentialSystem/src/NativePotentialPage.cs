using ifapp.Game.Data;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.UI;
using ifapp.Game.UI.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>三栏成绩页面；静态面板按设计尺寸绘图，命中、拖动与滚动仍由游戏原生组件管理。</summary>
internal sealed class NativePotentialPage : IDisposable
{
    private const int Width = 1700, Height = 1000;
    private readonly BrowserWidgets widgets;
    private readonly PotentialBrowser browser;
    private readonly SongSelectScene scene;
    private readonly BrowserGraphic frame, line, pip;
    private readonly Constrained2D viewport, content;
    private readonly UIScrollView scroll;
    private readonly UIButton track;
    private readonly UiProgressVerticalSliderContainer slider;
    private readonly Dictionary<string, UIButton> buttons = new();
    private readonly HashSet<string> visibleButtons = new();
    private readonly Dictionary<int, PixelImage> portraits = new();
    private readonly List<BrowserCard> cards = new();
    private Texture2D? frameTexture;
    private RankedPlay[] plays = Array.Empty<RankedPlay>();
    private PotentialSnapshot? displayedSnapshot;
    private Vector2 size;
    private float scale, cardScale, viewportHeight, viewportWidth, maximum;
    private int columns;
    private bool dirty = true;
    private string hoveredButton = "";
    private string displayedQuery = "", displayedStatus = "";
    private bool displayedFocus, displayedExport;

    /// <summary>拥有本页原生控件、位图和可见卡片池；初始化失败时完整回收。</summary>
    internal NativePotentialPage(SongSelectScene scene, InputReceiver receiver, PotentialBrowser browser)
    {
        this.scene = scene;
        this.browser = browser;
        widgets = new BrowserWidgets(scene, receiver);
        try
        {
            var parent = widgets.Root.transform;
            frame = widgets.Graphic(parent, "Results.Frame", Texture2D.whiteTexture, Color.white);
            frame.Depth(0.1f);
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
            track = widgets.HitButton(parent, "Results.Scrollbar", () => { });
            track.flags |= UIButton.Flags.CanContinuedDragOutsideOfBounds | UIButton.Flags.CanSelectedButtonBePressed;
            line = widgets.Graphic(track.transform, "Results.Scrollbar.Line", Texture2D.whiteTexture, PotentialPresentation.Color(0x233845));
            pip = widgets.Graphic(track.transform, "Results.Scrollbar.Thumb", Texture2D.whiteTexture, PotentialPresentation.Color(0x83b9c2));
            line.Depth(-0.04f); pip.Depth(-0.06f);
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
        }
        catch { Dispose(); throw; }
    }

    /// <summary>状态变化后重画面板；滚动帧只移动卡片，不重复生成大图。</summary>
    internal void Invalidate() => dirty = true;

    /// <summary>筛选变化时回到首行，原有完整 PTT 排名保持不变。</summary>
    internal void SetPlays(RankedPlay[] filtered)
    {
        plays = filtered;
        dirty = true;
        if (columns > 0) { SetBounds(); SetScroll(0); }
        foreach (var card in cards) card.Invalidate();
    }

    /// <summary>将设计图左上角坐标换成页面中心偏移；统一缩放保留卡片比例。</summary>
    private Vector2 Center(float x, float y, float width, float height) => new Vector2(x + width / 2 - Width / 2f, Height / 2f - y - height / 2) * scale;

    /// <summary>只在布局或内容变化时生成面板；输入区域和图片使用相同矩形。</summary>
    internal void Render(PotentialSnapshot snapshot, CoverCache covers, string query, string message, bool focused, bool exporting)
    {
        var available = widgets.Size;
        if (size != available)
        {
            size = available;
            scale = Math.Min(size.x / Width, size.y / Height);
            dirty = true;
        }
        if (displayedSnapshot != snapshot || displayedQuery != query || displayedStatus != message
            || displayedFocus != focused || displayedExport != exporting)
            dirty = true;
        if (dirty)
        {
            displayedSnapshot = snapshot; displayedQuery = query; displayedStatus = message;
            displayedFocus = focused; displayedExport = exporting;
            DrawFrame(snapshot, query, message, focused, exporting);
            dirty = false;
            InputManager.Instance?.RequestLastMouseMoveReplay();
        }
        bool list = browser.Tab == PotentialTab.Scores && !browser.HistoryExpanded;
        viewport.gameObject.SetActive(list);
        track.gameObject.SetActive(list && maximum > 0);
        if (list)
        {
            slider._cE(maximum > 0 ? 1f - content.constrained2DTransform.offset.y / maximum : 1f);
            RenderCards(covers);
        }
        widgets.SyncHitAreas();
    }

    /// <summary>生成当前页签的可见内容与按钮；没有显示的控件同时退出命中。</summary>
    private void DrawFrame(PotentialSnapshot snapshot, string query, string message, bool focused, bool exporting)
    {
        visibleButtons.Clear();
        using var image = new WindowsBitmap(Width, Height);
        image.Fill(0, 0, Width, Height, 0x0b1822);
        image.Fill(30, 101, 1640, 2, 0x294650);
        Button(image, "Close", (30, 25, 145, 52), "‹ 返回", browser.RequestClose);
        Button(image, "Scores", (200, 25, 220, 52), "成绩", () => browser.ChangeTab(PotentialTab.Scores), browser.Tab == PotentialTab.Scores);
        Button(image, "Appearance", (440, 25, 270, 52), "头像与名片", () => browser.ChangeTab(PotentialTab.Appearance), browser.Tab == PotentialTab.Appearance);
        Button(image, "Settings", (730, 25, 220, 52), "设置", () => browser.ChangeTab(PotentialTab.Settings), browser.Tab == PotentialTab.Settings);
        Button(image, "Cross", (1600, 25, 60, 52), "×", browser.RequestClose, color: 0xff6573);
        image.RoundedFill((30, 125, 240, 820), 7, 0x10232d);
        if (browser.Tab == PotentialTab.Scores)
        {
            DrawFilters(image, query, focused);
            if (browser.HistoryExpanded && browser.Selected != null)
            {
                Button(image, "BackToList", (310, 125, 240, 48), "‹ 返回成绩列表", browser.CollapseHistory);
                Button(image, "PreviousPlay", (1220, 125, 195, 48), "‹ 上一次", () => browser.MovePlay(-1));
                Button(image, "NextPlay", (1435, 125, 195, 48), "下一次 ›", () => browser.MovePlay(1));
                var history = PlayHistoryImage.Full(browser.Selected, browser.History, browser.SelectedPlay);
                image.Image(history, 310, 190, 1320, 750);
                Hit("HistoryPlot", (310, 190, 1320, 750), SelectPlotPoint);
            }
            else DrawScores(image, snapshot, exporting);
        }
        else if (browser.Tab == PotentialTab.Appearance) DrawAppearance(image, snapshot, query, focused);
        else DrawSettings(image, snapshot);
        image.Text(message, 30, 957, 1620, 30, 20, PlayHistoryImage.Muted);
        foreach (var pair in buttons) pair.Value.gameObject.SetActive(visibleButtons.Contains(pair.Key));
        Upload(image.ToPixels());
    }

    /// <summary>筛选、排序和来源集中在左列，文本区域与输入框严格共用尺寸。</summary>
    private void DrawFilters(WindowsBitmap image, string query, bool focused)
    {
        image.Text("搜索谱面", 50, 145, 195, 35, 21, PlayHistoryImage.Muted);
        Button(image, "Search", (45, 194, 210, 58), string.IsNullOrEmpty(query) && !focused ? "歌名 / 作者 / 难度" : query, browser.FocusSearch, focused);
        image.Text("难度", 50, 281, 195, 35, 21, PlayHistoryImage.Muted);
        var difficulties = new byte[] { 0, 1, 2, 4, 8 };
        for (int i = 0; i < difficulties.Length; i++)
        {
            byte value = difficulties[i];
            Button(image, "Difficulty" + value, (45, 327 + i * 60, 210, 48), value == 0 ? "全部难度" : PotentialPresentation.Difficulty(value),
                () => browser.SetDifficulty(value), browser.Settings.BrowserDifficulty == value);
        }
        image.Text("排序", 50, 653, 195, 35, 21, PlayHistoryImage.Muted);
        Button(image, "Sort", (45, 699, 210, 52), PotentialBrowser.SortNames[browser.Settings.BrowserSort], browser.CycleSort);
        image.Text("定数来源", 50, 786, 195, 35, 21, PlayHistoryImage.Muted);
        Button(image, "Source", (45, 835, 210, 52), CommunityConstants.DisplayName(browser.Settings.Source), browser.CycleSource);
        image.Text($"共 {plays.Length} 张谱面", 48, 903, 210, 30, 19, PlayHistoryImage.Muted);
    }

    /// <summary>详情按需占用右列，重排时锚定原首行谱面；完整记录返回不改变滚动偏移。</summary>
    private void DrawScores(WindowsBitmap image, PotentialSnapshot snapshot, bool exporting)
    {
        bool detail = browser.Selected != null;
        int width = detail ? 910 : 1320;
        image.RoundedFill((310, 125, width, 108), 7, 0x152c38);
        var values = new[] { ("PTT", snapshot.Potential), ("B10", snapshot.B10Average), ("B50", snapshot.B50Average) };
        for (int i = 0; i < values.Length; i++)
        {
            int x = 334 + i * width / 3;
            image.Text(values[i].Item1, x, 139, width / 3 - 25, 30, 21, PlayHistoryImage.Muted);
            image.Text(PotentialCalculator.Format(values[i].Item2), x, 175, width / 3 - 25, 46, 39, i == 0 ? PlayHistoryImage.Accent : PlayHistoryImage.Foreground, true);
        }
        image.Text("成绩", 310, 260, 120, 40, 27, PlayHistoryImage.Foreground, true);
        image.Text(PotentialBrowser.SortNames[browser.Settings.BrowserSort], 430, 265, 280, 35, 20, PlayHistoryImage.Muted);
        Button(image, "Export", (310 + width - 240, 254, 240, 48), exporting ? "正在导出…" : "导出 B50 图片", browser.RequestExport);
        LayoutList(width);
        if (plays.Length == 0) image.Text("没有符合条件的已游玩谱面", 340, 420, width - 60, 60, 29, PlayHistoryImage.Muted);
        if (detail)
        {
            image.RoundedFill((1240, 125, 390, 820), 7, PlayHistoryImage.Background);
            image.Text("谱面详情", 1260, 144, 260, 35, 24, PlayHistoryImage.Foreground, true);
            Button(image, "CollapseDetail", (1560, 135, 50, 44), "›", browser.CollapseDetail);
            image.Image(PlayHistoryImage.Summary(browser.Selected!, browser.History, browser.HistoryError), 1245, 194, 380, 650);
            Button(image, "ExpandHistory", (1260, 862, 350, 52), "查看完整游玩记录 ›", browser.ExpandHistory, true);
        }
    }

    /// <summary>只使用游戏已有角色头像；选择不改变原生存档角色。</summary>
    private void DrawAppearance(WindowsBitmap image, PotentialSnapshot snapshot, string query, bool focused)
    {
        image.Text("个性化", 50, 145, 195, 35, 24, PlayHistoryImage.Foreground, true);
        image.Text("头像", 50, 215, 195, 35, 22, PlayHistoryImage.Accent);
        image.Text("名片", 50, 269, 195, 35, 22, PlayHistoryImage.Muted);
        image.Text("选择角色头像", 310, 135, 500, 45, 30, PlayHistoryImage.Foreground, true);
        Button(image, "FollowPortrait", (1320, 130, 310, 50), "跟随游戏当前角色", () => browser.SetPortrait(-1), browser.Settings.Portrait == -1);
        var characters = Enum.GetValues<CharacterIdentifier>();
        for (int i = 0; i < characters.Length; i++)
        {
            int character = (int)characters[i];
            int x = 310 + i % 9 * 147, y = 207 + i / 9 * 161;
            image.RoundedFill((x, y, 133, 145), 7, browser.Settings.Portrait == character ? 0x40696eU : 0x1c3340U);
            image.Image(Portrait(character), x + 18, y + 6, 97, 97);
            image.Text(characters[i].ToString(), x + 6, y + 108, 121, 28, 17, PlayHistoryImage.Foreground);
            Hit("Portrait" + character, (x, y, 133, 145), () => browser.SetPortrait(character));
        }
        image.Text("名片昵称", 310, 716, 200, 35, 23, PlayHistoryImage.Muted);
        string name = focused ? query : browser.Settings.ProfileName;
        Button(image, "ProfileName", (310, 767, 580, 55), string.IsNullOrEmpty(name) ? "点击输入昵称，留空使用角色名称" : name, browser.FocusName, focused);
        image.Text("配色", 310, 847, 100, 35, 23, PlayHistoryImage.Muted);
        string[] names = { "青", "紫", "红", "蓝" };
        for (int i = 0; i < names.Length; i++)
        {
            int color = i;
            Button(image, "NamecardColor" + i, (420 + i * 115, 840, 95, 50), names[i], () => browser.SetNamecardColor(color), browser.Settings.NamecardColor == i);
        }
        int selected = browser.PortraitCharacter;
        image.RoundedFill((960, 728, 670, 180), 12, PotentialAppearance.NamecardColors[browser.Settings.NamecardColor]);
        image.Image(Portrait(selected), 980, 743, 145, 145);
        image.Text(string.IsNullOrWhiteSpace(browser.Settings.ProfileName) ? ((CharacterIdentifier)selected).ToString() : browser.Settings.ProfileName,
            1145, 752, 460, 58, 32, PlayHistoryImage.Foreground, true);
        image.Text($"PTT {PotentialCalculator.Format(snapshot.Potential)}", 1145, 829, 460, 45, 30, PlayHistoryImage.Accent);
    }

    /// <summary>设置页复用已有来源，不创建第二套定数开关。</summary>
    private void DrawSettings(WindowsBitmap image, PotentialSnapshot snapshot)
    {
        image.Text("潜力值系统", 50, 145, 195, 35, 23, PlayHistoryImage.Foreground, true);
        image.Text("定数与计算", 50, 215, 195, 35, 21, PlayHistoryImage.Accent);
        image.Text("谱面定数来源", 330, 150, 600, 45, 31, PlayHistoryImage.Foreground, true);
        Button(image, "SettingsSource", (1050, 145, 540, 55), CommunityConstants.DisplayName(browser.Settings.Source) + "  ›", browser.CycleSource, true);
        image.Text("切换后重新计算 PTT、排名及分享图，保留原始游玩分数。", 330, 230, 1250, 45, 24, PlayHistoryImage.Muted);
        image.Text("官方等级：使用游戏当前等级。", 330, 330, 1250, 45, 25, PlayHistoryImage.Foreground);
        image.Text("安小璃：每次启动后台联网更新一次，超时后沿用本地副本。", 330, 410, 1250, 45, 25, PlayHistoryImage.Foreground);
        image.Text("萌泪酱：使用 v6 定数，新谱面在后台离线计算。", 330, 490, 1250, 45, 25, PlayHistoryImage.Foreground);
        image.Text("PTT =（B50 总和 + B10 总和）/ 60", 330, 625, 1250, 45, 28, PlayHistoryImage.Accent, true);
        image.Text($"当前曲库：{snapshot.Catalog.Count} 张谱面", 330, 705, 1250, 45, 24, PlayHistoryImage.Muted);
    }

    /// <summary>头像材质可能来自图集，按原生 UV 复制，临时 GPU/CPU 资源全部释放。</summary>
    private PixelImage Portrait(int character)
    {
        if (portraits.TryGetValue(character, out var cached)) return cached;
        var mapping = scene.songSelectRemembranceContainer.characterUniversalIconMapping;
        var material = mapping._wI((CharacterIdentifier)character, default);
        var pixels = PotentialAppearance.Capture(material);
        portraits.Add(character, pixels);
        return pixels;
    }

    /// <summary>画出的按钮与原生点击范围严格使用同一设计矩形。</summary>
    private void Button(WindowsBitmap image, string key, (int X, int Y, int Width, int Height) box,
        string label, Action clicked, bool selected = false, uint color = PlayHistoryImage.Foreground)
    {
        image.RoundedFill(box, 5, hoveredButton == key ? 0x466c76U : selected ? 0x355960U : 0x1c3340U);
        image.Text(label, box.X + 14, box.Y + 5, box.Width - 28, box.Height - 10, 23, selected ? PlayHistoryImage.Accent : color);
        Hit(key, box, clicked);
    }

    /// <summary>按钮仅创建一次；后续重画更新位置和可见性，回调统一经过页面异常边界。</summary>
    private void Hit(string key, (int X, int Y, int Width, int Height) box, Action clicked)
    {
        if (!buttons.TryGetValue(key, out var button))
        {
            button = widgets.HitButton(widgets.Root.transform, "Results." + key, () => browser.Guard(clicked), hovered =>
            {
                string next = hovered ? key : hoveredButton == key ? "" : hoveredButton;
                if (hoveredButton == next) return;
                hoveredButton = next;
                dirty = true;
            });
            buttons.Add(key, button);
        }
        button.gameObject.SetActive(true);
        BrowserWidgets.Place(button.constrained2D, Center(box.X, box.Y, box.Width, box.Height), new Vector2(box.Width, box.Height) * scale);
        visibleButtons.Add(key);
    }

    /// <summary>原生投影提供点击点在图像中的比例；点击曲线区按横轴选择最近一局。</summary>
    private void SelectPlotPoint()
    {
        var layout = BrowserWidgets.ReadLayout(buttons["HistoryPlot"].constrained2D);
        var mouse = InputManager.Instance.GetCurrentMousePositionNormalizedInUISpace();
        var offset = mouse - layout.CalculatedNormalizedPositionInScreen;
        float x = (offset.x / layout.CalculatedNormalizedSizeOfScreen.x + 0.5f) * PlayHistoryImage.Width;
        float y = (0.5f - offset.y / layout.CalculatedNormalizedSizeOfScreen.y) * PlayHistoryImage.Height;
        if (x < PlayHistoryImage.PlotLeft || x > PlayHistoryImage.PlotRight || y < PlayHistoryImage.PlotTop - 10 || y > PlayHistoryImage.PlotBottom + 10) return;
        float position = (x - PlayHistoryImage.PlotLeft) / (PlayHistoryImage.PlotRight - PlayHistoryImage.PlotLeft);
        browser.SelectPlay((int)Math.Round(position * Math.Max(0, browser.History.Count - 1)));
    }

    /// <summary>按显示宽度安排两列或三列，不改变导出卡片的长宽比。</summary>
    private void LayoutList(int width)
    {
        viewport.gameObject.SetActive(true);
        float oldRow = cardScale > 0 ? content.constrained2DTransform.offset.y / ((B50Image.CardHeight + B50Image.Gap) * cardScale) : 0;
        int nextColumns = width < 1000 ? 2 : 3;
        float newWidth = (width - 30) * scale;
        float nextScale = newWidth / (nextColumns * B50Image.CardWidth + (nextColumns - 1) * B50Image.Gap);
        bool changed = columns != nextColumns || cardScale != nextScale;
        float first = oldRow * Math.Max(1, columns);
        columns = nextColumns; viewportWidth = newWidth; viewportHeight = 625 * scale; cardScale = nextScale;
        BrowserWidgets.Place(viewport, Center(310, 320, width - 30, 625), new Vector2(viewportWidth, viewportHeight));
        // 面板重画和返回详情会重复布局，必须保留滚动节点的已有偏移。
        BrowserWidgets.Place(content, content.constrained2DTransform.offset, new Vector2(viewportWidth, viewportHeight));
        track.gameObject.SetActive(true);
        BrowserWidgets.Place(track.constrained2D, Center(310 + width - 22, 320, 22, 625), new Vector2(22, 625) * scale);
        line.Show(Vector2.zero, new Vector2(7, 585) * scale, Texture2D.whiteTexture);
        pip.Show(Vector2.zero, new Vector2(16, 54) * scale, Texture2D.whiteTexture);
        SetBounds();
        if (changed) SetScroll(first / columns * (B50Image.CardHeight + B50Image.Gap) * cardScale);
    }

    /// <summary>滚动、滑块与卡片池共享同一内容边界。</summary>
    private void SetBounds()
    {
        int rows = (plays.Length + columns - 1) / Math.Max(1, columns);
        maximum = Math.Max(0, rows * (B50Image.CardHeight + B50Image.Gap) * cardScale - B50Image.Gap * cardScale - viewportHeight);
        scroll._b = new Rect(0, 0, 0, maximum);
        scroll._sTA();
        NativeButtons.SetState(track, maximum > 0);
    }

    /// <summary>仅成绩列表响应滚轮，详情和设置不误滚动隐藏列表。</summary>
    internal void Wheel(Vector2 delta)
    {
        if (browser.Tab != PotentialTab.Scores || browser.HistoryExpanded || !float.IsFinite(delta.y) || delta.y == 0) return;
        float steps = Mathf.Abs(delta.y) >= 120 ? delta.y / 120 : delta.y;
        SetScroll(content.constrained2DTransform.offset.y - Mathf.Clamp(steps, -8, 8) * (B50Image.CardHeight + B50Image.Gap) * cardScale);
    }

    /// <summary>手动移动时取消旧拖动惯性。</summary>
    private void SetScroll(float position)
    {
        content._wQA(0, Mathf.Clamp(position, 0, maximum));
        scroll._gkb = Vector3.zero;
    }

    /// <summary>只建立可见卡片；卡片图形与点击区域一起裁切。</summary>
    private void RenderCards(CoverCache covers)
    {
        float rowHeight = (B50Image.CardHeight + B50Image.Gap) * cardScale;
        float offset = Mathf.Clamp(content.constrained2DTransform.offset.y, 0, maximum);
        int startRow = Math.Max(0, (int)(offset / rowHeight));
        int visibleRows = (int)Math.Ceiling(viewportHeight / rowHeight) + 1;
        int count = Math.Max(0, Math.Min(plays.Length - startRow * columns, visibleRows * columns));
        while (cards.Count < count) cards.Add(new BrowserCard(widgets, viewport.transform, play => browser.Guard(() => browser.Select(play))));
        for (int i = 0; i < cards.Count; i++)
        {
            if (i >= count) { cards[i].Hide(); continue; }
            int index = startRow * columns + i;
            float width = B50Image.CardWidth * cardScale;
            var center = new Vector2(-viewportWidth / 2 + index % columns * (width + B50Image.Gap * cardScale) + width / 2,
                viewportHeight / 2 - index / columns * rowHeight - B50Image.CardHeight / 2f * cardScale + offset);
            cards[i].Render(plays[index], covers, center, width, cardScale, viewportHeight);
        }
    }

    /// <summary>面板纹理尺寸固定，只在内容变化时上传；旧纹理随页面销毁。</summary>
    private void Upload(PixelImage pixels)
    {
        frameTexture ??= new Texture2D(pixels.Width, pixels.Height, TextureFormat.RGBA32, false)
        { name = "PotentialSystem.Page", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        frameTexture.LoadRawTextureData(new Il2CppStructArray<byte>(pixels.Rgba));
        frameTexture.Apply(false, false);
        frame.Show(Vector2.zero, new Vector2(Width, Height) * scale, frameTexture);
    }

    /// <summary>原生单行文字按实测宽度缩小；选歌与结算展示共用此布局入口。</summary>
    internal static void PlaceLabel(NativeLabel label, Vector2 center, float width, float fontSize, float align = 0)
    {
        var text = label.Text;
        text.presention = (text.presention & ~FastText.FastText.FastTextPresentation.AutoLineBreak) | FastText.FastText.FastTextPresentation.IgnoreLinebreak;
        text.SetFontSize(fontSize, FastText.FastText.UpdateType.DontUpdateMesh);
        text.EnsureDirtyFlagsProcessed();
        var layout = text.GetComponent<Constrained2D>();
        BrowserWidgets.Place(layout, center, layout.constrained2DTransform.sizeInPixels);
        float measured = layout._mrA().x;
        if (measured > width && measured > 0)
        {
            text.SetFontSize(fontSize * width / measured, FastText.FastText.UpdateType.Default);
            layout._PQA(0x3f); measured = width;
        }
        if (align != 0 && measured > 0)
            BrowserWidgets.Place(layout, center + new Vector2(align * (width - measured) / 2, 0), layout.constrained2DTransform.sizeInPixels);
    }

    /// <summary>先注销原生滚动与按钮，再回收本页纹理和托管头像副本。</summary>
    public void Dispose()
    {
        try { if (scroll != null) UIManager._A?._ItA(scroll); }
        finally
        {
            widgets.Dispose();
            foreach (var card in cards) card.Dispose();
            cards.Clear(); portraits.Clear();
            if (frameTexture != null) Object.Destroy(frameTexture);
            frameTexture = null;
        }
    }
}

/// <summary>仅为可见行保留卡片纹理；图形和原生点击区域共用裁切后的矩形。</summary>
internal sealed class BrowserCard : IDisposable
{
    private readonly BrowserGraphic graphic;
    private readonly UIButton button;
    private Texture2D? texture;
    private RankedPlay? bound;
    private PixelImage? cover;
    private string? coverError;

    /// <summary>点击回调读取当前绑定成绩，池化复用后不会指向旧谱面。</summary>
    internal BrowserCard(BrowserWidgets widgets, Transform parent, Action<RankedPlay> selected)
    {
        graphic = widgets.Graphic(parent, "Results.Card", Texture2D.whiteTexture, Color.white);
        button = widgets.HitButton(parent, "Results.CardHit", () => { if (bound != null) selected(bound); },
            hovered => graphic.Tint(hovered ? new Color(0.82f, 1f, 1f) : Color.white));
        // 卡片允许原生滚动视图接收按下，拖动超过阈值后取消本次点击。
        button.flags = UIButton.Flags.DoesDragCauseCancel | UIButton.Flags.CanCauseCursorHover;
        var layout = button.constrained2D.constrained2DTransform;
        layout.zPosition = -0.08f;
        button.constrained2D.constrained2DTransform = layout;
        button.constrained2D._PQA(0x3f);
    }

    internal void Invalidate() => bound = null;
    internal void Hide() { graphic.Layout.gameObject.SetActive(false); button.gameObject.SetActive(false); }

    /// <summary>只在成绩或封面变化后重画；拖动时只移动位置和 UV。</summary>
    internal void Render(RankedPlay play, CoverCache covers, Vector2 center, float width, float scale, float height)
    {
        var pixels = covers.GetPixels(play.Chart, out var error);
        if (bound != play || !ReferenceEquals(cover, pixels) || coverError != error)
        {
            var card = B50Image.RenderCard(play, pixels, error == null ? "封面加载中" : "封面不可用");
            texture ??= new Texture2D(card.Width, card.Height, TextureFormat.RGBA32, false)
            { name = "PotentialSystem.ResultCard", hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.LoadRawTextureData(new Il2CppStructArray<byte>(card.Rgba));
            texture.Apply(false, false);
            bound = play; cover = pixels; coverError = error;
        }
        graphic.Show(center, new Vector2(width, B50Image.CardHeight * scale), texture!, -height / 2, height / 2);
        button.gameObject.SetActive(graphic.Layout.gameObject.activeSelf);
        if (button.gameObject.activeSelf)
            BrowserWidgets.Place(button.constrained2D, graphic.Layout.constrained2DTransform.offset, graphic.Layout.constrained2DTransform.sizeInPixels);
    }

    public void Dispose() { if (texture != null) Object.Destroy(texture); texture = null; }
}
