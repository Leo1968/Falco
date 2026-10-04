using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Falco.App.Services;

public class AppRow { public string Name, Version, SizeText, UninstallString, KeyPath; public double SizeMB; }
public class StartupRow { public string Name, Command, Location, Kind, RunKey; public bool Enabled = true; }
public class ResidueRow { public string Path, Note; public bool Checked; }
public class TaskRow { public string Name, Path, Action, State; }

/// <summary>软件页：启动项（含启停/删除）/ 已安装应用 / 卸载残留（C# 移植）。</summary>
public static class SoftwareService
{
    // StartupApproved 值格式：12 字节，首字节 2=启用、3=禁用（后 8 字节为 FILETIME）
    private static byte[] ApprovedPayload(bool enabled)
        => enabled ? new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 } : new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

    private static bool IsDisabled(RegistryKey ak, string name)
    {
        if (ak == null) return false;
        var v = ak.GetValue(name) as byte[];
        return v != null && v.Length > 0 && v[0] == 3;
    }

    public static List<StartupRow> GetStartups()
    {
        var rows = new List<StartupRow>();
        var keys = new[]
        {
            (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "HKCU Run", "hkcu",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "HKLM Run", "hklm",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "HKLM Run (32)", "hklm32",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
        };
        foreach (var (hive, sub, loc, kind, approved) in keys)
        {
            using var k = hive.OpenSubKey(sub);
            if (k == null) continue;
            using var ak = hive.OpenSubKey(approved);
            foreach (var name in k.GetValueNames())
                rows.Add(new StartupRow
                {
                    Name = name,
                    Command = $"{k.GetValue(name)}",
                    Location = loc,
                    Kind = kind,
                    RunKey = sub,
                    Enabled = !IsDisabled(ak, name)
                });
        }
        // 启动文件夹（shell:startup）
        var folder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
        if (Directory.Exists(folder))
        {
            using var ak = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder");
            foreach (var f in new DirectoryInfo(folder).EnumerateFiles("*.lnk"))
                rows.Add(new StartupRow
                {
                    Name = Path.GetFileNameWithoutExtension(f.Name),
                    Command = ResolveShortcut(f.FullName) ?? f.FullName,
                    Location = Lang.T("soft.startup.folder"),
                    Kind = "folder",
                    RunKey = f.FullName,
                    Enabled = !IsDisabled(ak, f.Name)
                });
        }
        return rows;
    }

    /// <summary>启用/禁用启动项：写 Explorer\StartupApproved 二进制值（Windows 任务管理器同机制）。HKLM 需管理员。</summary>
    public static void SetStartupEnabled(StartupRow s, bool enabled)
    {
        BackupStartup(s);
        var approved = s.Kind switch
        {
            "hkcu" => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            "hklm" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            "hklm32" => @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            _ => @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder",
        };
        var hive = s.Kind == "hkcu" || s.Kind == "folder" ? Registry.CurrentUser : Registry.LocalMachine;
        using var k = hive.CreateSubKey(approved);
        k.SetValue(s.Name, ApprovedPayload(enabled), RegistryValueKind.Binary);
        AppEnv.Log($"启动项{(enabled ? "已启用" : "已禁用")}：{s.Name}");
    }

    /// <summary>删除启动项：注册表值移除（含 StartupApproved 清理）/ 快捷方式进回收站。先备份。</summary>
    public static void DeleteStartup(StartupRow s)
    {
        BackupStartup(s);
        if (s.Kind == "folder")
        {
            if (File.Exists(s.RunKey))
                Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(s.RunKey,
                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            AppEnv.Log($"启动项已删除（回收站）：{s.Name}");
            return;
        }
        var (hive, sub, approved) = s.Kind switch
        {
            "hkcu" => (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run",
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            "hklm" => (Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"),
            _ => (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"),
        };
        using (var k = hive.CreateSubKey(sub))
            k.DeleteValue(s.Name, false);
        using (var ak = hive.CreateSubKey(approved))
            ak.DeleteValue(s.Name, false);
        AppEnv.Log($"启动项已删除（已备份）：{s.Name}");
    }

    private static void BackupStartup(StartupRow s)
    {
        try
        {
            var safe = new string(s.Name.Where(char.IsLetterOrDigit).ToArray());
            if (safe.Length == 0) safe = "item";
            File.WriteAllText(Path.Combine(AppEnv.BackupDir, $"startup-{safe}.json"),
                JsonSerializer.Serialize(new { s.Name, s.Command, s.Location, s.Kind, s.RunKey, s.Enabled },
                    new JsonSerializerOptions { WriteIndented = true }));
            AppEnv.Log($"启动项已备份：{s.Name}");
        }
        catch (Exception ex) { AppEnv.Log($"启动项备份失败：{s.Name}：{ex.Message}", "WARN"); }
    }

    // ---------- 计划任务（COM Schedule.Service，免依赖；TriggerType：8=开机 9=登录） ----------

    /// <summary>计划任务：登录/启动触发的第三方任务（Microsoft 系统任务过滤，误禁风险高）。</summary>
    public static List<TaskRow> GetLogonTasks()
    {
        var rows = new List<TaskRow>();
        try
        {
            dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
            svc.Connect();
            dynamic folder = svc.GetFolder("\\");
            dynamic tasks = folder.GetTasks(1);   // 1 = 递归子文件夹
            foreach (var t in tasks)
            {
                try
                {
                    string path = t.Path;
                    if (path.StartsWith("\\Microsoft", StringComparison.OrdinalIgnoreCase)) continue;
                    int state = (int)t.State;
                    bool hasLogon = false;
                    string actionText = "";
                    foreach (var trig in t.Definition.Triggers)
                    {
                        if ((int)trig.TriggerType is 8 or 9) { hasLogon = true; break; }   // BOOT / LOGON
                    }
                    if (!hasLogon) continue;
                    foreach (var a in t.Definition.Actions)
                    {
                        try { actionText = $"{a.Path} {a.Arguments}".Trim(); } catch { }
                        if (actionText.Length > 0) break;
                    }
                    rows.Add(new TaskRow
                    {
                        Name = t.Name,
                        Path = path,
                        Action = actionText,
                        State = state switch { 1 => "Disabled", 3 => "Ready", 4 => "Running", _ => state.ToString() }
                    });
                }
                catch { /* 单个任务读取失败跳过 */ }
            }
        }
        catch (Exception ex) { AppEnv.Log($"计划任务枚举失败：{ex.Message}", "WARN"); }
        return rows.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>禁用计划任务（不提供删除——第三方计划任务误删风险高，可回任务计划程序手工管理）。</summary>
    public static void DisableTask(string path)
    {
        dynamic svc = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        svc.Connect();
        var idx = path.LastIndexOf('\\');
        var folderPath = idx <= 0 ? "\\" : path[..idx];
        var taskName = path[(idx + 1)..];
        dynamic folder = svc.GetFolder(folderPath);
        dynamic task = folder.GetTask(taskName);
        task.Enabled = false;
        AppEnv.Log($"计划任务已禁用：{path}");
    }

    public static List<AppRow> GetInstalledApps()
    {
        var rows = new List<AppRow>();
        var paths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
        };
        foreach (var (hive, sub) in new[] { (Registry.LocalMachine, paths[0]), (Registry.LocalMachine, paths[1]), (Registry.CurrentUser, paths[2]) })
        {
            using var baseKey = hive.OpenSubKey(sub);
            if (baseKey == null) continue;
            foreach (var subName in baseKey.GetSubKeyNames())
            {
                using var k = baseKey.OpenSubKey(subName);
                var name = k?.GetValue("DisplayName") as string;
                if (string.IsNullOrEmpty(name)) continue;
                if ((int?)k.GetValue("SystemComponent") == 1) continue;
                var est = k.GetValue("EstimatedSize") as int?;
                rows.Add(new AppRow
                {
                    Name = name,
                    Version = k.GetValue("DisplayVersion") as string ?? "",
                    SizeMB = est.HasValue ? est.Value / 1024.0 : 0,
                    SizeText = est.HasValue ? $"{est.Value / 1024.0:0.#} MB" : "—",
                    UninstallString = k.GetValue("UninstallString") as string ?? k.GetValue("QuietUninstallString") as string ?? "",
                    KeyPath = $"{hive.Name}\\{sub}\\{subName}"
                });
            }
        }
        return rows.GroupBy(r => r.KeyPath, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                   .OrderByDescending(r => r.SizeMB).ToList();
    }

    public static void Uninstall(AppRow app)
    {
        if (string.IsNullOrEmpty(app.UninstallString)) { AppEnv.Log($"无卸载命令：{app.Name}", "WARN"); return; }
        var cmd = app.UninstallString.Trim('"');
        AppEnv.Log($"调用卸载：{app.Name}");
        Process.Start(new ProcessStartInfo("cmd.exe", $"/c start \"\" {app.UninstallString}") { UseShellExecute = false, CreateNoWindow = true });
    }

    /// <summary>卸载残留：数据目录 / 失效卸载键 / 死快捷方式（保守策略：疑似共享默认不勾选）。</summary>
    public static List<ResidueRow> ScanResidue()
    {
        var rows = new List<ResidueRow>();
        var installedApps = GetInstalledApps().Select(a => a.Name.ToLowerInvariant()).ToList();
        var installed = string.Join("|", installedApps);
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)),
        };
        foreach (var root in roots)
        {
            DirectoryInfo dir;
            try { dir = new DirectoryInfo(root); } catch { continue; }
            foreach (var sub in dir.EnumerateDirectories())
            {
                if (IsSystemFolder(sub.Name)) continue;
                var size = CleanService.MeasureMB(sub.FullName);
                if (size < 1) continue;
                var nameLower = sub.Name.ToLowerInvariant();
                // 词边界匹配（避免 "code" 误保护 "vscode"）或全名相等（CJK 名无 \b 边界，保底直接相等）
                var possiblyShared = installedApps.Contains(nameLower)
                                     || Regex.IsMatch(installed, @"\b" + Regex.Escape(nameLower) + @"\b");
                rows.Add(new ResidueRow
                {
                    Path = sub.FullName,
                    Note = possiblyShared ? Lang.F("soft.residue.shared", size.ToString("0.#")) : $"{size:0.#} MB",
                    Checked = !possiblyShared
                });
            }
        }

        // 失效快捷方式
        foreach (var lnk in EnumerateShortcuts())
        {
            try
            {
                var target = ResolveShortcut(lnk);
                if (target != null && !File.Exists(target) && !Directory.Exists(target))
                    rows.Add(new ResidueRow { Path = lnk, Note = Lang.T("soft.residue.dead"), Checked = true });
            }
            catch { }
        }
        return rows.OrderByDescending(r => r.Checked).ToList();
    }

    private static bool IsSystemFolder(string name)
        => name[0] == '.' || name is "Microsoft" or "Programs" or "Temp" or "Packages" or "Google" or "Mozilla" or "Adobe"
           or "win32" or "CMake" or "Python" or "pip" or "npm" or "pnpm" or "NuGet" or "Docker" or "GitHub" or "Claude";

    private static IEnumerable<string> EnumerateShortcuts()
    {
        var dirs = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs"),
        };
        foreach (var d in dirs.Where(Directory.Exists))
            foreach (var f in new DirectoryInfo(d).EnumerateFiles("*.lnk", SearchOption.AllDirectories))
                yield return f.FullName;
    }

    private static string ResolveShortcut(string lnk)
    {
        try
        {
            var ws = Type.GetTypeFromProgID("WScript.Shell");
            var shell = Activator.CreateInstance(ws);
            var s = ws.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
            return ws.InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, s, null) as string;
        }
        catch { return null; }
    }
}

