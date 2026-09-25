using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace PrinterShareFixer.Core.Update;

/// <summary>下载更新包、校验 SHA-256 并解压到临时目录。</summary>
public static class UpdateDownloader
{
    private static readonly HttpClient Client = CreateClient();

    public static string StagingRoot(Version version) =>
        Path.Combine(Path.GetTempPath(), "PrinterShareFixer-update", version.ToString());

    public static async Task<StagedUpdate> DownloadAndStageAsync(
        UpdateRelease release,
        bool currentIsSelfContained,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var asset = release.FindPackage(currentIsSelfContained)
            ?? throw new InvalidOperationException("该版本没有找到可用的 zip 更新包。");

        var folder = StagingRoot(release.Version);
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, asset.Name);

        var url = BuildDownloadUrl(asset.DownloadUrl);
        progress?.Report(new UpdateDownloadProgress("正在下载更新包", 0, asset.SizeBytes > 0 ? asset.SizeBytes : null));

        using (var response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"下载失败：服务器返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var total = response.Content.Headers.ContentLength ?? (asset.SizeBytes > 0 ? asset.SizeBytes : null);
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var target = File.Create(zipPath);

            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                progress?.Report(new UpdateDownloadProgress("正在下载更新包", received, total));
            }
        }

        if (!string.IsNullOrWhiteSpace(asset.Sha256))
        {
            progress?.Report(new UpdateDownloadProgress("正在校验文件完整性", 0, null));
            var actual = await ComputeSha256Async(zipPath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(actual, asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(zipPath);
                throw new InvalidOperationException("更新包校验失败（SHA-256 不一致），已删除下载的文件。");
            }
        }

        progress?.Report(new UpdateDownloadProgress("正在解压更新包", 0, null));
        var extractRoot = Path.Combine(folder, "extracted");
        if (Directory.Exists(extractRoot))
        {
            Directory.Delete(extractRoot, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, extractRoot);

        var appDirectory = FindAppDirectory(extractRoot)
            ?? throw new InvalidOperationException("更新包里没有找到 PrinterShareFixer.exe，可能下载到了错误的文件。");

        return new StagedUpdate(release.Version.ToString(), zipPath, appDirectory, asset);
    }

    /// <summary>镜像前缀（PSF_UPDATE_MIRROR）存在时优先走镜像，便于网络受限的环境。</summary>
    private static string BuildDownloadUrl(string originalUrl)
    {
        var mirror = UpdateChecker.MirrorPrefix;
        if (string.IsNullOrWhiteSpace(mirror))
        {
            return originalUrl;
        }

        if (!mirror.EndsWith('/'))
        {
            mirror += "/";
        }

        return mirror.Contains("{url}", StringComparison.OrdinalIgnoreCase)
            ? mirror.Replace("{url}", originalUrl, StringComparison.OrdinalIgnoreCase)
            : mirror + originalUrl;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>在解压结果里找出主程序所在目录（zip 里通常多一层包名目录）。</summary>
    private static string? FindAppDirectory(string root)
    {
        var candidates = Directory
            .EnumerateFiles(root, "PrinterShareFixer.exe", SearchOption.AllDirectories)
            .Select(Path.GetDirectoryName)
            .Where(dir => !string.IsNullOrEmpty(dir))
            .Cast<string>()
            .ToList();

        return candidates
            .OrderByDescending(dir => Directory.GetFiles(dir!, "*.dll").Length)
            .FirstOrDefault();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // 删除失败不影响结果
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PrinterShareFixer/{AppInfo.Version}");
        return client;
    }
}
