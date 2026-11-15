using ifapp.Game.Data;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.Scenes.Recipe;
using ifapp.Game.UI.Common;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>在卡牌页面副本中展示潜力值内容；复用原版外壳、导航和三栏图形。</summary>
internal sealed class NativePotentialPage : IDisposable
{
    private readonly RecipeScene scene;
    private readonly PotentialBrowser browser;
    private readonly InputReceiver receiver;
    private readonly BrowserWidgets widgets;
    private readonly Constrained2D viewport, content;
    private readonly UIScrollView scroll;
    private readonly UIButton scrollbar;
    private readonly ifapp.Game.UI.UiProgressVerticalSliderContainer slider;
    private readonly Constrained2D scrollbarIcon;
    private readonly Dictionary<string, UIButton> buttons = new();
    private readonly Dictionary<string, NativeLabel> labels = new();
    private readonly HashSet<string> visibleButtons = new(), visibleLabels = new();
    private readonly List<BrowserCard> cards = new();
    private readonly Dictionary<int, PixelImage> portraits = new();
    private readonly Dictionary<int, Texture2D> portraitTextures = new();
    private readonly Dictionary<int, BrowserGraphic> portraitGraphics = new();
    private readonly BrowserGraphic historyImage;
    private Texture2D? historyTexture;
    private PotentialSettingsSection? settings;
    private Constrained2D? settingsHost;
    private RankedPlay[] plays = Array.Empty<RankedPlay>();
    private Rect originalGrid, rightColumn, filterRow, sortRow, listBounds;
    private Vector2 size;
    private float maximum, cardScale, viewportWidth, viewportHeight, rowHeight;
    private int columns;
    private bool dirty = true;
    private PotentialTab? displayedTab;
    private PotentialSnapshot? displayedSnapshot;
    private string displayedQuery = "", displayedStatus = "";
    private bool displayedFocus, displayedExport;
    private readonly Il2CppSystem.Action close, openSettings, home;
    private readonly List<TopBar._Pc> tabCallbacks = new();

