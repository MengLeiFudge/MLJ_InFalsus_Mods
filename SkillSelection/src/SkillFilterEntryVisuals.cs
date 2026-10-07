using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.UI.Common;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod;

/// <summary>为制卡栏入口提供不含模式状态块的原版图案，并拥有专用贴图和材质的生命周期。</summary>
internal sealed class SkillFilterEntryVisuals : IDisposable
{
    /// <summary>仅由模组入口使用的贴图，不覆盖游戏原版纹理。</summary>
    private Texture2D? texture;
    /// <summary>沿用原版 Shader 和参数的材质副本。</summary>
    private Material? material;

    /// <summary>固定使用“有特性”图文层，并按可见图形宽度接在最后一个颜色按钮后。</summary>
    /// <param name="button">已经复制、尚未显示的入口按钮。</param>
    /// <param name="lastColor">原版颜色栏最右按钮。</param>
    /// <param name="previousColor">倒数第二个颜色按钮，用于沿用颜色栏的图形间距。</param>
    internal SkillFilterEntryVisuals(UIButton button, UIButton lastColor, UIButton previousColor)
    {
        try
        {
            var content = button.transform.Find("TraitsOnly")
                ?? throw new InvalidOperationException("原版特性入口缺少有特性图文层。");
            var graphic = content.Find("btn-traitswitch-traitsonly")?.GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("原版特性入口缺少图案布局。");
            var colorGraphic = lastColor.transform.GetChild(0).GetComponent<Constrained2D>()
                ?? throw new InvalidOperationException("原版颜色按钮缺少图案布局。");
            for (int i = 0; i < button.transform.childCount; i++)
            {
                var child = button.transform.GetChild(i);
                child.gameObject.SetActive(child == content);
            }

            using var stream = typeof(SkillFilterEntryVisuals).Assembly.GetManifestResourceStream(
                "InFalsusMod.Assets.trait-filter-button.png")
                ?? throw new InvalidOperationException("缺少特性筛选入口图案资源。");
            // 当前 interop 的 ImageConversion.LoadImage 经由 Il2CppSystem.ReadOnlySpan 包装，运行时缺少
            // GetPinnableReference；改为托管侧解码后用 LoadRawTextureData 直接写入 RGBA32 像素。
            var image = PngRgba.Decode(stream);
            texture = new Texture2D(image.Width, image.Height, TextureFormat.RGBA32, false)
            {
                name = "SkillSelection.Entry",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            texture.LoadRawTextureData(new Il2CppStructArray<byte>(image.BottomUpPixels));
            texture.Apply(false, true);
            var originalMaterial = graphic.constrained2DTransform.renderableTargetMaterial;
            if (originalMaterial == null)
                throw new InvalidOperationException("原版特性入口缺少图案材质。");
            material = new Material(originalMaterial)
            {
                name = "SkillSelection.Entry",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = texture
            };
            graphic._irA(material);

            button.transform.SetParent(lastColor.transform.parent, false);
            var reference = lastColor.constrained2D.constrained2DTransform;
            var previous = previousColor.constrained2D.constrained2DTransform;
            var layout = button.constrained2D.constrained2DTransform;
            // 原版根容器宽 484.5，但颜色图形只有 195；用原栏中心步长补上入口图形宽度差。
            float colorStep = reference.offset.x - previous.offset.x;
            float widthDifference = graphic.constrained2DTransform.sizeInPixels.x
                - colorGraphic.constrained2DTransform.sizeInPixels.x;
            layout.anchor = reference.anchor;
            layout.parentAlignment = reference.parentAlignment;
            layout.offset = reference.offset + new Vector2(colorStep + widthDifference / 2f, 0f);
            button.constrained2D.constrained2DTransform = layout;
            button.constrained2D._rQA();
            button.constrained2D._PQA(0x3f);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>入口销毁或准备失败后释放专用资源；重复清理安全。</summary>
    public void Dispose()
    {
        if (material != null)
            Object.Destroy(material);
        material = null;
        if (texture != null)
            Object.Destroy(texture);
        texture = null;
    }
}
