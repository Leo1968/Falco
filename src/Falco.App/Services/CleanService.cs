using System.Diagnostics;
using System.IO;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;

namespace Falco.App.Services;

public class ScanRow
{
    public bool Checked;
    public string Path, Note, SizeText;
    public string Kind;    // 内部类别（protected/rebuildable/temp/wu/thumbs/recycle），逻辑判断用；Note 仅显示
    public double SizeMB;
}

/// <summary>清理引擎：系统层 / 构建产物 / 开发缓存（白名单）/ 安装包（C# 移植，删除优先进回收站）。</summary>
public static class CleanService
{
    // ---------- 系统层 ----------
    public static double MeasureMB(string path)
    {
        try
        {
            if (!Directory.Exists(path)) return 0;
            return new DirectoryInfo(path).EnumerateFiles("*", System.IO.SearchOption.AllDirectories).Sum(f => f.Length) / 1024.0 / 1024;
        }
        catch { return 0; }
    }

    public static double TempMB()
        => MeasureMB(Path.GetTempPath()) + MeasureMB(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp"));

    public static double WuMB()
        => MeasureMB(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"));

    public static double ThumbsMB()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
        try { return new DirectoryInfo(dir).EnumerateFiles("*cache_*.db").Sum(f => f.Length) / 1024.0 / 1024; }
        catch { return 0; }
    }

    public static void CleanTemp()
    {
        foreach (var p in new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") })
            TryDeleteChildren(p);
        AppEnv.Log("临时文件清理完成。");
    }

    public static void CleanWu()
    {
        AppEnv.Log("停止 wuauserv / bits 服务…");
        ScService.TryStop("wuauserv"); ScService.TryStop("bits");
        TryDeleteChildren(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download"));
        ScService.TryConfig("wuauserv", "Automatic"); ScService.TryConfig("bits", "Automatic");
        ScService.TryStart("bits"); ScService.TryStart("wuauserv");
        AppEnv.Log("Windows Update 下载缓存清理完成（未完成的更新会自动重新下载）。");
    }

    public static void CleanRecycle()
    {
        try
        {
            var drive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.System));
            Shell.Run("cmd.exe", $"/c rd /s /q {drive}$Recycle.Bin");
            AppEnv.Log("回收站已清空。");
        }
        catch (Exception ex) { AppEnv.Log($"回收站清理：{ex.Message}", "WARN"); }
    }

    /// <summary>缩略图/图标缓存清理：尽力删除可解锁文件，不杀 explorer（被占用项跳过，重启后自然释放）。</summary>
    public static (int deleted, int total) CleanThumbs()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Windows", "Explorer");
        int deleted = 0, total = 0;
        try
        {
            foreach (var f in new DirectoryInfo(dir).EnumerateFiles("*cache_*.db"))
            {
                total++;
                try { f.Delete(); deleted++; }
                catch { /* 被 explorer 占用，跳过 */ }
            }
        }
        catch { }
        AppEnv.Log($"缩略图/图标缓存：删除 {deleted}/{total}（被占用项跳过，不重启 explorer）。");
        return (deleted, total);
    }