    /// <summary>只接入隔离后的显示副本；原版场景控制器不参与本页生命周期。</summary>
    internal NativePotentialPage(RecipeScene scene, GameObject copyRoot, InputReceiver receiver, PotentialBrowser browser)
    {
        this.scene = scene; this.receiver = receiver; this.browser = browser;
        PrepareScene(scene, copyRoot);
        widgets = new BrowserWidgets(scene, receiver);
        try
        {
            viewport = widgets.Container(widgets.Root.transform, "Scores.Viewport", Vector2.one, hitArea: true);
            content = widgets.Container(viewport.transform, "Scores.Content", Vector2.one);
            scroll = viewport.gameObject.AddComponent<UIScrollView>();
            scroll.boxCollider = viewport.GetComponent<BoxCollider>();
            scroll.contentContainer = content;
            scroll.contentContainerTransform = content.transform;
            scroll.scrollDirection = _m._lI._ZJb;
            scroll.weight = 0.22f; scroll.maxForce = 1600f;
            scroll.constrainScrollToBounds = true;
            UIManager._A._KtA(scroll, receiver);
            historyImage = widgets.Graphic(widgets.Root.transform, "Chart.History", Texture2D.whiteTexture, Color.white);
            close = (Action)(() => browser.Guard(browser.RequestClose));
            openSettings = (Action)(() => browser.Guard(() => browser.ChangeTab(PotentialTab.Settings)));
            scene.topBar._DO(receiver);
            scene.topBar.OnBackPressed = close;
            home = (Action)(() => browser.Guard(browser.RequestHome));
            scene.topBar.OnHubPressed = home;
            scene.topBar.OnSettingsOutsidePressed = openSettings;
            foreach (var tab in Enum.GetValues<PotentialTab>())
            {
                var target = tab;
                tabCallbacks.Add((Action)(() => browser.Guard(() => browser.ChangeTab(target))));
            }
            ReadNativeBounds();
            scrollbar = widgets.HitButton(widgets.Root.transform, "Scores.Scrollbar", () => { });
            scrollbar.flags |= UIButton.Flags.CanContinuedDragOutsideOfBounds | UIButton.Flags.CanSelectedButtonBePressed;
            scrollbarIcon = scene.cardInventoryLayer.scrollBarIconContainer.self;
            scrollbarIcon.transform.SetParent(scrollbar.transform, false);
            BrowserWidgets.Rebind(scrollbarIcon);
            var iconLayout = scrollbarIcon.constrained2DTransform;
            iconLayout.anchor = new Vector2(0.5f, 0.5f);
            iconLayout.parentAlignment = Constrained2DAlignment.Center;
            iconLayout.offset = Vector2.zero;
            scrollbarIcon.constrained2DTransform = iconLayout;
            slider = scrollbar.gameObject.AddComponent<ifapp.Game.UI.UiProgressVerticalSliderContainer>();
            slider.inputLayer = receiver;
            slider.line = scrollbar.constrained2D;
            slider.pipIcon = scrollbarIcon;
            slider.pipSelector = scrollbar;
            slider.pipSelectorC2D = scrollbar.constrained2D;
            slider.boxCollider = scrollbar.ButtonCollider;
            ifapp.Game.UI.UiProgressVerticalSliderContainer._ab moved = (Action<ifapp.Game.UI.UiProgressVerticalSliderContainer, float>)((_, value) => browser.Guard(() => SetScroll((1 - value) * maximum)));
            widgets.Keep(moved);
            slider._BE(moved);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>隐藏副本中的库存内容；显示与输入仅由本模组和 TopBar 控件管理。</summary>
    private static void PrepareScene(RecipeScene scene, GameObject copyRoot)
    {
        // 隐藏按钮尚未执行 Awake，先补齐同节点碰撞体；原版注销及 TopBar 登记都直接用它作字典键。
        foreach (var button in copyRoot.GetComponentsInChildren<UIButton>(true))
        {
            button.ButtonCollider = button.GetComponent<BoxCollider>();
            if (button.ButtonCollider != null) UIManager._A._ktA(button);
        }
        var inventory = scene.cardInventoryLayer;
        inventory.gridScrollView.enabled = false;
        inventory.gridScrollView.contentContainer.gameObject.SetActive(false);
        scene.overviewContainer.gameObject.SetActive(false);
        scene.craftingLayer.gameObject.SetActive(false);
        scene.inventoryLayer.gameObject.SetActive(false);
        scene.recipeSelectedLayer.gameObject.SetActive(false);
        inventory.gameObject.SetActive(true);
        inventory.cardFilterLayer.gameObject.SetActive(false);
        inventory.cardFilterLayerOutside.gameObject.SetActive(false);
        inventory.cardLimitIndicatorContainer.gameObject.SetActive(false);
        inventory.addRandomButton.gameObject.SetActive(false);
        inventory.selectMultipleButton.gameObject.SetActive(false);
        inventory.floatingDeleteButtonElements.Container.gameObject.SetActive(false);
        inventory.inventorySelectedContainer.enabled = false;
        foreach (var indicator in inventory.filterIndicatorOffOn) indicator.gameObject.SetActive(false);
        inventory.filterButton.gameObject.SetActive(false);
        inventory.sortButton.gameObject.SetActive(false);
        inventory.scrollBarButton.gameObject.SetActive(false);
        scene.guidanceButton.gameObject.SetActive(false);
        // 卡牌场景的 TopBar 未配置帮助按钮；该引用在原版资源中允许为空。
        if (scene.topBar.guideButton != null) scene.topBar.guideButton.gameObject.SetActive(false);
        foreach (var button in scene.topBar.GetComponentsInChildren<UIButton>(true))
            if (button.ButtonCollider != null) button.ButtonCollider.enabled = true;
    }

    /// <summary>三栏边界取自原版实际控件；窗口比例变化时重新读取。</summary>
    private void ReadNativeBounds()
    {
        var native = scene.cardInventoryLayer;
        // 原版列表占整页，详情和筛选父节点为零尺寸定位容器；范围须读取实际图形。
        var page = BoundsWhenVisible(native.gridScrollView.transform, "");
        var sidebar = BoundsWhenVisible(native.transform, "backing-sidebar-right");
        filterRow = BoundsWhenVisible(native.filterButton.transform, "btn-filter");
        sortRow = BoundsWhenVisible(native.sortButton.transform, "btn-sort");
        originalGrid = Rect.MinMaxRect(filterRow.xMax + Gap, page.yMin, sidebar.xMin - Gap, filterRow.yMax);
        rightColumn = Rect.MinMaxRect(sidebar.xMin, page.yMin, Math.Min(sidebar.xMax, page.xMax), filterRow.yMax);
        if (originalGrid.width <= 0 || originalGrid.height <= 0 || rightColumn.width <= 0)
            throw new InvalidOperationException($"卡牌副本三栏范围无效：内容={originalGrid}，详情={rightColumn}。");
    }

    private Rect BoundsWhenVisible(Transform parent, string path)
    {
        var node = path.Length == 0 ? parent : parent.Find(path);
        var target = node == null ? null : node.GetComponent<Constrained2D>();
        if (target == null) throw new InvalidOperationException($"卡牌副本缺少布局组件：{parent.name}/{path}。");
        var hidden = new List<GameObject>();
        for (var ancestor = target.transform; ancestor != null; ancestor = ancestor.parent)
            if (!ancestor.gameObject.activeSelf) hidden.Add(ancestor.gameObject);
        try
        {
            foreach (var root in hidden.AsEnumerable().Reverse()) root.SetActive(true);
            var bounds = widgets.Bounds(target);
            if (!float.IsFinite(bounds.xMin) || !float.IsFinite(bounds.yMin) || !float.IsFinite(bounds.width)
                || !float.IsFinite(bounds.height) || bounds.width <= 0 || bounds.height <= 0)
                throw new InvalidOperationException($"卡牌副本图形范围无效：{parent.name}/{path}={bounds}。");
            return bounds;
        }
        finally { foreach (var root in hidden) root.SetActive(false); }
    }

    internal void Invalidate() => dirty = true;

    internal void SetPlays(RankedPlay[] filtered)
    {
        plays = filtered; dirty = true;
        if (columns > 0) { SetBounds(); SetScroll(0); }
        foreach (var card in cards) card.Invalidate();
    }

    internal void Render(PotentialSnapshot snapshot, CoverCache covers, string query, string message, bool focused, bool exporting)
    {
        if (size != widgets.Size)
        {
            size = widgets.Size;
            ReadNativeBounds();
            dirty = true;
        }
        if (displayedSnapshot != snapshot || displayedQuery != query || displayedStatus != message
            || displayedFocus != focused || displayedExport != exporting) dirty = true;
        if (dirty)
        {
            visibleButtons.Clear(); visibleLabels.Clear();
            displayedSnapshot = snapshot; displayedQuery = query; displayedStatus = message;
            displayedFocus = focused; displayedExport = exporting;
            if (displayedTab != browser.Tab) { UpdateTabs(); displayedTab = browser.Tab; }
            foreach (var graphic in portraitGraphics.Values) graphic.Layout.gameObject.SetActive(false);
            historyImage.Layout.gameObject.SetActive(false);
            settings?.Hide();
            scene.cardInventoryLayer.inventorySelectedContainer.gameObject.SetActive(false);
            if (browser.Tab == PotentialTab.Scores) RenderScores(snapshot, query, focused, exporting);
            else if (browser.Tab == PotentialTab.Appearance) RenderAppearance(snapshot, query, focused);
            else RenderSettings(snapshot);
            float font = scene.cardInventoryLayer.sortButtonDescriptionText.FontSize;
            var bottom = new Rect(originalGrid.xMin, originalGrid.yMin, BodyBounds.width, filterRow.height);
            Label("Status", message, bottom, font * 0.9f, PlayHistoryImage.Muted);
            foreach (var pair in buttons) pair.Value.gameObject.SetActive(visibleButtons.Contains(pair.Key));
            foreach (var pair in labels) pair.Value.Text.gameObject.SetActive(visibleLabels.Contains(pair.Key));
            dirty = false;
            InputManager.Instance.RequestLastMouseMoveReplay();
        }
        bool list = browser.Tab == PotentialTab.Scores && !browser.HistoryExpanded;
        viewport.gameObject.SetActive(list);
        scrollbar.gameObject.SetActive(list && maximum > 0);
        if (list) { RenderCards(covers); UpdateScrollbar(); }
        widgets.SyncHitAreas();
    }

    private Rect BodyBounds => Rect.MinMaxRect(originalGrid.xMin, originalGrid.yMin, rightColumn.xMax, originalGrid.yMax);
    private float Gap => Math.Max(8f, filterRow.height * 0.25f);

    /// <summary>通过原版 TopBar 的标签接口保留斜切形状、选中态及导航命中。</summary>
    private void UpdateTabs()
    {
        string[] names = { "成绩", "头像与名片", "设置" };
        var tabs = new Il2CppSystem.Collections.Generic.List<TopBar._qc>();
        for (int i = 0; i < names.Length; i++)
            tabs.Add(new TopBar._qc { _Es = (int)browser.Tab == i, _fs = Str.TextParameters.CreateDirectString(names[i]), _Fs = tabCallbacks[i] });
        scene.topBar._FJ(receiver, tabs, true);
        for (int i = 0; i < names.Length; i++)
            foreach (var text in scene.topBar.tabButtons[i].GetComponentsInChildren<FastText.FastText>(true))
            {
                PotentialFonts.Apply(text);
                text.SetTextNonLocalized(names[i], FastText.FastText.UpdateType.DontUpdateMesh);
                text.targetLocalization = NativeLabel.CurrentLanguage();
                text.dirtyFlags |= FastText.FastText.DirtyFlags.FontChanged | FastText.FastText.DirtyFlags.TextChanged;
                text.EnsureDirtyFlagsProcessed();
            }
    }

    private void RenderScores(PotentialSnapshot snapshot, string query, bool focused, bool exporting)
    {
        float font = scene.cardInventoryLayer.sortButtonDescriptionText.FontSize;
        Button("Search", focused || query.Length > 0 ? query : "搜索谱面", filterRow, browser.FocusSearch, focused);
        Button("Sort", PotentialBrowser.SortNames[browser.Settings.BrowserSort], sortRow, browser.CycleSort);
        float top = sortRow.yMin - Gap;
        byte[] difficulties = { 0, 1, 2, 4, 8 };
        foreach (byte difficulty in difficulties)
        {
            byte target = difficulty;
            var row = new Rect(filterRow.xMin, top - filterRow.height, filterRow.width, filterRow.height);
            Button("Difficulty" + difficulty, difficulty == 0 ? "全部难度" : PotentialPresentation.Difficulty(difficulty), row,
                () => browser.SetDifficulty(target), browser.Settings.BrowserDifficulty == target);
            top = row.yMin - Gap;
        }
        Label("Count", $"{plays.Length} 张谱面", new Rect(filterRow.xMin, top - filterRow.height, filterRow.width, filterRow.height), font, PlayHistoryImage.Muted);
        var body = browser.Selected == null || browser.HistoryExpanded ? BodyBounds : originalGrid;
        float header = filterRow.height;
        float exportWidth = filterRow.width * 1.15f;
        Button("Export", exporting ? "正在导出…" : "导出 B50", new Rect(body.xMax - exportWidth, body.yMax - header, exportWidth, header), browser.RequestExport);
        float metricGap = Gap * 1.5f;
        float metricWidth = Math.Min(filterRow.width * 1.35f, (body.width - exportWidth - metricGap * 3) / 3f);
        string[] names = { "PTT", "B10", "B50" };
        decimal[] values = { snapshot.Potential, snapshot.B10Average, snapshot.B50Average };
        for (int i = 0; i < names.Length; i++)
            Label(names[i], $"{names[i]}  {PotentialCalculator.Format(values[i])}",
                new Rect(body.xMin + i * (metricWidth + metricGap), body.yMax - header, metricWidth, header), font * 1.3f,
                i == 0 ? PlayHistoryImage.Accent : PlayHistoryImage.Foreground);
        if (browser.HistoryExpanded && browser.Selected != null)
        {
            float barTop = body.yMax - header - Gap - filterRow.height;
            Button("BackToList", "返回成绩列表", new Rect(body.xMin, barTop, filterRow.width * 1.15f, filterRow.height), browser.CollapseHistory);
            var imageBounds = Rect.MinMaxRect(body.xMin, body.yMin + filterRow.height + Gap, body.xMax, barTop - Gap);
            ShowHistory(PlayHistoryImage.Full(browser.Selected, browser.History, browser.SelectedPlay), FitImage(imageBounds, PlayHistoryImage.Width, PlayHistoryImage.Height));
            HitPlot(historyImage.Layout);
            Button("PreviousPlay", "上一次", new Rect(body.xMax - 2 * filterRow.width - Gap, barTop, filterRow.width, filterRow.height), () => browser.MovePlay(-1));
            Button("NextPlay", "下一次", new Rect(body.xMax - filterRow.width, barTop, filterRow.width, filterRow.height), () => browser.MovePlay(1));
            return;
        }
        LayoutList(Rect.MinMaxRect(body.xMin, body.yMin + filterRow.height + Gap * 0.5f, body.xMax - Gap * 2, body.yMax - header - Gap));
        if (plays.Length == 0) Label("Empty", "没有符合条件的已游玩谱面", listBounds, font * 1.15f, PlayHistoryImage.Muted, 0);
        if (browser.Selected != null) RenderDetail();
    }

    /// <summary>原版右栏保留分隔与标题；卡牌内容替换为同一谱面的成绩记录图。</summary>
    private void RenderDetail()
    {
        var selected = scene.cardInventoryLayer.inventorySelectedContainer;
        selected.gameObject.SetActive(true);
        foreach (var child in selected.GetComponentsInChildren<UIButton>(true)) child.gameObject.SetActive(false);
        selected.activeContainer.gameObject.SetActive(false);
        selected.traitDetailsContainer.gameObject.SetActive(false);
        foreach (var text in selected.GetComponentsInChildren<FastText.FastText>(true)) text.gameObject.SetActive(false);
        var body = rightColumn;
        Button("Collapse", "收起详情", new Rect(body.xMax - filterRow.width - Gap, body.yMax - filterRow.height, filterRow.width, filterRow.height), browser.CollapseDetail);
        var image = PlayHistoryImage.Summary(browser.Selected!, browser.History, browser.HistoryError);
        ShowHistory(image, FitImage(Rect.MinMaxRect(body.xMin + Gap, body.yMin + filterRow.height + Gap * 2, body.xMax - Gap, body.yMax - filterRow.height - Gap), image.Width, image.Height));
        Button("ExpandHistory", "完整游玩记录", new Rect(body.xMin + Gap, body.yMin + Gap, body.width - Gap * 2, filterRow.height), browser.ExpandHistory);
    }

    /// <summary>头像与名片在原版三栏中分区展示：左栏控制名片与角色跟随，主区展示角色网格。</summary>
    private void RenderAppearance(PotentialSnapshot snapshot, string query, bool focused)
    {
        float font = scene.cardInventoryLayer.sortButtonDescriptionText.FontSize;
        float leftX = filterRow.xMin;
        float leftW = filterRow.width;
        float rowH = filterRow.height;
        float top = filterRow.yMax;
        Label("AppearanceSectionPortrait", "头像设置", new Rect(leftX, top - rowH * 0.8f, leftW, rowH * 0.8f), font * 0.95f, PlayHistoryImage.Muted);
        top -= rowH * 0.8f + Gap * 0.5f;
        Button("FollowPortrait", "跟随游戏角色", new Rect(leftX, top - rowH, leftW, rowH), () => browser.SetPortrait(-1), browser.Settings.Portrait == -1);
        top -= rowH + Gap * 1.5f;
        Label("AppearanceSectionName", "名片昵称", new Rect(leftX, top - rowH * 0.8f, leftW, rowH * 0.8f), font * 0.95f, PlayHistoryImage.Muted);
        top -= rowH * 0.8f + Gap * 0.5f;
        string currentName = focused ? query : string.IsNullOrEmpty(browser.Settings.ProfileName) ? "点击输入昵称" : browser.Settings.ProfileName;
        Button("ProfileName", currentName, new Rect(leftX, top - rowH, leftW, rowH), browser.FocusName, focused);
        top -= rowH + Gap * 1.5f;
        Label("AppearanceSectionStyle", "名片款式", new Rect(leftX, top - rowH * 0.8f, leftW, rowH * 0.8f), font * 0.95f, PlayHistoryImage.Muted);
        top -= rowH * 0.8f + Gap * 0.5f;
        string[] colors = { "阶梯 4", "阶梯 12", "阶梯 8", "阶梯 2" };
        for (int i = 0; i < colors.Length; i++)
        {
            int color = i;
            Button("Color" + i, colors[i], new Rect(leftX, top - rowH, leftW, rowH), () => browser.SetNamecardColor(color), browser.Settings.NamecardColor == i);
            top -= rowH + Gap * 0.8f;
        }
        var body = BodyBounds;
        float header = rowH;
        int selectedChar = browser.Settings.Portrait;
        string activePortrait = selectedChar < 0 ? $"当前：跟随游戏角色 ({(CharacterIdentifier)browser.PortraitCharacter})" : $"当前：{(CharacterIdentifier)selectedChar}";
        Label("AppearanceTitle", "选择角色头像", new Rect(body.xMin, body.yMax - header, body.width * 0.35f, header), font * 1.25f, PlayHistoryImage.Foreground);
        Label("AppearanceCurrent", activePortrait, new Rect(body.xMin + body.width * 0.32f, body.yMax - header, body.width * 0.43f, header), font, PlayHistoryImage.Muted);
        Label("AppearancePTT", "PTT  " + PotentialCalculator.Format(snapshot.Potential), new Rect(body.xMax - body.width * 0.25f, body.yMax - header, body.width * 0.25f, header), font * 1.25f, PlayHistoryImage.Accent, 1);
        var grid = Rect.MinMaxRect(body.xMin, body.yMin + rowH + Gap, body.xMax, body.yMax - header - Gap * 1.5f);
        var characters = Enum.GetValues<CharacterIdentifier>();
        const int cols = 9;
        int rows = (characters.Length + cols - 1) / cols;
        float labelH = rowH * 0.65f;
        float colGap = Gap * 1.2f;
        float rowGap = Gap * 1.1f;
        float cellW = (grid.width - colGap * (cols - 1)) / cols;
        float cellH = (grid.height - rowGap * (rows - 1)) / rows;
        float tile = Math.Min(cellW, cellH - labelH);
        float totalH = rows * (tile + labelH) + (rows - 1) * rowGap;
        float startY = grid.center.y + totalH / 2f;
        for (int i = 0; i < characters.Length; i++)
        {
            int character = (int)characters[i];
            int col = i % cols, row = i / cols;
            float cellX = grid.xMin + col * (cellW + colGap);
            float topY = startY - row * (tile + labelH + rowGap);
            var bounds = new Rect(cellX + (cellW - tile) / 2f, topY - tile, tile, tile);
            if (!portraitGraphics.TryGetValue(character, out var graphic))
            {
                var pixels = Portrait(character);
                var texture = Texture(pixels, "Portrait." + character);
                portraitTextures.Add(character, texture);
                portraitGraphics.Add(character, graphic = widgets.Graphic(widgets.Root.transform, "Portrait." + character, texture, Color.white));
            }
            bool chosen = selectedChar == character || (selectedChar < 0 && browser.PortraitCharacter == character);
            graphic.Tint(chosen ? Color.white : new Color(0.76f, 0.82f, 0.88f));
            graphic.Show(bounds.center, bounds.size, portraitTextures[character]);
            Hit("Portrait" + character, new Rect(cellX, topY - tile - labelH, cellW, tile + labelH), () => browser.SetPortrait(character));
            Label("PortraitName" + character, FormatCharacterName(characters[i].ToString()), new Rect(cellX + 2f, topY - tile - labelH, cellW - 4f, labelH), font * 0.82f,
                selectedChar == character ? PlayHistoryImage.Accent : chosen ? PlayHistoryImage.Foreground : PlayHistoryImage.Muted, 0);
        }
    }

    private static string FormatCharacterName(string raw) => raw switch
    {
        "GhostManFemale" => "Ghost (F)",
        "GhostManMale" => "Ghost (M)",
        "GhostManStudent" => "Ghost (S)",
        _ => raw
    };

    private void RenderSettings(PotentialSnapshot snapshot)
    {
        var body = BodyBounds;
        settingsHost ??= widgets.Container(widgets.Root.transform, "Settings.Content", Vector2.one);
        BrowserWidgets.Place(settingsHost, body.center, body.size);
        settings ??= new PotentialSettingsSection(settingsHost.transform, receiver, browser.Settings);
        settings.Show(new Rect(-body.width / 2, -body.height / 2, body.width, body.height));
    }

    private PixelImage Portrait(int character)
    {
        if (portraits.TryGetValue(character, out var cached)) return cached;
        var material = browser.Portraits?._wI((CharacterIdentifier)character, default)
            ?? throw new InvalidOperationException("角色头像资源不可用。");
        var pixels = PotentialAppearance.Capture(material);
        portraits.Add(character, pixels);
        return pixels;
    }

    private void Label(string key, string text, Rect bounds, float font, uint color, float align = -1)
    {
        if (!labels.TryGetValue(key, out var label))
            labels.Add(key, label = new NativeLabel(scene.sortButtonDescriptionText, widgets.Root.transform, key,
                new Vector2(0.5f, 0.5f), Constrained2DAlignment.Center, Vector2.zero, font));
        label.Text.gameObject.SetActive(true);
        label.Set(text, PotentialPresentation.Color(color));
        PlaceLabel(label, bounds.center, bounds.width, font, align);
        visibleLabels.Add(key);
    }

    private void Button(string key, string text, Rect bounds, Action action, bool selected = false)
    {
        if (!buttons.TryGetValue(key, out var button))
            buttons.Add(key, button = widgets.NativeButton(widgets.Root.transform, key, text, () => browser.Guard(action)));
        NativeButtons.UpdateText(button, scene.cardInventoryLayer.filterButton, text);
        button.gameObject.SetActive(true);
        BrowserWidgets.Place(button.constrained2D, bounds.center, bounds.size);
        NativeButtons.SetState(button, true, selected);
        visibleButtons.Add(key);
    }

    private void Hit(string key, Rect bounds, Action action)
    {
        if (!buttons.TryGetValue(key, out var button))
            buttons.Add(key, button = widgets.HitButton(widgets.Root.transform, key, () => browser.Guard(action)));
        button.gameObject.SetActive(true);
        BrowserWidgets.Place(button.constrained2D, bounds.center, bounds.size);
        visibleButtons.Add(key);
    }

    private void HitPlot(Constrained2D image) => Hit("HistoryPlot", widgets.Bounds(image), SelectPlotPoint);

    private void SelectPlotPoint()
    {
        var layout = BrowserWidgets.ReadLayout(buttons["HistoryPlot"].constrained2D);
        var mouse = InputManager.Instance.GetCurrentMousePositionNormalizedInUISpace();
        var offset = mouse - layout.CalculatedNormalizedPositionInScreen;
        float x = (offset.x / layout.CalculatedNormalizedSizeOfScreen.x + 0.5f) * PlayHistoryImage.Width;
        float y = (0.5f - offset.y / layout.CalculatedNormalizedSizeOfScreen.y) * PlayHistoryImage.Height;
        if (x < PlayHistoryImage.PlotLeft || x > PlayHistoryImage.PlotRight || y < PlayHistoryImage.PlotTop - 10 || y > PlayHistoryImage.PlotBottom + 10) return;
        browser.SelectPlay((int)Math.Round((x - PlayHistoryImage.PlotLeft) / (PlayHistoryImage.PlotRight - PlayHistoryImage.PlotLeft) * Math.Max(0, browser.History.Count - 1)));
    }

    private static Rect FitImage(Rect bounds, int width, int height)
    {
        float scale = Math.Min(bounds.width / width, bounds.height / height);
        var size = new Vector2(width, height) * scale;
        return new Rect(bounds.center - size / 2, size);
    }

    private void ShowHistory(PixelImage image, Rect bounds)
    {
        if (historyTexture != null) Object.Destroy(historyTexture);
        historyTexture = Texture(image, "Chart.History");
        historyImage.Show(bounds.center, bounds.size, historyTexture);
    }

    private static Texture2D Texture(PixelImage image, string name)
    {
        var texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
        { name = "PotentialSystem." + name, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        texture.LoadRawTextureData(new Il2CppStructArray<byte>(image.Rgba));
        texture.Apply(false, false);
        return texture;
    }

    /// <summary>使用原版滚动区域安排横向成绩卡，展开详情和返回时保持首个可见谱面。</summary>
    private void LayoutList(Rect bounds)
    {
        viewport.gameObject.SetActive(true);
        float first = rowHeight > 0 ? content.constrained2DTransform.offset.y / rowHeight * Math.Max(1, columns) : 0;
        int nextColumns = Math.Max(1, (int)((bounds.width + Gap) / (B50Image.CardWidth * 0.75f + Gap)));
        float nextScale = (bounds.width - (nextColumns - 1) * Gap) / (nextColumns * B50Image.CardWidth);
        bool changed = columns != nextColumns || cardScale != nextScale;
        columns = nextColumns; cardScale = nextScale; listBounds = bounds;
        viewportWidth = bounds.width; viewportHeight = bounds.height;
        rowHeight = B50Image.CardHeight * cardScale + Gap;
        BrowserWidgets.Place(viewport, bounds.center, bounds.size);
        BrowserWidgets.Place(content, content.constrained2DTransform.offset, bounds.size);
        SetBounds();
        if (changed) SetScroll(first / columns * rowHeight);
    }

    private void SetBounds()
    {
        int rows = (plays.Length + columns - 1) / Math.Max(1, columns);
        maximum = Math.Max(0, rows * rowHeight - Gap - viewportHeight);
        scroll._b = new Rect(0, 0, 0, maximum);
        scroll._sTA();
    }

    /// <summary>原版滑块图形接入游戏的纵向滑块控制器，点击、持续拖动和滚轮共用同一偏移。</summary>
    private void UpdateScrollbar()
    {
        if (maximum <= 0) return;
        scrollbar.gameObject.SetActive(true);
        scrollbarIcon.gameObject.SetActive(true);
        float thumb = viewportHeight * viewportHeight / (viewportHeight + maximum);
        float width = Math.Max(12f, Gap);
        BrowserWidgets.Place(scrollbar.constrained2D, new Vector2(listBounds.xMax + Gap, listBounds.center.y), new Vector2(width * 2, viewportHeight));
        scene.cardInventoryLayer.scrollBarIconContainer._ky(thumb);
        BrowserWidgets.Place(scrollbarIcon, Vector2.zero, new Vector2(width, thumb));
        slider._cE(1f - Mathf.Clamp(content.constrained2DTransform.offset.y, 0, maximum) / maximum);
    }

    internal void Wheel(Vector2 delta)
    {
        if (browser.Tab != PotentialTab.Scores || browser.HistoryExpanded || !float.IsFinite(delta.y) || delta.y == 0) return;
        float steps = Mathf.Abs(delta.y) >= 120 ? delta.y / 120 : delta.y;
        SetScroll(content.constrained2DTransform.offset.y - Mathf.Clamp(steps, -8, 8) * rowHeight);
    }

    private void SetScroll(float position)
    {
        content._wQA(0, Mathf.Clamp(position, 0, maximum));
        scroll._gkb = Vector3.zero;
    }

    private void RenderCards(CoverCache covers)
    {
        float offset = Mathf.Clamp(content.constrained2DTransform.offset.y, 0, maximum);
        int startRow = Math.Max(0, (int)(offset / rowHeight));
        int count = Math.Max(0, Math.Min(plays.Length - startRow * columns, ((int)Math.Ceiling(viewportHeight / rowHeight) + 1) * columns));
        while (cards.Count < count) cards.Add(new BrowserCard(widgets, viewport.transform, play => browser.Guard(() => browser.Select(play))));
        for (int i = 0; i < cards.Count; i++)
        {
            if (i >= count) { cards[i].Hide(); continue; }
            int index = startRow * columns + i;
            float width = B50Image.CardWidth * cardScale;
            var center = new Vector2(-viewportWidth / 2 + index % columns * (width + Gap) + width / 2,
                viewportHeight / 2 - index / columns * rowHeight - B50Image.CardHeight / 2f * cardScale + offset);
            cards[i].Render(plays[index], covers, center, width, cardScale, viewportHeight);
        }
    }

    /// <summary>原生单行文字按实测宽度缩小；选歌与结算展示共用。</summary>
    internal static void PlaceLabel(NativeLabel label, Vector2 center, float width, float fontSize, float align = 0)
    {
        var text = label.Text;
        text.presention = (text.presention & ~FastText.FastText.FastTextPresentation.AutoLineBreak) | FastText.FastText.FastTextPresentation.IgnoreLinebreak;
        var bounds = label.Measure(fontSize);
        float scale = bounds.width > width && bounds.width > 0f ? width / bounds.width : 1f;
        if (scale < 1f) bounds = label.Measure(fontSize * scale);
        var layout = text.GetComponent<Constrained2D>();
        if (bounds.width <= 0f || bounds.height <= 0f)
        {
            BrowserWidgets.Place(layout, center, layout.constrained2DTransform.sizeInPixels);
            return;
        }
        var target = center + new Vector2(align * Math.Max(0f, width - bounds.width) / 2f, 0f);
        var origin = target - bounds.center;
        BrowserWidgets.Place(layout, origin, layout.constrained2DTransform.sizeInPixels);
    }

    public void Dispose()
    {
        try
        {
            if (scene != null && scene.topBar != null)
            {
                foreach (var button in scene.topBar.GetComponentsInChildren<UIButton>(true))
                    if (button.ButtonCollider != null) UIManager._A?._ktA(button);
                scene.topBar.OnBackPressed = null;
                scene.topBar.OnHubPressed = null;
                scene.topBar.OnSettingsOutsidePressed = null;
            }
            if (scroll != null) UIManager._A?._ItA(scroll);
        }
        finally
        {
            settings?.Dispose();
            foreach (var label in labels.Values) label.Dispose();
            widgets.Dispose();
            foreach (var card in cards) card.Dispose();
            foreach (var texture in portraitTextures.Values) Object.Destroy(texture);
            if (historyTexture != null) Object.Destroy(historyTexture);
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
        button.flags = UIButton.Flags.DoesDragCauseCancel | UIButton.Flags.CanCauseCursorHover;
        var layout = button.constrained2D.constrained2DTransform;
        layout.zPosition = -0.08f;
        button.constrained2D.constrained2DTransform = layout;
        button.constrained2D._PQA(0x3f);
    }

    internal void Invalidate() => bound = null;
    internal void Hide() { graphic.Layout.gameObject.SetActive(false); button.gameObject.SetActive(false); }

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
