extern alias GameData;

using System.Globalization;
using ifapp.Game.Data;
using SaveOwner = GameData::_K._NH;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.Scenes.Recipe;
using UnityEngine;
using Key = UnityEngine.InputSystem.Key;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>潜力值页面的三个导航分区。</summary>
internal enum PotentialTab { Scores, Appearance, Settings }

/// <summary>成绩与个性化页面会话，拥有筛选、逐局选择、输入和封面生命周期。</summary>
internal sealed class PotentialBrowser : IDisposable
{
    /// <summary>当前原生场景、排名快照及共享徽章。</summary>
    private readonly PotentialBadges badges;
    private SongSelectScene? owner;
    private RecipeScene? inventory;
    private bool loading;
    private Il2CppSystem.Action<RecipeScene>? loaded;
    private float openedAt;
    internal CharacterUniversalIconMapping? Portraits { get; private set; }
    private PotentialSnapshot? snapshot, exporting, filteredSnapshot;
    private PotentialInputReceiver? receiver;
    private NativePotentialPage? view;
    private CoverCache? covers;
    /// <summary>搜索内容和 Unicode 文本选区；筛选后仍保留原始排名。</summary>
    private string search = "", filteredSearch = "";
    private int caret, anchor;
    private bool focused, closeRequested, returnToHub;
    /// <summary>入口释放后再打开；封面导出等待有明确的时间上限。</summary>
    private int requestedFrame = -1;
    private float exportStarted;
    private string status = "";
    private int filteredSort = -1, filteredDifficulty = -1;
    private bool editName;
    private string nameDraft = "";
    /// <summary>编辑昵称与搜索共享输入法和选区逻辑，但保留各自的文本。</summary>
    private string EditText { get => editName ? nameDraft : search; set { if (editName) nameDraft = value; else search = value; } }
    internal PotentialSettings Settings => Plugin.Settings ?? throw new InvalidOperationException("潜力值配置不可用。");
    internal PotentialTab Tab { get; private set; }
    internal RankedPlay? Selected { get; private set; }
    internal IReadOnlyList<ChartPlay> History { get; private set; } = Array.Empty<ChartPlay>();
    internal string HistoryError { get; private set; } = "";
    internal int SelectedPlay { get; private set; }
    internal bool HistoryExpanded { get; private set; }
    internal static readonly string[] SortNames = { "PTT 降序", "谱面定数 升序", "谱面定数 降序", "分数 降序", "歌名" };
    /// <summary>头像跟随设置只读取当前游戏角色，不改写角色选择。</summary>
    internal int PortraitCharacter => Settings.Portrait >= 0 ? Settings.Portrait
        : (int)(SaveOwner._CEb?._DEb?._iEb?.GeneralSaveState?.SelectedCharacter ?? default);

    internal bool IsOpen => receiver != null;
    internal DataAccess? DataAccess => inventory == null ? null : inventory.dataAccess;

    internal PotentialBrowser(PotentialBadges badges) => this.badges = badges;

    /// <summary>只在选歌场景拥有输入时预约打开，防止场景切换期间抢占输入。</summary>
    internal void RequestOpen(SongSelectScene scene, PotentialSnapshot? data, PotentialTab tab = PotentialTab.Scores)
    {
        if (IsOpen || loading || inventory != null || requestedFrame >= 0 || data == null || InputManager.Instance?.IsHeadOfStack(scene) != true)
            return;
        Tab = tab;
        owner = scene;
        Portraits = Object.Instantiate(scene.songSelectRemembranceContainer.characterUniversalIconMapping);
        Portraits.hideFlags = HideFlags.HideAndDontSave;
        snapshot = data;
        requestedFrame = Time.frameCount;
    }