    // ---------- 白名单（~\.config\falco\whitelist.txt，与 PS 版同一文件） ----------
    public static string WhitelistFile()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "falco");
        Directory.CreateDirectory(dir);
        var f = Path.Combine(dir, "whitelist.txt");
        if (!File.Exists(f))
            File.WriteAllText(f, "# Falco 白名单：每行一个路径模式，支持 * 通配，不区分大小写，# 开头为注释\r\n");
        return f;
    }

    public static List<string> ReadWhitelist()
        => File.ReadAllLines(WhitelistFile())
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("#"))
            .ToList();

    public static void AddWhitelist(string path)
    {
        File.AppendAllText(WhitelistFile(), path + "\r\n");
        AppEnv.Log($"已加入白名单：{path}");
    }

    // ---------- 开发缓存（对标 mo clean） ----------
    private static readonly (string path, string label, string proc)[] FixedCacheTargets =
    {
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @"AppData\Roaming\npm-cache"), "clean.cache.npm", null),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"pnpm\store"), "clean.cache.pnpm", null),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"pip\Cache"), "clean.cache.pip", null),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".nuget\packages"), "clean.cache.nuget", null),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), @".gradle\caches"), "clean.cache.gradle", null),
        (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Mozilla\Firefox\Profiles"), "clean.cache.firefox", "firefox"),
    };

    /// <summary>Chrome/Edge 逐 Profile 枚举缓存目录（覆盖多用户配置，不只 Default）。</summary>
    private static List<(string path, string note, string proc)> BrowserCacheTargets()
    {
        var list = new List<(string, string, string)>();
        foreach (var (baseDir, label, proc) in new[]
        {
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Google\Chrome\User Data"), "clean.cache.chrome", "chrome"),
            (Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\User Data"), "clean.cache.edge", "msedge"),
        })
        {
            if (!Directory.Exists(baseDir)) continue;
            foreach (var profile in new DirectoryInfo(baseDir).EnumerateDirectories())
            {
                var cache = Path.Combine(profile.FullName, "Cache");
                if (!Directory.Exists(cache)) continue;
                list.Add((cache, Lang.T(label) + " · " + profile.Name, proc));
            }
        }
        return list;
    }

    public static List<ScanRow> ScanCaches()
    {
        var patterns = ReadWhitelist();
        var rows = new List<ScanRow>();
        var targets = BrowserCacheTargets();
        foreach (var t in FixedCacheTargets)
            targets.Add((t.path, Lang.T(t.label), t.proc));
        foreach (var (path, note, proc) in targets)
        {
            if (!Directory.Exists(path)) continue;
            if (patterns.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase) || path.Contains(p.TrimEnd('*'))))
            { rows.Add(new ScanRow { Path = path, Kind = "protected", Note = Lang.T("clean.note.protected"), SizeText = "—", Checked = false }); continue; }
            if (proc != null && Process.GetProcessesByName(proc).Any())
            { rows.Add(new ScanRow { Path = path, Kind = "protected", Note = Lang.F("clean.skip.running", note), SizeText = "—", Checked = false }); continue; }
            var mb = MeasureMB(path);
            rows.Add(new ScanRow { Path = path, Note = note, SizeText = $"{mb:0.#} MB", SizeMB = mb, Checked = true });
        }
        return rows;
    }

    public static void CleanCaches(IEnumerable<ScanRow> rows)
    {
        foreach (var r in rows.Where(r => r.Checked && r.SizeMB > 0))
        {
            try { TryDeleteChildren(r.Path); AppEnv.Log($"缓存已清理：{r.Path}"); }
            catch (Exception ex) { AppEnv.Log($"缓存清理失败 {r.Path}：{ex.Message}", "ERROR"); }
        }
    }

    // ---------- 构建产物（对标 mo purge：7 天活跃/密钥/嵌套仓库跳过） ----------
    private static readonly HashSet<string> AlwaysDirs = new(StringComparer.OrdinalIgnoreCase)
        { "node_modules", ".next", ".nuxt", ".turbo", "venv", ".venv", "__pycache__", ".gradle" };
    private static readonly HashSet<string> ProjDirs = new(StringComparer.OrdinalIgnoreCase)
        { "target", "build", "dist", ".build", "bin", "obj" };
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
        { "appdata", ".git", "$recycle.bin", "windows", "program files", "program files (x86)", "programdata", "system volume information", "recovery", "msocache", "intel", "perflogs" };

    public static List<ScanRow> ScanPurge(string root, int activeDays = 7)
    {
        var threshold = DateTime.Now.AddDays(-activeDays);
        var found = new List<string>();
        Walk(new DirectoryInfo(root), 0, found);
        var rows = new List<ScanRow>();
        foreach (var d in found)
        {
            var reason = PurgeSkipReason(d, threshold);
            var size = reason == null ? MeasureMB(d) : 0;
            rows.Add(new ScanRow
            {
                Path = d,
                Kind = reason == null ? "rebuildable" : "skip",
                Note = reason == null ? Lang.T("clean.note.rebuildable") : Lang.F("clean.skip", reason),
                SizeText = reason == null ? $"{size:0.#} MB" : "—",
                SizeMB = size,
                Checked = reason == null
            });
        }
        return rows.OrderByDescending(r => r.SizeMB).ToList();
    }

    private static void Walk(DirectoryInfo dir, int depth, List<string> found)
    {
        if (depth > 5) return;
        DirectoryInfo[] subs;
        try { subs = dir.GetDirectories(); } catch { return; }
        foreach (var s in subs)
        {
            if (SkipDirs.Contains(s.Name)) continue;
            if (AlwaysDirs.Contains(s.Name)) { found.Add(s.FullName); continue; }
            if (ProjDirs.Contains(s.Name) && LooksLikeProject(dir)) { found.Add(s.FullName); continue; }
            Walk(s, depth + 1, found);
        }
    }

    private static bool LooksLikeProject(DirectoryInfo dir)
    {
        try
        {
            return dir.EnumerateFiles().Any(f => f.Name is "package.json" or "Cargo.toml" or "pom.xml" or "build.gradle" or "*.csproj" or "*.sln" or "requirements.txt" || f.Extension is ".csproj" or ".sln" or ".pyproj");
        }
        catch { return false; }
    }

    private static string PurgeSkipReason(string dir, DateTime threshold)
    {
        try
        {
            if (Directory.Exists(Path.Combine(dir, ".git"))) return Lang.T("clean.skip.nested");
            if (new DirectoryInfo(dir).EnumerateFiles().Any(f =>
                    f.Name is "id_rsa" or ".env" or "credentials.json" or "secrets.json" || f.Name.EndsWith(".pem"))) return Lang.T("clean.skip.keys");
            var latest = LatestWrite(new DirectoryInfo(dir));
            if (latest > threshold) return Lang.T("clean.skip.active");
        }
        catch { return Lang.T("clean.skip.unmeasurable"); }
        return null;
    }

    private static DateTime LatestWrite(DirectoryInfo dir)
    {
        var latest = DateTime.MinValue;
        try
        {
            foreach (var f in dir.EnumerateFiles("*", System.IO.SearchOption.AllDirectories))
                if (f.LastWriteTime > latest) latest = f.LastWriteTime;
        }
        catch { }
        return latest;
    }

    public static void CleanPurge(IEnumerable<ScanRow> rows)
    {
        foreach (var r in rows.Where(r => r.Checked && r.SizeMB > 0))
        {
            try
            {
                FileSystem.DeleteDirectory(r.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                AppEnv.Log($"构建产物已删除（回收站）：{r.Path}");
            }
            catch (Exception ex) { AppEnv.Log($"删除失败 {r.Path}：{ex.Message}", "ERROR"); }
        }
    }

    /// <summary>安装包扫描：用户 Downloads / 桌面 / Public Downloads（默认），可选追加任意文件夹（顶层）。</summary>
    // ---------- 安装包（对标 mo installer） ----------
    public static List<ScanRow> ScanInstallers(string extraRoot = null)
    {
        var dirs = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Users", "Public", "Downloads"),
        };
        if (!string.IsNullOrWhiteSpace(extraRoot) && Directory.Exists(extraRoot)) dirs.Add(extraRoot);
        var cutoff = DateTime.Now.AddDays(-30);
        var rows = new List<ScanRow>();
        foreach (var d in dirs.Where(Directory.Exists))
        {
            foreach (var f in new DirectoryInfo(d).EnumerateFiles())
            {
                var isInstaller = f.Extension is ".exe" or ".msi" or ".msix" or ".msp"
                                  || (f.Extension == ".zip" && f.Length > 50 * 1024 * 1024);
                if (!isInstaller) continue;
                rows.Add(new ScanRow
                {
                    Path = f.FullName,
                    Note = f.LastWriteTime < cutoff ? Lang.T("clean.inst.aged") : f.LastWriteTime.ToString("yyyy-MM-dd"),
                    SizeText = $"{f.Length / 1024.0 / 1024:0.#} MB",
                    SizeMB = f.Length / 1024.0 / 1024,
                    Checked = f.LastWriteTime < cutoff
                });
            }
        }
        return rows.GroupBy(r => r.Path, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                   .OrderByDescending(r => r.SizeMB).ToList();
    }

    public static void CleanInstallers(IEnumerable<ScanRow> rows)
    {
        foreach (var r in rows.Where(r => r.Checked))
        {
            try
            {
                FileSystem.DeleteFile(r.Path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                AppEnv.Log($"安装包已删除（回收站）：{r.Path}");
            }
            catch (Exception ex) { AppEnv.Log($"删除失败 {r.Path}：{ex.Message}", "ERROR"); }
        }
    }

    // ---------- 内部 ----------
    private static void TryDeleteChildren(string path, string pattern = "*")
    {
        if (!Directory.Exists(path)) return;
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles(pattern))
            { try { f.Delete(); } catch { } }
            if (pattern == "*")
                foreach (var d in new DirectoryInfo(path).EnumerateDirectories())
                { try { d.Delete(true); } catch { } }
        }
        catch { }
    }
}
