namespace PrinterShareFixer.Core.Runtime;

/// <summary>日志与备份文件的存放位置。</summary>
public static class AppPaths
{
    private static readonly Lazy<string> RootLazy = new(ResolveRoot, isThreadSafe: true);

    public static string Root => RootLazy.Value;

    public static string LogDirectory => Path.Combine(Root, "Logs");

    public static string BackupDirectory => Path.Combine(Root, "Backups");

    public static string EnsureLogDirectory()
    {
        Directory.CreateDirectory(LogDirectory);
        return LogDirectory;
    }

    public static string EnsureBackupDirectory(string stamp)
    {
        var path = Path.Combine(BackupDirectory, stamp);
        Directory.CreateDirectory(path);
        return path;
    }

    private static string ResolveRoot()
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PrinterShareFixer"),
                     Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrinterShareFixer"),
                 })
        {
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, ".write-test");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return candidate;
            }
            catch
            {
                // 换下一个候选目录
            }
        }

        return Path.Combine(Path.GetTempPath(), "PrinterShareFixer");
    }
}
