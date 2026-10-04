using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;

namespace Falco.App.Services;

/// <summary>与 PS 版共用的路径与日志（C:\ProgramData\Falco）。</summary>
public static class AppEnv
{
    public static readonly string BaseDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco");
    public static readonly string BackupDir = Path.Combine(BaseDir, "Backups");
    public static readonly string LogFile = Path.Combine(BaseDir, "Falco.log");
    public static readonly string StateFile = Path.Combine(BaseDir, "state.json");
    public static readonly string RestoreScript = Path.Combine(BaseDir, "Restore-Falco.ps1");
    public static readonly string TweakStateFile = Path.Combine(BaseDir, "tweaks-state.json");

    static AppEnv()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(BackupDir);
    }

    public static bool IsAdmin()
    {
        using var id = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public static void Log(string message, string level = "INFO")
    {
        try
        {
            File.AppendAllText(LogFile,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\r\n");
        }
        catch { }
    }
}

public static class Shell
{
    public static (int code, string output) Run(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe, args)
            { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            using var p = Process.Start(psi);
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(20000);
            return (p.ExitCode, o.Trim());
        }
        catch (Exception ex) { return (-1, ex.Message); }
    }
}
