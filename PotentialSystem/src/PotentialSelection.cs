extern alias GameData;

using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.Input;
using ifapp.Game.Scenes;
using ifapp.Game.Scenes.SongSelect;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using SongRow = GameData::_K._SH;

namespace InFalsusMod.Potential;

/// <summary>选歌场景的徽章入口、卡片排名和潜力值排序；原版歌曲列表与输入仍由游戏管理。</summary>
internal sealed class PotentialSelection : IDisposable
{
    /// <summary>本视图绑定的原生场景与共享页面。</summary>
    private readonly SongSelectScene scene;
    private readonly PotentialBrowser browser;
    private PotentialView? account;
    private UIButton? entry, sortButton;
    private NativeLabel? selectedRank, sortLabel;
    /// <summary>透明材质仅覆盖入口副本的图形，原版状态动画仍可正常驱动碰撞体。</summary>
    private Texture2D? transparent;
    private readonly List<Material> transparentMaterials = new();
    private bool? buttonsEnabled;
    private int buttonsSortMode = -1;
    /// <summary>池化小卡按对象身份复用文字，歌曲切换时更新内容。</summary>
    private readonly Dictionary<IntPtr, NativeLabel> ranks = new();
    /// <summary>当前排名快照及排序模式；零为原版，一为降序，二为升序。</summary>
    private PotentialSnapshot? snapshot, sortedSnapshot;
    private int sortMode;
    private byte sortedDifficulty;
    private IntPtr sortedList;
    private int sortedCount;
    /// <summary>避免关闭恢复过程中再次参与排序。</summary>
    private bool disposed;

    /// <summary>建立独立控件，全部使用原版按钮输入与原生字体。</summary>
    internal PotentialSelection(SongSelectScene scene, PotentialBadges badges, PotentialBrowser browser)
    {
        this.scene = scene;
        this.browser = browser;
        try
        {
            var graphic = scene.leftSongCardContainer.songCardPrefab.jacket;
            account = new PotentialView(scene.topBar.text, scene.scene.transform, graphic, badges);
            entry = NativeButtons.Create(scene.backButton, scene, "PotentialSystem.OpenResults", " ", _ => browser.RequestOpen(scene, snapshot));
            NativeButtons.Place(entry, scene.scene.transform, Vector2.zero, new Vector2(220f, 220f));
            var layout = entry.constrained2D.constrained2DTransform;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Top;
            layout.offset = new Vector2(0f, -115f);
            entry.constrained2D.constrained2DTransform = layout;
            HideButtonGraphics(entry);
            if (entry.ButtonCollider.TryCast<BoxCollider>() is { } collider)
            {
                collider.center = Vector3.zero;
                collider.size = new Vector3(220f, 220f, collider.size.z);
            }
            entry.constrained2D._PQA(0x3f);
            sortButton = NativeButtons.Create(scene.backButton, scene, "PotentialSystem.Sort", " ", _ => CycleSort());
            HideButtonGraphics(sortButton);
            var original = scene.songSelectSortingButton.button.constrained2D.constrained2DTransform;
            // anchor 是控件自身的枢轴；先还原原按钮中心，再在其右侧留出 20 像素间距。
            var originalCenter = original.offset + Vector2.Scale(new Vector2(0.5f, 0.5f) - original.anchor, original.sizeInPixels);
            NativeButtons.Place(sortButton, scene.songSelectSortingButton.button.transform.parent,
                originalCenter + new Vector2(original.sizeInPixels.x / 2f + 145f, 0f), new Vector2(250f, 64f));
            layout = sortButton.constrained2D.constrained2DTransform;
            layout.parentAlignment = original.parentAlignment;
            sortButton.constrained2D.constrained2DTransform = layout;
            sortButton.constrained2D._PQA(0x3f);
            if (sortButton.ButtonCollider.TryCast<BoxCollider>() is { } sortCollider)
            {
                sortCollider.center = Vector3.zero;
                sortCollider.size = new Vector3(250f, 64f, sortCollider.size.z);
            }
            sortLabel = new NativeLabel(scene.topBar.text, sortButton.transform, "PotentialSort",
                new Vector2(0.5f, 0.5f), Constrained2DAlignment.Center, Vector2.zero, 3.6f);
            UpdateSortText();
            selectedRank = new NativeLabel(scene.topBar.text, scene.largeSongCard.self.transform,
                "SelectedRank", new Vector2(0.5f, 0.5f), Constrained2DAlignment.Bottom, new Vector2(0f, -35f), 4.4f);
        }
        catch { Dispose(); throw; }
    }

