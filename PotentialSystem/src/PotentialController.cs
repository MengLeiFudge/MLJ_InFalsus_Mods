extern alias GameData;

using System.Globalization;
using Il2CppInterop.Runtime.Attributes;
using ifapp.Game.Data;
using ifapp.Game.Scenes;
using UnityEngine;
using Object = UnityEngine.Object;
using InputManager = ifapp.Game.Input.InputManager;
using SaveOwner = GameData::_K._NH;

namespace InFalsusMod.Potential;

/// <summary>在 Unity 主线程维护当前存档成绩、选歌展示、浏览输入生命周期与结算动画。</summary>
public sealed class PotentialController : MonoBehaviour
{
    /// <summary>结算数字从旧值过渡到新值的非缩放秒数。</summary>
    private const float AnimationSeconds = 1.2f;
    /// <summary>当前原版存档完整路径，同时作为账号隔离身份。</summary>
    private string? savePath;
    /// <summary>读取失败的存档身份；保留原文件并避免逐帧重读坏数据。</summary>
    private string? blockedPath;
    /// <summary>当前账号的模组最佳成绩。</summary>
    private PotentialProfile? profile;
    /// <summary>当前账号的独立模组文件。</summary>
    private PotentialStorage? storage;
    /// <summary>当前游戏实际加载的可用谱面。</summary>
    private ChartCatalog? catalog;
    /// <summary>当前模组成绩与动态曲库的完整计算结果。</summary>
    private PotentialSnapshot? snapshot;
    /// <summary>上次成功同步的原生成绩对象，切换存档或替换对象时重新读取历史。</summary>
    private IntPtr syncedResults;
    /// <summary>上次成功同步的最高分更新号；单局历史变化另由实际条数识别。</summary>
    private long syncedUpdateId = long.MinValue;
    /// <summary>上次同步的历史实际长度；游戏未破纪录时 updateId 不增加，但历史仍追加。</summary>
    private int syncedHistoryLength = -1;
    /// <summary>当前选歌场景及其自有文字。</summary>
    private SongSelectScene? selection;
    /// <summary>当前结算场景及其自有文字。</summary>
    private ResultsScene? resultsScene;
    /// <summary>选歌界面的展示副本。</summary>
    private PotentialSelection? selectionView;
    /// <summary>选歌与结算共用的徽章资源，所有视图释放后再回收。</summary>
    private PotentialBadges? badges;
    /// <summary>仅选歌入口可打开的完整成绩浏览页。</summary>
    private PotentialBrowser? browser;
    /// <summary>结算界面的展示副本。</summary>
    private PotentialView? resultsView;
    /// <summary>同一场景创建文字失败后不重复尝试。</summary>
    private bool selectionViewFailed, resultsViewFailed;
    /// <summary>下一次查找活动场景的非缩放时间。</summary>
    private float nextDiscovery;
    /// <summary>结算中已观察的成绩身份，防止场景刷新重复计入。</summary>
    private (ushort Song, byte Difficulty, ulong Score, byte Lamp, byte Clear, ulong Time)? observedResult;
    /// <summary>结算前后的账号潜力值。</summary>
    private decimal before, after;
    /// <summary>结算动画开始的非缩放时间。</summary>
    private float animationStart;
    /// <summary>当前结算对应的单曲展示行。</summary>
    private string resultChartLine = "";
    /// <summary>当前结算提升值或 Keep 文案。</summary>
    private string resultStatus = "Keep";
    /// <summary>写入失败时保留内存成绩并在界面明确提示未保存。</summary>
    private bool saveFailed;
    /// <summary>新导入或新结算申请的一次保存；失败后不会逐帧重复尝试。</summary>
    private bool saveRequested;

    /// <summary>绑定由 IL2CPP 创建的组件。</summary>
    /// <param name="pointer">原生 Unity 组件指针。</param>
    public PotentialController(IntPtr pointer) : base(pointer) { }

