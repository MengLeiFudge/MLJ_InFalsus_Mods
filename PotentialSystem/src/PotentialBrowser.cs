using ifapp.Game.Input;
using ifapp.Game.Scenes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>可滚动的全部最佳成绩页面，拥有输入归属和封面会话；B50 导出只在点击后执行。</summary>
internal sealed class PotentialBrowser : IDisposable
{
    /// <summary>页面的数据、宿主场景及共享徽章。</summary>
    private PotentialSnapshot? snapshot, exporting;
    private readonly PotentialBadges badges;
    private SongSelectScene? owner;
    private InputReceiver? receiver;
    private CoverCache? covers;
    /// <summary>滚动、搜索与过滤结果不限制 B50 以外的成绩。</summary>
    private Vector2 scroll;
    private string search = "", filteredSearch = "";
    private PotentialSnapshot? filteredSnapshot;
    private RankedPlay[] filtered = Array.Empty<RankedPlay>();
    /// <summary>字体与 GUI 样式由本页面建立，关闭时释放字体资产。</summary>
    private Font? font;
    private GUIStyle? titleStyle, textStyle, smallStyle, numberStyle, buttonStyle, searchStyle, scrollbarStyle;
    private float styledScale;
    /// <summary>打开延后一帧，避免入口鼠标释放继续触发页面按钮。</summary>
    private int requestedFrame = -1;
    /// <summary>等待导出封面的有限时间与明确反馈。</summary>
    private float exportStarted;
    private string status = "每个已游玩谱面显示其最佳表现，可滚动浏览全部成绩。";
    /// <summary>持有输入时页面才可见。</summary>
    internal bool IsOpen => receiver != null;

    /// <summary>共享徽章的寿命由控制器保证，页面不销毁它们。</summary>
    internal PotentialBrowser(PotentialBadges badges) => this.badges = badges;

    /// <summary>从原版入口预约打开，数据缺失或非选歌输入头时不抢占输入。</summary>
    internal void RequestOpen(SongSelectScene scene, PotentialSnapshot? data)
    {
        if (IsOpen || data == null || InputManager.Instance?.IsHeadOfStack(scene) != true)
            return;
        owner = scene;
        snapshot = data;
        requestedFrame = Time.frameCount;
    }

    /// <summary>更新输入生命周期与异步导出；页面消失或账号切换时取消未完成的导出。</summary>
    internal void Update(SongSelectScene? scene, PotentialSnapshot? data)
    {
        if (owner == null || owner != scene || !owner.isActiveAndEnabled || data == null)
        {
            Close();
            return;
        }
        snapshot = data;
        if (requestedFrame >= 0 && Time.frameCount > requestedFrame)
        {
            requestedFrame = -1;
            if (InputManager.Instance?.IsHeadOfStack(owner) != true)
                return;
            var root = new GameObject("InFalsus.PotentialSystem.BrowserInput");
            try
            {
                root.transform.SetParent(owner.transform, false);
                receiver = root.AddComponent<PotentialInputReceiver>();
                covers = new CoverCache();
                InputManager.Instance.TakeInputControl(receiver);
                scroll = Vector2.zero;
                search = "";
                status = "每个已游玩谱面显示其最佳表现，可滚动浏览全部成绩。";
            }
            catch { Close(); if (root != null) Object.Destroy(root); throw; }
        }
        if (!IsOpen)
            return;
        if (InputManager.Instance?.IsHeadOfStack(receiver) != true)
        {
            Close();
            return;
        }
        if (exporting == null)
            return;
        try
        {
            bool ready = covers!.Prepare(exporting.B50, out var images, out var error);
            if (error != null)
                throw new InvalidOperationException("封面加载失败，未导出：" + error);
            if (!ready)
            {
                status = $"正在准备 B50 封面：{images.Count}/{exporting.B50.Count}，关闭页面可取消。";
                if (Time.realtimeSinceStartup - exportStarted > 40f)
                    throw new TimeoutException("封面加载超时，请稍后再次导出。");
                return;
            }
            var badge = badges.Pixels(PotentialBadges.Tier(exporting.Potential));
            byte[] dib = B50Image.Render(exporting, images, badge);
            ClipboardImage.Copy(dib);
            status = $"B50 图片已复制到剪贴板（{exporting.B50.Count} 项），可直接粘贴。";
            exporting = null;
        }
        catch (Exception ex)
        {
            exporting = null;
            status = "导出失败：" + ex.Message;
            Plugin.Logger.LogError($"B50 图片导出失败：{ex}");
        }
    }

