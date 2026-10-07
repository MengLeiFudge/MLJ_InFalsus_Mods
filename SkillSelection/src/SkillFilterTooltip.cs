using FastText.Data;
using ifapp.Game.Data;
using ifapp.Game.Scenes;
using ifapp.Game.UI.Common;
using Str;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod;

/// <summary>拥有原版特性说明资源的独立副本，展示各实际等级的本地化效果，不接收鼠标输入。</summary>
internal sealed class SkillFilterTooltip : IDisposable
{
    /// <summary>完整的制卡特性说明副本；所有子对象随它一同销毁。</summary>
    private readonly TraitIconHoverContainer view;
    /// <summary>当前场景的技能定义和动态本地化映射。</summary>
    private readonly DataAccess data;
    /// <summary>已绑定到副本布局的技能族标题。</summary>
    private readonly Text title;
    /// <summary>包含各实际等级效果的自动换行文字。</summary>
    private readonly Text description;
    /// <summary>当前显示的技能族；为空时需要重新填入内容。</summary>
    private string? shownFamily;
    /// <summary>当前说明使用的游戏语言。</summary>
    private Strings.Localization language;

    /// <summary>复制已有特性说明资源，并把显示布局约束到筛选浮窗的像素坐标中。</summary>
    /// <param name="template">当前制卡界面的完整原版 TraitIconHoverContainer。</param>
    /// <param name="parent">筛选浮窗的 ContentContainerTopLeft。</param>
    /// <param name="data">当前制卡场景的数据访问对象。</param>
    internal SkillFilterTooltip(TraitIconHoverContainer template, Transform parent, DataAccess data)
    {
        this.data = data;
        var root = Object.Instantiate(template.gameObject, parent, false);
        root.name = "InFalsus.SkillSelection.Tooltip";
        root.SetActive(false);
        try
        {
            view = root.GetComponent<TraitIconHoverContainer>()
                ?? throw new InvalidOperationException("原版特性说明副本缺少组件。");
            if (view.self == null || view.icon == null || view.traitDescriptionContainer == null
                || view.traitDescriptionBacking == null || view.titleText == null || view.descriptionText == null)
                throw new InvalidOperationException("原版特性说明资源未完整绑定。");
            SkillFilterVisuals.PrepareDisplayClone(root, parent.gameObject.layer);
            title = view.titleText.GetComponent<Text>()
                ?? throw new InvalidOperationException("原版特性说明缺少标题文字。");
            description = view.descriptionText.GetComponent<Text>()
                ?? throw new InvalidOperationException("原版特性说明缺少效果文字。");

            // 原组件没有动画图依赖；保留说明背景和特性图标，隐藏只服务于原版粒子详情的装饰。
            var content = view.icon.transform.parent;
            for (int i = 0; i < content.childCount; i++)
            {
                var child = content.GetChild(i);
                child.gameObject.SetActive(child.Pointer == view.icon.transform.Pointer
                    || child.Pointer == view.traitDescriptionContainer.transform.Pointer);
            }
            var layout = view.self.constrained2DTransform;
            layout.anchor = new Vector2(0.5f, 0.5f);
            layout.parentAlignment = Constrained2DAlignment.Center;
            layout.offset = Vector2.zero;
            layout.renderableScale = Vector2.one;
            layout.zPosition = -0.5f;
            view.self.constrained2DTransform = layout;
            view.traitDescriptionContainer._sQA(Vector2.zero);
            view.icon.style = TraitIcon._BI._tIb;

            var backing = view.traitDescriptionBacking.constrained2DTransform;
            backing.anchor = new Vector2(0f, 1f);
            backing.offset = new Vector2(144f, 0f);
            backing.renderableScale = Vector2.one;
            view.traitDescriptionBacking.constrained2DTransform = backing;
            ConfigureText(title, view.titleText, 3.6f, new Vector2(180f, -32f));
            ConfigureText(description, view.descriptionText, 3.25f, new Vector2(180f, -90f));
        }
        catch
        {
            Object.Destroy(root);
            throw;
        }
    }

