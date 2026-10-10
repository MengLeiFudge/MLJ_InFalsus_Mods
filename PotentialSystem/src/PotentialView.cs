using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;
using Text = FastText.FastText;

namespace InFalsusMod.Potential;

/// <summary>拥有大号原生 PTT 文字和 Arcaea 徽章，结算视图同时显示单曲与变化状态。</summary>
internal sealed class PotentialView : IDisposable
{
    /// <summary>本视图创建的文字、图形与材质；徽章纹理由控制器共享。</summary>
    private readonly List<NativeLabel> labels = new();
    private readonly PotentialBadges badges;
    private readonly bool results;
    private Constrained2D? graphic;
    private Material? material;
    private int displayedTier = -1;

    /// <summary>选歌徽章置于顶部中央；结算徽章置于右上并展示数值变化。</summary>
    internal PotentialView(Text template, Transform parent, Constrained2D graphicTemplate,
        PotentialBadges badges, bool results = false)
    {
        this.badges = badges;
        this.results = results;
        try
        {
            var anchor = new Vector2(0.5f, 0.5f);
            var alignment = results ? Constrained2DAlignment.TopRight : Constrained2DAlignment.Top;
            var center = results ? new Vector2(-220f, -150f) : new Vector2(0f, -115f);
            var root = Object.Instantiate(graphicTemplate.gameObject, parent, false);
            graphic = root.GetComponent<Constrained2D>();
            if (graphic == null)
            {
                Object.Destroy(root);
                throw new InvalidOperationException("徽章模板缺少布局。");
            }
            root.name = "InFalsus.PotentialSystem.Badge";
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            var layout = graphic.constrained2DTransform;
            var sourceMaterial = layout.renderableTargetMaterial
                ?? throw new InvalidOperationException("徽章模板缺少图形材质。");
            layout.transform = root.transform;
            layout.meshRenderer = root.GetComponent<MeshRenderer>();
            layout.meshFilter = root.GetComponent<MeshFilter>();
            layout.type = Constrained2DType.Sprite;
            layout.anchor = anchor;
            layout.parentAlignment = alignment;
            layout.offset = center;
            layout.zPosition = -0.2f;
            layout.sizeInPixels = new Vector2(190f, 190f);
            layout.renderableScale = Vector2.one;
            layout.color = Color.white;
            layout.spriteBlendEffect = default;
            graphic.constrained2DTransform = layout;
            material = new Material(sourceMaterial) { name = "PotentialSystem.Badge", hideFlags = HideFlags.HideAndDontSave };
            graphic._irA(material);
            root.SetActive(true);
            graphic._rQA();
            graphic._PQA(0x3f);
            labels.Add(new NativeLabel(template, parent, "Account", anchor, alignment, center, 5.8f));
            labels.Add(new NativeLabel(template, parent, "Maximum", anchor, alignment, center + new Vector2(0f, -108f), 2.7f));
            labels.Add(new NativeLabel(template, parent, "Status", anchor, alignment, center + new Vector2(0f, -146f), 2.8f));
            if (results)
                labels.Add(new NativeLabel(template, parent, "Play", anchor, alignment, center + new Vector2(0f, -187f), 2.7f));
        }
        catch { Dispose(); throw; }
    }

    /// <summary>刷新徽章档位与数值，动画由控制器推进；不重复生成相同网格。</summary>
    internal void Render(PotentialSnapshot? snapshot, decimal displayPotential, string chartLine, string status)
    {
        int tier = snapshot == null ? 8 : PotentialBadges.Tier(displayPotential);
        if (tier != displayedTier)
        {
            material!.mainTexture = badges.Texture(tier);
            graphic!._irA(material);
            displayedTier = tier;
        }
        labels[0].Set(snapshot == null ? "--.---" : PotentialCalculator.Format(displayPotential), Color.white);
        labels[1].Set(snapshot == null ? "PTT 数据不可用" : $"PTT / MAX {PotentialCalculator.Format(snapshot.Maximum)}", Color.white);
        labels[2].Set(results || snapshot == null || status.StartsWith("Unsaved", StringComparison.Ordinal)
            ? status : "点击查看全部成绩", new Color(0.7f, 1f, 0.85f));
        if (results)
            labels[3].Set(chartLine, Color.white);
    }

    /// <summary>先销毁副本图形，再释放本视图材质；共享纹理留给控制器回收。</summary>
    public void Dispose()
    {
        foreach (var label in labels)
            label.Dispose();
        labels.Clear();
        if (graphic != null)
            Object.Destroy(graphic.gameObject);
        graphic = null;
        if (material != null)
            Object.Destroy(material);
        material = null;
    }
}