    /// <summary>在同一异常边界内管理原生视图、过滤结果与异步导出。</summary>
    internal void Update(SongSelectScene? scene, PotentialSnapshot? data)
    {
        if (closeRequested) { Close(); return; }
        if (requestedFrame >= 0 && Time.frameCount > requestedFrame)
        {
            requestedFrame = -1;
            if (owner == null || owner != scene || InputManager.Instance?.IsHeadOfStack(owner) != true)
            { Close(); return; }
            loading = true;
            openedAt = Time.realtimeSinceStartup;
            loaded = (Action<RecipeScene>)RecipeReady;
            CoreScene._c._Bj<SongSelectScene, RecipeScene>(_d._AB._nl, null, null, loaded);
        }
        if (loading)
        {
            if (Time.realtimeSinceStartup - openedAt > 45f)
            {
                Plugin.Logger.LogError("原版卡牌页面加载超时，已取消潜力值页面接入。");
                Close();
            }
            return;
        }
        if (inventory == null || snapshot == null) { if (IsOpen) Close(); return; }
        if (data != null) snapshot = data;
        if (!IsOpen)
        {
            var root = new GameObject("InFalsus.PotentialSystem.BrowserInput");
            try
            {
                root.layer = inventory.gameObject.layer;
                root.transform.SetParent(inventory.transform, false);
                receiver = root.AddComponent<PotentialInputReceiver>();
                receiver.Bind(this);
                covers = new CoverCache();
                search = "";
                caret = anchor = 0;
                focused = false;
                filteredSnapshot = null;
                status = "";
                view = new NativePotentialPage(inventory, receiver, this);
                InputManager.Instance.TakeInputControl(receiver);
                InputManager.Instance.RequestLastMouseMoveReplay();
            }
            catch { Close(); if (root != null) Object.Destroy(root); throw; }
        }
        if (!IsOpen)
            return;
        if (InputManager.Instance?.IsHeadOfStack(receiver) != true)
        { Close(); return; }
        if (filteredSnapshot != snapshot || filteredSearch != search || filteredSort != Settings.BrowserSort
            || filteredDifficulty != Settings.BrowserDifficulty)
        {
            string query = search.Trim();
            var filtered = snapshot.Plays.Where(play => (Settings.BrowserDifficulty == 0 || play.Play.Difficulty == Settings.BrowserDifficulty)
                && (string.IsNullOrEmpty(query)
                || (play.Chart?.Title ?? play.Play.ChartId).Contains(query, StringComparison.OrdinalIgnoreCase)
                || (play.Chart?.Artist ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)
                || PotentialPresentation.Difficulty(play.Play.Difficulty).Contains(query, StringComparison.OrdinalIgnoreCase)));
            var ordered = Settings.BrowserSort switch
            {
                1 => filtered.OrderBy(p => p.Chart == null).ThenBy(p => p.Chart?.Constant),
                2 => filtered.OrderBy(p => p.Chart == null).ThenByDescending(p => p.Chart?.Constant),
                3 => filtered.OrderByDescending(p => p.Play.Score),
                4 => filtered.OrderBy(p => p.Chart?.Title ?? p.Play.ChartId, StringComparer.CurrentCultureIgnoreCase),
                _ => filtered.OrderBy(p => p.Rank ?? int.MaxValue)
            };
            view!.SetPlays(ordered.ThenBy(p => p.Play.SongId).ThenBy(p => p.Play.Difficulty).ToArray());
            if (filteredSnapshot != snapshot && Selected != null)
            {
                var current = snapshot.Plays.FirstOrDefault(p => p.Play.Key == Selected.Play.Key && p.Play.ChartId == Selected.Play.ChartId);
                if (current == null) CollapseDetail();
                else Select(current);
            }
            filteredSnapshot = snapshot;
            filteredSearch = search;
            filteredSort = Settings.BrowserSort;
            filteredDifficulty = Settings.BrowserDifficulty;
        }
        UpdateExport();
        string value = editName ? nameDraft : search;
        string displayed = focused ? value.Insert(Math.Min(caret, value.Length), "|") : value;
        view!.Render(snapshot, covers!, displayed, status, focused, exporting != null);
    }

    /// <summary>冻结点击时的 B50 快照；搜索和滚动不会改变导出内容。</summary>
    internal void RequestExport()
    {
        if (!IsOpen || snapshot == null || exporting != null)
            return;
        focused = false;
        exporting = snapshot;
        exportStarted = Time.realtimeSinceStartup;
        status = "正在准备 B50 封面……";
    }