    /// <summary>按当前窗口高度放大系统中文字体，不依赖原版小号文字的网格尺寸。</summary>
    private void EnsureStyles(float scale)
    {
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont("Microsoft YaHei UI", 32);
        if (titleStyle != null && Mathf.Approximately(styledScale, scale))
            return;
        GUIStyle Label(int size, bool bold = false)
        {
            var style = new GUIStyle()
            {
                font = font, fontSize = Mathf.RoundToInt(size * scale),
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
                wordWrap = false, richText = false, alignment = TextAnchor.MiddleLeft
            };
            style.normal.textColor = Color.white;
            return style;
        }
        titleStyle = Label(36, true);
        textStyle = Label(23, true);
        smallStyle = Label(18);
        numberStyle = Label(32, true);
        buttonStyle = CopyStyle(GUI.skin.button);
        buttonStyle.font = font;
        buttonStyle.fontSize = Mathf.RoundToInt(23 * scale);
        searchStyle = CopyStyle(GUI.skin.textField);
        searchStyle.font = font;
        searchStyle.fontSize = Mathf.RoundToInt(22 * scale);
        scrollbarStyle = CopyStyle(GUI.skin.verticalScrollbar);
        scrollbarStyle.fixedWidth = 18f * scale;
        styledScale = scale;
    }

    /// <summary>当前 IL2CPP 接口没有样式复制构造函数，逐项复制到自有样式，保留皮肤资源归属。</summary>
    private static GUIStyle CopyStyle(GUIStyle source) => new()
    {
        name = source.name, normal = source.normal, hover = source.hover, active = source.active,
        focused = source.focused, onNormal = source.onNormal, onHover = source.onHover,
        onActive = source.onActive, onFocused = source.onFocused,
        border = source.border, margin = source.margin, padding = source.padding,
        alignment = source.alignment, clipping = source.clipping,
        fixedWidth = source.fixedWidth, fixedHeight = source.fixedHeight,
        stretchWidth = source.stretchWidth, stretchHeight = source.stretchHeight,
        richText = false, wordWrap = false
    };

