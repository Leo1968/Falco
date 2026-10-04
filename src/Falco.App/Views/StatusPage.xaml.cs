using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Media3D;
using System.Windows.Media.Imaging;
using Falco.App.Models;
using Falco.App.Services;

namespace Falco.App.Views;

public partial class StatusPage : UserControl
{
    // 状态色取自主题字典（属性每次动态取，切换主题即时刷新）
    private static System.Windows.Media.Brush Green => Services.Theme.FindBrush("BrushGreen");
    private static System.Windows.Media.Brush Yellow => Services.Theme.FindBrush("BrushYellow");
    private static System.Windows.Media.Brush Red => Services.Theme.FindBrush("BrushRed");

    private readonly Queue<double> _cpuHist = new();
    private readonly Queue<double> _gpuHist = new();
    private readonly Queue<double> _netHist = new();
    private readonly Queue<double> _tempHist = new();

    // 名称存语言资源 key（与电源卡三个胶囊按钮同源），避免英文界面显示系统中文方案名
    private static readonly (string guid, string key)[] PowerSchemes =
    {
        ("a1841308-3541-4fab-b81a-f91556f20b7a", "power.saver"),
        ("381b4222-f694-41f0-9685-ff5bb260df2e", "power.balanced"),
        ("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", "power.high"),
    };
    private readonly MetricsService _metrics;
    private HardwareInfo _hw;
    private DateTime? _boot;       // 系统开机时间（健康度"长时间未重启"）
    private DateTime? _appStart;   // Falco 进程启动时间（"已运行"显示）
    private MetricSnapshot _last;  // 最新快照（全面体检用）

    public StatusPage(MetricsService metrics)
    {
        InitializeComponent();
        _metrics = metrics;
        LoadHardware();
        BtnBoost.Tag = Services.BrandAsset.LoadFromIco();
        try { _appStart = System.Diagnostics.Process.GetCurrentProcess().StartTime; } catch { }
        try { _boot = DateTime.Now - TimeSpan.FromMilliseconds(Environment.TickCount64); } catch { }
        _metrics.SnapshotReady += s => Dispatcher.BeginInvoke(() => Apply(s));
        InitializePlanet();

        // 每时名画：Masterpieces 轮换（小时数 % 总数，整点自动换画）
        _artTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _artTimer.Tick += (_, __) => LoadArtwork();
        _artTimer.Start();
        Loaded += (_, __) => LoadArtwork();
    }

    private System.Windows.Threading.DispatcherTimer _artTimer;
    private long _artKey = -1;
    private string? _lastArtFile;               // 当前展示的画作（换画防重复）
    private DateTime _lastArtClick;             // 「换一幅」上次抓取时间（60s 冷却）

    // 策展流派 zh → en（CuratedArt.Style 仅中文；随机源流派本就是英文）
    private static readonly Dictionary<string, string> StyleEn = new()
    {
        ["文艺复兴"] = "Renaissance",
        ["北方文艺复兴"] = "Northern Renaissance",
        ["威尼斯画派"] = "Venetian School",
        ["样式主义"] = "Mannerism",
        ["巴洛克"] = "Baroque",
        ["古典主义"] = "Classicism",
        ["浪漫主义"] = "Romanticism",
        ["印象派"] = "Impressionism",
        ["后印象派"] = "Post-Impressionism",
        ["现实主义"] = "Realism",
        ["新印象派"] = "Neo-Impressionism",
        ["表现主义"] = "Expressionism",
        ["维也纳分离派"] = "Vienna Secession",
        ["新古典主义"] = "Neoclassicism",
        ["拉斐尔前派"] = "Pre-Raphaelite",
        ["地区主义"] = "Regionalism",
        ["抽象艺术"] = "Abstract",
        ["浮世绘"] = "Ukiyo-e",
        ["野兽派"] = "Fauvism",
    };

