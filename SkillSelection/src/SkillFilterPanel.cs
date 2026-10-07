using InputManager = ifapp.Game.Input.InputManager;
using Il2CppInterop.Runtime.Attributes;
using ifapp.Game.Data;
using ifapp.Game.Scenes.Recipe;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod;

/// <summary>在制卡界面维护原版技能筛选入口，并借用配方筛选浮窗承载原生图标控件。</summary>
public sealed class SkillFilterPanel : MonoBehaviour
{
    /// <summary>左侧控制区宽度，单位为布局像素；从原 680 扩大到 1.35 倍。</summary>
    private const float SidebarWidth = 918f;

    /// <summary>跨原生钩子与 Unity Update 传递失败状态时使用的短临界区。</summary>
    private static readonly object failureSync = new();
    /// <summary>原生材料视图失败后等待主线程显示的本地化键。</summary>
    private static string? pendingFilterFailure;

    /// <summary>面板所属的当前制卡界面。</summary>
    private CraftingLayer? owner;
    /// <summary>同时持有当前制卡界面和目标配方筛选浮窗的场景。</summary>
    private RecipeScene? recipeScene;
    /// <summary>位于颜色筛选栏右侧的原版入口。</summary>
    private UIButton? entryButton;
    /// <summary>为入口提供当前语言可见文字和字体的原版按钮。</summary>
    private UIButton? entryTextTemplate;
    /// <summary>入口独有的图案资源，随当前制卡界面释放。</summary>
    private SkillFilterEntryVisuals? entryVisuals;
    /// <summary>入口上次是否可点击，用于同步恢复原生悬停登记。</summary>
    private bool entryInputEnabled;
    /// <summary>同一制卡实例创建入口失败后不逐帧重试。</summary>
    private bool entryAttempted;
    /// <summary>当前制卡实例准备原版筛选浮窗失败后不重复尝试。</summary>
    private bool shellFailed;
    /// <summary>入口激活或语言切换后需要重新写入模组文字。</summary>
    private bool entryTextDirty = true;

    /// <summary>当前借用的、已由 RecipeScene 初始化的完整原版筛选浮窗。</summary>
    private RecipeFilterLayer? filterLayer;
    /// <summary>借用浮窗原有的筛选回调，归还时原样恢复。</summary>
    private RecipeFilterLayer._wd? originalFilterChanged;
    /// <summary>借用期间隐藏的原版筛选内容及其原始活动状态。</summary>
    private readonly List<BuiltInElementState> builtInElementStates = new();
    /// <summary>原版打开函数和居中定位会修改的布局，归还时恢复。</summary>
    private readonly List<BuiltInLayoutState> builtInLayoutStates = new();
    /// <summary>模组标题副本及其本地化键，不改写原版标题。</summary>
    private readonly List<Heading> headings = new();
    /// <summary>为动态技能图标提供原版材质和层级的场景模板。</summary>
    private TraitIcon? traitIconTemplate;
    /// <summary>技能图标读取的当前游戏定义。</summary>
    private GameData? gameData;

    /// <summary>入口点击所在帧；下一帧才切换输入所有权，负值表示没有待打开请求。</summary>
    private int openRequestedFrame = -1;
    /// <summary>浮窗是否可见或正在执行原版关闭动画。</summary>
    private bool open;
    /// <summary>是否已经调用原版关闭入口并等待动画隐藏浮窗。</summary>
    private bool closing;
    /// <summary>筛选条件改变后，在下一次 Update 刷新原版列表。</summary>
    private bool refresh;
    /// <summary>下一次发现制卡界面的非缩放时间，单位秒。</summary>
    private float nextDiscovery;
    /// <summary>入口上次显示的语言。</summary>
    private Str.Strings.Localization entryLanguage;
    /// <summary>浮窗控件上次显示的语言。</summary>
    private Str.Strings.Localization controlsLanguage;
    /// <summary>筛选或语言变化后要求重绘全部控件。</summary>
    private bool controlsDirty = true;

    /// <summary>按伤害、回复、增益、减益四行排列的完整技能族。</summary>
    private readonly TraitInfo[] familyTraits = SkillFilterCatalog.Families;
    /// <summary>当前筛选浮窗专用的原版特性说明副本。</summary>
    private SkillFilterTooltip? tooltip;
    /// <summary>原生悬停回调只记录目标，说明刷新留在 Update 的异常边界。</summary>
    private int hoveredTrait = -1;
    /// <summary>原版浮窗中的全部重置按钮。</summary>
    private UIButton? resetButton;
    /// <summary>独立多选技能等级 I/II/III 的原版按钮。</summary>
    private readonly List<UIButton> tierButtons = new();
    /// <summary>与全部技能族一一对应的原版方形状态按钮。</summary>
    private readonly List<UIButton> traitButtons = new();
    /// <summary>与技能按钮一一对应的原版技能图标。</summary>
    private readonly List<TraitIcon> traitIcons = new();
    /// <summary>固定显示在图标下方、随宿主按钮一同销毁的本地化名称。</summary>
    private readonly List<FastText.FastText> traitNames = new();
    /// <summary>要求命中任一所选技能的原版长按钮。</summary>
    private UIButton? matchAnyButton;
    /// <summary>要求命中全部所选技能的原版长按钮。</summary>
    private UIButton? matchAllButton;

    /// <summary>等待原版 Modal 空闲后显示的失败消息键。</summary>
    private string? pendingErrorKey;
    /// <summary>当前由本组件占用的原版错误 Modal。</summary>
    private NativeModalSession? errorSession;

