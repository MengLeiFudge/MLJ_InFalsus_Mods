using BepInEx.Configuration;
using HarmonyLib;
using ifapp.Game.Input;
using ifapp.Game.UI.Common;
using ifapp.Game.UI.Settings;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Lowiro.UI.Projection;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>定数来源配置与游戏和 UI 设置页的原生分类，配置只归本模组所有。</summary>
internal sealed class PotentialSettings : IDisposable
{
    /// <summary>默认安小璃；来源索引独立保存在当前 profile，缺项统一回退等级。</summary>
    private readonly ConfigEntry<int> source;
    /// <summary>原生设置页按对象身份持有副本，场景销毁或插件退出时释放。</summary>
    private readonly Dictionary<IntPtr, PotentialSettingsView> views = new();
    /// <summary>当前选择的定数来源，控制器据此重建全部计算快照。</summary>
    internal ChartConstantSource Source => (ChartConstantSource)source.Value;

    /// <summary>使用 BepInEx 的当前 profile 配置，不写入游戏存档或外部定数素材。</summary>
    /// <param name="config">本模组的 profile 配置。</param>
    internal PotentialSettings(ConfigFile config) => source = config.Bind("PotentialSystem", "ChartConstantSource", (int)ChartConstantSource.AnXiaoLi,
        new ConfigDescription("谱面定数来源：0 官方等级、1 安小璃（默认）、2 萌泪酱；缺失定数回退等级。",
            new AcceptableValueList<int>(Enum.GetValues<ChartConstantSource>().Select(value => (int)value).ToArray())));

    /// <summary>设置页打开后建立分类；再次打开时读取已保存的来源和当前语言。</summary>
    internal void Show(UiSettingGameplayPanel panel)
    {
        foreach (var key in views.Where(pair => !pair.Value.Alive).Select(pair => pair.Key).ToArray())
        {
            views[key].Dispose();
            views.Remove(key);
        }
        if (!views.TryGetValue(panel.Pointer, out var view))
            views.Add(panel.Pointer, view = new PotentialSettingsView(panel, source));
        Refresh(panel);
    }

    /// <summary>原生设置页更新时只在值或语言变化后刷新文字，不争取新的输入归属。</summary>
    internal void Refresh(UiSettingGameplayPanel panel)
    {
        if (!views.TryGetValue(panel.Pointer, out var view))
            return;
        try
        {
            if (view.Alive)
                view.Refresh();
            else
            {
                views.Remove(panel.Pointer);
                view.Dispose();
            }
        }
        catch (Exception ex)
        {
            views.Remove(panel.Pointer);
            view.Dispose();
            Plugin.Logger.LogError($"潜力值设置刷新失败，已释放本次控件：{ex}");
        }
    }

    /// <summary>只接管带有本模组分类的页面，统一原生滚轮与滑块拖动的扩展范围。</summary>
    internal bool TryScroll(UiSettingGameplayPanel panel, float position)
    {
        if (!views.TryGetValue(panel.Pointer, out var view) || !view.Alive)
            return false;
        try
        {
            view.Scroll(position);
            return true;
        }
        catch (Exception ex)
        {
            views.Remove(panel.Pointer);
            view.Dispose();
            Plugin.Logger.LogError($"潜力值设置滚动更新失败，已恢复原生页面：{ex}");
            return false;
        }
    }

    /// <summary>注销本模组设置控件，保留用户选择的配置。</summary>
    public void Dispose()
    {
        foreach (var view in views.Values) view.Dispose();
        views.Clear();
    }
}

/// <summary>克隆原生文字速度所在的一行分类，保留左右箭头、滚动遮罩、字体及按钮效果。</summary>
internal sealed class PotentialSettingsView : IDisposable
{
    /// <summary>借用原生设置页身份；控件与配置由本模组持有。</summary>
    private readonly UiSettingGameplayPanel panel;
    /// <summary>与配置持有者共享来源，切换后由控制器重建计算快照。</summary>
    private readonly ConfigEntry<int> source;
    /// <summary>本分类的克隆根节点及左右箭头选择器。</summary>
    private GameObject? root;
    private UiSettingMultiselectorArrow? selector;
    /// <summary>跨 IL2CPP 的事件保持到控件完成注销。</summary>
    private Il2CppSystem.Action<UiSettingMultiselectorArrow, int>? changed;
    private Il2CppSystem.Action<UiSettingMultiselectorArrow, bool>? hovered;
    /// <summary>最后提交到原生控件的来源和语言，避免逐帧改写。</summary>
    private int? displayed;
    private Str.Strings.Localization language;
    /// <summary>原版手工内容边界与新增分类高度；释放分类时恢复原有滚动范围。</summary>
    private readonly float originalBound, addedHeight;
    private bool boundsExtended;
    /// <summary>父页和分类仍存在时才可刷新。</summary>
    internal bool Alive => panel != null && root != null;