    /// <summary>矩形背景使用白纹理着色，并立即恢复 GUI 颜色。</summary>
    private static void Fill(Rect rect, uint rgb, float alpha = 1f)
    {
        var previous = GUI.color;
        var color = PotentialPresentation.Color(rgb);
        color.a = alpha;
        GUI.color = color;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    /// <summary>在 OnGUI 绘制可滚动全列表；只为可见卡片加载封面，避免每帧遍历绘制所有图片。</summary>
    internal void Draw()
    {
        if (!IsOpen || snapshot == null || InputManager.Instance?.IsHeadOfStack(receiver) != true)
            return;
        var current = Event.current;
        if (current.type == EventType.KeyDown && current.keyCode == KeyCode.Escape)
        {
            current.Use();
            Close();
            return;
        }
        float scale = Mathf.Clamp(Screen.height / 1080f, 0.65f, 2f);
        EnsureStyles(scale);
        var oldColor = GUI.color;
        int oldDepth = GUI.depth;
        GUI.color = Color.white;
        GUI.depth = -1000;
        try
        {
            Fill(new Rect(0, 0, Screen.width, Screen.height), 0x060b16, 0.97f);
            float padding = 28f * scale, contentWidth = Screen.width - padding * 2;
            GUI.DrawTexture(new Rect(padding, padding, 135f * scale, 135f * scale),
                badges.Texture(PotentialBadges.Tier(snapshot.Potential)), ScaleMode.ScaleToFit, true);
            GUI.Label(new Rect(padding + 23f * scale, padding + 48f * scale, 112f * scale, 50f * scale),
                PotentialCalculator.Format(snapshot.Potential), numberStyle!);
            float textX = padding + 156f * scale;
            GUI.Label(new Rect(textX, padding, contentWidth - 460f * scale, 52f * scale), "潜力值 · 全部最佳成绩", titleStyle!);
            GUI.Label(new Rect(textX, padding + 61f * scale, contentWidth - 500f * scale, 36f * scale),
                $"PTT {PotentialCalculator.Format(snapshot.Potential)}  /  MAX {PotentialCalculator.Format(snapshot.Maximum)}", textStyle!);
            GUI.Label(new Rect(textX, padding + 105f * scale, contentWidth - 500f * scale, 31f * scale),
                $"B50 {PotentialCalculator.Format(snapshot.B50Average)}   B10 {PotentialCalculator.Format(snapshot.B10Average)}   已游玩 {snapshot.Plays.Count} 谱面", smallStyle!);
            bool oldEnabled = GUI.enabled;
            bool export;
            try
            {
                GUI.enabled = exporting == null;
                export = GUI.Button(new Rect(Screen.width - padding - 350f * scale, padding + 12f * scale, 240f * scale, 56f * scale), "导出 B50 到剪贴板", buttonStyle!);
            }
            finally { GUI.enabled = oldEnabled; }
            if (GUI.Button(new Rect(Screen.width - padding - 94f * scale, padding + 12f * scale, 94f * scale, 56f * scale), "关闭", buttonStyle!))
            {
                Close();
                return;
            }
            if (export)
            {
                exporting = snapshot;
                exportStarted = Time.realtimeSinceStartup;
                status = "正在准备 B50 封面……";
            }
            float controlsY = padding + 155f * scale;
            GUI.Label(new Rect(padding, controlsY, 90f * scale, 44f * scale), "搜索", textStyle!);
            search = GUI.TextField(new Rect(padding + 90f * scale, controlsY, 330f * scale, 44f * scale), search, searchStyle!);
            GUI.Label(new Rect(padding + 442f * scale, controlsY, contentWidth - 442f * scale, 44f * scale), status, smallStyle!);
            if (filteredSnapshot != snapshot || filteredSearch != search)
            {
                filtered = snapshot.Plays.Where(play => string.IsNullOrWhiteSpace(search)
                    || (play.Chart?.Title ?? play.Play.ChartId).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || (play.Chart?.Artist ?? "").Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)
                    || PotentialPresentation.Difficulty(play.Play.Difficulty).Contains(search.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
                filteredSnapshot = snapshot;
                filteredSearch = search;
                scroll = Vector2.zero;
            }
            float top = controlsY + 62f * scale;
            var viewport = new Rect(padding, top, contentWidth, Math.Max(100f, Screen.height - top - padding));
            float gap = 16f * scale;
            int columns = Math.Max(1, Math.Min(5, (int)((contentWidth - 24f * scale + gap) / (330f * scale + gap))));
            float cardWidth = (contentWidth - 24f * scale - gap * (columns - 1)) / columns;
            float cardHeight = 220f * scale;
            int rows = (filtered.Length + columns - 1) / columns;
            float contentHeight = Math.Max(viewport.height, rows * (cardHeight + gap));
            scroll = GUI.BeginScrollView(viewport, scroll, new Rect(0, 0, contentWidth - 24f * scale, contentHeight),
                false, false, GUIStyle.none, scrollbarStyle!);
            try
            {
                if (filtered.Length == 0)
                    GUI.Label(new Rect(24f * scale, 28f * scale, contentWidth - 72f * scale, 60f * scale), "没有符合条件的已游玩谱面。", textStyle!);
                int startRow = Math.Max(0, (int)(scroll.y / (cardHeight + gap)) - 1);
                int endRow = Math.Min(rows, (int)((scroll.y + viewport.height) / (cardHeight + gap)) + 2);
                for (int i = startRow * columns; i < Math.Min(filtered.Length, endRow * columns); i++)
                    DrawCard(filtered[i], new Rect(i % columns * (cardWidth + gap), i / columns * (cardHeight + gap), cardWidth, cardHeight), scale);
            }
            finally { GUI.EndScrollView(); }
            if (current.type is EventType.MouseDown or EventType.MouseUp or EventType.ScrollWheel)
                current.Use();
        }
        finally { GUI.color = oldColor; GUI.depth = oldDepth; }
    }

    /// <summary>单张成绩卡保留全列表排名，即使搜索结果改变也不重新编号。</summary>
    private void DrawCard(RankedPlay play, Rect rect, float scale)
    {
        Fill(rect, 0x1b263b);
        Fill(new Rect(rect.x, rect.y, rect.width, 4f * scale), PotentialPresentation.DifficultyRgb(play.Play.Difficulty));
        float x = rect.x + 12f * scale, y = rect.y + 12f * scale;
        GUI.Label(new Rect(x, y, 110f * scale, 42f * scale), play.Rank.HasValue ? $"#{play.Rank}" : "#--", textStyle!);
        GUI.Label(new Rect(rect.xMax - 145f * scale, y, 133f * scale, 42f * scale),
            play.Potential.HasValue ? PotentialCalculator.Format(play.Potential.Value) : "--.---", numberStyle!);
        var coverRect = new Rect(x, y + 48f * scale, 112f * scale, 112f * scale);
        var texture = covers!.GetTexture(play.Chart, out var error);
        if (texture != null)
            GUI.DrawTexture(coverRect, texture, ScaleMode.ScaleToFit, true);
        else
        {
            Fill(coverRect, 0x111929);
            GUI.Label(coverRect, error == null ? "加载中" : "封面不可用", smallStyle!);
        }
        float infoX = x + 124f * scale, infoWidth = rect.xMax - infoX - 12f * scale;
        var old = GUI.color;
        GUI.color = PotentialPresentation.Color(PotentialPresentation.DifficultyRgb(play.Play.Difficulty));
        GUI.Label(new Rect(infoX, y + 46f * scale, infoWidth, 32f * scale), PotentialPresentation.Difficulty(play.Play.Difficulty), smallStyle!);
        GUI.color = old;
        GUI.Label(new Rect(infoX, y + 79f * scale, infoWidth, 32f * scale),
            play.Chart == null ? "定数不可用" : $"定数 {play.Chart.Constant}.0", smallStyle!);
        GUI.Label(new Rect(infoX, y + 112f * scale, infoWidth, 31f * scale), PotentialPresentation.Score(play.Play.Score), smallStyle!);
        GUI.Label(new Rect(infoX, y + 144f * scale, infoWidth, 28f * scale), PotentialPresentation.Lamp(play.Play.Lamp), smallStyle!);
        GUI.Label(new Rect(x, y + 168f * scale, rect.width - 24f * scale, 32f * scale), play.Chart?.Title ?? play.Play.ChartId, textStyle!);
    }

    /// <summary>归还本页面的输入并取消导出；关闭不会触碰原生成绩或剪贴板。</summary>
    internal void Close()
    {
        requestedFrame = -1;
        exporting = null;
        owner = null;
        snapshot = filteredSnapshot = null;
        filtered = Array.Empty<RankedPlay>();
        var current = receiver;
        receiver = null;
        var resources = covers;
        covers = null;
        try
        {
            if (current is not null)
            {
                try { InputManager.Instance?.ReleaseInputControl(current); }
                catch (Exception ex) { Plugin.Logger.LogError($"成绩浏览页归还原版输入失败：{ex}"); }
                finally { if (current != null) Object.Destroy(current.gameObject); }
            }
        }
        finally { resources?.Dispose(); }
    }

    /// <summary>插件退出时同时释放页面字体。</summary>
    public void Dispose()
    {
        try { Close(); }
        finally
        {
            if (font != null)
                Object.Destroy(font);
            font = null;
        }
    }
}