    /// <summary>按当前难度刷新可见卡片排名，并在曲包、难度或成绩变化后重新应用自选排序。</summary>
    internal void Render(PotentialSnapshot? data, string status)
    {
        snapshot = data;
        account?.Render(data, data?.Potential ?? 0m, "", status);
        if (buttonsEnabled != (data != null) || buttonsSortMode != sortMode)
        {
            NativeButtons.SetState(entry!, data != null);
            NativeButtons.SetState(sortButton!, data != null, sortMode != 0);
            buttonsEnabled = data != null;
            buttonsSortMode = sortMode;
        }
        if (sortMode != 0 && data != null && InputManager.Instance?.IsHeadOfStack(scene) == true
            && (sortedSnapshot != data || sortedDifficulty != (byte)scene._Sr
                || sortedList != scene._rr?.Pointer || sortedCount != scene._rr?.Count))
            RefreshOrder();
        UpdateCards(scene.leftSongCardContainer);
        UpdateCards(scene.rightSongCardContainer);
        selectedRank?.Set(PotentialPresentation.RankLine(data?.Find(new ChartKey(scene._sr.Value, (byte)scene._Sr))), new Color(0.7f, 1f, 0.85f));
    }

    /// <summary>小卡数据会被原版反复绑定，文字依据当前实际 SongId 更新而不依赖位置。</summary>
    private void UpdateCards(SongSelectSmallCardScrollContainer container)
    {
        if (container?.leftToRightOrderedContainers == null)
            return;
        foreach (var card in container.leftToRightOrderedContainers)
        {
            if (card == null || card.self == null)
                continue;
            if (!ranks.TryGetValue(card.Pointer, out var label) || label.Text == null)
            {
                label = new NativeLabel(scene.topBar.text, card.self.transform, "ChartRank",
                    new Vector2(0.5f, 0.5f), Constrained2DAlignment.Bottom, new Vector2(0f, -27f), 3.6f);
                ranks[card.Pointer] = label;
            }
            var row = card._py;
            var play = row == null ? null : snapshot?.Find(new ChartKey(row._A.Value, (byte)scene._Sr));
            label.Set(PotentialPresentation.RankLine(play), new Color(0.7f, 1f, 0.85f));
        }
    }

    /// <summary>额外按钮在降序、升序、原版之间切换；不把非法值写入原生排序枚举。</summary>
    private void CycleSort()
    {
        if (snapshot == null || InputManager.Instance?.IsHeadOfStack(scene) != true)
            return;
        try
        {
            sortMode = (sortMode + 1) % 3;
            UpdateSortText();
            RefreshOrder();
        }
        catch (Exception ex)
        {
            sortMode = 0;
            UpdateSortText();
            Plugin.Logger.LogError($"潜力值排序失败，已退出该模式：{ex}");
            RestoreOrder();
        }
    }

    /// <summary>原版排序按钮被点击时退出潜力值模式，让原版完整处理其十个排序选项。</summary>
    internal void NativeSortRequested(SongSelectScene current)
    {
        if (scene != current || sortMode == 0)
            return;
        sortMode = 0;
        UpdateSortText();
    }