/// <summary>分析页：关键服务 / 大文件 / 目录占用（C# 移植）。</summary>
public static class AnalyzeService
{
    public static List<(string name, string state, string startType)> GetKeyServices()
    {
        var list = new List<(string, string, string)>();
        foreach (var n in new[] { "SysMain", "WSearch", "DoSvc", "wuauserv", "WinDefend", "BITS", "Themes", "AudioSrv" })
        {
            var st = ScService.State(n) ?? Lang.T("ana.svc.missing");
            var sst = ScService.StartType(n) ?? "—";
            list.Add((n, st, sst));
        }
        return list;
    }

    public record BigFile(string Path, double SizeMB, string Modified);

    public static List<BigFile> ScanBigFiles(string root, int minMB, int top = 100)
    {
        var min = minMB * 1024L * 1024;
        return new DirectoryInfo(root)
            .EnumerateFiles("*", SearchOption.AllDirectories)
            .Where(f => f.Length >= min)
            .OrderByDescending(f => f.Length)
            .Take(top)
            .Select(f => new BigFile(f.FullName, f.Length / 1024.0 / 1024, f.LastWriteTime.ToString("yyyy-MM-dd")))
            .ToList();
    }

    public record DirRow(string Name, string SizeText, int Files, string FullPath, double SizeMB);

    public static List<DirRow> ScanFolders(string root)
    {
        var rows = new List<DirRow>();
        foreach (var d in new DirectoryInfo(root).EnumerateDirectories())
        {
            double mb = 0; int files = 0;
            try
            {
                foreach (var f in d.EnumerateFiles("*", SearchOption.AllDirectories))
                { mb += f.Length; files++; }
            }
            catch { }
            rows.Add(new DirRow(d.Name, Models.Format.Bytes(mb), files, d.FullName, mb / 1024 / 1024));
        }
        return rows.OrderByDescending(r => r.SizeMB).ToList();
    }
}
