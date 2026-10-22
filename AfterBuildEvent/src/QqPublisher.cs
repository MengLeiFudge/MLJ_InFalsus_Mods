using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AfterBuildEvent;

/// <summary>把已生成的 ZIP 交给本机 qqbot，按目标群、目录和包内容去重，明确保留失败状态。</summary>
internal static class QqPublisher
{
    /// <summary>与 DSP 交付工具共用的本机发布接口；不调用 Thunderstore。</summary>
    private const string Endpoint = "http://127.0.0.1:8080/admin/api/artifacts/publish-local";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    /// <summary>一个包本次仍需通知 qqbot 的群；其他群已有相同内容的成功记录。</summary>
    /// <param name="Package">本次最终 ZIP 及哈希。</param>
    /// <param name="Groups">尚未确认相同内容的接收群。</param>
    private sealed record PendingPackage(ModPackage Package, long[] Groups);

    /// <summary>本地成功缓存，仅在服务端确认上传或跳过后写入。</summary>
    /// <param name="Name">群文件名。</param>
    /// <param name="ContentSha256">服务端已确认的包内容哈希。</param>
    private sealed record PublishState(string Name, string ContentSha256);

    /// <summary>只发送有变化的群文件，不重试结果未知的请求，也不把接收请求误报为完成上传。</summary>
    /// <param name="context">本项目路径与明确配置的接收群。</param>
    /// <param name="packages">已经完成本地替换、打包的指定模组。</param>
    /// <param name="messageFile">可选 UTF-8 发布说明；省略时按工作区状态生成或读取提交说明。</param>
    internal static async Task PublishAsync(RepositoryContext context, IReadOnlyList<ModPackage> packages, string? messageFile)
    {
        if (context.PublishGroupIds.Count == 0)
            throw new InvalidOperationException("本地更新、打包和路径修正已完成；尚未配置接收群，请在 DefaultPath.props 设置 QqGroupIds 后推送。未发送群文件。");

        var pending = new List<PendingPackage>();
        int localSkipped = 0;
        foreach (ModPackage package in packages)
        {
            long[] groups = context.PublishGroupIds.Where(group => !AlreadyPublished(context, group, package)).ToArray();
            localSkipped += context.PublishGroupIds.Count - groups.Length;
            if (groups.Length > 0)
                pending.Add(new PendingPackage(package, groups));
        }
        Console.WriteLine($"群推送本地去重：跳过 {localSkipped} 项未变化的群文件。");
        if (pending.Count == 0)
            return;

        string branch = await GitAsync(context.RootDirectory, "branch", "--show-current");
        string commit = await GitAsync(context.RootDirectory, "rev-parse", "HEAD");
        if (string.IsNullOrWhiteSpace(branch))
            throw new InvalidOperationException("群推送需要当前分支，不能使用分离的 HEAD。");
        bool dirty = !string.IsNullOrWhiteSpace(await GitAsync(context.RootDirectory, "status", "--porcelain", "--untracked-files=normal"));
        string detail = messageFile != null
            ? await File.ReadAllTextAsync(Path.GetFullPath(messageFile, context.RootDirectory), Encoding.UTF8)
            : dirty
                ? $"更新本地工作区构建：{string.Join("、", pending.Select(item => item.Package.Name))}。\n未提供专门的变更说明；包内容以本次实际构建产物为准。"
                : await GitAsync(context.RootDirectory, "log", "-1", "--pretty=%B");
        if (string.IsNullOrWhiteSpace(detail))
            throw new InvalidDataException("发布说明不能为空。");
        detail = detail.Trim();
        if (dirty)
            detail = $"未提交工作区构建，基准提交 {commit}。\n\n{detail}";
        if (detail.Length > 10000)
            throw new InvalidDataException("发布说明不能超过 10000 个字符。");
        string subject = dirty
            ? $"本地工作区更新：{string.Join("、", pending.Select(item => item.Package.PackageId))}"
            : await GitAsync(context.RootDirectory, "log", "-1", "--pretty=%s");
        var payload = new
        {
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            project_id = "mlj_infalsus_mods",
            branch,
            commit_hash = commit,
            commit_subject = subject,
            commit_detail = detail,
            files = pending.Select(item => new
            {
                path = item.Package.Path,
                name = item.Package.Name,
                sha256 = item.Package.Sha256,
                content_sha256 = item.Package.ContentSha256,
                targets = item.Groups,
                folder_id = context.PublishFolderId,
            }).ToArray(),
        };

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        using var body = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsync(Endpoint, body);
        }
        catch (OperationCanceledException ex)
        {
            throw new TimeoutException("群推送在 60 秒内未返回，远端结果未确定，未写成功缓存；请先核对群文件和 qqbot 状态再重试。", ex);
        }
        using (response)
        {
            string result = await response.Content.ReadAsStringAsync();
            string receipt = Path.Combine(context.ModZipsDirectory, "publish-receipts", $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.json");
            SaveJson(receipt, new { http_status = (int)response.StatusCode, body = result });
            Console.WriteLine($"群推送回执：{receipt}");
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"qqbot 返回 HTTP {(int)response.StatusCode}：{result}");

            using JsonDocument document = JsonDocument.Parse(result);
            JsonElement root = document.RootElement;
            JsonElement uploaded = RequireArray(root, "uploaded"), skipped = RequireArray(root, "skipped"), deleted = RequireArray(root, "deleted");
            var confirmations = uploaded.EnumerateArray().Concat(skipped.EnumerateArray()).ToArray();
            foreach (PendingPackage item in pending)
                foreach (long group in item.Groups)
                    if (!confirmations.Any(entry => entry.GetProperty("group_id").GetInt64() == group
                        && (entry.TryGetProperty("folder_id", out JsonElement folder) ? folder.GetString() : "") == context.PublishFolderId
                        && entry.GetProperty("name").GetString() == item.Package.Name
                        && entry.GetProperty("content_sha256").GetString() == item.Package.ContentSha256))
                        throw new InvalidDataException($"qqbot 未确认群 {group} 目标目录中的 {item.Package.Name}，未写成功缓存。");
            foreach (PendingPackage item in pending)
                foreach (long group in item.Groups)
                    SaveJson(StatePath(context, group, item.Package.Name), new PublishState(item.Package.Name, item.Package.ContentSha256));
            Console.WriteLine($"群推送完成：上传 {uploaded.GetArrayLength()} 项，服务端跳过 {skipped.GetArrayLength()} 项，删除同名旧文件 {deleted.GetArrayLength()} 项。");
        }
    }

    /// <summary>读取指定群的上次成功记录；损坏缓存明确报告，并交由服务端再次去重。</summary>
    /// <param name="context">本项目发布缓存位置。</param>
    /// <param name="group">接收群号。</param>
    /// <param name="package">准备发布的包。</param>
    /// <returns>该群是否已经确认过相同包名和内容。</returns>
    private static bool AlreadyPublished(RepositoryContext context, long group, ModPackage package)
    {
        string path = StatePath(context, group, package.Name);
        if (!File.Exists(path))
            return false;
        try
        {
            PublishState? state = JsonSerializer.Deserialize<PublishState>(File.ReadAllText(path), JsonOptions);
            return state?.Name == package.Name && state.ContentSha256 == package.ContentSha256;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Console.Error.WriteLine($"无法读取群推送缓存 {path}：{ex.Message}；由 qqbot 核对是否已上传。");
            return false;
        }
    }

    /// <summary>按群号、文件夹和文件名隔离缓存；根目录沿用旧路径，切换文件夹不会误用旧成功记录。</summary>
    /// <param name="context">本项目包目录。</param>
    /// <param name="group">接收群号。</param>
    /// <param name="name">生成包的文件名。</param>
    /// <returns>本地成功缓存路径。</returns>
    private static string StatePath(RepositoryContext context, long group, string name)
    {
        string directory = Path.Combine(context.ModZipsDirectory, "publish-content-sha256",
            group.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (context.PublishFolderId.Length > 0)
        {
            string folderKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context.PublishFolderId))).ToLowerInvariant();
            directory = Path.Combine(directory, "folders", folderKey);
        }
        return Path.Combine(directory, name + ".json");
    }

    /// <summary>服务端必须明确返回上传、跳过和删除结果，缺少结果不视为发布成功。</summary>
    /// <param name="root">qqbot 响应根对象。</param>
    /// <param name="name">必须存在的结果数组名。</param>
    /// <returns>经过类型校验的数组。</returns>
    private static JsonElement RequireArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"qqbot 响应缺少 {name} 数组，无法确认群文件发布结果。");
        return value;
    }

    /// <summary>以临时文件替换缓存或回执，避免中断留下半份 JSON。</summary>
    /// <param name="path">本项目 ModZips 下的目标文件。</param>
    /// <param name="value">需要保留的结果或成功记录。</param>
    private static void SaveJson(string path, object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, JsonOptions), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>只读取得发布所需的 Git 身份和工作区状态，超时仅结束自身启动的 Git 进程。</summary>
    /// <param name="directory">当前目标仓库。</param>
    /// <param name="arguments">分开传入的 Git 参数，不经 shell 拼接。</param>
    /// <returns>成功命令的标准输出；失败和超时均中断推送。</returns>
    private static async Task<string> GitAsync(string directory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("无法启动 Git。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Task<string> output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        Task<string> error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
        }
        catch (OperationCanceledException ex)
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
            throw new TimeoutException("读取发布 Git 信息超时，停止群推送。", ex);
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"读取发布 Git 信息失败：{await error}");
        return (await output).Trim();
    }
}
