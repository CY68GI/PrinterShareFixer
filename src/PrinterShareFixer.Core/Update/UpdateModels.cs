namespace PrinterShareFixer.Core.Update;

/// <summary>Release 里的一个可下载文件。</summary>
public sealed record UpdateAsset(string Name, long SizeBytes, string? Sha256, string DownloadUrl)
{
    public bool IsPackage => Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);

    public string SizeText => SizeBytes <= 0
        ? "未知大小"
        : SizeBytes >= 1024L * 1024 * 1024
            ? $"{SizeBytes / 1024d / 1024 / 1024:F2} GB"
            : $"{SizeBytes / 1024d / 1024:F1} MB";
}

/// <summary>GitHub 上的一个发行版本。</summary>
public sealed record UpdateRelease(
    Version Version,
    string TagName,
    string Title,
    string Notes,
    IReadOnlyList<UpdateAsset> Assets,
    string HtmlUrl)
{
    /// <summary>
    /// 挑选与当前安装形态匹配的更新包：
    /// 自带运行时的安装选不带 requires-dotnet 的包，精简安装反之。
    /// </summary>
    public UpdateAsset? FindPackage(bool currentIsSelfContained)
    {
        var packages = Assets.Where(a => a.IsPackage).ToList();
        if (packages.Count == 0)
        {
            return null;
        }

        var slim = packages.FirstOrDefault(a => a.Name.Contains("requires-dotnet", StringComparison.OrdinalIgnoreCase));
        var full = packages.FirstOrDefault(a => !a.Name.Contains("requires-dotnet", StringComparison.OrdinalIgnoreCase));

        var preferred = currentIsSelfContained ? full ?? slim : slim ?? full;
        return preferred;
    }
}

public enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateRelease? Release, string Message)
{
    public static UpdateCheckResult Failed(string message) => new(UpdateCheckStatus.Failed, null, message);
}

/// <summary>下载进度。</summary>
public sealed record UpdateDownloadProgress(string Stage, long BytesReceived, long? TotalBytes)
{
    public double? Percent => TotalBytes is > 0
        ? Math.Min(100d, BytesReceived * 100d / TotalBytes.Value)
        : null;
}

/// <summary>已经下载并解压好、等待替换的更新。</summary>
public sealed record StagedUpdate(
    string Version,
    string ZipPath,
    string AppDirectory,
    UpdateAsset Asset);
