using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Falco.App.Models;

/// <summary>一轮采集的完整快照（UI 消费）。</summary>
public class MetricSnapshot
{
    public double Cpu { get; init; }
    public double Gpu { get; init; }
    public bool GpuOk { get; init; }
    public string GpuName { get; init; } = "";

    public double MemPct { get; init; }
    public double MemUsedGB { get; init; }
    public double MemTotGB { get; init; }

    public double? CpuTemp { get; init; }
    public double? GpuTemp { get; init; }
    public string TempSource { get; init; } = "";
    public DateTime? BootTime { get; init; }   // 系统开机时间（仅健康度"长时间未重启"判断用）
    public DateTime? AppStart { get; init; }   // Falco 进程启动时间（状态页"已运行"显示用）

    public double DiskFreeGB { get; init; }
    public double DiskTotGB { get; init; }
    public double DiskPct { get; init; }
    public double DiskAllFreeGB { get; init; }
    public double DiskAllTotGB { get; init; }
    public double DiskAllPct { get; init; }
    public string DiskDetail { get; init; } = "";

    public double NetUp { get; init; }       // bytes/s
    public double NetDown { get; init; }
    public string NetName { get; init; } = "";

    public IReadOnlyList<ProcInfo> TopProcs { get; init; } = Array.Empty<ProcInfo>();
    public int ProcCount { get; init; }
}

/// <summary>进程行（含图标与圆点画刷，UI 线程消费）。</summary>
public class ProcInfo
{
    public string Name { get; init; } = "";
    public int Pid { get; init; }
    public double CpuPct { get; init; }
    public string CpuText { get; init; } = "";
    public double WsMB { get; init; }
    public string MemText { get; init; } = "";
    public ImageSource Icon { get; init; }
    public SolidColorBrush DotBrush { get; init; }
}

/// <summary>硬件信息（hardware.json 缓存兼容 PS 版格式）。</summary>
public class HardwareInfo
{
    public string CPU { get; set; } = "";
    public int Cores { get; set; }
    public int Threads { get; set; }
    public double RAM_GB { get; set; }
    public string SystemDisk { get; set; } = "";
    public string DiskType { get; set; } = "";
    public string Windows { get; set; } = "";
    public string Build { get; set; } = "";
}

public static class Format
{
    public static string NetRate(double bps)
    {
        if (bps < 1024) return $"{bps:0} B/s";
        if (bps < 1024 * 1024) return $"{bps / 1024:0.#} KB/s";
        return $"{bps / 1024 / 1024:0.#} MB/s";
    }

    public static string Bytes(double b)
    {
        if (b < 1024) return $"{b:0} B";
        if (b < 1024 * 1024) return $"{b / 1024:0} KB";
        if (b < 1024.0 * 1024 * 1024) return $"{b / 1024 / 1024:0.#} MB";
        return $"{b / 1024 / 1024 / 1024:0.#} GB";
    }

	public static string UpTime(TimeSpan span)
		=> span.TotalHours >= 48 ? Falco.App.Services.Lang.F("uptime.days", span.TotalDays.ToString("0.#"))
		 : span.TotalHours >= 1 ? Falco.App.Services.Lang.F("uptime.hours", (int)span.TotalHours)
		 : Falco.App.Services.Lang.F("uptime.mins", Math.Max(1, (int)span.TotalMinutes));
}