    /// <summary>绑定 IL2CPP 创建的组件。</summary>
    /// <param name="pointer">组件原生指针。</param>
    public SkillFilterPanel(IntPtr pointer) : base(pointer) { }

    /// <summary>跟随制卡界面生命周期，维护原版浮窗并在按钮回调之外刷新材料列表。</summary>
    public void Update()
    {
        DiscoverOwner();
        if (owner == null)
            return;

        ObserveFilterLayer();
        ObserveErrorModal();
        ConsumeFilterFailure();
        OpenRequestedPanel();
        UpdateEntry();
        if (open)
        {
            HideBuiltInFilterContent();
            if (!closing && (controlsDirty || controlsLanguage != ModText.Language))
                TryRefreshControls();
        }
        UpdateTooltip();
        RefreshCraftingList();
        TryShowError();
    }

    /// <summary>原生筛选回调准备失败时恢复不筛选，并把玩家提示推迟到 Unity 主线程。</summary>
    /// <param name="exception">准备材料视图时的异常。</param>
    internal static void ReportFilterFailure(Exception exception)
    {
        SkillFilter.Families.Clear();
        lock (failureSync)
            pendingFilterFailure = "FilterFailed";
        Plugin.Logger.LogError($"技能筛选视图准备失败，已恢复不筛选：{exception}");
    }

    /// <summary>发现活动制卡界面；owner 变化时归还借用浮窗并清空会话筛选。</summary>
    private void DiscoverOwner()
    {
        if (owner != null && owner.isActiveAndEnabled)
            return;
        CleanupOwner();
        if (Time.realtimeSinceStartup < nextDiscovery)
            return;
        nextDiscovery = Time.realtimeSinceStartup + 0.5f;
        foreach (var layer in Object.FindObjectsOfType<CraftingLayer>())
            if (layer.isActiveAndEnabled)
            {
                owner = layer;
                break;
            }
    }

    /// <summary>原版 ESC、关闭按钮或动画结束后归还借用的筛选浮窗。</summary>
    private void ObserveFilterLayer()
    {
        if (!open || filterLayer != null && filterLayer._bx())
            return;
        open = false;
        closing = false;
        ReleaseFilterLayer();
        controlsDirty = true;
    }

    /// <summary>错误窗口被确定、ESC 或关闭按钮结束后释放回调会话。</summary>
    private void ObserveErrorModal()
    {
        if (errorSession == null || errorSession.IsOpen)
            return;
        if (errorSession.Failure != null)
            Plugin.Logger.LogWarning($"原版技能筛选错误窗口状态读取失败，按已关闭清理：{errorSession.Failure}");
        errorSession = null;
    }

    /// <summary>从原生钩子的短临界区取出失败状态，并关闭筛选浮窗以便显示原版 Modal。</summary>
    private void ConsumeFilterFailure()
    {
        string? error;
        lock (failureSync)
        {
            error = pendingFilterFailure;
            pendingFilterFailure = null;
        }
        if (error == null)
            return;
        pendingErrorKey = error;
        controlsDirty = true;
        refresh = true;
        ClosePanel();
    }

    /// <summary>判断当前制卡界面是否拥有输入，避免覆盖游戏的其他弹窗。</summary>
    /// <returns>能否从筛选栏打开原版浮窗。</returns>
    private bool CanOpen() => owner != null && owner.isActiveAndEnabled && !owner.DisableInputEvents
        && InputManager.Instance != null && InputManager.Instance.IsHeadOfStack(owner);

    /// <summary>在最右颜色按钮后创建始终可用的原版技能选择入口。</summary>
    private void UpdateEntry()
    {
        if (owner == null)
            return;
        var layoutTemplate = owner.iotaTraitFilterButton;
        var colors = owner.colorFilterButtons;
        if (layoutTemplate == null || layoutTemplate.constrained2D == null || colors == null || colors.Length < 2)
            return;
        if (!entryAttempted)
        {
            entryAttempted = true;
            try
            {
                var last = colors[colors.Length - 1];
                entryTextTemplate = layoutTemplate;
                entryButton = NativeButtons.Create(entryTextTemplate, owner, "SkillSelection.Button",
                    ModText.Get("TraitsButton"), Toggle);
                entryVisuals = new SkillFilterEntryVisuals(entryButton, last, colors[colors.Length - 2]);
                entryTextDirty = true;
                Plugin.Logger.LogDebug("已建立技能选择入口。");
            }
            catch (Exception ex)
            {
                if (entryButton != null)
                    Object.Destroy(entryButton.gameObject);
                entryButton = null;
                entryTextTemplate = null;
                entryVisuals?.Dispose();
                entryVisuals = null;
                Plugin.Logger.LogError($"技能选择入口创建失败：{ex}");
            }
        }
        if (entryButton == null || entryTextTemplate == null)
            return;

        bool visible = owner.isActiveAndEnabled;
        if (entryButton.gameObject.activeSelf != visible)
        {
            entryButton.gameObject.SetActive(visible);
            entryTextDirty = visible;
            entryInputEnabled = false;
        }
        if (!visible)
            return;

        if (entryTextDirty || entryLanguage != ModText.Language)
        {
            SkillFilterVisuals.UpdateButtonText(entryButton, entryTextTemplate, ModText.Get("TraitsButton"));
            SkillFilterVisuals.AlignEntryLabel(entryButton);
            entryLanguage = ModText.Language;
            entryTextDirty = false;
        }
        bool enabled = CanOpen() && openRequestedFrame < 0 && !shellFailed
            && errorSession == null && pendingErrorKey == null;
        NativeButtons.SetState(entryButton, enabled);
        if (enabled && !entryInputEnabled)
        {
            // _dSA 重新启用时回到普通态，但 UIManager 仍可能缓存“已悬停”。
            // 清除本按钮的旧登记后重放位置，让第一次按下前先恢复完整悬停态。
            UIManager._A._ktA(entryButton);
            UIManager._A._mtA(entryButton, owner);
            InputManager.Instance.RequestLastMouseMoveReplay();
        }
        entryInputEnabled = enabled;
    }

