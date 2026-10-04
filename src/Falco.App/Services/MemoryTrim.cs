using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Falco.App.Services;

/// <summary>
/// 内存自收缩：GC 压实（含 LOH）后把工作集还给系统。
/// 常驻监控型进程的提交内存会随名画解码峰值/池保留缓慢增长且 .NET 不主动归还，
/// 定时收缩 + 托盘化时强制收缩，任务管理器内存可稳定在低位。
/// </summary>
internal static class MemoryTrim
{
    private static DateTime _last = DateTime.MinValue;

    /// <summary>force=false 时 30 分钟内不重复；force=true 立即执行（托盘化时）。</summary>
    public static void Trim(bool force = false)
    {
        if (!force && (DateTime.Now - _last).TotalMinutes < 30) return;
        _last = DateTime.Now;
        try
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
        }
        catch { }
        try
        {
            SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
        }
        catch { }
    }

    [DllImport("kernel32.dll")]
    private static extern bool SetProcessWorkingSetSize(IntPtr hProcess, IntPtr min, IntPtr max);
}
