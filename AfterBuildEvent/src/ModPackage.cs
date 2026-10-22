using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace AfterBuildEvent;

/// <summary>一个已经生成的模组包；文件哈希用于完整性校验，内容哈希用于跨打包时间去重。</summary>
/// <param name="PackageId">本地模组及 Thunderstore 包名。</param>
/// <param name="Path">ZIP 的绝对路径。</param>
/// <param name="Sha256">ZIP 文件字节的 SHA-256。</param>
/// <param name="ContentSha256">按条目名称、大小和内容计算的 SHA-256，与 qqbot 算法一致。</param>
internal sealed record ModPackage(string PackageId, string Path, string Sha256, string ContentSha256)
{
    /// <summary>群文件显示名称，始终保持生成的 ZIP 文件名。</summary>
    internal string Name => System.IO.Path.GetFileName(Path);

    /// <summary>从最终 ZIP 读取内容，避免对未写入包中的源文件计算发布哈希。</summary>
    /// <param name="packageId">生成该文件的模组名。</param>
    /// <param name="path">最终 ZIP 路径。</param>
    /// <returns>可供发布和本地去重的包元数据。</returns>
    internal static ModPackage Read(string packageId, string path)
    {
        using var input = File.OpenRead(path);
        string sha256 = Convert.ToHexString(SHA256.HashData(input)).ToLowerInvariant();
        input.Position = 0;
        using var archive = new ZipArchive(input, ZipArchiveMode.Read);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[81920];
        byte[] separator = { 0 };
        foreach (ZipArchiveEntry entry in archive.Entries.OrderBy(item => item.FullName, StringComparer.OrdinalIgnoreCase))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(entry.FullName));
            hash.AppendData(separator);
            hash.AppendData(Encoding.UTF8.GetBytes(entry.Length.ToString(CultureInfo.InvariantCulture)));
            hash.AppendData(separator);
            using Stream contents = entry.Open();
            int read;
            while ((read = contents.Read(buffer, 0, buffer.Length)) > 0)
                hash.AppendData(buffer, 0, read);
            hash.AppendData(separator);
        }
        string contentSha256 = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new ModPackage(packageId, System.IO.Path.GetFullPath(path), sha256, contentSha256);
    }
}
