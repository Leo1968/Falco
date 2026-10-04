using System.Diagnostics;
using Falco.App.Services;
using System.IO;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LibreHardwareMonitor.Hardware;
using Falco.App.Models;

namespace Falco.App.Services;

/// <summary>
/// 后台指标采集：CPU/内存/磁盘/网络/温度(LHM)/进程（含图标提取缓存）/电源计划。
/// 每 2 秒产生一份 MetricSnapshot（后台线程触发，UI 侧自行调度）。
/// </summary>
public class MetricsService : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime { public uint Lo, Hi; public long Value => ((long)Hi << 32) | (uint)Lo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatus
    {
        public uint DwLength;
        public uint DwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll")]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatus buf);

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);

    private readonly Thread _loop;
    private volatile bool _stop;
    private readonly int _cores = Environment.ProcessorCount;

    // CPU 基线
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _hasBase;

    // 网络基线
    private long _prevUp, _prevDown;
    private DateTime _prevNet = DateTime.UtcNow;
    private string _netName = "";

    // 进程基线
    private Dictionary<int, TimeSpan> _prevCpu = new();
    private DateTime _prevProc = DateTime.UtcNow;

    // 多磁盘汇总缓存（与 PS 版一致：显示合计，健康度用系统盘）；系统盘字段随缓存保存，
    // 非刷新轮从缓存取（否则系统盘字段在多数轮次为 0，底部状态条会显示"可用 0 GB"）
    private (double AllFree, double AllTot, double AllPct, string Detail, double SysFree, double SysTot, double SysPct)? _diskCache;

    // GPU 引擎计数器 / ACPI 温度 / WMI 缓存
    private double _lastGpu; private bool _lastGpuOk;
    private double? _acpiTemp;
    private string _gpuNameWmi; private bool _wmiTried;
    private DateTime? _bootTime;            // 系统开机（仅健康度"长时间未重启"用，见 Collect 内注释）
    private readonly DateTime? _appStart;   // Falco 进程启动（状态页"已运行"显示）

    // 图标缓存（exe 路径 → 图标）与通用回退
    private readonly Dictionary<string, ImageSource> _icons = new(StringComparer.OrdinalIgnoreCase);
    private ImageSource _fallbackIcon;

    // LHM
    private Computer _lhm;
    private bool _lhmOk;

    public event Action<MetricSnapshot> SnapshotReady;
    public string PowerGuid { get; private set; } = "";

    public MetricsService()
    {
        _loop = new Thread(Run) { IsBackground = true, Name = "FalcoMetrics" };
        try { using var self = Process.GetCurrentProcess(); _appStart = self.StartTime; } catch { }
    }

    public void Start()
    {
        try
        {
            _fallbackIcon = MakeIconSource(System.Drawing.SystemIcons.Application);
        }
        catch { /* 图标不可用时行内留空 */ }
        _loop.Start();
    }

