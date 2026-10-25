using ifapp.Game.Input;
using ifapp.Game.UI.Common;
using ifapp.Game.UI.Settings;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>模组设置标签中的原版分类副本；保留原版标题、设置行与左右箭头。</summary>
internal sealed class PotentialSettingsSection : IDisposable
{
    private readonly GameObject root;
    private readonly Constrained2D category;
    private readonly UiSettingMultiselectorArrow selector;
    private readonly PotentialSettings settings;
    private readonly Il2CppSystem.Action<UiSettingMultiselectorArrow, int> changed;
    private readonly Il2CppSystem.Action<UiSettingMultiselectorArrow, bool> hovered;
    private int displayed = -1;
    private Str.Strings.Localization language;

    internal PotentialSettingsSection(Transform parent, InputReceiver receiver, PotentialSettings settings)
    {
        this.settings = settings;
        var template = Resources.FindObjectsOfTypeAll<UiSettingGameplayPanel>()
            .FirstOrDefault(panel => panel != null && panel.visualNovelContainer != null)?.visualNovelContainer
            ?? throw new InvalidOperationException("原版设置分类尚未载入。");
        root = Object.Instantiate(template.gameObject, parent, false);
        root.name = "InFalsus.PotentialSystem.Settings.Category";
        root.SetActive(true);
        try
        {
            foreach (var layout in root.GetComponentsInChildren<Constrained2D>(true)) BrowserWidgets.Rebind(layout);
            category = root.GetComponent<Constrained2D>();
            selector = root.GetComponentInChildren<UiSettingMultiselectorArrow>(true)
                ?? throw new InvalidOperationException("原版设置分类缺少左右箭头选择器。");
            foreach (var button in root.GetComponentsInChildren<UIButton>(true))
            {
                button._Gjb = null; button._hjb = null; button._Hjb = null;
                button._ijb = null; button._Ijb = null; button.UserComponent = null;
                button.constrained2D = button.GetComponent<Constrained2D>();
                button.ButtonCollider = button.GetComponent<BoxCollider>();
            }
            hovered = (Action<UiSettingMultiselectorArrow, bool>)((_, _) => { });
            changed = (Action<UiSettingMultiselectorArrow, int>)((_, index) =>
            {
                try { settings.SetSource(index); Refresh(); }
                catch (Exception ex) { Plugin.Logger.LogError($"定数来源设置失败：{ex}"); }
            });
            selector._rg(receiver, new Il2CppReferenceArray<Str.TextParameters>(Enum.GetValues<ChartConstantSource>()
                .Select(value => Str.TextParameters.CreateDirectString(CommunityConstants.DisplayName(value))).ToArray()), hovered, changed);
            selector._sg(_B._Kb._Pj);
            var value = category.constrained2DTransform;
            value.anchor = new Vector2(0.5f, 1f);
            value.parentAlignment = Constrained2DAlignment.Center;
            value.offset = Vector2.zero;
            value.sizeInParentNormalized = new Vector2(1f, value.sizeInParentNormalized.y);
            category.constrained2DTransform = value;
            Refresh();
        }
        catch { Dispose(); throw; }
    }

    /// <summary>保留分类内部原生间距；仅把整块分类放到模组设置内容区域顶部。</summary>
    internal void Show(Rect bounds)
    {
        root.SetActive(true);
        var value = category.constrained2DTransform;
        value.offset = new Vector2(bounds.center.x, bounds.yMax);
        category.constrained2DTransform = value;
        category._PQA(0x3f);
        Refresh();
    }

    internal void Hide() { if (root != null) root.SetActive(false); }

    private void Refresh()
    {
        var current = NativeLabel.CurrentLanguage();
        if (displayed == (int)settings.Source && language == current) return;
        selector._Tg((int)settings.Source);
        foreach (var text in root.GetComponentsInChildren<Text>(true))
            if (text.transform.parent == root.transform) SetText(text, "潜力值系统");
        foreach (var text in selector.titleTexts) SetText(text, "谱面定数来源");
        foreach (var text in selector.valueText) SetText(text, CommunityConstants.DisplayName(settings.Source));
        displayed = (int)settings.Source;
        language = current;
    }

    private static void SetText(Text text, string value)
    {
        text.meshRenderer = text.GetComponent<MeshRenderer>();
        text.meshFilter = text.GetComponent<MeshFilter>();
        PotentialFonts.Apply(text);
        text.SetTextNonLocalized(value, Text.UpdateType.DontUpdateMesh);
        text.targetLocalization = NativeLabel.CurrentLanguage();
        text.dirtyFlags |= Text.DirtyFlags.FontChanged | Text.DirtyFlags.TextChanged;
        text.EnsureDirtyFlagsProcessed();
    }

    public void Dispose()
    {
        if (root == null) return;
        root.SetActive(false);
        foreach (var button in root.GetComponentsInChildren<UIButton>(true)) UIManager._A?._ktA(button);
        Object.Destroy(root);
    }
}
