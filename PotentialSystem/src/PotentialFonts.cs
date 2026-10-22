using FastText.Data;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Text = FastText.FastText;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>为模组文字提供完整简中字形；字体与 FastText 的进程级字形缓存共用生命周期。</summary>
internal static class PotentialFonts
{
    /// <summary>字体缓存不随页面关闭或插件重载重复创建；原生 FastText 在退出时统一回收字形资源。</summary>
    private static FastTextFontFamily? chinese;

    /// <summary>仅给传入的模组文字副本绑定字体，保留其字号、字重、颜色和原生字体设置。</summary>
    /// <param name="text">本模组拥有的文字组件。</param>
    internal static void Apply(Text text)
    {
        if (chinese == null)
            chinese = Create(text.fontFamilyAsset);
        if (text.fontFamilyAsset != chinese)
            text.SetFontFamilyAsset(chinese);
    }

    /// <summary>创建六个字重槽位均有效的字体族；中文界面不依赖原生子集或系统字体。</summary>
    /// <param name="template">提供 FastText 排版设置的原生字体族。</param>
    /// <returns>由本模块保留、供所有模组文字共享的字体族。</returns>
    private static FastTextFontFamily Create(FastTextFontFamily template)
    {
        var assets = new List<FastTextAsset>();
        FastTextFontFamily? family = null;
        try
        {
            foreach (string weight in new[] { "Regular", "Bold", "Light" })
            {
                string name = "NotoSansCJKsc-" + weight + ".otf";
                using var stream = typeof(PotentialFonts).Assembly.GetManifestResourceStream("PotentialSystem.Fonts." + name)
                    ?? throw new InvalidDataException("缺少嵌入字体：" + name);
                using var bytes = new MemoryStream();
                stream.CopyTo(bytes);
                var asset = ScriptableObject.CreateInstance<FastTextAsset>();
                assets.Add(asset);
                asset.name = asset.Identifier = "PotentialSystem." + name;
                asset.hideFlags = HideFlags.HideAndDontSave;
                asset.RawBytes = new Il2CppStructArray<byte>(bytes.ToArray());
                asset.PointSizeMultiplier = 1f;
            }
            family = ScriptableObject.CreateInstance<FastTextFontFamily>();
            family.name = "PotentialSystem.Chinese";
            family.hideFlags = HideFlags.HideAndDontSave;
            family.settings = template.settings;
            family.behaviours = template.behaviours;
            family.supportsLocalization = false;
            // Regular / Bold / Italic / Light / Medium / SemiBold；没有独立斜体时仍提供完整中文字形。
            family.fontAssets = new Il2CppReferenceArray<FastTextAsset>(new[]
            {
                assets[0], assets[1], assets[0], assets[2], assets[0], assets[1]
            });
            return family;
        }
        catch
        {
            if (family != null) Object.Destroy(family);
            foreach (var asset in assets) Object.Destroy(asset);
            throw;
        }
    }
}