    /// <summary>新分类放在辅助模式分类后，扩展原生手工内容边界并保留模板样式。</summary>
    /// <param name="panel">拥有设置输入与滚动布局的原生页。</param>
    /// <param name="source">已约束合法取值的当前 profile 来源配置。</param>
    internal PotentialSettingsView(UiSettingGameplayPanel panel, ConfigEntry<int> source)
    {
        this.panel = panel;
        this.source = source;
        try
        {
            originalBound = panel.optionsScrollView.ContentContainerBoundY;
            if (!float.IsFinite(originalBound) || originalBound < 0f)
                throw new InvalidOperationException("原生设置内容边界无效。");
            var template = panel.visualNovelContainer ?? throw new InvalidOperationException("设置页缺少文字速度分类。");
            var lastCategory = panel.assistMode.self.transform.parent.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("辅助模式缺少原生分类容器。");
            root = Object.Instantiate(template.gameObject, panel.optionsScrollView.contentContainerTransform, false);
            root.name = "InFalsus.PotentialSystem.Settings";
            root.SetActive(false);
            foreach (var layout in root.GetComponentsInChildren<Constrained2D>(true))
            {
                var transform = layout.constrained2DTransform;
                transform.transform = layout.transform;
                transform.meshRenderer = layout.GetComponent<MeshRenderer>();
                transform.meshFilter = layout.GetComponent<MeshFilter>();
                transform.fastText = layout.GetComponent<Text>();
                layout.constrained2DTransform = transform;
            }
            selector = root.GetComponentInChildren<UiSettingMultiselectorArrow>(true)
                ?? throw new InvalidOperationException("定数来源控件复制失败。");
            foreach (var button in root.GetComponentsInChildren<UIButton>(true))
            {
                button._Gjb = null;
                button._hjb = null;
                button._Hjb = null;
                button._ijb = null;
                button._Ijb = null;
                button.UserComponent = null;
                button.constrained2D = button.GetComponent<Constrained2D>();
                button.ButtonCollider = button.GetComponent<BoxCollider>();
            }
            changed = (Action<UiSettingMultiselectorArrow, int>)((_, index) =>
            {
                if (!Enum.IsDefined(typeof(ChartConstantSource), index))
                {
                    Plugin.Logger.LogWarning($"定数来源收到无效选项：{index}。");
                    return;
                }
                try
                {
                    source.Value = index;
                    displayed = null;
                    Refresh();
                }
                catch (Exception ex)
                {
                    Dispose();
                    Plugin.Logger.LogError($"定数来源切换失败，已释放本次控件：{ex}");
                }
            });
            hovered = (Action<UiSettingMultiselectorArrow, bool>)((_, _) => { });
            var options = Enum.GetValues<ChartConstantSource>()
                .Select(value => Str.TextParameters.CreateDirectString(CommunityConstants.DisplayName(value))).ToArray();
            selector._rg(panel, new Il2CppReferenceArray<Str.TextParameters>(options), hovered, changed);
            selector._sg(_B._Kb._Pj);
            var category = root.GetComponent<Constrained2D>();
            var categoryLayout = category.constrained2DTransform;
            addedHeight = Math.Abs(lastCategory.constrained2DTransform.offset.y - template.constrained2DTransform.offset.y);
            if (!float.IsFinite(addedHeight) || addedHeight <= 0f)
                throw new InvalidOperationException("原生设置分类间距无效。");
            categoryLayout.offset = lastCategory.constrained2DTransform.offset - new Vector2(0f, addedHeight);
            category.constrained2DTransform = categoryLayout;
            root.SetActive(true);
            foreach (var layout in root.GetComponentsInChildren<Constrained2D>(true))
            {
                layout._rQA();
                layout._RQA();
                layout._PQA(0x3f);
            }
            Refresh();
            _m._wi._ERA(false);
            Scroll(1f);
            InputManager.Instance.RequestLastMouseMoveReplay();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>沿用原版滑块比例和坐标映射；原生 _xF 写死 650，新增分类必须使用扩展后的范围。</summary>
    internal void Scroll(float position)
    {
        // 原生布局使用设计分辨率：顶部留 185，滑块中心的底部限位额外留 40。
        float viewportHeight = ProjectionValues.CameraPixelSize.y - 185f;
        float bound = originalBound + addedHeight;
        if (!float.IsFinite(position) || !float.IsFinite(viewportHeight) || viewportHeight <= 40f
            || !float.IsFinite(bound) || bound <= 0f)
            throw new InvalidOperationException("原生设置滚动布局尚未就绪。");
        float thumbHeight = viewportHeight / (1f + bound / viewportHeight);
        float minimum = thumbHeight * 0.5f / viewportHeight;
        float maximum = (viewportHeight - thumbHeight * 0.5f - 40f) / viewportHeight;
        if (maximum <= minimum)
            throw new InvalidOperationException("原生设置滑块范围无效。");
        float value = Mathf.Clamp(position, minimum, maximum);
        float progress = (value - minimum) / (maximum - minimum);
        panel.optionsScrollView.ContentContainerBoundY = bound;
        boundsExtended = true;
        panel.scrollBarIconContainer._ky(thumbHeight);
        panel.scrollBarIconContainer.self._jrA(bound > 1f ? 1f : 0f);
        panel.optionsScrollView.contentContainer._TQA((1f - progress) * bound);
        panel.scrollBarIconContainer.self._TQA(-185f - (viewportHeight - thumbHeight) * (1f - progress));
        panel._Yi = value;
    }

    /// <summary>沿用模板字形颜色和阴影；类别及选项名称不依赖游戏本地化枚举。</summary>
    internal void Refresh()
    {
        if (!Alive || selector == null)
            return;
        var current = NativeLabel.CurrentLanguage();
        if (displayed == source.Value && language == current)
            return;
        selector._Tg(source.Value);
        foreach (var title in selector.titleTexts)
            SetText(title, "谱面定数来源");
        foreach (var text in root!.GetComponentsInChildren<Text>(true))
            if (text.transform.parent == root.transform)
                SetText(text, "潜力值系统");
        foreach (var text in selector.valueText)
            SetText(text, CommunityConstants.DisplayName((ChartConstantSource)source.Value));
        displayed = source.Value;
        language = current;
    }

    /// <summary>保留原生文字的材质及缓存，在语言选择后重新构建非本地化文字。</summary>
    private static void SetText(Text text, string value)
    {
        text.meshRenderer = text.GetComponent<MeshRenderer>();
        text.meshFilter = text.GetComponent<MeshFilter>();
        PotentialFonts.Apply(text);
        text.SetTextNonLocalized(value, Text.UpdateType.DontUpdateMesh);
        text.targetLocalization = NativeLabel.CurrentLanguage();
        text.dirtyFlags |= Text.DirtyFlags.FontChanged | Text.DirtyFlags.TextChanged;
        text.EnsureDirtyFlagsProcessed();
        text.GetComponent<Constrained2D>()._PQA(0x3f);
    }

    /// <summary>注销自有按钮并恢复原生滚动边界；设置样式与输入栈继续由游戏管理。</summary>
    public void Dispose()
    {
        if (root != null)
        {
            root.SetActive(false);
            foreach (var button in root.GetComponentsInChildren<UIButton>(true))
            {
                try { UIManager._A?._ktA(button); }
                catch (Exception ex) { Plugin.Logger.LogError($"注销定数来源设置按钮失败：{ex}"); }
            }
            Object.Destroy(root);
        }
        root = null;
        selector = null;
        changed = null;
        hovered = null;
        if (boundsExtended && panel != null && panel.optionsScrollView != null)
        {
            boundsExtended = false;
            try
            {
                panel.optionsScrollView.ContentContainerBoundY = originalBound;
                panel._xF(1f);
            }
            catch (Exception ex) { Plugin.Logger.LogError($"恢复原生设置滚动范围失败：{ex}"); }
        }
    }
}

/// <summary>原版游戏和 UI 设置页取得输入后附加本模组分类，不修改原版选项枚举。</summary>
[HarmonyPatch(typeof(UiSettingGameplayPanel), nameof(UiSettingGameplayPanel._WF))]
internal static class PotentialSettingsOpenPatch
{
    [HarmonyPostfix]
    private static void Postfix(UiSettingGameplayPanel __instance)
    {
        try { Plugin.Settings?.Show(__instance); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值设置接入失败：{ex}"); }
    }
}

/// <summary>让游戏语言或配置变化同步至已打开的设置页。</summary>
[HarmonyPatch(typeof(UiSettingGameplayPanel), nameof(UiSettingGameplayPanel.Update))]
internal static class PotentialSettingsUpdatePatch
{
    [HarmonyPostfix]
    private static void Postfix(UiSettingGameplayPanel __instance)
    {
        try { Plugin.Settings?.Refresh(__instance); }
        catch (Exception ex) { Plugin.Logger.LogError($"潜力值设置刷新失败：{ex}"); }
    }
}

/// <summary>原生滚轮、滑块按下及拖动共用此入口，只扩展包含潜力值分类的页面。</summary>
[HarmonyPatch(typeof(UiSettingGameplayPanel), nameof(UiSettingGameplayPanel._xF))]
internal static class PotentialSettingsScrollPatch
{
    [HarmonyPrefix]
    private static bool Prefix(UiSettingGameplayPanel __instance, float __0)
        => Plugin.Settings?.TryScroll(__instance, __0) != true;
}
