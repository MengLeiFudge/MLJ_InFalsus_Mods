extern alias GameData;

using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.Scenes.SongSelect;
using ifapp.Game.UI.Common;
using UnityEngine;
using SongRow = GameData::_K._SH;
using SaveOwner = GameData::_K._NH;
using SortType = ifapp.Game.SongSelectSortType;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>选歌场景的徽章入口、卡片排名和原生排序扩展；游戏继续管理歌曲列表与输入。</summary>
internal sealed class PotentialSelection : IDisposable
{
    /// <summary>原版排序选项按枚举顺序排列，扩展选项只保存在本视图中。</summary>
    private static readonly SortType[] NativeSortTypes = Enum.GetValues<SortType>();
    /// <summary>原版选项后的双向循环顺序，以及各项使用的数值和方向。</summary>
    private static readonly (string Label, bool ByConstant, bool Descending)[] CustomSortTypes =
    {
        ("歌曲定数 升序", true, false),
        ("歌曲定数 降序", true, true),
        ("歌曲潜力值 升序", false, false),
        ("歌曲潜力值 降序", false, true)
    };
    /// <summary>本视图绑定的原生场景与共享页面。</summary>
    private readonly SongSelectScene scene;
    private readonly PotentialBrowser browser;
    private PotentialView? account;
    /// <summary>头像和 PTT 各有独立菱形碰撞体；委托在注销前保持有效。</summary>
    private readonly List<UIButton> entries = new();
    private readonly List<UIButton._EI> openResults = new();
    private SelectedChartInfo? selectedInfo;
    private bool? entryEnabled;
    /// <summary>池化小卡按对象身份复用文字，歌曲切换时更新内容。</summary>
    private readonly Dictionary<IntPtr, NativeScoreLine> ranks = new();
    /// <summary>当前数据与上次排序使用的数据，用于在成绩或定数来源变化后刷新顺序。</summary>
    private PotentialSnapshot? snapshot, sortedSnapshot;
    /// <summary>零为原版，其余为 CustomSortTypes 的下标加一；原生枚举始终保留合法值。</summary>
    private int sortMode;
    private Str.Strings.Localization sortLanguage;
    private byte sortedDifficulty;
    private IntPtr sortedList;
    private int sortedCount;
    /// <summary>避免关闭恢复过程中再次参与排序。</summary>
    private bool disposed;