    /// <summary>按场景生命周期显示 PTT，并用非缩放时间推进结算数值动画。</summary>
    public void Update()
    {
        try
        {
            bool selectionAvailable = false;
            if (Time.realtimeSinceStartup >= nextDiscovery)
            {
                nextDiscovery = Time.realtimeSinceStartup + 0.5f;
                DiscoverScenes();
            }
            if (resultsScene != null && resultsScene.isActiveAndEnabled)
            {
                bool available = EnsureProfile(resultsScene.dataAccess);
                if (available && InputManager.Instance?.IsHeadOfStack(resultsScene) == true)
                {
                    ObserveResult();
                    TrySave();
                }
                EnsureResultsView();
                float progress = Mathf.Clamp01((Time.realtimeSinceStartup - animationStart) / AnimationSeconds);
                decimal blend = (decimal)(1f - Mathf.Pow(1f - progress, 3f));
                try
                {
                    resultsView?.Render(available ? snapshot : null, before + (after - before) * blend,
                        available ? resultChartLine : "", Status(resultStatus));
                }
                catch (Exception ex)
                {
                    resultsView?.Dispose();
                    resultsView = null;
                    resultsViewFailed = true;
                    Plugin.Logger.LogError($"结算潜力值文字刷新失败：{ex}");
                }
            }
            if (selection != null && selection.isActiveAndEnabled)
            {
                bool available = EnsureProfile(selection.dataAccess);
                selectionAvailable = available;
                if (available && (resultsScene == null || !resultsScene.isActiveAndEnabled)
                    && InputManager.Instance?.IsHeadOfStack(selection) == true)
                {
                    SynchronizeHistory();
                    TrySave();
                }
                EnsureSelectionView();
                try
                {
                    selectionView?.Render(available ? snapshot : null, Status("(B50 + B10) / 60"));
                }
                catch (Exception ex)
                {
                    selectionView?.Dispose();
                    selectionView = null;
                    selectionViewFailed = true;
                    browser?.Close();
                    Plugin.Logger.LogError($"选歌潜力值界面刷新失败：{ex}");
                }
            }
            UpdateBrowser(selectionAvailable ? snapshot : null);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"潜力值更新失败，已停止本组件：{ex}");
            ReleaseViews();
            enabled = false;
        }
    }

    /// <summary>只在用户实际进入的选歌场景载入/导入成绩，避免游玩结束前提前吞掉结算变化。</summary>
    /// <param name="scene">已完成 Start 的原版选歌场景。</param>
    [HideFromIl2Cpp]
    internal void EnterSelection(SongSelectScene scene)
    {
        if (selection != scene)
        {
            selectionView?.Dispose();
            selectionView = null;
            selectionViewFailed = false;
            selection = scene;
        }
        if (!EnsureProfile(scene.dataAccess, refreshCatalog: true))
            return;
        SynchronizeHistory();
        TrySave();
        EnsureSelectionView();
    }

    /// <summary>结算场景读取真实单次成绩，更新独立纪录并显示本次变化。</summary>
    /// <param name="scene">已完成 Start 的原版结算场景。</param>
    [HideFromIl2Cpp]
    internal void EnterResults(ResultsScene scene)
    {
        browser?.Close();
        if (resultsScene != scene)
        {
            resultsView?.Dispose();
            resultsView = null;
            resultsViewFailed = false;
            resultsScene = scene;
            observedResult = null;
        }
        if (EnsureProfile(scene.dataAccess, refreshCatalog: true))
            ObserveResult();
        EnsureResultsView();
    }

    /// <summary>补充插件加载时已存在的场景，并释放原版已销毁场景的文字。</summary>
    [HideFromIl2Cpp]
    private void DiscoverScenes()
    {
        // 结算优先，避免同时存在的旧选歌场景先导入本次结果而吞掉增加动画。
        if (resultsScene == null)
        {
            resultsView?.Dispose();
            resultsView = null;
            foreach (var scene in Object.FindObjectsOfType<ResultsScene>())
                if (scene.isActiveAndEnabled)
                {
                    EnterResults(scene);
                    break;
                }
        }
        if (selection == null)
        {
            selectionView?.Dispose();
            selectionView = null;
            if (resultsScene != null && resultsScene.isActiveAndEnabled)
                return;
            foreach (var scene in Object.FindObjectsOfType<SongSelectScene>())
                if (scene.isActiveAndEnabled)
                {
                    EnterSelection(scene);
                    break;
                }
        }
    }

    /// <summary>从当前原生存档容器获得文件归属，载入账号时以完整历史校准最佳成绩。</summary>
    /// <param name="access">当前场景读取的游戏元数据。</param>
    /// <param name="refreshCatalog">场景建立时重新读取真实谱面，支持更新的曲库。</param>
    /// <returns>当前账号是否具有可计算且已验证的潜力值状态。</returns>
    [HideFromIl2Cpp]
    private bool EnsureProfile(DataAccess access, bool refreshCatalog = false)
    {
        var owner = SaveOwner._CEb;
        var container = owner?._DEb;
        var data = access?.SongData;
        string? path = container?._JEb;
        if (container?._iEb?.GameResults == null || data == null || string.IsNullOrWhiteSpace(path))
            return false;
        if (string.Equals(blockedPath, path, StringComparison.OrdinalIgnoreCase))
            return false;
        bool changed = !string.Equals(savePath, path, StringComparison.OrdinalIgnoreCase);
        try
        {
            if (changed)
            {
                browser?.Close();
                var nextStorage = new PotentialStorage(path);
                var nextProfile = nextStorage.Load() ?? PotentialProfile.Create();
                var nextCatalog = ChartCatalog.Read(data);
                nextProfile.SynchronizeHistory(container._iEb.GameResults, nextCatalog);
                savePath = path;
                blockedPath = null;
                storage = nextStorage;
                profile = nextProfile;
                catalog = nextCatalog;
                saveFailed = false;
                observedResult = null;
                syncedResults = container._iEb.GameResults.Pointer;
                syncedUpdateId = container._iEb.GameResults.updateId;
                syncedHistoryLength = container._iEb.GameResults.history.Length;
                snapshot = profile.Calculate(catalog);
                saveRequested = profile.Dirty;
                Plugin.Logger.LogInfo($"潜力值已载入：{catalog.Count} 个动态谱面，上限 {PotentialCalculator.Format(catalog.Maximum)}。");
            }
            else if (refreshCatalog || catalog?.Language != NativeLabel.CurrentLanguage())
            {
                catalog = ChartCatalog.Read(data);
                snapshot = profile!.Calculate(catalog);
                syncedUpdateId = long.MinValue;
                saveRequested |= profile.Dirty;
            }
            return profile != null && catalog != null;
        }
        catch (Exception ex)
        {
            savePath = path;
            blockedPath = path;
            profile = null;
            storage = null;
            catalog = null;
            snapshot = null;
            Plugin.Logger.LogError($"潜力值载入失败，已保留原文件并停止该存档的模组写入：{ex}");
            return false;
        }
    }

    /// <summary>按对象、更新号与历史实际长度同步逐局记录；结算路径须在捕获本次成绩与时间之后调用。</summary>
    [HideFromIl2Cpp]
    private void SynchronizeHistory()
    {
        var results = SaveOwner._CEb?._DEb?._iEb?.GameResults;
        if (results == null || profile == null || catalog == null)
            return;
        int historyLength = results.history?.Length ?? -1;
        if (syncedResults == results.Pointer && syncedUpdateId == results.updateId && syncedHistoryLength == historyLength)
            return;
        profile.SynchronizeHistory(results, catalog);
        snapshot = profile.Calculate(catalog);
        syncedResults = results.Pointer;
        syncedUpdateId = results.updateId;
        syncedHistoryLength = historyLength;
        saveRequested |= profile.Dirty;
    }

    /// <summary>读取当前原版结算副本，先捕获本局时间及旧值，再同步完整历史与新值。</summary>
    [HideFromIl2Cpp]
    private void ObserveResult()
    {
        if (profile == null || catalog == null || snapshot == null)
            return;
        var result = ResultsScene._gq;
        var identity = (result.SongId.Value, (byte)result.Difficulty, result.PlayerScore,
            (byte)result.Lamp, (byte)result.CalculatedResultClear, result.SecondsSinceEpochUtc);
        if (observedResult == identity || result.SongId.Value == 0)
            return;
        before = snapshot.Potential;
        var key = new ChartKey(result.SongId.Value, (byte)result.Difficulty);
        if (catalog.TryGet(key, out var chart))
        {
            decimal play = PotentialCalculator.Play(chart.Constant, result.PlayerScore,
                PotentialCalculator.CompletionLamp((byte)result.CalculatedResultClear));
            profile.Observe(result, catalog, DateTimeOffset.UtcNow);
            snapshot = profile.Calculate(catalog);
            resultChartLine = $"本次 {PotentialCalculator.Format(play)} / 定数 {chart.Constant.ToString("F1", CultureInfo.InvariantCulture)}";
        }
        else
        {
            resultChartLine = "Unrated chart";
            Plugin.Logger.LogWarning($"结算谱面不在当前可用曲库中，未猜测定数：{key}。");
        }
        SynchronizeHistory();
        after = snapshot.Potential;
        animationStart = Time.realtimeSinceStartup;
        decimal visibleDelta = (decimal.Floor(after * 1000m) - decimal.Floor(before * 1000m)) / 1000m;
        resultStatus = visibleDelta == 0m ? "Keep" : "+" + PotentialCalculator.Format(visibleDelta);
        observedResult = identity;
        saveRequested |= profile.Dirty;
        TrySave();
    }

    /// <summary>保存失败时保留待保存成绩；在场景刷新、历史更新或新结算事件到来时再次保存。</summary>
    [HideFromIl2Cpp]
    private void TrySave()
    {
        if (!saveRequested || profile?.Dirty != true || storage == null)
            return;
        saveRequested = false;
        try
        {
            storage.Save(profile);
            saveFailed = false;
        }
        catch (Exception ex)
        {
            saveFailed = true;
            Plugin.Logger.LogError($"潜力值写入或旧文件归档失败，内存纪录保留，请检查文件与日志：{ex}");
        }
    }

    /// <summary>把存储失败显式显示在数值附近，避免玩家误以为成绩已保存。</summary>
    /// <param name="normal">正常的计算或变化文案。</param>
    /// <returns>正常文案或明确的失败状态。</returns>
    [HideFromIl2Cpp]
    private string Status(string normal) => snapshot == null
        ? "Data error | See BepInEx log" : saveFailed ? "Unsaved | See BepInEx log" : normal;

    /// <summary>原版顶部文字就绪后创建选歌展示；UI 失败不停止成绩计算。</summary>
    [HideFromIl2Cpp]
    private void EnsureSelectionView()
    {
        if (selectionView != null || selectionViewFailed || selection?.topBar?.text == null || selection.scene == null)
            return;
        try
        {
            badges ??= new PotentialBadges();
            browser ??= new PotentialBrowser(badges);
            selectionView = new PotentialSelection(selection, badges, browser);
        }
        catch (Exception ex)
        {
            selectionViewFailed = true;
            Plugin.Logger.LogError($"选歌潜力值文字建立失败：{ex}");
        }
    }

    /// <summary>在结算主布局中创建独立文字，不修改原版分数或动画容器。</summary>
    [HideFromIl2Cpp]
    private void EnsureResultsView()
    {
        if (resultsView != null || resultsViewFailed || resultsScene?.topBar?.text == null
            || resultsScene.resultsTopBannerContainer?.self == null)
            return;
        try
        {
            var parent = resultsScene.resultsTopBannerContainer.self.transform.parent;
            badges ??= new PotentialBadges();
            resultsView = new PotentialView(resultsScene.topBar.text, parent,
                resultsScene.resultsTopBannerContainer.bottomPip, badges, results: true);
        }
        catch (Exception ex)
        {
            resultsViewFailed = true;
            Plugin.Logger.LogError($"结算潜力值文字建立失败：{ex}");
        }
    }

    /// <summary>释放所有模组创建的场景文字。</summary>
    [HideFromIl2Cpp]
    private void ReleaseViews()
    {
        try { browser?.Dispose(); }
        finally
        {
            browser = null;
            selectionView?.Dispose();
            resultsView?.Dispose();
            selectionView = null;
            resultsView = null;
            badges?.Dispose();
            badges = null;
        }
    }

    /// <summary>浏览页错误只关闭自有 UI，不停止成绩计算与保存。</summary>
    [HideFromIl2Cpp]
    private void UpdateBrowser(PotentialSnapshot? availableData)
    {
        try { browser?.Update(selectionView == null ? null : selection, availableData); }
        catch (Exception ex)
        {
            browser?.Close();
            selectionView?.Dispose();
            selectionView = null;
            selectionViewFailed = true;
            Plugin.Logger.LogError($"潜力值成绩浏览页更新失败：{ex}");
        }
    }

    /// <summary>绘制完整成绩页面；Unity 的 GUI 生命周期独立于原生选歌文字。</summary>
    public void OnGUI()
    {
        try { browser?.Draw(); }
        catch (Exception ex)
        {
            browser?.Close();
            Plugin.Logger.LogError($"潜力值成绩浏览页绘制失败，已关闭页面：{ex}");
        }
    }

    /// <summary>原生排序入口通知，交给选歌视图退出自选模式。</summary>
    [HideFromIl2Cpp]
    internal void NativeSortRequested(SongSelectScene scene) => selectionView?.NativeSortRequested(scene);

    /// <summary>原版建立好排序列表后按自选潜力值模式重排现有歌曲。</summary>
    [HideFromIl2Cpp]
    internal void NativeSortCompleted(SongSelectScene scene) => selectionView?.NativeSortCompleted(scene);

    /// <summary>组件退出时释放界面；保存由成绩变更事件同步完成。</summary>
    public void OnDestroy() => ReleaseViews();
}