    /// <summary>记录入口点击，在原生鼠标事件分发结束后打开浮窗。</summary>
    /// <param name="button">触发操作的原版入口。</param>
    private void Toggle(UIButton button)
    {
        if (button != entryButton)
            return;
        if (open)
        {
            ClosePanel();
            return;
        }
        if (openRequestedFrame < 0 && CanOpen() && !shellFailed && errorSession == null && pendingErrorKey == null)
            openRequestedFrame = Time.frameCount;
    }

    /// <summary>在入口点击的下一帧取得浮窗输入，避免新控件进入尚未结束的原生点击分发。</summary>
    private void OpenRequestedPanel()
    {
        if (openRequestedFrame < 0 || Time.frameCount <= openRequestedFrame)
            return;
        openRequestedFrame = -1;
        if (open || !CanOpen() || shellFailed || errorSession != null || pendingErrorKey != null)
            return;
        try
        {
            EnsureFilterLayer();
            // owner 为空时浮窗自行取得输入所有权，ESC、关闭按钮和技能控件共用同一接收者。
            filterLayer!._ZW(RecipeFilterLayer._Wd._QcA, SkillFilterVisuals.CenterOffset(filterLayer), null);
            // 原生打开函数会重设背景锚点，使用打开后的几何边界校正最终中心。
            filterLayer.self._sQA(SkillFilterVisuals.CenterOffset(filterLayer));
            if (!filterLayer._bx())
                throw new InvalidOperationException("原版配方筛选浮窗未进入可见状态。");
            open = true;
            closing = false;
            HideBuiltInFilterContent();
            // UIManager 按登记序号倒序命中按钮。_ZW 会登记整面背景拦截层，
            // 模组控件必须在它之后创建并登记，才能取得背景前方的点击优先级。
            CreateFilterControls();
            controlsDirty = true;
            RefreshControls();
            Plugin.Logger.LogDebug("已打开技能选择浮窗。");
        }
        catch (Exception ex)
        {
            if (filterLayer != null)
                ForceCloseFilterLayer(filterLayer, ex);
            ReleaseFilterLayer();
            open = false;
            closing = false;
            shellFailed = true;
            pendingErrorKey = "FilterRefreshFailed";
            Plugin.Logger.LogError($"原版技能筛选浮窗准备或打开失败：{ex}");
        }
    }

    /// <summary>借用当前 RecipeScene 的完整原版浮窗，记录归还状态并准备控件模板。</summary>
    private void EnsureFilterLayer()
    {
        if (filterLayer != null)
            return;
        if (owner == null || entryButton == null)
            throw new InvalidOperationException("制卡筛选入口尚未就绪。");

        var layer = FindAvailableFilterLayer()
            ?? throw new InvalidOperationException("当前制卡场景没有空闲且完整绑定的 RecipeFilterLayer。");
        filterLayer = layer;
        originalFilterChanged = layer.OnFilterChanged;
        try
        {
            layer.OnFilterChanged = null;
            foreach (var target in new[] { layer.self, layer.contentContainer, layer.backing,
                layer.cornerBrackets, layer.buttons.ResetAllButton.transform.parent.GetComponent<Constrained2D>() })
                builtInLayoutStates.Add(new BuiltInLayoutState(target, target.constrained2DTransform));
            gameData = recipeScene?.dataAccess?.GameData
                ?? throw new InvalidOperationException("当前游戏技能定义尚未就绪。");
            TraitCatalog.Validate(gameData);
            traitIconTemplate = FindTraitIconTemplate()
                ?? throw new InvalidOperationException("场景中没有完整的原版技能图标模板。");
            HideBuiltInFilterContent();
        }
        catch
        {
            ReleaseFilterLayer();
            throw;
        }
    }