    /// <summary>健康度卡 3D 动画地球：NASA 等距贴图 + UV 球 + 30 秒自转（对标 react-anim-globe）。</summary>
    private void InitializePlanet()
    {
        try
        {
            var mapPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "earth-map.jpg");
            if (!File.Exists(mapPath)) return;
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.UriSource = new Uri(mapPath);
            src.EndInit();
            src.Freeze();

            GlobeBrush.ImageSource = src;
            GlobeGeometry.Geometry = MakeSphere(1.0, 48, 32);

            // 自西向东自转（0→360°，30 秒一圈，无限循环）
            var spin = new System.Windows.Media.Animation.DoubleAnimation
            {
                From = 0, To = 360,
                Duration = new Duration(TimeSpan.FromSeconds(30)),
                RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever,
            };
            GlobeSpin.BeginAnimation(AxisAngleRotation3D.AngleProperty, spin);
        }
        catch { }
    }

    /// <summary>UV 球网格（等距圆柱投影贴图）。</summary>
    private static MeshGeometry3D MakeSphere(double radius, int slices, int stacks)
    {
        var mesh = new MeshGeometry3D();
        for (int y = 0; y <= stacks; y++)
        {
            var v = (double)y / stacks;
            var phi = v * Math.PI;
            for (int x = 0; x <= slices; x++)
            {
                var u = (double)x / slices;
                var theta = u * 2 * Math.PI;
                var p = new Point3D(
                    radius * Math.Sin(phi) * Math.Cos(theta),
                    radius * Math.Cos(phi),
                    radius * Math.Sin(phi) * Math.Sin(theta));
                mesh.Positions.Add(p);
                mesh.Normals.Add(new Vector3D(p.X, p.Y, p.Z));
                mesh.TextureCoordinates.Add(new Point(u, 1 - v));
            }
        }
        for (int y = 0; y < stacks; y++)
        {
            for (int x = 0; x < slices; x++)
            {
                var a = y * (slices + 1) + x;
                var b = a + slices + 1;
                mesh.TriangleIndices.Add(a); mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(a + 1);
                mesh.TriangleIndices.Add(b); mesh.TriangleIndices.Add(b + 1); mesh.TriangleIndices.Add(a + 1);
            }
        }
        return mesh;
    }

    private static readonly string[] ArtExts = { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    private static string ArtFolder()
    {
        var shared = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "Masterpieces");
        if (Directory.Exists(shared) && Directory.EnumerateFiles(shared).Any(f => ArtExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant()))) return shared;
        var local = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Masterpieces");
        if (Directory.Exists(local)) return local;
        return shared;   // 都没有 → 显示提示
    }

    private void LoadArtwork()
    {
        var key = DateTime.Now.ToFileTime() / (TimeSpan.TicksPerMinute * 15);   // 每 15 分钟换一张（含后台抓新画）
        if (key == _artKey) return;
        _artKey = key;

        // 先随机展示本地库一张（离线/抓取慢时也有画看），再后台从 5 馆抓新画替换
        if (!ShowRandomLocal())
            ArtTitle.Text = Lang.T("art.grabbing");
        GrabAndShow();
    }

    /// <summary>随机展示本地一张画作（避开当前这张）。返回是否成功。</summary>
    private bool ShowRandomLocal()
    {
        try
        {
            var dir = ArtFolder();
            var files = Directory.Exists(dir)
                ? Directory.EnumerateFiles(dir).Where(f => ArtExts.Contains(System.IO.Path.GetExtension(f).ToLowerInvariant())).ToArray()
                : Array.Empty<string>();
            // 防重复：换画时避开当前这张
            if (!string.IsNullOrEmpty(_lastArtFile))
                files = files.Where(f => !string.Equals(f, _lastArtFile, StringComparison.OrdinalIgnoreCase)).ToArray();
            // 策展优先：已有策展画作时 70% 概率只在其内部轮换
            var curated = files.Where(f => System.IO.Path.GetFileName(f).StartsWith("curated-", StringComparison.Ordinal)).ToArray();
            if (curated.Length > 0 && Random.Shared.Next(100) < 70) files = curated;
            if (files.Length == 0) return false;
            var pick = files[Random.Shared.Next(files.Length)];
            ShowFile(pick);
            return true;
        }
        catch (Exception ex)
        {
            ArtTitle.Text = Lang.F("art.error", ex.Message);
            return false;
        }
    }

    /// <summary>「换一幅」：瞬时换本地另一张（防重复）；距上次点击 ≥60 秒时同时后台抓一幅新画（冷却防打爆 API）。</summary>
    private void OnNextArt(object sender, RoutedEventArgs e)
    {
        var fresh = (DateTime.Now - _lastArtClick).TotalSeconds >= 60;
        if (fresh) _lastArtClick = DateTime.Now;
        ShowRandomLocal();
        if (fresh) GrabAndShow();
    }

    /// <summary>后台从 5 大博物馆随机抓一张新画入库并展示；失败保持当前画面。</summary>
    private void GrabAndShow()
    {
        System.Threading.Tasks.Task.Run(() =>
        {
            var meta = Services.MuseumApi.FetchRandom();
            if (meta == null) return;
            ArtworkService.AppendCatalog(meta);
            Dispatcher.BeginInvoke(() => ShowFile(System.IO.Path.Combine(ArtworkService.Folder, meta.File)));
        });
    }

    /// <summary>加载本地图片文件并刷新介绍面板（catalog 元数据优先，回退文件名）。</summary>
    private void ShowFile(string path)
    {
        try
        {
            var src = new BitmapImage();
            src.BeginInit();
            src.CacheOption = BitmapCacheOption.OnLoad;
            src.DecodePixelWidth = 1280;   // 显示上限 ~1120 物理px；降低解码内存与峰值
            src.UriSource = new Uri(path);
            src.EndInit();
            src.Freeze();
            ArtImage.Source = src;
            ArtBlurBrush.ImageSource = src;   // 玻璃浮层的磨砂取景层（同一图源，UniformToFill 出血模糊）
            _lastArtFile = path;

            var meta = ArtworkService.Lookup(System.IO.Path.GetFileName(path));
            var en = Lang.Current == "en";
            ArtTitle.Text = (en ? meta?.TitleEn : null) ?? meta?.Title ?? System.IO.Path.GetFileNameWithoutExtension(path);
            ArtArtist.Text = (en ? meta?.ArtistEn : null) ?? meta?.Artist ?? "";
            ArtYear.Text = string.IsNullOrEmpty(meta?.Year) ? "—" : meta.Year;
            // 策展条目的流派只有中文（随机源本就是英文），英文界面按映射表翻译
            var style = string.IsNullOrEmpty(meta?.Style) ? "—" : meta.Style;
            ArtStyle.Text = en && style != "—" && StyleEn.TryGetValue(style, out var se) ? se : style;
            var museum = (en ? meta?.MuseumEn : null) ?? meta?.Museum;
            ArtMuseum.Text = string.IsNullOrEmpty(museum) ? Lang.T("art.local") : museum;
            // 策展 credit 不读缓存（旧条目缓存里是中文），展示时按当前语言生成
            var credit = meta?.Credit ?? "";
            if (meta?.Source?.StartsWith("curated:", StringComparison.Ordinal) == true)
                credit = Lang.T("art.credit.openaccess");
            ArtCredit.Text = credit;
        }
        catch (Exception ex)
        {
            ArtTitle.Text = Lang.F("art.error", ex.Message);
        }
    }

    private void LoadHardware()
    {
        // PS 版兼容缓存：C:\ProgramData\Falco\hardware.json（PS 版 1.3.0 机器上可能已有）
        try
        {
            var f = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "hardware.json");
            if (File.Exists(f)) _hw = JsonSerializer.Deserialize<HardwareInfo>(File.ReadAllText(f));
        }
        catch { }
        if (_hw != null)
        {
            FillHardwareBadges();
            return;
        }

        // 无缓存（新机器）：后台 WMI 自检 → 回填徽章并写缓存，后续启动与 PS 版共用
        Task.Run(() =>
        {
            HardwareInfo? hw = null;
            try
            {
                hw = DetectHardware();
                var cache = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "hardware.json");
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(cache)!);
                File.WriteAllText(cache, JsonSerializer.Serialize(hw, new JsonSerializerOptions { WriteIndented = true }));
                AppEnv.Log("硬件信息自检完成并已缓存。");
            }
            catch (Exception ex) { AppEnv.Log($"硬件自检失败：{ex.Message}", "WARN"); }
            if (hw == null) return;
            Dispatcher.BeginInvoke(() => { _hw = hw; FillHardwareBadges(); });
        });
    }

    private void FillHardwareBadges()
    {
        if (_hw == null) return;
        var cpu = _hw.CPU?.Replace("(R)", "").Replace("(TM)", "").Replace("(C)", "").Trim() ?? "";
        Tag1.Text = cpu.Length > 22 ? cpu[..22] + "…" : cpu;
        Tag2.Text = $"{_hw.RAM_GB:0.#} GB";
        Tag3.Text = $"Win{_hw.Build}";
    }

    /// <summary>WMI 硬件自检：CPU 型号/核线程、内存容量、Windows build。</summary>
    private static HardwareInfo DetectHardware()
    {
        var hw = new HardwareInfo();
        foreach (var o in new System.Management.ManagementObjectSearcher("SELECT Name, NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor").Get())
        {
            try
            {
                hw.CPU = (string)o["Name"];
                hw.Cores = (int)(uint)o["NumberOfCores"];
                hw.Threads = (int)(uint)o["NumberOfLogicalProcessors"];
            }
            catch { }
            finally { o.Dispose(); }
            break;   // 取第一颗物理 CPU
        }
        foreach (var o in new System.Management.ManagementObjectSearcher("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").Get())
        {
            try { hw.RAM_GB = (ulong)o["TotalPhysicalMemory"] / 1073741824.0; }
            catch { }
            finally { o.Dispose(); }
            break;
        }
        try
        {
            hw.Build = Microsoft.Win32.Registry.GetValue(
                @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuild", "") as string ?? "";
        }
        catch { }
        return hw;
    }

    private void Apply(MetricSnapshot s)
    {
        _last = s;

        // ---- CPU ----
        CpuPct.Text = $"{(int)s.Cpu}%";
        var (word, brush) = s.Cpu < 30 ? (Lang.T("common.load.low"), Green) : s.Cpu < 60 ? (Lang.T("common.load.mid"), Yellow) : (Lang.T("common.load.high"), Red);
        CpuWord.Text = word; CpuWord.Foreground = brush;
        Push(_cpuHist, s.Cpu, 30); DrawBars(CpuChart, _cpuHist, "#34D399");
        CpuBadge.Text = s.CpuTemp.HasValue ? $"{(int)s.CpuTemp.Value}°" : "";
        CpuSub.Text = Lang.F("cpu.threads", word, _hw?.Threads ?? s.TopProcs.Count);

        // ---- GPU ----
        if (!s.GpuOk)
        {
            GpuPct.Text = "—"; GpuWord.Text = "";
            GpuSub.Text = Lang.F("gpu.nocounter", Trim(s.GpuName, 26));
        }
        else
        {
            GpuPct.Text = $"{(int)s.Gpu}%";
            var (gw, gb) = s.Gpu < 30 ? (Lang.T("common.load.low"), Green) : s.Gpu < 60 ? (Lang.T("common.load.mid"), Yellow) : (Lang.T("common.load.high"), Red);
            GpuWord.Text = gw; GpuWord.Foreground = gb;
            GpuSub.Text = $"{gw} · {Trim(s.GpuName, 26)}";
        }
        Push(_gpuHist, s.GpuOk ? s.Gpu : 0, 60); DrawLine(GpuLine, GpuChart, _gpuHist, 100);

        // ---- 内存 ----
        MemPct.Text = $"{(int)s.MemPct}%";
        var pressure = s.MemPct >= 90 ? Lang.T("temp.hot") : s.MemPct >= 75 ? Lang.T("health.note.mem") : Lang.T("temp.normal");
        MemBadge.Text = Lang.F("mem.pressure", pressure);
        MemBar.Value = s.MemPct;
        MemSub.Text = Lang.F("mem.sub", s.MemUsedGB.ToString("0.##"), s.MemTotGB.ToString("0.#"));

        // ---- 温度 ----
        if (s.CpuTemp.HasValue)
        {
            var t = s.CpuTemp.Value;
            TempVal.Text = $"{(int)t}°";
            TempBar.Value = Math.Min(105, t);
            TempBar.Foreground = t >= 80 ? Red : t >= 70 ? Yellow : Green;
            TempBadge.Text = t >= 80 ? Lang.T("temp.hot") : t >= 70 ? Lang.T("temp.high") : Lang.T("temp.normal");
            _tempHist.Enqueue(t); while (_tempHist.Count > 150) _tempHist.Dequeue();
            var peak = _tempHist.Count > 0 ? _tempHist.Max() : t;
            TempSub.Text = Lang.F("temp.peak", (int)peak);
        }
        else
        {
            TempVal.Text = "—"; TempBar.Value = 0;
            TempBadge.Text = "";
            // 区分原因：非管理员时 LHM 无法加载内核驱动读传感器（最常见），提示可操作；管理员仍无则确为硬件不支持
            TempSub.Text = AppEnv.IsAdmin() ? Lang.T("temp.nosupport") : Lang.T("temp.needadmin");
        }

        // ---- GPU 温度（仅 LHM 暴露；iGPU 多数无机内传感器，读不到显 "--"）----
        if (s.GpuTemp.HasValue)
        {
            var g = s.GpuTemp.Value;
            GpuTempVal.Text = $"{(int)g}°";
            GpuTempBar.Value = Math.Min(105, g);
            GpuTempBar.Foreground = g >= 80 ? Red : g >= 70 ? Yellow : Green;
        }
        else
        {
            GpuTempVal.Text = "--°"; GpuTempBar.Value = 0;
        }

        // ---- 磁盘（全部固定盘合计，与 PS 版一致；健康度评分仍用系统盘）----
        DiskFree.Text = $"{(int)s.DiskAllFreeGB} GB";
        DiskBadge.Text = Lang.F("disk.total", (int)s.DiskAllTotGB);
        DiskBar.Value = s.DiskAllPct;
        DiskSub.Text = Lang.F("disk.sub", ((int)(s.DiskAllTotGB - s.DiskAllFreeGB)).ToString(), ((int)s.DiskAllTotGB).ToString())
            + (string.IsNullOrEmpty(s.DiskDetail) ? "" : " · " + s.DiskDetail);

        // ---- 网络 ----
        NetSpeed.Text = Format.NetRate(s.NetUp + s.NetDown);
        var badge = string.IsNullOrEmpty(s.NetName) ? "" : Trim(s.NetName, 14);
        NetBadge.Text = badge;
        NetSub.Text = $"↑{Format.NetRate(s.NetUp)} ↓{Format.NetRate(s.NetDown)} · {(string.IsNullOrEmpty(s.NetName) ? Lang.T("net.none") : s.NetName)}";
        Push(_netHist, s.NetUp + s.NetDown, 60);
        DrawLine(NetLine, NetChart, _netHist, Math.Max(1024, _netHist.Max() == 0 ? 1024 : _netHist.Max()));

        // ---- 电源 ----
        HighlightPower(_metrics.PowerGuid);

        // ---- 健康度（与 PS 版同一套扣分权重） ----
        double score = 100; string note = "";
        if (s.DiskFreeGB < 50) score -= (50 - s.DiskFreeGB) / 30 * 15;
        if (s.DiskPct > 80) score -= (s.DiskPct - 80) / 20 * 10;
        if (s.DiskFreeGB < 10) note = Lang.T("health.note.disk.critical");
        else if (s.DiskFreeGB < 20) note = Lang.T("health.note.disk.low");
        else if (s.DiskPct >= 90) note = Lang.T("health.note.disk.veryhigh");
        else if (s.DiskPct >= 80) note = Lang.T("health.note.disk.high");
        score -= Math.Max(0, s.MemPct - 70) / 25 * 15;
        if (s.MemPct >= 85 && note == "") note = Lang.T("health.note.mem");
        var cpuAvg = _cpuHist.Count > 0 ? _cpuHist.Average() : s.Cpu;
        score -= Math.Max(0, cpuAvg - 55) / 35 * 15;
        if (cpuAvg > 70 && note == "") note = Lang.T("health.note.cpu");
        if (s.CpuTemp.HasValue && s.CpuTemp.Value >= 80) { score -= 10; if (note == "") note = Lang.T("health.note.temp"); }
        if (s.BootTime.HasValue) _boot = s.BootTime;
        if (s.AppStart.HasValue) _appStart = s.AppStart;
        if (_appStart.HasValue)
            HealthUp.Text = Lang.F("health.uptime", Format.UpTime(DateTime.Now - _appStart.Value), _appStart.Value.ToString("M/d HH:mm"));
        if (_boot.HasValue && (DateTime.Now - _boot.Value).TotalHours > 168)
        { score -= 5; if (note == "") note = Lang.T("health.note.uptime"); }
        score = Math.Clamp(Math.Round(score), 5, 100);
        HealthScore.Text = $"{(int)score}";
        var (hw2, hc) = score >= 85 ? (Lang.T("health.verygood"), Green) : score >= 70 ? (Lang.T("health.good"), Green) : score >= 50 ? (Lang.T("health.fair"), Yellow) : (Lang.T("health.poor"), Red);
        HealthWord.Text = hw2; HealthWord.Foreground = hc;
        HealthScore.Foreground = score < 50 ? Red : score < 85 ? Yellow : Green;
        HealthNote.Text = note;
    }

    private static string Trim(string s, int len) => string.IsNullOrEmpty(s) ? s : (s.Length > len ? s[..len] + "…" : s);

    private static void Push(Queue<double> q, double v, int cap)
    {
        q.Enqueue(v);
        while (q.Count > cap) q.Dequeue();
    }

    private static void DrawBars(Canvas c, Queue<double> hist, string colorHex)
    {
        if (c.ActualWidth < 10) return;
        c.Children.Clear();
        var arr = hist.ToArray();
        int n = arr.Length; if (n < 1) return;
        double gap = 2, bw = Math.Max(2, (c.ActualWidth - (n - 1) * gap) / n);
        var color = (Color)ColorConverter.ConvertFromString(colorHex);
        for (int i = 0; i < n; i++)
        {
            var v = Math.Clamp(arr[i], 2, 100);
            var h = Math.Max(2.0, c.ActualHeight * v / 100);
            var r = new Rectangle { Width = bw, Height = h, Fill = new SolidColorBrush(color), RadiusX = 1, RadiusY = 1 };
            c.Children.Add(r);
            Canvas.SetLeft(r, i * (bw + gap));
            Canvas.SetTop(r, c.ActualHeight - h);
        }
    }

    private static void DrawLine(Polyline line, Canvas c, Queue<double> hist, double max)
    {
        if (c.ActualWidth < 10) return;
        var arr = hist.ToArray();
        int n = arr.Length; if (n < 2) return;
        var pts = new PointCollection(n);
        for (int i = 0; i < n; i++)
        {
            double x = c.ActualWidth * i / (n - 1);
            double y = c.ActualHeight - Math.Clamp(arr[i] / max, 0, 1) * c.ActualHeight;
            pts.Add(new Point(x, Math.Max(1, y)));
        }
        line.Points = pts;
    }

    private void HighlightPower(string guid)
    {
        if (!string.IsNullOrEmpty(guid))
        {
            Highlight(PwrSaver, guid.Equals(PowerSchemes[0].guid, StringComparison.OrdinalIgnoreCase));
            Highlight(PwrBalanced, guid.Equals(PowerSchemes[1].guid, StringComparison.OrdinalIgnoreCase));
            Highlight(PwrHigh, guid.Equals(PowerSchemes[2].guid, StringComparison.OrdinalIgnoreCase));
            var scheme = PowerSchemes.FirstOrDefault(x => x.guid.Equals(guid, StringComparison.OrdinalIgnoreCase));
            PowerName.Text = scheme.key == null ? guid : Lang.T(scheme.key + ".short");
        }
    }

    private void Highlight(Button b, bool on)
    {
        b.Background = on ? (System.Windows.Media.Brush)FindResource("BrushAccent") : null;
        b.Foreground = on
            ? Services.Theme.FindBrush("BrushOnAccent")
            : (System.Windows.Media.Brush)FindResource("BrushText2");
        b.FontWeight = on ? FontWeights.Bold : FontWeights.Normal;
    }

    private void OnPowerSaver(object sender, RoutedEventArgs e) => SetPower("a1841308-3541-4fab-b81a-f91556f20b7a");
    private void OnPowerBalanced(object sender, RoutedEventArgs e) => SetPower("381b4222-f694-41f0-9685-ff5bb260df2e");
    private void OnPowerHigh(object sender, RoutedEventArgs e) => SetPower("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");

    // ---------- 快速操作（参考微软电脑管家：立即加速大按钮 + 2×2 宫格） ----------

    /// <summary>立即加速：修剪全进程工作集释放内存（不结束进程），完成后弹结果。</summary>
    private async void OnBoost(object sender, RoutedEventArgs e)
    {
        BtnBoost.IsEnabled = false;
        try
        {
            var (freedGB, trimmed, total) = await System.Threading.Tasks.Task.Run(Services.BoostService.Run);
            var dlg = new BoostResultDialog(freedGB, trimmed, total) { Owner = Window.GetWindow(this) };
            dlg.ShowDialog();
        }
        finally { BtnBoost.IsEnabled = true; }
    }

    /// <summary>全面体检：基于最新快照 + 注册表/目录扫描的逐项检查报告。</summary>
    private void OnCheckup(object sender, RoutedEventArgs e)
    {
        if (_last == null) return;   // 首个快照未到（约 2 秒），忽略
        var dlg = new CheckupDialog(_last, _boot) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    /// <summary>进程管理：全量进程对话框（搜索 / 结束）。</summary>
    private void OnProcs(object sender, RoutedEventArgs e)
    {
        var dlg = new ProcDialog(_metrics) { Owner = Window.GetWindow(this) };
        dlg.ShowDialog();
    }

    /// <summary>深度清理：跳转清理页。</summary>
    private void OnClean(object sender, RoutedEventArgs e) => GoPage("clean");

    /// <summary>开机管理：跳转软件页（启动项区）。</summary>
    private void OnStartup(object sender, RoutedEventArgs e) => GoPage("soft");

    /// <summary>一键优化：跳转优化页。</summary>
    private void OnOpt(object sender, RoutedEventArgs e) => GoPage("opt");

    /// <summary>系统分析：跳转分析页。</summary>
    private void OnAna(object sender, RoutedEventArgs e) => GoPage("ana");

    private void GoPage(string key) => (Window.GetWindow(this) as MainWindow)?.Navigate(key);

    private static void SetPower(string guid)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("powercfg.exe", $"/setactive {guid}")
            { CreateNoWindow = true, UseShellExecute = false };
            System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
        }
        catch { }
    }
}