    /// <summary>用原版字体和自动换行承载完整说明，文字位置以说明副本原点为基准。</summary>
    /// <param name="label">原版字体文字。</param>
    /// <param name="constrained">与文字绑定的副本布局。</param>
    /// <param name="fontSize">原版字体大小。</param>
    /// <param name="offset">正 X 向右、负 Y 向下的布局像素位置。</param>
    private static void ConfigureText(Text label, Constrained2D constrained, float fontSize, Vector2 offset)
    {
        var layout = constrained.constrained2DTransform;
        layout.anchor = new Vector2(0.5f, 0.5f);
        layout.parentAlignment = Constrained2DAlignment.Center;
        layout.offset = offset;
        constrained.constrained2DTransform = layout;
        label.SetFontSize(fontSize, Text.UpdateType.DontUpdateMesh);
        label.SetAlignment(AlignmentOptions.TopLeft, Text.UpdateType.DontUpdateMesh);
        label.presention = (label.presention | Text.FastTextPresentation.AutoLineBreak)
            & ~Text.FastTextPresentation.IgnoreLinebreak;
        label.SetMaxAutoLineBreakPixelWidth(900, Text.UpdateType.DontUpdateMesh);
    }

    /// <summary>在目标按钮旁显示该族的全部实际等级，并按原版背景的可用范围避让边缘。</summary>
    /// <param name="family">该技能族的代表定义。</param>
    /// <param name="anchor">按钮中心在 ContentContainerTopLeft 中的布局像素位置。</param>
    /// <param name="availableSize">筛选背景的完整设计宽高，单位为布局像素。</param>
    internal void Show(TraitInfo family, Vector2 anchor, Vector2 availableSize)
    {
        if (shownFamily == family.Family && language == ModText.Language)
            return;
        var mapping = data.DynamicStringMapping
            ?? throw new InvalidOperationException("游戏特性说明本地化映射尚未就绪。");
        var lines = new List<string>();
        foreach (var info in TraitCatalog.Entries.Where(info => info.Family == family.Family).OrderBy(info => info.Tier))
        {
            var text = mapping.Get(DynamicStringTypeFlags.TraitDescription, ModText.Language,
                new TraitId { Value = (short)info.Id }).CreateString();
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException($"技能 {info.Id} 缺少当前语言的效果说明。");
            lines.Add($"{new string('I', info.Tier)}  {text}");
        }

        view.gameObject.SetActive(true);
        view.self._rQA();
        var specification = data.GameData.traits[family.Id];
        view.icon._qsA(ref specification, false, false, true);
        SkillFilterVisuals.CenterTraitIcon(view.icon, 160f);
        view.icon.self._sQA(new Vector2(50f, -100f));
        SkillFilterVisuals.SetText(title, ModText.TraitName(family.Family));
        SkillFilterVisuals.SetText(description, string.Join("\n\n", lines));
        view.titleText._PQA(0x3f);
        view.descriptionText._PQA(0x3f);
        // 原版按实际字形尺寸扩展说明背景，保留多语言及多行文本的适配。
        view._eR();
        var backing = view.traitDescriptionBacking.constrained2DTransform;
        var size = Vector2.Scale(backing.sizeInPixels,
            Vector2.Scale(backing.renderableScale, backing.typeSpecificExtraScale));
        // 图标占据 X=-30..130、Y=-180..-20；说明背景从 (144, 0) 向右下展开。
        // 先在按钮左右选边，再保留浮窗内侧 24 像素边距；不随鼠标移动，避免悬停抖动。
        float right = 144f + size.x;
        float height = Math.Max(180f, size.y);
        float x = anchor.x + 120f;
        if (x + right > availableSize.x - 24f)
            x = anchor.x - 100f - right;
        x = Mathf.Clamp(x, 54f, availableSize.x - 24f - right);
        float y = Mathf.Clamp(anchor.y + 110f, height + 24f - availableSize.y, -24f);
        view.self._sQA(new Vector2(x, y));
        view.self._PQA(0x3f);
        shownFamily = family.Family;
        language = ModText.Language;
    }

    /// <summary>鼠标离开、输入切换或关闭筛选时隐藏说明，下次悬停重新填充当前语言。</summary>
    internal void Hide()
    {
        shownFamily = null;
        if (view != null && view.gameObject.activeSelf)
            view.gameObject.SetActive(false);
    }

    /// <summary>释放筛选浮窗专用的资源副本，原制卡组件保持原状。</summary>
    public void Dispose()
    {
        if (view != null)
        {
            view.gameObject.SetActive(false);
            Object.Destroy(view.gameObject);
        }
    }
}
