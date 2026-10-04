using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Falco.App.Services;

/// <summary>
/// 立即加速：遍历进程修剪工作集（EmptyWorkingSet），把闲置进程的物理内存页换出到页面文件，
/// 对应电脑管家"一键加速"的释放内存语义。安全：不结束任何进程；换出页下次访问自动载回。
/// </summary>
public static class BoostService
{
    [DllImport("psapi.dll")]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>();
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile,
                      ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX buffer);

    private static ulong AvailPhys()
    {
        var m = new MEMORYSTATUSEX();
        GlobalMemoryStatusEx(m);
        return m.ullAvailPhys;
    }

    /// <summary>执行一轮加速，返回（释放 GB，修剪进程数，系统进程总数）。</summary>
    public static (double freedGB, int trimmed, int total) Run()
    {
        var before = AvailPhys();
        int trimmed = 0;
        var self = Environment.ProcessId;
        var procs = Process.GetProcesses();
        foreach (var p in procs)
        {
            try
            {
                if (p.Id == self) continue;   // 不修剪自己，避免 UI 卡顿
                if (EmptyWorkingSet(p.Handle)) trimmed++;
            }
            catch { /* 受保护/已退出进程忽略 */ }
            finally { try { p.Dispose(); } catch { } }
        }
        var freed = (double)(AvailPhys() - before) / 1024 / 1024 / 1024;
        AppEnv.Log($"立即加速：修剪 {trimmed}/{procs.Length} 个进程，释放 {freed:0.##} GB");
        return (freed, trimmed, procs.Length);
    }
}