    /// <summary>建立独立入口和排名文字，排序沿用游戏已有控件。</summary>
    internal PotentialSelection(SongSelectScene scene, PotentialBadges badges, PotentialBrowser browser)
    {
        this.scene = scene;
        this.browser = browser;
        try
        {
            var graphic = scene.leftSongCardContainer.songCardPrefab.jacket;
            account = new PotentialView(scene.topBar.text, scene.scene.transform, graphic, badges);
            var saved = SaveOwner._CEb?._DEb?._iEb?.GeneralSaveState
                ?? throw new InvalidOperationException("当前选歌角色尚未载入。");
            var icons = scene.songSelectRemembranceContainer?.characterUniversalIconMapping
                ?? throw new InvalidOperationException("选歌场景缺少原生角色头像映射。");
            account.SetPortrait(icons._wI(saved.SelectedCharacter, default));
            CreateEntry("Portrait", PotentialView.PortraitCenter, PotentialView.FrameSize);
            CreateEntry("PTT", PotentialView.BadgeCenter, PotentialView.BadgeSize);
            selectedInfo = new SelectedChartInfo(scene);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>建立旋转 45 度的正方形碰撞体，与显示菱形共用中心和对角线长度。</summary>
    /// <param name="name">入口名称。</param>
    /// <param name="center">相对头像容器中心的设计像素偏移。</param>
    /// <param name="size">菱形水平及垂直对角线长度。</param>
    private void CreateEntry(string name, Vector2 center, float size)
    {
        var root = new GameObject("InFalsus.PotentialSystem." + name);
        root.SetActive(false);
        root.layer = scene.backButton.gameObject.layer;
        root.transform.SetParent(account!.Entry.transform, false);
        var constrained = root.AddComponent<Constrained2D>();
        var entry = root.AddComponent<UIButton>();
        entries.Add(entry);
        entry.constrained2D = constrained;
        entry.button = scene.backButton.button;
        entry.sfxSetType = scene.backButton.sfxSetType;
        UIButton._EI click = (Action<UIButton>)OpenResults;
        openResults.Add(click);
        entry._DSA(click);
        var hit = new GameObject("DiamondHitArea");
        hit.layer = root.layer;
        hit.transform.SetParent(root.transform, false);
        hit.transform.localRotation = Quaternion.Euler(0f, 0f, 45f);
        entry.ButtonCollider = hit.AddComponent<BoxCollider>();
        entry.ButtonCollider.enabled = false;
        entry.flags = UIButton.Flags.DoesConsumeTouchOnPress | UIButton.Flags.DoesHoverBlockOtherHover
            | UIButton.Flags.CanCauseCursorHover;
        var layout = entry.constrained2D.constrained2DTransform;
        layout.transform = entry.transform;
        layout.meshRenderer = null;
        layout.meshFilter = null;
        layout.fastText = null;
        layout.type = Constrained2DType.Container;
        layout.anchor = new Vector2(0.5f, 0.5f);
        layout.parentAlignment = Constrained2DAlignment.Center;
        layout.offset = center;
        layout.sizeInPixels = Vector2.zero;
        layout.sizeInParentNormalized = Vector2.zero;
        layout.renderableScale = Vector2.one;
        layout.color = Color.white;
        layout.zPosition = -0.02f;
        layout.overrideMaterialRenderQueueOffset = 0;
        entry.constrained2D.constrained2DTransform = layout;
        NativeButtons.SetState(entry, false);
        entry.gameObject.SetActive(true);
        entry.constrained2D._rQA();
        entry.constrained2D._RQA();
        entry.constrained2D._PQA(0x3f);
        BrowserWidgets.Place(entry.constrained2D, center, Vector2.one * size);
    }

    /// <summary>原生菱形碰撞体完成点击判定；顶部被裁切部分及选歌输入失效时不打开。</summary>
    private void OpenResults(UIButton button)
    {
        var input = InputManager.Instance;
        if (entryEnabled == true && entries.Any(entry => entry.Pointer == button.Pointer)
            && button.ButtonCollider.enabled && scene.isActiveAndEnabled && !scene.DisableInputEvents
            && input?.IsHeadOfStack(scene) == true)
            browser.RequestOpen(scene, snapshot);
    }

    /// <summary>刷新当前难度排名，并在曲包、难度、定数来源或成绩变化后重新应用自选排序。</summary>
    internal void Render(PotentialSnapshot? data, string status)
    {
        snapshot = data;
        account?.Render(data, status);
        var input = InputManager.Instance;
        bool enabled = data != null && scene.isActiveAndEnabled && !scene.DisableInputEvents
            && input?.IsHeadOfStack(scene) == true;
        var point = input == null ? new Vector2(-1f, -1f) : input.GetCurrentMousePositionNormalizedInUISpace();
        bool visible = point.x is >= 0f and <= 1f && point.y is >= 0f and <= 1f;
        bool inputChanged = entryEnabled != enabled;
        bool hitAreaChanged = false;
        _m._wi._ERA(false);
        foreach (var entry in entries)
        {
            if (inputChanged) NativeButtons.SetState(entry, enabled);
            var container = entry.constrained2D;
            var layout = container.constrained2DTransform;
            BrowserWidgets.Place(container, layout.offset, layout.sizeInPixels);
            // 只前移自有命中区域；不禁用覆盖顶部的原生 blocker 或改动其输入归属。
            float depth = BrowserWidgets.ReadLayout(container).CalculatedZFromCamera;
            float front = depth;
            if (scene.topBar.topbarBlockers != null)
                foreach (var blocker in scene.topBar.topbarBlockers)
                    if (blocker != null && blocker.isActiveAndEnabled && blocker.constrained2D != null)
                        front = Math.Min(front, BrowserWidgets.ReadLayout(blocker.constrained2D).CalculatedZFromCamera - 0.02f);
            if (front < depth)
            {
                layout = container.constrained2DTransform;
                layout.zPosition += front - depth;
                container.constrained2DTransform = layout;
                container._PQA(0x3f);
            }
            _m._wi._ERA(false);
            bool wasEnabled = entry.ButtonCollider.enabled;
            BrowserWidgets.SyncHitArea(container, entry.ButtonCollider);
            var hitSize = entry.ButtonCollider.size;
            entry.ButtonCollider.size = new Vector3(hitSize.x * 0.70710678f, hitSize.y * 0.70710678f, hitSize.z);
            // 菱形由原生射线判定；这里只裁切 UI 黑边，不用另一套布局坐标重复裁切入口。
            entry.ButtonCollider.enabled &= enabled && visible;
            hitAreaChanged |= wasEnabled != entry.ButtonCollider.enabled;
            if (inputChanged && enabled)
            {
                UIManager._A._ktA(entry);
                UIManager._A._mtA(entry, scene);
            }
        }
        entryEnabled = enabled;
        // 越过游戏裁切边界后，即使鼠标停下，也要让原生输入重新命中新启用的碰撞体。
        if (enabled && (inputChanged || hitAreaChanged)) input?.RequestLastMouseMoveReplay();
        if (sortMode != 0 && sortLanguage != NativeLabel.CurrentLanguage())
            UpdateSortText();
        if (sortMode != 0 && data != null && InputManager.Instance?.IsHeadOfStack(scene) == true
            && (sortedSnapshot != data || sortedDifficulty != (byte)scene._Sr
                || sortedList != scene._rr?.Pointer || sortedCount != scene._rr?.Count))
            RefreshOrder();
        UpdateCards(scene.leftSongCardContainer);
        UpdateCards(scene.rightSongCardContainer);
        selectedInfo?.Render(data, new ChartKey(scene._sr.Value, (byte)scene._Sr));
    }

    /// <summary>池化小卡按歌曲行实际绑定的 SongId 和难度更新，保持与原生等级、分数一致。</summary>
    private void UpdateCards(SongSelectSmallCardScrollContainer container)
    {
        if (container?.leftToRightOrderedContainers == null)
            return;
        foreach (var card in container.leftToRightOrderedContainers)
        {
            if (card == null || card.self == null)
                continue;
            if (!ranks.TryGetValue(card.Pointer, out var label) || !label.Alive)
            {
                label = new NativeScoreLine(card.self.transform, card.highscore);
                ranks[card.Pointer] = label;
            }
            var row = card._py;
            var key = row == null ? default : new ChartKey(row._A.Value, (byte)row._e);
            label.Set(row == null ? "" : PotentialPresentation.RankLine(snapshot, key),
                PotentialPresentation.PotentialColor(row == null ? null : snapshot?.Find(key)?.Potential));
        }
    }

    /// <summary>扩展原按钮的双向循环；原生 _cO 参数是目标枚举下标，方向由当前合法值推得。</summary>
    /// <returns>是否已处理当前切换；其他场景和非循环调用交还原版。</returns>
    internal bool TryCycleSort(SongSelectSortingButton button, int requested)
    {
        if (disposed || button != scene.songSelectSortingButton || snapshot == null
            || InputManager.Instance?.IsHeadOfStack(scene) != true)
            return false;
        int direction = requested - (int)button._Bs;
        if (direction != 1 && direction != -1)
            return false;
        int current = sortMode == 0 ? Array.IndexOf(NativeSortTypes, button._Bs)
            : NativeSortTypes.Length + sortMode - 1;
        if (current < 0)
            return false;
        int count = NativeSortTypes.Length + CustomSortTypes.Length;
        int next = (current + direction + count) % count;
        try
        {
            if (next < NativeSortTypes.Length)
            {
                sortMode = 0;
                button._Bs = NativeSortTypes[next];
                // BO 同时更新原生名称并调用原版 OnValueChanged，不能再次手工调用该回调。
                button._BO();
            }
            else
            {
                sortMode = next - NativeSortTypes.Length + 1;
                UpdateSortText();
                RefreshOrder();
            }
        }
        catch (Exception ex)
        {
            sortMode = 0;
            Plugin.Logger.LogError($"潜力值排序切换失败，已退出该模式：{ex}");
            RestoreOrder();
        }
        return true;
    }

    /// <summary>原版主动设置排序时退出自有模式，名称与后续刷新均由原生回调维护。</summary>
    internal void NativeSortRequested(SongSelectScene current)
    {
        if (scene == current)
            sortMode = 0;
    }

    /// <summary>原版建立当前曲包列表后只调整现有行，不增加歌曲或改变解锁状态。</summary>
    internal void NativeSortCompleted(SongSelectScene current)
    {
        if (disposed || scene != current || sortMode == 0 || snapshot == null || scene._rr == null)
            return;
        try
        {
            byte difficulty = (byte)scene._Sr;
            var sort = CustomSortTypes[sortMode - 1];
            var rows = new List<(SongRow Row, decimal? Value)>();
            foreach (var row in scene._rr)
            {
                var key = new ChartKey(row._A.Value, (byte)row._e);
                decimal? value = sort.ByConstant
                    ? snapshot.Catalog.TryGet(key, out var chart) ? chart.Constant : null
                    : snapshot.Find(key)?.Potential;
                rows.Add((row, value));
            }
            // 定数排序包含未游玩谱面；单曲 PTT 缺少成绩时，两种方向均置于末尾。
            var availableFirst = rows.OrderByDescending(row => row.Value.HasValue);
            var ordered = sort.Descending
                ? availableFirst.ThenByDescending(row => row.Value).ThenBy(row => row.Row._A.Value)
                : availableFirst.ThenBy(row => row.Value).ThenBy(row => row.Row._A.Value);
            var result = ordered.Select(row => row.Row).ToArray();
            for (int i = 0; i < result.Length; i++)
                scene._rr[i] = result[i];
            sortedSnapshot = snapshot;
            sortedDifficulty = difficulty;
            sortedList = scene._rr.Pointer;
            sortedCount = scene._rr.Count;
        }
        catch
        {
            sortMode = 0;
            throw;
        }
    }

    /// <summary>复用原版 Ao→ON→ZN 流程，保留当前歌曲身份并刷新两侧滚动容器。</summary>
    private void RefreshOrder()
    {
        var selected = scene._sr;
        scene._Ao(scene._Tr);
        scene._ON(selected);
        scene.StartCoroutine(scene._ZN());
    }

    /// <summary>恢复原生名称和排序；失败只记录，不影响自有控件的回收。</summary>
    private void RestoreOrder()
    {
        if (scene == null || !scene.isActiveAndEnabled)
            return;
        try { scene.songSelectSortingButton._BO(); }
        catch (Exception ex) { Plugin.Logger.LogError($"恢复原版选歌排序失败：{ex}"); }
    }

    /// <summary>定数与单曲潜力值名称使用原按钮的文字组件及当前语言字体。</summary>
    private void UpdateSortText()
    {
        if (sortMode == 0)
            return;
        var text = scene.songSelectSortingButton.sortText;
        text.SetTextNonLocalized(CustomSortTypes[sortMode - 1].Label, Text.UpdateType.DontUpdateMesh);
        sortLanguage = NativeLabel.CurrentLanguage();
        text.targetLocalization = sortLanguage;
        text.dirtyFlags |= Text.DirtyFlags.FontChanged | Text.DirtyFlags.TextChanged;
        text.EnsureDirtyFlagsProcessed();
        text.GetComponent<Constrained2D>()._PQA(0x3f);
    }

    /// <summary>退出自有排序，先注销入口再释放文字和图形。</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        bool restore = sortMode != 0;
        sortMode = 0;
        if (restore)
            RestoreOrder();
        foreach (var entry in entries)
        {
            // 原生 UIButton.OnDestroy 已注销随场景销毁的入口，不能再用其空 Collider 作字典键。
            if (entry == null || entry.ButtonCollider == null)
                continue;
            entry.ButtonCollider.enabled = false;
            try { UIManager._A?._ktA(entry); }
            catch (Exception ex) { Plugin.Logger.LogError($"注销潜力值徽章入口失败：{ex}"); }
        }
        entries.Clear();
        openResults.Clear();
        account?.Dispose();
        account = null;
        selectedInfo?.Dispose();
        selectedInfo = null;
        foreach (var label in ranks.Values)
            label.Dispose();
        ranks.Clear();
    }
}
