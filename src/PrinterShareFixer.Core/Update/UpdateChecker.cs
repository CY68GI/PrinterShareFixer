using System.Net.Http;
using System.Text.Json;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core.Update;

/// <summary>
/// 从 GitHub Releases 检查是否有新版本。
/// 只依赖 api.github.com（无需登录），并把"上次检查时间"缓存到程序数据目录，避免频繁请求。
/// </summary>
public static class UpdateChecker
{
    /// <summary>默认仓库；用环境变量 PSF_UPDATE_REPO 可以覆盖（方便 fork 的人）。</summary>
    public const string DefaultRepository = "CY68GI/PrinterShareFixer";

    /// <summary>下载镜像前缀，例如填 https://ghproxy.com/ 时优先从镜像下载（国内网络更稳）。</summary>
    private const string MirrorVariable = "PSF_UPDATE_MIRROR";

    private static readonly TimeSpan AutoCheckInterval = TimeSpan.FromHours(24);

    private static readonly HttpClient Client = CreateClient();

    public static string Repository =>
        Environment.GetEnvironmentVariable("PSF_UPDATE_REPO") is { Length: > 0 } custom
            ? custom.Trim()
            : DefaultRepository;

    public static string? MirrorPrefix =>
        Environment.GetEnvironmentVariable(MirrorVariable) is { Length: > 0 } mirror
            ? mirror.Trim()
            : null;

    /// <summary>当前运行的是"自带运行时"的安装吗（决定下载哪个更新包）。</summary>
    public static bool IsSelfContainedInstall =>
        File.Exists(Path.Combine(AppContext.BaseDirectory, "coreclr.dll"));

    public static string CurrentVersionText => AppInfo.Version;

    /// <summary>下载页地址（查询失败时的兜底入口）。</summary>
    public static string ReleasesPageUrl => $"https://github.com/{Repository}/releases/latest";

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var url = $"https://api.github.com/repos/{Repository}/releases/latest";
        try
        {
            using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(
                    $"查询更新失败：GitHub 返回 {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var release = ParseRelease(json);
            if (release is null)
            {
                return UpdateCheckResult.Failed("查询更新失败：返回内容无法解析。");
            }

            var current = ParseVersion(AppInfo.Version);
            var result = current is not null && release.Version <= current
                ? new UpdateCheckResult(UpdateCheckStatus.UpToDate, release, $"已是最新版本（v{AppInfo.Version}）。")
                : new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release, $"发现新版本 v{release.Version}。");

            RememberCheck(result);
            return result;
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed($"查询更新失败：{ex.Message}");
        }
    }

    /// <summary>距上次检查是否已经超过 24 小时（启动时静默检查用）。</summary>
    public static bool ShouldAutoCheck()
    {
        var state = LoadState();
        return state.LastCheck is null || DateTimeOffset.Now - state.LastCheck.Value >= AutoCheckInterval;
    }

    /// <summary>最近一次检查发现的新版本（用于界面提示），没有则为 null。</summary>
    public static string? LastKnownNewerVersion()
    {
        var state = LoadState();
        var current = ParseVersion(AppInfo.Version);
        var latest = ParseVersion(state.LatestVersion);
        return current is not null && latest is not null && latest > current
            ? state.LatestVersion
            : null;
    }

    public static void RememberCheck(UpdateCheckResult result)
    {
        try
        {
            var state = new UpdateState
            {
                LastCheck = DateTimeOffset.Now,
                LatestVersion = result.Release?.Version.ToString() ?? string.Empty,
            };

            Directory.CreateDirectory(AppPaths.Root);
            File.WriteAllText(
                StatePath,
                JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 缓存失败不影响更新流程
        }
    }

    private static string StatePath => Path.Combine(AppPaths.Root, "update-state.json");

    private static UpdateState LoadState()
    {
        try
        {
            if (File.Exists(StatePath))
            {
                var state = JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(StatePath));
                if (state is not null)
                {
                    return state;
                }
            }
        }
        catch
        {
            // 读不出来就当没检查过
        }

        return new UpdateState();
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"PrinterShareFixer/{AppInfo.Version}");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static UpdateRelease? ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        var version = ParseVersion(tag);
        if (version is null)
        {
            return null;
        }

        var assets = new List<UpdateAsset>();
        if (root.TryGetProperty("assets", out var assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() ?? string.Empty : string.Empty;
                var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var parsedSize) ? parsedSize : 0;
                var digest = asset.TryGetProperty("digest", out var digestElement) ? digestElement.GetString() : null;
                var sha256 = digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                    ? digest["sha256:".Length..]
                    : null;

                if (name.Length > 0 && url.Length > 0)
                {
                    assets.Add(new UpdateAsset(name, size, sha256, url));
                }
            }
        }

        var title = root.TryGetProperty("name", out var titleElement) ? titleElement.GetString() ?? tag : tag;
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;
        var htmlUrl = root.TryGetProperty("html_url", out var htmlElement) ? htmlElement.GetString() ?? string.Empty : string.Empty;

        return new UpdateRelease(version, tag, title, notes.Trim(), assets, htmlUrl);
    }

    private static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var cleaned = text.Trim().TrimStart('v', 'V');
        var dash = cleaned.IndexOf('-');
        if (dash > 0)
        {
            // 预发布版本不参与比较
            return null;
        }

        return Version.TryParse(cleaned, out var version) ? version : null;
    }

    private sealed class UpdateState
    {
        public DateTimeOffset? LastCheck { get; set; }

        public string LatestVersion { get; set; } = string.Empty;
    }
}