    /// <summary>从当前 owner 所属 RecipeScene 选择字段完整且关闭的原版配方筛选浮窗。</summary>
    /// <returns>可暂时借用的原版浮窗；没有时为空。</returns>
    private RecipeFilterLayer? FindAvailableFilterLayer()
    {
        if (owner == null)
            return null;
        foreach (var scene in Resources.FindObjectsOfTypeAll<RecipeScene>())
        {
            if (scene == null || !scene.gameObject.scene.IsValid() || scene.craftingLayer == null
                || scene.craftingLayer.Pointer != owner.Pointer)
                continue;
            var candidate = scene.recipeFilterLayer;
            var buttons = candidate?.buttons;
            if (candidate == null || candidate.self == null || candidate.contentContainer == null
                || candidate.backing == null || candidate.cornerBrackets == null
                || candidate.closeButton == null || candidate.openAnimator == null
                || candidate.closeAnimator == null || buttons == null
                || buttons.ResetAllButton == null || buttons.ColorTypeButtons == null
                || buttons.ColorTypeButtons.Length == 0 || buttons.TierButtons == null
                || buttons.TierButtons.Length < 3 || buttons.TraitButtons == null
                || buttons.TraitButtons.Length < 2 || buttons.MasteryProgressButtons == null
                || buttons.MasteryProgressButtons.Length == 0 || candidate.transform.parent != null
                && !candidate.transform.parent.gameObject.activeInHierarchy)
                continue;
            try
            {
                if (!candidate._bx())
                {
                    recipeScene = scene;
                    return candidate;
                }
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"跳过状态不可读的原版配方筛选浮窗：{ex.Message}");
            }
        }
        return null;
    }

    /// <summary>选择拥有完整材质绑定的场景技能图标，优先使用设计尺寸较大的版本。</summary>
    /// <returns>可安全复制的原版技能图标；没有时为空。</returns>
    private static TraitIcon? FindTraitIconTemplate()
    {
        TraitIcon? best = null;
        float bestArea = -1f;
        bool bestUsesSmallStyle = false;
        foreach (var icon in Resources.FindObjectsOfTypeAll<TraitIcon>())
        {
            if (icon == null || !icon.gameObject.scene.IsValid() || icon.self == null
                || icon.frame == null || icon.icon == null || icon.traitIconAssets == null)
                continue;
            bool usesSmallStyle = icon.style == TraitIcon._BI._sIb;
            var size = icon.self.constrained2DTransform.sizeInPixels;
            float area = Math.Abs(size.x * size.y);
            if (best != null && (!usesSmallStyle || bestUsesSmallStyle) && area <= bestArea)
                continue;
            if (bestUsesSmallStyle && !usesSmallStyle)
                continue;
            best = icon;
            bestArea = area;
            bestUsesSmallStyle = usesSmallStyle;
        }
        return best;
    }

    /// <summary>隐藏浮窗自带筛选内容；遮罩、边框、关闭按钮和动画仍由原版管理。</summary>
    private void HideBuiltInFilterContent()
    {
        var layer = filterLayer;
        var buttons = layer?.buttons;
        if (layer == null || buttons == null)
            return;
        if (builtInElementStates.Count == 0)
        {
            var parent = buttons.ResetAllButton.transform.parent;
            RememberBuiltInElement(parent.Find("TitleText")?.gameObject);
            RememberBuiltInElement(parent.Find("TitleTextShadow")?.gameObject);
            RememberBuiltInElement(buttons.ResetAllButton.gameObject);
            RememberBuiltInElement(buttons.TierButtons[0].transform.parent.gameObject);
            RememberBuiltInElement(buttons.ColorTypeButtons[0].transform.parent.gameObject);
            RememberBuiltInElement(buttons.TraitButtons[0].transform.parent.gameObject);
            RememberBuiltInElement(buttons.MasteryProgressButtons[0].transform.parent.gameObject);
        }
        foreach (var state in builtInElementStates)
            if (state.Target != null && state.Target.activeSelf)
                state.Target.SetActive(false);
    }

    /// <summary>记录一个原版内容对象借用前的活动状态。</summary>
    /// <param name="target">待隐藏的原版对象。</param>
    private void RememberBuiltInElement(GameObject? target)
    {
        if (target == null || builtInElementStates.Any(state => state.Target.Pointer == target.Pointer))
            return;
        builtInElementStates.Add(new BuiltInElementState(target, target.activeSelf));
    }

    /// <summary>在原版完整外壳内放置侧栏操作和全部技能族，不保留原版颜色或精通内容。</summary>
    private void CreateFilterControls()
    {
        var layer = filterLayer ?? throw new InvalidOperationException("原版筛选浮窗尚未建立。");
        var buttons = layer.buttons ?? throw new InvalidOperationException("原版筛选浮窗缺少按钮定义。");
        var tiers = buttons.TierButtons;
        var traits = buttons.TraitButtons;
        var parent = buttons.ResetAllButton.transform.parent;
        var nameTemplate = traits[0].transform.parent.Find("TitleText")?.GetComponent<FastText.FastText>()
            ?? throw new InvalidOperationException("原版特性分区缺少名称字体模板。");

        AddHeading(parent, parent, "TraitsButton", new Vector2(36f, -57f));
        resetButton = CreateIconControl(buttons.ResetAllButton, "Reset", _ => ResetSelection());
        SkillFilterVisuals.Place(resetButton, parent, new Vector2(SidebarWidth / 2f, -165f));

        AddHeading(tiers[0].transform.parent, parent, "TierTitle", new Vector2(130f, -255f));
        for (int i = 0; i < 3; i++)
        {
            int tier = i + 1;
            var button = CreateIconControl(tiers[i], $"Tier{tier}", _ => ToggleTier(tier));
            tierButtons.Add(button);
            SkillFilterVisuals.Place(button, parent, new Vector2(SidebarWidth / 2f + (i - 1) * 250f, -375f));
        }

        int index = 0;
        for (int row = 0; row < SkillFilterCatalog.Groups.Length; row++)
        {
            var group = SkillFilterCatalog.Groups[row];
            // 四行中心为 -110/-310/-510/-710；128 像素状态框之外留出两行名称高度。
            float y = -110f - row * 200f;
            // 标题右缘靠近技能行；原版字体基准线补偿 14 像素，使字形视觉中心与图标等高。
            var heading = AddHeading(traits[0].transform.parent, parent, group.TitleKey, new Vector2(1160f, y - 14f));
            heading.SetFontSize(3.2f, FastText.FastText.UpdateType.DontUpdateMesh);
            heading.SetAlignment(FastText.Data.AlignmentOptions.Right, FastText.FastText.UpdateType.DontUpdateMesh);
            for (int column = 0; column < group.TraitIds.Length; column++, index++)
            {
                int capturedIndex = index;
                // 方形选中框保留原版状态；内部六边形图标负责区分效果和触发条件。
                var button = CreateIconControl(tiers[0], $"Trait{index + 1:00}", _ => ToggleTrait(capturedIndex));
                traitButtons.Add(button);
                button._eSA((Action<UIButton, bool>)((_, isHovered) => SetHoveredTrait(capturedIndex, isHovered)));
                SkillFilterVisuals.ResizeTraitButton(button);
                SkillFilterVisuals.Place(button, parent, new Vector2(1330f + column * 290f, y), false);
                traitIcons.Add(CreateTraitIcon(traitIconTemplate!, button, index));
                traitNames.Add(SkillFilterVisuals.CreateTraitName(nameTemplate, button, index,
                    ModText.TraitName(familyTraits[index].Family)));
                button.gameObject.SetActive(true);
            }
        }

        matchAnyButton = CreateTextControl(traits[0], "MatchAny", "MatchAnyCompact", _ => SetMatchMode(false));
        SkillFilterVisuals.Place(matchAnyButton, parent, new Vector2(SidebarWidth / 2f, -590f));
        matchAllButton = CreateTextControl(traits[1], "MatchAll", "MatchAllCompact", _ => SetMatchMode(true));
        SkillFilterVisuals.Place(matchAllButton, parent, new Vector2(SidebarWidth / 2f, -710f));
        var hoverTemplate = owner?.traitIconHoverContainer
            ?? throw new InvalidOperationException("当前制卡界面缺少原版特性说明组件。");
        tooltip = new SkillFilterTooltip(hoverTemplate, parent, recipeScene!.dataAccess);
    }

    /// <summary>复制原版分区标题到模组内容区；源标题及其本地化身份保持不变。</summary>
    /// <param name="source">包含原版 TitleText 的分区。</param>
    /// <param name="parent">原版浮窗内容的左上角容器。</param>
    /// <param name="key">模组标题的五语资源键。</param>
    /// <param name="offset">相对内容左上角的位置，单位为原版布局像素。</param>
    /// <returns>面板持有的标题副本，可在首次绘制前调整字号。</returns>
    private FastText.FastText AddHeading(Transform source, Transform parent, string key, Vector2 offset)
    {
        var template = source.Find("TitleText")?.GetComponent<FastText.FastText>()
            ?? throw new InvalidOperationException("原版筛选分区缺少标题文字。");
        var label = SkillFilterVisuals.CreateLabel(template, parent, key, ModText.Get(key), offset);
        headings.Add(new Heading(label, key));
        return label;
    }

    /// <summary>复制原版文字按钮，返回时仍隐藏，由调用者放入模组内容区。</summary>
    /// <param name="template">原版长按钮模板。</param>
    /// <param name="name">对象名称后缀。</param>
    /// <param name="textKey">初始本地化文字键。</param>
    /// <param name="clicked">按钮动作。</param>
    /// <returns>隐藏且已登记输入的按钮。</returns>
    [HideFromIl2Cpp]
    private UIButton CreateTextControl(UIButton template, string name, string textKey, Action<UIButton> clicked)
    {
        return NativeButtons.Create(template, filterLayer!, "SkillSelection." + name,
            ModText.Get(textKey), clicked);
    }

    /// <summary>复制不要求文字层级的原版图标按钮，并登记到当前筛选浮窗。</summary>
    /// <param name="template">提供布局、碰撞体及动画的原版按钮。</param>
    /// <param name="name">对象名称后缀。</param>
    /// <param name="clicked">按钮动作。</param>
    /// <returns>已登记输入归属且初始隐藏的按钮。</returns>
    [HideFromIl2Cpp]
    private UIButton CreateIconControl(UIButton template, string name, Action<UIButton> clicked)
    {
        var manager = UIManager._A;
        var layer = filterLayer;
        if (manager == null || layer == null || template.constrained2D == null)
            throw new InvalidOperationException("原生图标按钮输入或布局尚未就绪。");
        var root = Object.Instantiate(template.gameObject, template.transform.parent, false);
        root.name = "InFalsus.SkillSelection." + name;
        root.SetActive(false);
        try
        {
            var button = root.GetComponent<UIButton>();
            if (button == null || button.constrained2D == null || button.ButtonCollider == null)
                throw new InvalidOperationException("原生图标按钮缺少布局或碰撞体。");
            button._Gjb = null;
            button._hjb = null;
            button._Hjb = null;
            button._ijb = null;
            button._Ijb = null;
            button.UserComponent = null;
            button._DSA(clicked);
            button._dSA(_m._dI._YIb);
            manager._mtA(button, layer);
            return button;
        }
        catch
        {
            Object.Destroy(root);
            throw;
        }
    }

    /// <summary>在方形状态按钮中央复制完整原版特性图标，内部按钮不接收输入。</summary>
    /// <param name="template">完整原版技能图标。</param>
    /// <param name="host">负责点击、悬停和选中状态的方形按钮。</param>
    /// <param name="slot">从零开始的技能族索引。</param>
    /// <returns>可按技能定义刷新的图标。</returns>
    private static TraitIcon CreateTraitIcon(TraitIcon template, UIButton host, int slot)
    {
        var root = Object.Instantiate(template.gameObject, host.transform, false);
        root.name = $"InFalsus.SkillSelection.TraitIcon{slot + 1:00}";
        root.SetActive(false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale = Vector3.one;
        try
        {
            SkillFilterVisuals.PrepareDisplayClone(root, host.gameObject.layer);
            var icon = root.GetComponent<TraitIcon>() ?? root.GetComponentInChildren<TraitIcon>(true)
                ?? throw new InvalidOperationException("原版技能图标模板层级已改变。");
            if (icon.self == null || icon.frame == null || icon.icon == null)
                throw new InvalidOperationException("原版技能图标缺少布局组件。");
            // 原版 small 完整样式保留触发条件外框；图标自身不呈现具体等级。
            icon.style = TraitIcon._BI._tIb;

            // Container 的 sizeInPixels 不会缩放子图像；图标和容器必须分别设置。
            SkillFilterVisuals.CenterTraitIcon(icon);
            root.SetActive(true);
            icon.self._rQA();
            icon.self._PQA(0x3f);
            return icon;
        }
        catch
        {
            Object.Destroy(root);
            throw;
        }
    }

    /// <summary>在 Unity Update 异常边界内刷新控件；原生资源失效时关闭浮窗并排队提示。</summary>
    private void TryRefreshControls()
    {
        try
        {
            RefreshControls();
        }
        catch (Exception ex)
        {
            pendingErrorKey = "FilterRefreshFailed";
            ClosePanel();
            Plugin.Logger.LogError($"技能筛选原版控件刷新失败：{ex}");
        }
    }

    /// <summary>使用当前语言和筛选状态更新完整的一屏控件及原版技能图标。</summary>
    private void RefreshControls()
    {
        var nativeButtons = filterLayer?.buttons;
        if (nativeButtons == null || gameData == null || resetButton == null || matchAnyButton == null
            || matchAllButton == null || tierButtons.Count != 3 || traitButtons.Count != familyTraits.Length
            || traitIcons.Count != familyTraits.Length || traitNames.Count != familyTraits.Length)
            throw new InvalidOperationException("技能筛选控件或图标未完整建立。");

        // 重置按钮保持可见可点；零条件时执行重置本身就是安全的空操作。
        NativeButtons.SetState(resetButton, true);
        foreach (var heading in headings)
            SkillFilterVisuals.SetText(heading.Label, ModText.Get(heading.Key));
        foreach (var label in resetButton.GetComponentsInChildren<FastText.FastText>(true))
            SkillFilterVisuals.RefreshTextMesh(label);
        for (int i = 0; i < tierButtons.Count; i++)
            NativeButtons.SetState(tierButtons[i], true, SkillFilter.Tiers.Contains(i + 1));

        UpdateTextControl(matchAnyButton, nativeButtons.TraitButtons[0], ModText.Get("MatchAnyCompact"), true,
            !SkillFilter.MatchAll);
        UpdateTextControl(matchAllButton, nativeButtons.TraitButtons[1], ModText.Get("MatchAllCompact"), true,
            SkillFilter.MatchAll);

        for (int index = 0; index < familyTraits.Length; index++)
        {
            var info = familyTraits[index];
            if (info.Id <= 0 || info.Id >= gameData.traits.Length)
                throw new InvalidOperationException($"技能图标编号 {info.Id} 已失效。");
            var specification = gameData.traits[info.Id];
            // 最后一个参数隐藏等级标记；技能族图标不带模板的大尺寸等级图层。
            traitIcons[index]._qsA(ref specification, false, false, true);
            SkillFilterVisuals.CenterTraitIcon(traitIcons[index]);
            NativeButtons.SetState(traitButtons[index], true, SkillFilter.Families.Contains(info.Family));
            SkillFilterVisuals.SetText(traitNames[index], ModText.TraitName(info.Family));
        }

        controlsLanguage = ModText.Language;
        controlsDirty = false;
    }

    /// <summary>同步一个原版文字控件的本地化文字和视觉状态。</summary>
    /// <param name="button">目标复制按钮。</param>
    /// <param name="template">对应位置的原版文字模板。</param>
    /// <param name="text">已完成本地化的文字。</param>
    /// <param name="enabled">是否允许点击。</param>
    /// <param name="selected">是否使用原版选中态。</param>
    private static void UpdateTextControl(UIButton button, UIButton template, string text, bool enabled, bool selected)
    {
        SkillFilterVisuals.UpdateButtonText(button, template, text, true);
        NativeButtons.SetState(button, enabled, selected);
    }

    /// <summary>直接选择任一或全部匹配方式并请求刷新材料。</summary>
    /// <param name="matchAll">是否要求所有已选技能族同时存在。</param>
    private void SetMatchMode(bool matchAll)
    {
        if (SkillFilter.MatchAll == matchAll)
            return;
        SkillFilter.MatchAll = matchAll;
        FilterChanged();
    }

    /// <summary>切换一个精确技能等级；空选择与三个等级全选具有相同匹配结果。</summary>
    /// <param name="tier">按钮对应的一至三级。</param>
    private void ToggleTier(int tier)
    {
        if (!SkillFilter.Tiers.Remove(tier))
            SkillFilter.Tiers.Add(tier);
        FilterChanged();
    }

    /// <summary>恢复任意匹配、不限制等级且不选择技能族。</summary>
    private void ResetSelection()
    {
        if (SkillFilter.Families.Count == 0 && SkillFilter.Tiers.Count == 0 && !SkillFilter.MatchAll)
            return;
        SkillFilter.Families.Clear();
        SkillFilter.Tiers.Clear();
        SkillFilter.MatchAll = false;
        FilterChanged();
    }

    /// <summary>切换指定技能族的选择；索引直接对应全部可见按钮。</summary>
    /// <param name="index">从零开始的技能族索引。</param>
    private void ToggleTrait(int index)
    {
        if (index < 0 || index >= familyTraits.Length)
            return;
        string family = familyTraits[index].Family;
        if (!SkillFilter.Families.Remove(family))
            SkillFilter.Families.Add(family);
        FilterChanged();
    }

    /// <summary>统一记录筛选变化，并把原生列表刷新推迟到 Update 的异常边界。</summary>
    private void FilterChanged()
    {
        controlsDirty = true;
        refresh = true;
        string tiers = SkillFilter.Tiers.Count == 0 ? "全部" : string.Join(",", SkillFilter.Tiers.OrderBy(tier => tier));
        Plugin.Logger.LogDebug($"特性筛选：已选{SkillFilter.Families.Count}项，等级={tiers}，匹配全部={SkillFilter.MatchAll}。");
    }

    /// <summary>接收原版按钮悬停事件，只记录索引，不在输入分发中创建或更新文字。</summary>
    /// <param name="index">完整展示列表中的技能族索引。</param>
    /// <param name="isHovered">鼠标是否进入该按钮。</param>
    private void SetHoveredTrait(int index, bool isHovered)
    {
        if (isHovered)
            hoveredTrait = index;
        else if (hoveredTrait == index)
            hoveredTrait = -1;
    }

    /// <summary>只在筛选浮窗拥有输入时更新说明；离开或关闭后隐藏，失败沿用面板异常边界。</summary>
    private void UpdateTooltip()
    {
        if (tooltip == null)
            return;
        try
        {
            if (!open || closing || filterLayer == null || filterLayer.DisableInputEvents || InputManager.Instance == null
                || !InputManager.Instance.IsHeadOfStack(filterLayer))
                hoveredTrait = -1;
            if (hoveredTrait < 0 || hoveredTrait >= traitButtons.Count)
            {
                tooltip.Hide();
                return;
            }
            tooltip.Show(familyTraits[hoveredTrait], traitButtons[hoveredTrait].constrained2D.constrained2DTransform.offset,
                filterLayer!.backing.constrained2DTransform.sizeInPixels);
        }
        catch (Exception ex)
        {
            pendingErrorKey = "FilterRefreshFailed";
            ClosePanel();
            Plugin.Logger.LogError($"特性悬浮说明刷新失败：{ex}");
        }
    }

    /// <summary>按现有制卡路径刷新材料和当前详情；失败时关闭浮窗并排队显示原版错误窗口。</summary>
    private void RefreshCraftingList()
    {
        if (!refresh || owner == null)
            return;
        refresh = false;
        try
        {
            var detail = owner.bottomInsetIotaScrollContainer?._a;
            owner._ku();
            if (detail != null && detail.Id.Value is >= 1 and <= 40)
                SkillFilterNativeHooks.RefreshInstances(owner, detail);
        }
        catch (Exception ex)
        {
            pendingErrorKey = "FilterRefreshFailed";
            ClosePanel();
            Plugin.Logger.LogError(ex);
        }
    }

    /// <summary>筛选浮窗关闭后，使用空闲 UiModal 显示失败；游戏 Modal 被占用时继续等待。</summary>
    private void TryShowError()
    {
        if (pendingErrorKey == null || errorSession != null || open)
            return;
        try
        {
            if (!NativeModal.TryShowMessage(ModText.Get("TraitsTitle"), ModText.Get(pendingErrorKey), out var session))
                return;
            errorSession = session;
            pendingErrorKey = null;
        }
        catch (Exception ex)
        {
            pendingErrorKey = null;
            Plugin.Logger.LogError($"原版技能筛选错误窗口创建失败：{ex}");
        }
    }

    /// <summary>通过 RecipeFilterLayer 的原版关闭动画和输入栈恢复路径结束浮窗。</summary>
    private void ClosePanel()
    {
        hoveredTrait = -1;
        tooltip?.Hide();
        if (!open || closing)
            return;
        if (filterLayer == null)
        {
            open = false;
            return;
        }
        try
        {
            filterLayer._Ax();
            closing = true;
        }
        catch (Exception ex)
        {
            ForceCloseFilterLayer(filterLayer, ex, false);
            ReleaseFilterLayer();
            open = false;
            closing = false;
            Plugin.Logger.LogError($"原版技能筛选浮窗关闭失败：{ex}");
        }
    }

    /// <summary>异常或 owner 销毁时释放输入并立即隐藏浮窗，不依赖关闭动画图仍然有效。</summary>
    /// <param name="layer">当前借用的原版浮窗。</param>
    /// <param name="failure">需要附加清理详情的原始异常；正常清理时为空。</param>
    /// <param name="invokeClose">是否先调用一次原版关闭入口。</param>
    private static void ForceCloseFilterLayer(RecipeFilterLayer layer, Exception? failure, bool invokeClose = true)
    {
        if (invokeClose)
        {
            try
            {
                if (layer._qcA != null)
                    layer._Ax();
            }
            catch (Exception ex)
            {
                if (failure != null)
                    failure.Data["FilterLayerClose"] = ex.ToString();
                else
                    Plugin.Logger.LogWarning($"清理技能筛选输入层失败：{ex}");
            }
        }

        try
        {
            var manager = InputManager.Instance;
            if (manager != null && manager.IsHeadOfStack(layer))
                manager.ReleaseInputControl(layer);
            layer._qcA = null;
        }
        catch (Exception ex)
        {
            if (failure != null)
                failure.Data["FilterLayerInputRelease"] = ex.ToString();
            else
                Plugin.Logger.LogWarning($"释放技能筛选输入所有权失败：{ex}");
        }

        try
        {
            // 原版 _ZW 只激活 contentContainer；保留 self，才能在归还后再次原生打开。
            if (layer.contentContainer != null && layer.contentContainer.gameObject.activeSelf)
                layer.contentContainer.gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            if (failure != null)
                failure.Data["FilterLayerHide"] = ex.ToString();
            else
                Plugin.Logger.LogWarning($"隐藏技能筛选原版浮窗失败：{ex}");
        }
    }

    /// <summary>归还浮窗回调和原版内容状态，并销毁本次创建的模组控件。</summary>
    private void ReleaseFilterLayer()
    {
        tooltip?.Dispose();
        tooltip = null;
        hoveredTrait = -1;
        var layer = filterLayer;
        if (layer != null)
        {
            try
            {
                layer.OnFilterChanged = originalFilterChanged;
            }
            catch (Exception ex)
            {
                Plugin.Logger.LogDebug($"恢复原版配方筛选回调失败：{ex.Message}");
            }
        }
        originalFilterChanged = null;

        foreach (var state in builtInLayoutStates)
            if (state.Target != null)
            {
                state.Target.constrained2DTransform = state.Layout;
                state.Target._PQA(0x3f);
            }
        builtInLayoutStates.Clear();
        foreach (var heading in headings)
            if (heading.Label != null)
                Object.Destroy(heading.Label.gameObject);
        headings.Clear();
        foreach (var state in builtInElementStates)
            if (state.Target != null && state.Target.activeSelf != state.Active)
                state.Target.SetActive(state.Active);
        builtInElementStates.Clear();

        DestroyControl(resetButton);
        foreach (var button in tierButtons)
            DestroyControl(button);
        foreach (var button in traitButtons)
            DestroyControl(button);
        DestroyControl(matchAnyButton);
        DestroyControl(matchAllButton);

        filterLayer = null;
        gameData = null;
        traitIconTemplate = null;
        ClearControlReferences();
    }

    /// <summary>销毁一个仅属于技能选择浮窗的动态按钮。</summary>
    /// <param name="button">待销毁按钮。</param>
    private static void DestroyControl(UIButton? button)
    {
        if (button != null)
            Object.Destroy(button.gameObject);
    }

    /// <summary>释放当前制卡 owner 的入口和借用浮窗，并清空只属于该制卡会话的筛选状态。</summary>
    private void CleanupOwner()
    {
        if (filterLayer != null)
            ForceCloseFilterLayer(filterLayer, null, !closing);
        ReleaseFilterLayer();
        openRequestedFrame = -1;
        open = false;
        closing = false;
        if (entryButton != null)
            Object.Destroy(entryButton.gameObject);
        entryButton = null;
        entryTextTemplate = null;
        entryVisuals?.Dispose();
        entryVisuals = null;
        entryInputEnabled = false;
        entryAttempted = false;
        entryTextDirty = true;
        shellFailed = false;
        owner = null;
        recipeScene = null;
        refresh = false;
        controlsDirty = true;
        SkillFilter.Families.Clear();
        SkillFilter.Tiers.Clear();
        SkillFilter.MatchAll = false;
    }

    /// <summary>清空随模组控件一同销毁的包装引用。</summary>
    private void ClearControlReferences()
    {
        resetButton = null;
        tierButtons.Clear();
        traitButtons.Clear();
        traitIcons.Clear();
        traitNames.Clear();
        matchAnyButton = null;
        matchAllButton = null;
    }

    /// <summary>一个原版内容对象及其在借用前的活动状态。</summary>
    /// <param name="Target">原版 GameObject。</param>
    /// <param name="Active">借用前是否活动。</param>
    private readonly record struct BuiltInElementState(GameObject Target, bool Active);

    /// <summary>原版布局的独立值副本，避免浮窗定位污染下次原版打开。</summary>
    /// <param name="Target">本次借用期间被原生打开函数修改的布局组件。</param>
    /// <param name="Layout">借用前的完整序列化布局值。</param>
    private readonly record struct BuiltInLayoutState(Constrained2D Target, Constrained2DTransform Layout);

    /// <summary>模组创建的分区标题和对应五语文案。</summary>
    /// <param name="Label">属于本次浮窗的文字副本。</param>
    /// <param name="Key">模组文案资源键。</param>
    private readonly record struct Heading(FastText.FastText Label, string Key);

    /// <summary>组件卸载时关闭自己的原版 UI，并归还借用浮窗。</summary>
    public void OnDestroy()
    {
        var closingSession = errorSession;
        closingSession?.Close();
        if (closingSession?.Failure != null)
            Plugin.Logger.LogWarning($"组件卸载时关闭原版技能筛选错误窗口失败：{closingSession.Failure}");
        errorSession = null;
        pendingErrorKey = null;
        CleanupOwner();
    }
}