    /// <summary>等待异步封面并生成独立图片；只有剪贴板交付成功后提示已复制。</summary>
    private void UpdateExport()
    {
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
            ClipboardImage.Copy(B50Image.Render(exporting, images, badge));
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

    /// <summary>原生输入回调的异常不逃回游戏输入循环，下一次更新统一关闭页面。</summary>
    internal void Guard(Action action)
    {
        try { if (IsOpen && !closeRequested) action(); }
        catch (Exception ex)
        {
            closeRequested = true;
            Plugin.Logger.LogError($"潜力值成绩页输入失败，已请求关闭：{ex}");
        }
    }

    /// <summary>切换页签保留筛选与列表偏移，昵称结束编辑时写入配置。</summary>
    internal void ChangeTab(PotentialTab tab) { FinishEdit(); Tab = tab; view?.Invalidate(); }
    internal void RequestClose() { FinishEdit(); closeRequested = true; }
    internal void RequestHome() { FinishEdit(); returnToHub = true; closeRequested = true; }
    internal void FocusSearch() { FinishEdit(); editName = false; focused = true; caret = anchor = search.Length; }
    internal void FocusName() { FinishEdit(); editName = true; nameDraft = Settings.ProfileName; focused = true; caret = anchor = nameDraft.Length; }
    /// <summary>昵称只在编辑结束时保存，避免每次按键写配置。</summary>
    private void FinishEdit()
    {
        bool saveName = editName;
        editName = false; focused = false;
        if (saveName)
        {
            try { Settings.ProfileName = nameDraft.Trim(); }
            catch (Exception ex)
            {
                status = "昵称保存失败，请查看日志。";
                Plugin.Logger.LogError($"名片昵称保存失败：{ex}");
            }
        }
    }
    internal void SetDifficulty(byte difficulty) { FinishEdit(); Settings.BrowserDifficulty = difficulty; }
    internal void CycleSort() { FinishEdit(); Settings.BrowserSort = (Settings.BrowserSort + 1) % SortNames.Length; }
    internal void CycleSource() { FinishEdit(); Settings.CycleSource(); view?.Invalidate(); }
    internal void SetPortrait(int character) { FinishEdit(); Settings.Portrait = character; view?.Invalidate(); }
    internal void SetNamecardColor(int color) { FinishEdit(); Settings.NamecardColor = color; view?.Invalidate(); }

    /// <summary>选择成绩时读取当前账号的原始历史；失败只影响详情，保留成绩列表。</summary>
    internal void Select(RankedPlay play)
    {
        FinishEdit(); Selected = play; HistoryError = "";
        try
        {
            if (play.Chart == null) throw new InvalidDataException("谱面身份已变化，无法匹配历史。");
            var results = SaveOwner._CEb?._DEb?._iEb?.GameResults ?? throw new InvalidDataException("游戏逐局记录不可用。");
            History = PlayHistory.Read(results, play.Play.Key);
        }
        catch (Exception ex)
        {
            History = Array.Empty<ChartPlay>();
            HistoryError = "历史读取失败，请查看日志。";
            Plugin.Logger.LogError($"谱面 {play.Play.Key} 历史读取失败：{ex}");
        }
        SelectedPlay = Math.Max(0, History.Count - 1);
        view?.Invalidate();
    }
    internal void CollapseDetail() { Selected = null; History = Array.Empty<ChartPlay>(); HistoryExpanded = false; view?.Invalidate(); }
    internal void ExpandHistory() { FinishEdit(); if (Selected != null && HistoryError.Length == 0) HistoryExpanded = true; view?.Invalidate(); }
    internal void CollapseHistory() { HistoryExpanded = false; view?.Invalidate(); }
    internal void MovePlay(int delta) => SelectPlay(SelectedPlay + delta);
    internal void SelectPlay(int index) { SelectedPlay = Math.Clamp(index, 0, Math.Max(0, History.Count - 1)); view?.Invalidate(); }

    /// <summary>原生滚轮只移动本页成绩列表。</summary>
    internal void Wheel(Vector2 delta) => Guard(() => view?.Wheel(delta));

    /// <summary>按原生字符事件输入，包括输入法已经提交的中文。</summary>
    internal void Text(char character) => Guard(() =>
    {
        if (focused && !char.IsControl(character))
            ReplaceSelection(character.ToString());
    });

    /// <summary>原生键盘负责退出与搜索编辑，选歌和开始快捷键保持被本页隔离。</summary>
    internal void Key(Key key, bool control, bool shift) => Guard(() =>
    {
        if (key == UnityEngine.InputSystem.Key.Escape)
        {
            if (focused) { FinishEdit(); return; }
            if (HistoryExpanded) { CollapseHistory(); return; }
            if (Selected != null && Tab == PotentialTab.Scores) { CollapseDetail(); return; }
            RequestClose(); return;
        }
        if (key == UnityEngine.InputSystem.Key.Tab)
        { if (focused) FinishEdit(); else if (Tab == PotentialTab.Scores) FocusSearch(); else if (Tab == PotentialTab.Appearance) FocusName(); return; }
        if (!focused)
            return;
        int first = Math.Min(caret, anchor), last = Math.Max(caret, anchor);
        if (control)
        {
            switch (key)
            {
                case UnityEngine.InputSystem.Key.A: anchor = 0; caret = EditText.Length; return;
                case UnityEngine.InputSystem.Key.C:
                case UnityEngine.InputSystem.Key.X:
                    if (first != last)
                    {
                        GUIUtility.systemCopyBuffer = EditText[first..last];
                        if (key == UnityEngine.InputSystem.Key.X) ReplaceSelection("");
                    }
                    return;
                case UnityEngine.InputSystem.Key.V:
                    ReplaceSelection(new string((GUIUtility.systemCopyBuffer ?? "").Where(c => !char.IsControl(c)).ToArray()));
                    return;
            }
        }
        switch (key)
        {
            case UnityEngine.InputSystem.Key.Backspace:
                if (first == last) anchor = Previous(caret);
                ReplaceSelection(""); break;
            case UnityEngine.InputSystem.Key.Delete:
                if (first == last) anchor = Next(caret);
                ReplaceSelection(""); break;
            case UnityEngine.InputSystem.Key.LeftArrow:
                caret = !shift && first != last ? first : Previous(caret); break;
            case UnityEngine.InputSystem.Key.RightArrow:
                caret = !shift && first != last ? last : Next(caret); break;
            case UnityEngine.InputSystem.Key.Home: caret = 0; break;
            case UnityEngine.InputSystem.Key.End: caret = EditText.Length; break;
            case UnityEngine.InputSystem.Key.Enter:
            case UnityEngine.InputSystem.Key.NumpadEnter: FinishEdit(); break;
        }
        if (!shift) anchor = caret;
    });

    /// <summary>以完整文本元素移动，避免退格或方向键拆开代理对和组合字符。</summary>
    private int Previous(int position) => StringInfo.ParseCombiningCharacters(EditText).LastOrDefault(i => i < position);
    private int Next(int position) => StringInfo.ParseCombiningCharacters(EditText).FirstOrDefault(i => i > position, EditText.Length);

    /// <summary>替换当前选区，限制原生搜索标签长度；完整歌名仍参与过滤和导出。</summary>
    private void ReplaceSelection(string text)
    {
        int first = Math.Min(caret, anchor), last = Math.Max(caret, anchor);
        int capacity = Math.Max(0, (editName ? 40 : 256) - (EditText.Length - last + first));
        if (text.Length > capacity)
        {
            int boundary = StringInfo.ParseCombiningCharacters(text).LastOrDefault(i => i <= capacity);
            text = text[..boundary];
        }
        EditText = EditText[..first] + text + EditText[last..];
        caret = anchor = first + text.Length;
    }

    /// <summary>同步注销本页控件，然后归还输入；关闭不改原生成绩和剪贴板。</summary>
    internal void Close()
    {
        var returningFrom = inventory;
        bool home = returnToHub;
        returnToHub = false;
        inventory = null;
        loading = false;
        loaded = null;
        if (Portraits != null) Object.Destroy(Portraits);
        Portraits = null;
        requestedFrame = -1;
        closeRequested = false;
        focused = false;
        exporting = null;
        FinishEdit();
        Selected = null;
        History = Array.Empty<ChartPlay>();
        HistoryExpanded = false;
        owner = null;
        snapshot = filteredSnapshot = null;
        var current = receiver;
        receiver = null;
        var page = view;
        view = null;
        var resources = covers;
        covers = null;
        try { page?.Dispose(); }
        finally
        {
            try
            {
                if (current is not null)
                {
                    current.Unbind();
                    try { InputManager.Instance?.ReleaseInputControl(current); }
                    catch (Exception ex) { Plugin.Logger.LogError($"成绩页归还原版输入失败：{ex}"); }
                    finally { if (current != null) Object.Destroy(current.gameObject); }
                }
            }
            finally
            {
                resources?.Dispose();
                if (returningFrom != null && CoreScene._c != null)
                {
                    if (home) CoreScene._c._Bj<RecipeScene, HubScene>(_d._AB._nl);
                    else CoreScene._c._Bj<RecipeScene, SongSelectScene>(_d._AB._nl);
                }
            }
        }
    }

    /// <summary>只接入本次入口请求加载的原生页面；普通卡牌页不受影响。</summary>
    private void RecipeReady(RecipeScene scene)
    {
        if (!loading)
        {
            if (scene != null && CoreScene._c != null)
                CoreScene._c._Bj<RecipeScene, SongSelectScene>(_d._AB._nl);
            return;
        }
        inventory = scene;
        loading = false;
        owner = null;
    }

    public void Dispose() => Close();
}
