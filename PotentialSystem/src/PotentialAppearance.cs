using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Object = UnityEngine.Object;

namespace InFalsusMod.Potential;

/// <summary>头像像素复制和名片配色；只操作模组展示副本。</summary>
internal static class PotentialAppearance
{
    /// <summary>名片配色的配置下标保持稳定。</summary>
    internal static readonly uint[] NamecardColors = { 0x244c52, 0x453554, 0x552f3c, 0x2b405c };

    /// <summary>复制原生材质的图集区域，返回底向上的 RGBA；调用后不保留任何 GPU 引用。</summary>
    /// <param name="material">游戏持有的头像材质，借用而不修改。</param>
    /// <returns>128×128 托管像素。</returns>
    internal static PixelImage Capture(Material material)
    {
        if (material == null || material.mainTexture == null) throw new InvalidOperationException("角色头像材质不可用。");
        const int size = 128;
        var previous = RenderTexture.active;
        var target = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32);
        Texture2D? texture = null;
        try
        {
            Graphics.Blit(material.mainTexture, target, material.mainTextureScale, material.mainTextureOffset);
            RenderTexture.active = target;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            texture.ReadPixels(new Rect(0, 0, size, size), 0, 0, false);
            Il2CppStructArray<byte> raw = texture.GetRawTextureData();
            return new PixelImage(size, size, raw.ToArray());
        }
        finally
        {
            if (texture != null) Object.Destroy(texture);
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
        }
    }
}