    private void Run()
    {
        // LHM 硬件枚举（约 1 秒）放后台线程，不阻塞界面启动
        try
        {
            _lhm = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMotherboardEnabled = true };
            _lhm.Open();
            _lhmOk = true;
        }
        catch { _lhmOk = false; }
        int tick = 0;
        while (!_stop)
        {
            try { SnapshotReady?.Invoke(Collect(tick)); }
            catch { /* 单轮失败不终止采集 */ }
            tick++;
            if (tick == 30 || tick % 900 == 0) MemoryTrim.Trim();   // 启动 1 分钟首缩，此后每 30 分钟
            Thread.Sleep(2000);
        }
        try { _lhm?.Close(); } catch { }
    }

    private MetricSnapshot Collect(int tick)
    {
        // ---- CPU 总占用 ----
        double cpu = 0;
        GetSystemTimes(out var idle, out var kernel, out var user);
        if (_hasBase)
        {
            var dIdle = (double)(idle.Value - _prevIdle);
            var dTotal = (double)((kernel.Value - _prevKernel) + (user.Value - _prevUser));
            if (dTotal > 0) cpu = Math.Clamp((1 - dIdle / dTotal) * 100, 0, 100);
        }
        _prevIdle = idle.Value; _prevKernel = kernel.Value; _prevUser = user.Value; _hasBase = true;

        // ---- 内存 ----
        var ms = new MemoryStatus { DwLength = (uint)Marshal.SizeOf<MemoryStatus>() };
        GlobalMemoryStatusEx(ref ms);
        double memPct = ms.ullTotalPhys > 0 ? (1 - (double)ms.ullAvailPhys / ms.ullTotalPhys) * 100 : 0;
        double memTotGB = ms.ullTotalPhys / 1024.0 / 1024 / 1024;

        // ---- 磁盘（汇总所有固定盘 + 系统盘单独保留；每 5 轮，其余轮读缓存）----
        double df, dt, dp;
        if (tick % 5 == 0 || _diskCache == null)
        {
            double sdf = 0, sdt = 0, sdp = 0;
            try
            {
                double allTot = 0, allFree = 0;
                var parts = new List<string>();
                foreach (var d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    var tot = d.TotalSize / 1024.0 / 1024 / 1024;
                    var free = d.AvailableFreeSpace / 1024.0 / 1024 / 1024;
                    allTot += tot; allFree += free;
                    parts.Add(Lang.F("disk.detail", d.Name.TrimEnd('\\', ' '), (int)free));
                    if (string.Equals(d.Name.TrimEnd('\\', ' '), Environment.SystemDirectory[..1] + ":", StringComparison.OrdinalIgnoreCase))
                    { sdf = free; sdt = tot; sdp = tot > 0 ? (tot - free) / tot * 100 : 0; }
                }
                if (allTot > 0)
                    _diskCache = (allFree, allTot, (allTot - allFree) / allTot * 100, string.Join(" · ", parts), sdf, sdt, sdp);
            }
            catch { }
        }
        var dc = _diskCache;
        df = dc?.SysFree ?? 0; dt = dc?.SysTot ?? 0; dp = dc?.SysPct ?? 0;

        // ---- 网络 ----
        double up = 0, down = 0; _netName = "";
        try
        {
            var nic = NetworkInterface.GetAllNetworkInterfaces()
                .FirstOrDefault(n => n.OperationalStatus == OperationalStatus.Up
                                  && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
                                  && n.GetIPv4Statistics() != null);
            if (nic != null)
            {
                var s = nic.GetIPv4Statistics();
                var now = DateTime.UtcNow;
                var elapsed = Math.Max(0.5, (now - _prevNet).TotalSeconds);
                up = Math.Max(0, (s.BytesSent - _prevUp) / elapsed);
                down = Math.Max(0, (s.BytesReceived - _prevDown) / elapsed);
                _prevUp = s.BytesSent; _prevDown = s.BytesReceived; _prevNet = now;
                _netName = nic.Description;
            }
        }
        catch { }

        // ---- GPU 占用（Windows GPU 引擎计数器，与 PS 版同源；每 3 轮一查）----
        double gpu; bool gpuOk;
        if (tick % 3 == 0)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");
                double sum = 0; int n = 0;
                foreach (var o in searcher.Get())
                {
                    try { var u = Convert.ToDouble(o["UtilizationPercentage"]); if (u > 0) sum += u; n++; }
                    catch { }
                }
                if (n > 0) { _lastGpu = Math.Min(100, sum); _lastGpuOk = true; }
            }
            catch { }
        }
        gpu = _lastGpu; gpuOk = _lastGpuOk;

        // ---- 温度（LHM 优先，ACPI 热区兜底；GPU 温度仅 LHM 暴露）----
        double? cpuTemp = null, gpuTemp = null;
        if (_lhmOk)
        {
            try
            {
                _lhm.Traverse(new UpdateVisitor());
                foreach (var hw in _lhm.Hardware)
                {
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        foreach (var sn in hw.Sensors)
                            if (sn.SensorType == SensorType.Temperature && sn.Value.HasValue
                                && (sn.Name.Contains("Package") || sn.Name.Contains("Core") || sn.Name.StartsWith("Tc")))
                            { cpuTemp ??= sn.Value.Value; }
                    }
                    else if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        foreach (var sn in hw.Sensors)
                            if (sn.SensorType == SensorType.Temperature && sn.Value.HasValue) gpuTemp ??= sn.Value.Value;
                    }
                }
            }
            catch { }
        }
        if (cpuTemp == null && tick % 5 == 0)
        {
            try
            {
                using var s = new System.Management.ManagementObjectSearcher("root\\wmi",
                    "SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature");
                double max = 0;
                foreach (var o in s.Get())
                {
                    var t = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                    if (t > 0 && t < 120 && t > max) max = t;
                }
                if (max > 0) _acpiTemp = max;
            }
            catch { }
        }
        if (cpuTemp == null) cpuTemp = _acpiTemp;

        // ---- GPU 名（WMI，一次）/ 开机时间（每轮重算）----
        if (!_wmiTried)
        {
            _wmiTried = true;
            try
            {
                using var s = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
                foreach (var o in s.Get()) { _gpuNameWmi = Convert.ToString(o["Name"]); break; }
            }
            catch { }
        }
        // 系统开机时间：GetTickCount64 语义（与任务管理器一致，含睡眠时段）。
        // 不用 WMI LastBootUpTime —— 快速启动（Fast Startup）下它不更新，会显示上上次启动。
        // 注意不能拿它当"已运行"展示：睡眠/快速启动断电时段也计入，会远大于用户感知时长；
        // 展示用 AppStart（Falco 进程启动时间）。
        try { _bootTime = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64); } catch { }
        string gpuName = _gpuNameWmi ?? "";

        // ---- 进程（CPU 增量 + 图标缓存）----
        var procs = Process.GetProcesses();
        var cur = new Dictionary<int, TimeSpan>();
        var rows = new List<(int pid, string name, double pct, double ws, string path)>();
        var nowP = DateTime.UtcNow;
        var elapsedP = Math.Max(0.5, (nowP - _prevProc).TotalSeconds);
        foreach (var p in procs)
        {
            string path = null;
            try { path = p.MainModule?.FileName; } catch { }
            TimeSpan total;
            try { total = p.TotalProcessorTime; } catch { continue; }
            cur[p.Id] = total;
            double pct = 0;
            if (_prevCpu.TryGetValue(p.Id, out var prev)) pct = Math.Clamp((total - prev).TotalSeconds / elapsedP / _cores * 100, 0, 100);
            rows.Add((p.Id, p.ProcessName, pct, p.WorkingSet64 / 1024.0 / 1024, path));
            try { p.Dispose(); } catch { }
        }
        _prevCpu = cur; _prevProc = nowP;

        var top = rows.OrderByDescending(r => r.pct).ThenByDescending(r => r.ws).Take(15)
            .Select(r => new ProcInfo
            {
                Name = r.name,
                Pid = r.pid,
                CpuPct = r.pct,
                CpuText = $"{r.pct:0.0}%",
                WsMB = r.ws,
                MemText = $"{r.ws:0} MB",
                Icon = IconFor(r.path),
                DotBrush = BrushFor(r.pct)
            }).ToList();

        // ---- 电源计划（低频）----
        if (tick % 15 == 0)
        {
            try
            {
                var out_ = RunPowerCfg("/getactivescheme");
                var m = System.Text.RegularExpressions.Regex.Match(out_ ?? "",
                    @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                if (m.Success) PowerGuid = m.Value;
            }
            catch { }
        }

        return new MetricSnapshot
        {
            Cpu = cpu, Gpu = gpu, GpuOk = gpuOk, GpuName = gpuName,
            MemPct = memPct, MemUsedGB = memTotGB * memPct / 100, MemTotGB = memTotGB,
            CpuTemp = cpuTemp, GpuTemp = gpuTemp, TempSource = _lhmOk ? "LibreHardwareMonitor" : "ACPI",
            BootTime = _bootTime,
            AppStart = _appStart,
            DiskFreeGB = df, DiskTotGB = dt, DiskPct = dp,
            DiskAllFreeGB = _diskCache?.AllFree ?? df,
            DiskAllTotGB = _diskCache?.AllTot ?? dt,
            DiskAllPct = _diskCache?.AllPct ?? dp,
            DiskDetail = _diskCache?.Detail ?? "",

            NetUp = up, NetDown = down, NetName = _netName,
            TopProcs = top, ProcCount = procs.Length
        };
    }

    internal static SolidColorBrush BrushFor(double pct)
    {
        var c = pct < 30 ? Color.FromRgb(52, 211, 153) : pct < 60 ? Color.FromRgb(251, 191, 36) : Color.FromRgb(239, 68, 68);
        var b = new SolidColorBrush(c); b.Freeze(); return b;
    }

    private ImageSource IconFor(string path)
    {
        if (string.IsNullOrEmpty(path)) return _fallbackIcon;
        lock (_icons)
        {
            if (_icons.TryGetValue(path, out var hit)) return hit;
            ImageSource src = _fallbackIcon;
            try
            {
                using var ico = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (ico != null) src = MakeIconSource(ico);
            }
            catch { }
            _icons[path] = src;
            return src;
        }
    }

    private static ImageSource MakeIconSource(System.Drawing.Icon ico)
    {
        var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        src.Freeze();
        return src;
    }

    private static string RunPowerCfg(string args)
    {
        var psi = new ProcessStartInfo("powercfg.exe", args) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true };
        using var p = Process.Start(psi);
        return p?.StandardOutput.ReadToEnd().Trim();
    }

    private sealed class UpdateVisitor : IVisitor
    {
        public void VisitComputer(IComputer computer) => computer.Traverse(this);
        public void VisitHardware(IHardware hardware)
        {
            hardware.Update();
            foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
        }
        public void VisitSensor(ISensor sensor) { }
        public void VisitParameter(IParameter parameter) { }
    }

    public void Dispose()
    {
        _stop = true;
        try { _lhm?.Close(); } catch { }
    }
}