    /// <summary>原版已建立/排序好当前曲包列表后，只调整现有行的顺序，不增加歌曲或改变解锁状态。</summary>
    internal void NativeSortCompleted(SongSelectScene current)
    {
        if (disposed || scene != current || sortMode == 0 || snapshot == null || scene._rr == null)
            return;
        try
        {
            byte difficulty = (byte)scene._Sr;
            var rows = new List<(SongRow Row, RankedPlay? Best)>();
            foreach (var row in scene._rr)
                rows.Add((row, snapshot.Find(new ChartKey(row._A.Value, difficulty))));
            var playedFirst = rows.OrderByDescending(row => row.Best != null);
            var ordered = sortMode == 1
                ? playedFirst.ThenByDescending(row => row.Best?.Potential).ThenBy(row => row.Row._A.Value)
                : playedFirst.ThenBy(row => row.Best?.Potential).ThenBy(row => row.Row._A.Value);
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
            UpdateSortText();
            throw;
        }
    }

    /// <summary>复用原版排序回调的 Ao→ON→ZN 流程，保留当前歌曲身份并刷新左右滚动容器。</summary>
    private void RefreshOrder()
    {
        var selected = scene._sr;
        scene._Ao(scene._Tr);
        // 原生 Ao 的后置补丁在这里已经把当前曲包列表按潜力值重排。
        scene._ON(selected);
        scene.StartCoroutine(scene._ZN());
    }

    /// <summary>恢复原版顺序失败只记录日志，不影响其他自有 UI 的销毁。</summary>
    private void RestoreOrder()
    {
        if (scene == null || !scene.isActiveAndEnabled)
            return;
        try { RefreshOrder(); }
        catch (Exception ex) { Plugin.Logger.LogError($"恢复原版选歌排序失败：{ex}"); }
    }

    /// <summary>模式变化时更新入口文案；原版排序名称与枚举保持由游戏维护。</summary>
    private void UpdateSortText()
    {
        sortLabel?.Set(sortMode == 1 ? "潜力值 ↓" : sortMode == 2 ? "潜力值 ↑" : "潜力值排序",
            sortMode == 0 ? Color.white : new Color(0.7f, 1f, 0.85f));
    }

    /// <summary>用完全透明的自有纹理隐藏原版按钮装饰，避免悬停动画重新启用 renderer 时露出返回图案。</summary>
    private void HideButtonGraphics(UIButton button)
    {
        if (transparent == null)
        {
            transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            { name = "PotentialSystem.Transparent", hideFlags = HideFlags.HideAndDontSave };
            transparent.LoadRawTextureData(new Il2CppStructArray<byte>(new byte[4]));
            transparent.Apply(false, true);
        }
        foreach (var graphic in button.GetComponentsInChildren<Constrained2D>(true))
        {
            var layout = graphic.constrained2DTransform;
            if (layout.type is not (Constrained2DType.Sprite or Constrained2DType.Sprite9Slice)
                || layout.renderableTargetMaterial == null)
                continue;
            var material = new Material(layout.renderableTargetMaterial)
            { name = "PotentialSystem.HitArea", hideFlags = HideFlags.HideAndDontSave, mainTexture = transparent };
            transparentMaterials.Add(material);
            graphic._irA(material);
        }
    }

    /// <summary>退出排序并释放自有对象；原版 UIButton.OnDestroy 会注销相应输入登记。</summary>
    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        bool restore = sortMode != 0;
        sortMode = 0;
        if (restore)
            RestoreOrder();
        account?.Dispose();
        account = null;
        selectedRank?.Dispose();
        sortLabel?.Dispose();
        selectedRank = sortLabel = null;
        foreach (var label in ranks.Values)
            label.Dispose();
        ranks.Clear();
        if (entry != null)
            Object.Destroy(entry.gameObject);
        if (sortButton != null)
            Object.Destroy(sortButton.gameObject);
        entry = sortButton = null;
        foreach (var material in transparentMaterials)
            if (material != null)
                Object.Destroy(material);
        transparentMaterials.Clear();
        if (transparent != null)
            Object.Destroy(transparent);
        transparent = null;
    }
}
