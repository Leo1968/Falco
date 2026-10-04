using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Falco.App.Models;
using Falco.App.Services;

namespace Falco.App.Views;

/// <summary>对话框公共底座：暗色主题、应用字体，与主窗口一致。</summary>
public class FalcoDialog : Window
{
    protected static T Res<T>(string key) where T : class => (T)Application.Current.TryFindResource(key);

    protected FalcoDialog(string title, double w, double h)
    {
        Title = title;
        Width = w; Height = h;
        MinWidth = w * 0.8; MinHeight = h * 0.7;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Res<SolidColorBrush>("BrushWindow");
        Foreground = Res<SolidColorBrush>("BrushText");
        FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI");
        FontSize = 12;
    }

    protected static TextBlock Txt(string s, double size, Brush fg, bool bold = false) => new()
    { Text = s, FontSize = size, Foreground = fg, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap };

    protected static TextBlock AddTxt(Panel p, string s, double size, Brush fg, bool bold = false, double mt = 0, double ml = 0, double mb = 0, double mr = 0)
    {
        var t = Txt(s, size, fg, bold);
        t.Margin = new Thickness(ml, mt, mr, mb);
        p.Children.Add(t);
        return t;
    }

    protected static Button ActBtn(string content, RoutedEventHandler onClick)
    {
        var b = new Button { Content = content, Style = Res<Style>("DlgBtn"), FontSize = 12 };
        b.Click += onClick;
        return b;
    }
}

/// <summary>立即加速结果。</summary>
public class BoostResultDialog : FalcoDialog
{
    public BoostResultDialog(double freedGB, int trimmed, int total) : base(Lang.T("dlg.boost.title"), 400, 324)
    {
        var green = Res<SolidColorBrush>("BrushGreen");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var sp = new StackPanel { Margin = new Thickness(28, 18, 28, 16) };

        // 星舰发射动态图标（替代 🚀 emoji）：火焰闪烁 + 舰体浮动；LayoutTransform 缩到 30%（约 36×40）
        var rocket = new StarshipIcon
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 4),
            LayoutTransform = new ScaleTransform(0.3, 0.3),
        };
        sp.Children.Add(rocket);
        AddTxt(sp, Lang.T("dlg.boost.done"), 18, Res<SolidColorBrush>("BrushText"), true, mt: 6);
        AddTxt(sp, Lang.F("dlg.boost.freed", freedGB.ToString("0.##")), 14, green, mt: 10);
        AddTxt(sp, freedGB < 0.05
            ? Lang.T("dlg.boost.fine")
            : Lang.F("dlg.boost.detail", trimmed, total), 11, fg2, mt: 8);
        AddTxt(sp, Lang.T("dlg.boost.note"), 10, fg2, mt: 6);

        var ok = ActBtn(Lang.T("common.ok"), (_, __) => Close());
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.MinWidth = 96;
        ok.Margin = new Thickness(0, 14, 0, 0);
        sp.Children.Add(ok);
        Content = sp;
    }
}

/// <summary>全面体检：逐项检查（磁盘/内存/CPU/温度/开机/启动项/临时文件）+ 综合分（与状态页同权重）。</summary>
public class CheckupDialog : FalcoDialog
{
    private sealed record Item(string Glyph, Brush Color, string Name, string Detail);

    private readonly MetricSnapshot _s;
    private readonly DateTime? _boot;
    private readonly StackPanel _list = new();

    public CheckupDialog(MetricSnapshot s, DateTime? boot) : base(Lang.T("dlg.checkup.title"), 580, 560)
    {
        _s = s; _boot = boot;
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var root = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };

        // 底部操作条
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom);
        var b1 = ActBtn(Lang.T("checkup.goclean"), (_, __) => Go("clean")); b1.Margin = new Thickness(0, 0, 8, 0);
        var b2 = ActBtn(Lang.T("checkup.gostartup"), (_, __) => Go("soft")); b2.Margin = new Thickness(0, 0, 8, 0);
        var b3 = ActBtn(Lang.T("common.close"), (_, __) => Close());
        footer.Children.Add(b1); footer.Children.Add(b2); footer.Children.Add(b3);

        // 头部：综合分
        var head = new StackPanel();
        DockPanel.SetDock(head, Dock.Top);
        AddTxt(head, "🩺 " + Lang.T("qa.checkup"), 15, Res<SolidColorBrush>("BrushText"), true);
        var scoreLine = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 12) };
        var score = Score();
        var scoreT = Txt($"{score:0}", 34, ScoreBrush(score), true);
        var wordT = Txt(Word(score), 13, ScoreBrush(score), true);
        wordT.Margin = new Thickness(12, 0, 0, 0);
        wordT.VerticalAlignment = VerticalAlignment.Center;
        scoreLine.Children.Add(scoreT);
        scoreLine.Children.Add(wordT);
        head.Children.Add(scoreLine);
        AddTxt(head, Lang.T("checkup.intro"), 10, fg2);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 8, 0, 0) };
        scroll.Content = _list;
        root.Children.Add(footer);
        root.Children.Add(head);
        root.Children.Add(scroll);
        Content = root;
        Loaded += (_, __) => RunChecks();
    }

    private static Brush ScoreBrush(double score) => score >= 70
        ? Res<SolidColorBrush>("BrushGreen")
        : score >= 50 ? Res<SolidColorBrush>("BrushYellow") : Res<SolidColorBrush>("BrushRed");

    private void Go(string key)
    {
        (Owner as MainWindow)?.Navigate(key);
        Close();
    }

    /// <summary>综合分：与状态页健康度同一套扣分权重（基于当前快照，CPU 用瞬时值）。</summary>
    private double Score()
    {
        double score = 100;
        if (_s.DiskFreeGB < 50) score -= (50 - _s.DiskFreeGB) / 30 * 15;
        if (_s.DiskPct > 80) score -= (_s.DiskPct - 80) / 20 * 10;
        score -= Math.Max(0, _s.MemPct - 70) / 25 * 15;
        score -= Math.Max(0, _s.Cpu - 55) / 35 * 15;
        if (_s.CpuTemp.HasValue && _s.CpuTemp.Value >= 80) score -= 10;
        if (_boot.HasValue && (DateTime.Now - _boot.Value).TotalHours > 168) score -= 5;
        return Math.Clamp(Math.Round(score), 5, 100);
    }

    private static string Word(double score)
        => score >= 85 ? Lang.T("health.verygood") : score >= 70 ? Lang.T("health.good") : score >= 50 ? Lang.T("health.fair") : Lang.T("health.poor");

    private void RunChecks()
    {
        var ok = Res<SolidColorBrush>("BrushGreen");
        var warn = Res<SolidColorBrush>("BrushYellow");
        var bad = Res<SolidColorBrush>("BrushRed");
        var dim = Res<SolidColorBrush>("BrushText2");
        var items = new List<Item>
        {
            _s.DiskFreeGB switch
            {
                < 10 => new("✕", bad, Lang.T("checkup.disk"), Lang.F("checkup.disk.crit", _s.DiskFreeGB.ToString("0.#"))),
                < 50 => new("⚠", warn, Lang.T("checkup.disk"), Lang.F("checkup.disk.low", _s.DiskFreeGB.ToString("0.#"))),
                _ => new("✓", ok, Lang.T("checkup.disk"), Lang.F("checkup.disk.ok", _s.DiskFreeGB.ToString("0.#"))),
            },
            _s.DiskPct >= 90 ? new("✕", bad, Lang.T("checkup.diskpct"), Lang.F("checkup.diskpct.full", (int)_s.DiskPct))
                : _s.DiskPct >= 80 ? new("⚠", warn, Lang.T("checkup.diskpct"), Lang.F("checkup.diskpct.high", (int)_s.DiskPct))
                : new("✓", ok, Lang.T("checkup.diskpct"), Lang.F("checkup.diskpct.ok", (int)_s.DiskPct)),
            _s.MemPct >= 85 ? new("⚠", warn, Lang.T("checkup.mem"), Lang.F("checkup.mem.high", (int)_s.MemPct, _s.MemUsedGB.ToString("0.#"), _s.MemTotGB.ToString("0.#")))
                : _s.MemPct >= 75 ? new("⚠", warn, Lang.T("checkup.mem"), Lang.F("checkup.mem.mid", (int)_s.MemPct))
                : new("✓", ok, Lang.T("checkup.mem"), Lang.F("checkup.mem.ok", (int)_s.MemPct, _s.MemUsedGB.ToString("0.#"), _s.MemTotGB.ToString("0.#"))),
            _s.Cpu >= 70 ? new("⚠", warn, Lang.T("checkup.cpu"), Lang.F("checkup.cpu.high", (int)_s.Cpu, _s.ProcCount))
                : new("✓", ok, Lang.T("checkup.cpu"), Lang.F("checkup.cpu.ok", (int)_s.Cpu, _s.ProcCount)),
            !_s.CpuTemp.HasValue ? new("·", dim, Lang.T("checkup.temp"), Lang.T("checkup.temp.na"))
                : _s.CpuTemp >= 80 ? new("⚠", warn, Lang.T("checkup.temp"), Lang.F("checkup.temp.high", (int)_s.CpuTemp))
                : new("✓", ok, Lang.T("checkup.temp"), Lang.F("checkup.temp.ok", (int)_s.CpuTemp)),
            !_boot.HasValue ? new("·", dim, Lang.T("checkup.uptime"), Lang.T("checkup.uptime.na"))
                : (DateTime.Now - _boot.Value).TotalHours > 168 ? new("⚠", warn, Lang.T("checkup.uptime"), Lang.F("checkup.uptime.long", Format.UpTime(DateTime.Now - _boot.Value)))
                : new("✓", ok, Lang.T("checkup.uptime"), Lang.F("checkup.uptime.ok", Format.UpTime(DateTime.Now - _boot.Value))),
        };
        Render(items);

        // 慢项（注册表/目录扫描）放后台，完成后追加
        _ = Task.Run(() =>
        {
            var extra = new List<Item>();
            try
            {
                var startups = SoftwareService.GetStartups().Count;
                extra.Add(startups > 15
                    ? new Item("⚠", warn, Lang.T("checkup.startup"), Lang.F("checkup.startup.many", startups))
                    : new Item("✓", ok, Lang.T("checkup.startup"), Lang.F("checkup.startup.ok", startups)));
            }
            catch (Exception ex) { extra.Add(new Item("·", dim, Lang.T("checkup.startup"), Lang.F("checkup.readfail", ex.Message))); }
            try
            {
                var temp = CleanService.TempMB();
                extra.Add(temp >= 500
                    ? new Item("⚠", warn, Lang.T("checkup.tempfiles"), Lang.F("checkup.tempfiles.many", temp.ToString("0.#")))
                    : new Item("✓", ok, Lang.T("checkup.tempfiles"), Lang.F("checkup.tempfiles.ok", temp.ToString("0.#"))));
            }
            catch (Exception ex) { extra.Add(new Item("·", dim, Lang.T("checkup.tempfiles"), Lang.F("checkup.readfail", ex.Message))); }
            try
            {
                var wu = CleanService.WuMB();
                extra.Add(wu >= 1024
                    ? new Item("⚠", warn, Lang.T("checkup.wu"), Lang.F("checkup.wu.many", wu.ToString("0.#")))
                    : new Item("✓", ok, Lang.T("checkup.wu"), Lang.F("checkup.wu.ok", wu.ToString("0.#"))));
            }
            catch { }
            try
            {
                var tweaks = new TweakEngine().DetectAll();
                var pending = tweaks.Count(r => r.State == Lang.T("opt.state.default"));
                extra.Add(pending > 0
                    ? new Item("⚠", warn, Lang.T("checkup.tweaks"), Lang.F("checkup.tweaks.many", pending))
                    : new Item("✓", ok, Lang.T("checkup.tweaks"), Lang.T("checkup.tweaks.ok")));
            }
            catch (Exception ex) { extra.Add(new Item("·", dim, Lang.T("checkup.tweaks"), Lang.F("checkup.readfail", ex.Message))); }
            Dispatcher.BeginInvoke(() => Render(extra));
        });
    }

    private void Render(IEnumerable<Item> items)
    {
        var fg2 = Res<SolidColorBrush>("BrushText2");
        foreach (var it in items)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 9) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var g = Txt(it.Glyph, 13, it.Color, true);
            g.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(g, 0); row.Children.Add(g);
            var n = Txt(it.Name, 12, Res<SolidColorBrush>("BrushText"));
            n.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(n, 1); row.Children.Add(n);
            var d = Txt(it.Detail, 11, fg2);
            d.VerticalAlignment = VerticalAlignment.Top;
            Grid.SetColumn(d, 2); row.Children.Add(d);
            _list.Children.Add(row);
        }
        var line = new Border { Height = 1, Background = Res<SolidColorBrush>("BrushCardBorder"), Margin = new Thickness(0, 2, 0, 11) };
        _list.Children.Add(line);
    }
}

/// <summary>进程管理：全量进程（CPU 双采样）/ 搜索 / 单行结束（关键系统进程保护）。</summary>
public class ProcDialog : FalcoDialog
{
    private sealed record ProcRow(string Name, int Pid, double Cpu, double MemMB, string? Host = null)
    {
        public bool Protected => ProtectedNames.Contains(Name.ToLowerInvariant());
    }

    /// <summary>内嵌浏览器组件（WebView2 等）：一个宿主派生多实例，行内标注宿主应用。比对用去 .exe 规范名（ProcessName 无扩展名，WMI Name 有）。</summary>
    private static readonly HashSet<string> EmbedderNames = new(StringComparer.OrdinalIgnoreCase) { "msedgewebview2" };

    private static string NormProc(string n)
        => (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n).ToLowerInvariant();

    private static readonly HashSet<string> ProtectedNames = new()
    { "system", "idle", "registry", "csrss", "smss", "wininit", "winlogon", "services", "lsass", "lsaiso", "dwm", "fontdrvhost" };

    private readonly TextBox _search = new();
    private readonly StackPanel _rows = new();
    private static TextBlock HintInit() => Txt(Lang.T("proc.sampling"), 11, (SolidColorBrush)Application.Current!.TryFindResource("BrushText2"));
    private readonly TextBlock _hint = HintInit();
    private List<ProcRow> _data = new();
    private bool _busy;

    private readonly MetricsService _metrics;

    public ProcDialog(MetricsService metrics) : base(Lang.T("dlg.proc.title"), 660, 680)
    {
        _metrics = metrics;
        var root = new DockPanel { Margin = new Thickness(16, 12, 16, 12) };

        // 按 CPU 排序 Top15 概览（自深度清理页迁入），置于搜索栏上方
        var overview = BuildOverview();
        DockPanel.SetDock(overview, Dock.Top);

        // 顶栏：搜索框 + 刷新
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        DockPanel.SetDock(top, Dock.Top);
        var refresh = new Button { Content = Lang.T("proc.refresh"), Style = Res<Style>("DlgBtn"), Margin = new Thickness(8, 0, 0, 0) };
        refresh.Click += (_, __) => Refresh();
        DockPanel.SetDock(refresh, Dock.Right);
        top.Children.Add(refresh);

        // 搜索框：深色描边 + 覆盖提示字
        _search.Height = 32;
        _search.VerticalContentAlignment = VerticalAlignment.Center;
        _search.Background = Res<SolidColorBrush>("BrushCardAlt");
        _search.BorderBrush = Res<SolidColorBrush>("BrushBorder2");
        _search.Foreground = Res<SolidColorBrush>("BrushText");
        _search.CaretBrush = Res<SolidColorBrush>("BrushText");
        _search.TextChanged += (_, __) => Render();
        var hint = new TextBlock { Text = Lang.T("proc.search"), Foreground = Res<SolidColorBrush>("BrushText2"), FontSize = 11 };
        hint.IsHitTestVisible = false;
        hint.VerticalAlignment = VerticalAlignment.Center;
        hint.Margin = new Thickness(6, 0, 0, 0);
        var plate = new Grid();
        plate.Children.Add(_search);
        plate.Children.Add(hint);
        _search.GotFocus += (_, __) => hint.Visibility = Visibility.Collapsed;
        _search.LostFocus += (_, __) => hint.Visibility = string.IsNullOrEmpty(_search.Text) ? Visibility.Visible : Visibility.Collapsed;
        top.Children.Add(plate);

        // 底部提示
        _hint.Margin = new Thickness(0, 8, 0, 0);
        DockPanel.SetDock(_hint, Dock.Bottom);

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        scroll.Content = _rows;
        root.Children.Add(overview);
        root.Children.Add(top);
        root.Children.Add(_hint);
        root.Children.Add(scroll);
        Content = root;
        Loaded += (_, __) => Refresh();
    }

    // ===== 按 CPU 排序 Top15 概览（自深度清理页迁入） =====

    /// <summary>概览卡：标题 + 汇总 + 快照 Top15 进程行（随 SnapshotReady 刷新）。</summary>
    private FrameworkElement BuildOverview()
    {
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");

        var sum = Txt(Lang.T("proc.sampling"), 11, fg2);
        sum.VerticalAlignment = VerticalAlignment.Center;
        var head = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
        var title = Txt(Lang.T("clean.proc"), 13, fg, bold: true);
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(sum);
        head.Children.Add(title);

        var list = new StackPanel();
        var card = new Border
        {
            Background = Res<SolidColorBrush>("BrushCard"),
            BorderBrush = Res<SolidColorBrush>("BrushCardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(16, 14, 16, 14),
            Margin = new Thickness(0, 0, 0, 10),
        };
        var body = new StackPanel();
        body.Children.Add(head);
        body.Children.Add(list);
        card.Child = body;

        Action<Models.MetricSnapshot> handler = null;
        handler = s => Dispatcher.BeginInvoke(() =>
        {
            list.Children.Clear();
            foreach (var p in s.TopProcs) list.Children.Add(TopRow(p, fg, fg2));
            sum.Text = Lang.F("clean.procs.summary", s.ProcCount);
        });
        _metrics.SnapshotReady += handler;
        Closed += (_, __) => _metrics.SnapshotReady -= handler;
        return card;
    }

    /// <summary>概览行：呼吸点 + 图标 + 名称 | PID | CPU 条 | 内存。</summary>
    private FrameworkElement TopRow(Models.ProcInfo p, SolidColorBrush fg, SolidColorBrush fg2)
    {
        var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

        var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dot = new System.Windows.Shapes.Ellipse { Width = 7, Height = 7, Fill = p.DotBrush, Margin = new Thickness(4, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center };
        dot.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.25, 1.0, new Duration(TimeSpan.FromMilliseconds(700))) { AutoReverse = true, RepeatBehavior = System.Windows.Media.Animation.RepeatBehavior.Forever });
        name.Children.Add(dot);
        if (p.Icon != null) name.Children.Add(new Image { Width = 16, Height = 16, Source = p.Icon, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        var t = Txt(p.Name, 12, fg);
        t.TextTrimming = TextTrimming.CharacterEllipsis;
        t.VerticalAlignment = VerticalAlignment.Center;
        name.Children.Add(t);
        Grid.SetColumn(name, 0); g.Children.Add(name);

        var pid = Txt(p.Pid.ToString(), 11, fg2);
        pid.VerticalAlignment = VerticalAlignment.Center;
        pid.TextAlignment = TextAlignment.Right;
        Grid.SetColumn(pid, 1); g.Children.Add(pid);

        var cpu = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        cpu.Children.Add(new System.Windows.Controls.ProgressBar { Width = 48, Height = 7, Value = p.CpuPct, VerticalAlignment = VerticalAlignment.Center });
        var ct = Txt(p.CpuText, 11, fg2);
        ct.Margin = new Thickness(8, 0, 0, 0);
        ct.MinWidth = 42;
        ct.TextAlignment = TextAlignment.Right;
        ct.VerticalAlignment = VerticalAlignment.Center;
        cpu.Children.Add(ct);
        Grid.SetColumn(cpu, 2); g.Children.Add(cpu);

        var mem = Txt(p.MemText, 11, fg);
        mem.TextAlignment = TextAlignment.Right;
        mem.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(mem, 3); g.Children.Add(mem);
        return g;
    }

    private static Dictionary<int, (string name, double memMB, TimeSpan cpu)> TakeSample()
    {
        var map = new Dictionary<int, (string, double, TimeSpan)>();
        foreach (var p in Process.GetProcesses())
        {
            try { map[p.Id] = (p.ProcessName, p.WorkingSet64 / 1048576.0, p.TotalProcessorTime); }
            catch { /* 受保护进程读不到 TotalProcessorTime，跳过 */ }
            finally { try { p.Dispose(); } catch { } }
        }
        return map;
    }

    /// <summary>内嵌浏览器进程 → 宿主友好名：沿父进程链上溯至第一个非内嵌进程（WMI 一次全量查询，失败静默不标注）。</summary>
    private static Dictionary<int, string> BuildHostMap(IEnumerable<ProcRow> rows)
    {
        var map = new Dictionary<int, string>();
        try
        {
            var embedders = rows.Where(r => EmbedderNames.Contains(NormProc(r.Name))).Select(r => r.Pid).ToHashSet();
            if (embedders.Count == 0) return map;
            var all = new Dictionary<int, (string name, int parent)>();
            using var searcher = new System.Management.ManagementObjectSearcher(
                "SELECT ProcessId, ParentProcessId, Name FROM Win32_Process");
            foreach (var o in searcher.Get())
            {
                try { all[(int)(uint)o["ProcessId"]] = ((string)o["Name"], (int)(uint)o["ParentProcessId"]); }
                finally { o.Dispose(); }
            }
            foreach (var pid in embedders)
            {
                var cur = pid;
                for (var guard = 0; guard < 8 && all.TryGetValue(cur, out var info); guard++)
                {
                    if (EmbedderNames.Contains(NormProc(info.name))) { if (info.parent != pid) cur = info.parent; continue; }
                    map[pid] = FriendlyHost(info.name);
                    break;
                }
            }
        }
        catch { /* WMI 不可用时不标注宿主 */ }
        return map;
    }

    /// <summary>宿主进程名 → 友好显示名（专有名词直译，其余去掉 .exe）。</summary>
    private static string FriendlyHost(string exe)
    {
        var n = exe.ToLowerInvariant();
        if (n == "searchhost.exe") return Lang.T("host.search");
        if (n == "widgets.exe") return Lang.T("host.widgets");
        if (n == "bingwallpaper.exe") return Lang.T("host.bingwallpaper");
        if (n == "wechatappex.exe") return Lang.T("host.wechat");
        if (n.StartsWith("clash-verge")) return "Clash Verge";
        return n.EndsWith(".exe") ? exe[..^4] : exe;
    }

    private async void Refresh()
    {
        if (_busy) return;
        _busy = true;
        _hint.Text = Lang.T("proc.sampling");
        _rows.Children.Clear();
        try
        {
            AppEnv.Log("ProcDialog: refresh start");
            var a = await Task.Run(TakeSample);
            AppEnv.Log("ProcDialog: sample A done");
            await Task.Delay(700);
            var b = await Task.Run(TakeSample);
            AppEnv.Log("ProcDialog: sample B done");
            var cores = Math.Max(1, Environment.ProcessorCount);
            var list = new List<ProcRow>();
            foreach (var pid in b.Keys)
            {
                var cur = b[pid];
                double cpu = 0;
                if (a.TryGetValue(pid, out var prev))
                    cpu = Math.Clamp((cur.cpu - prev.cpu).TotalSeconds / 0.7 / cores * 100, 0, 100);
                list.Add(new ProcRow(cur.name, pid, cpu, cur.memMB));
            }
            if (list.Any(r => EmbedderNames.Contains(NormProc(r.Name))))
            {
                var hosts = await Task.Run(() => BuildHostMap(list));
                if (hosts.Count > 0)
                    for (var i = 0; i < list.Count; i++)
                        if (hosts.TryGetValue(list[i].Pid, out var h))
                            list[i] = list[i] with { Host = h };
            }
            _data = list.OrderByDescending(r => r.MemMB).ToList();
            _hint.Text = Lang.F("proc.summary", _data.Count);
            Render();
        }
        catch (Exception ex)
        {
            _hint.Text = Lang.F("proc.readfail", ex.Message);
        }
        finally { _busy = false; }
    }

    private void Render()
    {
        _rows.Children.Clear();
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var yellow = Res<SolidColorBrush>("BrushYellow");
        var hover = Res<SolidColorBrush>("BrushCardAltHover");
        var transparent = Brushes.Transparent;
        var q = _search.Text?.Trim() ?? "";
        foreach (var r in _data.Where(r => string.IsNullOrEmpty(q) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase)))
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(88) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

            var name = new TextBlock { FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            name.Inlines.Add(new Run(r.Name));
            if (!string.IsNullOrEmpty(r.Host))
            {
                name.Inlines.Add(new Run("  ·  " + r.Host) { FontSize = 11, Foreground = fg2 });
                name.ToolTip = Lang.F("proc.host.tip", r.Host);
            }
            Grid.SetColumn(name, 0); row.Children.Add(name);
            var pid = Txt($"#{r.Pid}", 11, fg2);
            pid.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(pid, 1); row.Children.Add(pid);
            var cpu = Txt($"{r.Cpu:0.0}%", 11, r.Cpu > 30 ? yellow : fg2);
            cpu.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(cpu, 2); row.Children.Add(cpu);
            var mem = Txt($"{r.MemMB:0} MB", 11, fg);
            mem.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(mem, 3); row.Children.Add(mem);

            var kill = new Button
            {
                Content = Lang.T("proc.kill"), FontSize = 10, Padding = new Thickness(8, 2, 8, 2), Cursor = Cursors.Hand,
                Foreground = Res<SolidColorBrush>("BrushRed"),
            };
            kill.SetResourceReference(StyleProperty, "DlgBtn");
            if (r.Protected) { kill.IsEnabled = false; kill.ToolTip = Lang.T("proc.protected.tip"); }
            var pid_ = r.Pid; var name_ = r.Name;
            kill.Click += (_, __) => Kill(pid_, name_);
            Grid.SetColumn(kill, 4); row.Children.Add(kill);

            var host = new Border { Child = row, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5) };
            host.Background = transparent;
            host.MouseEnter += (_, __) => host.Background = hover;
            host.MouseLeave += (_, __) => host.Background = transparent;
            _rows.Children.Add(host);
        }
    }

    private void Kill(int pid, string name)
    {
        if (MessageBox.Show(this, Lang.F("proc.kill.confirm", name, pid) + "\n" + Lang.T("proc.kill.confirm2"), Lang.T("proc.kill.title"),
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            using var p = Process.GetProcessById(pid);
            p.Kill(entireProcessTree: true);
            AppEnv.Log($"进程管理：结束 {name} (#{pid})");
            _hint.Text = Lang.F("proc.ended", name, pid);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, Lang.F("proc.kill.fail", ex.Message), Lang.T("proc.kill.title"), MessageBoxButton.OK, MessageBoxImage.Error);
        }
        Refresh();
    }
}

/// <summary>隼徽菜单 → 设置：语言 / 主题 / 开机自启（HKCU Run 键）。分段按钮即时生效。</summary>
public class SettingsDialog : FalcoDialog
{
    private static readonly string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    private readonly List<(Button btn, Func<bool> isActive)> _segs = new();

    public SettingsDialog() : base(Lang.T("dlg.settings.title"), 380, 300)
    {
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var sp = new StackPanel { Margin = new Thickness(28, 24, 28, 16) };
        AddTxt(sp, Lang.T("dlg.settings.title"), 16, fg, true, mb: 18);

        AddRow(sp, Lang.T("settings.lang"), ("中文", "English"),
            () => Lang.Current == "zh", () => { if (Lang.Current != "zh") Lang.Toggle(); RefreshSegs(); });
        AddRow(sp, Lang.T("settings.theme"), (Lang.T("settings.theme.dark"), Lang.T("settings.theme.light")),
            () => Theme.Current == "dark", () => { if (Theme.Current != "dark") Theme.Toggle(); RefreshSegs(); });
        AddRow(sp, Lang.T("settings.autostart"), (Lang.T("settings.on"), Lang.T("settings.off")),
            () => GetAutostart(), () => { SetAutostart(!GetAutostart()); RefreshSegs(); });

        var ok = ActBtn(Lang.T("common.ok"), (_, __) => Close());
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.MinWidth = 96;
        ok.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(ok);
        Content = sp;
    }

    /// <summary>一行设置：左标签 + 右侧两枚互斥分段钮（当前项琥珀高亮）。</summary>
    private void AddRow(StackPanel sp, string label, (string a, string b) opts,
                        Func<bool> isFirstActive, Action onPick)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 14) };
        var lab = Txt(label, 12.5, Res<SolidColorBrush>("BrushText"));
        lab.VerticalAlignment = VerticalAlignment.Center;
        lab.Width = 110;
        row.Children.Add(lab);

        var segs = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < 2; i++)
        {
            var idx = i;
            var b = new Button
            {
                Content = idx == 0 ? opts.a : opts.b,
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = false,
                FontSize = 12,
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(0, 0, idx == 0 ? 2 : 0, 0)
            };
            var tpl = new ControlTemplate(typeof(Button));
            var f = new FrameworkElementFactory(typeof(Border), "bd");
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            f.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            f.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.AppendChild(cp);
            tpl.VisualTree = f;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.82));
            tpl.Triggers.Add(hover);
            b.Template = tpl;
            b.Click += (_, __) => onPick();
            _segs.Add((b, () => idx == 0 ? isFirstActive() : !isFirstActive()));
            segs.Children.Add(b);
        }
        row.Children.Add(segs);
        sp.Children.Add(row);
    }

    private void RefreshSegs()
    {
        var accent = Res<SolidColorBrush>("BrushAccent");
        var onAcc = new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x09));
        var capsule = Res<SolidColorBrush>("BrushCapsule");
        var fg = Res<SolidColorBrush>("BrushText");
        foreach (var (btn, isActive) in _segs)
        {
            var active = isActive();
            btn.Background = active ? accent : capsule;
            btn.Foreground = active ? onAcc : fg;
            btn.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        RefreshSegs();
    }

    internal static bool GetAutostart()
    {
        try { return Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKeyPath)?.GetValue("Falco") is string; }
        catch { return false; }
    }

    private static void SetAutostart(bool on)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKeyPath);
            if (on && !string.IsNullOrEmpty(Environment.ProcessPath))
                key.SetValue("Falco", $"\"{Environment.ProcessPath}\"");
            else
                key.DeleteValue("Falco", throwOnMissingValue: false);
            AppEnv.Log($"开机自启：{(on ? "开" : "关")}");
        }
        catch (Exception ex) { AppEnv.Log($"开机自启设置失败：{ex.Message}", "WARN"); }
    }
}

/// <summary>隼徽菜单 → 关于 Falco：版本信息 + Skywalker Labs 官网链接。</summary>
public class AboutDialog : FalcoDialog
{
    public AboutDialog() : base(Lang.T("dlg.about.title"), 400, 330)
    {
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var accent = Res<SolidColorBrush>("BrushAccent");
        var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 16) };

        var logo = new Image
        {
            Source = Services.BrandAsset.LoadFromIco(),
            Width = 56, Height = 56,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };
        RenderOptions.SetBitmapScalingMode(logo, BitmapScalingMode.HighQuality);
        sp.Children.Add(logo);

        AddTxt(sp, "Falco — Windows Optimizer", 17, fg, true).HorizontalAlignment = HorizontalAlignment.Center;
        AddTxt(sp, Lang.F("about.version", VersionText()), 12.5, fg2).HorizontalAlignment = HorizontalAlignment.Center;

        var pub = new TextBlock
        {
            FontSize = 12.5,
            Foreground = fg2,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 8, 0, 0)
        };
        var link = new System.Windows.Documents.Hyperlink
        {
            Foreground = accent,
            NavigateUri = new Uri("https://www.58begin.com/")
        };
        link.Inlines.Add("Skywalker Labs");
        link.RequestNavigate += (_, e) =>
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.ToString()) { UseShellExecute = true }); }
            catch { }
        };
        var pubSpan = new System.Windows.Documents.Span();
        pubSpan.Inlines.Add("Published by ");
        pubSpan.Inlines.Add(link);
        pub.Inlines.Add(pubSpan);
        sp.Children.Add(pub);

        AddTxt(sp, Lang.T("about.freeware"), 11, fg2).HorizontalAlignment = HorizontalAlignment.Center;

        var ok = ActBtn(Lang.T("common.ok"), (_, __) => Close());
        ok.HorizontalAlignment = HorizontalAlignment.Center;
        ok.MinWidth = 96;
        ok.Margin = new Thickness(0, 16, 0, 0);
        sp.Children.Add(ok);
        Content = sp;
    }

    internal static string VersionText()
    {
        var v = typeof(AboutDialog).Assembly.GetName().Version;
        return v == null ? "0.0.0" : v.ToString(3);
    }
}

/// <summary>隼徽菜单 → 检查更新：连 GitHub Releases 最新 gui-v 标签，比对当前版本。</summary>
public class UpdateDialog : FalcoDialog
{
    public UpdateDialog() : base(Lang.T("dlg.update.title"), 400, 230)
    {
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var sp = new StackPanel { Margin = new Thickness(28, 22, 28, 14) };
        AddTxt(sp, Lang.T("dlg.update.title"), 16, fg, true, mb: 14);

        var msg = Txt(Lang.T("update.checking"), 13, fg2);
        sp.Children.Add(msg);

        var actionRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 16, 0, 0) };
        sp.Children.Add(actionRow);
        Content = sp;

        Loaded += async (_, __) =>
        {
            var info = await Services.UpdateService.CheckAsync();
            if (!IsLoaded) return;
            if (info == null)
            {
                msg.Text = Lang.T("update.fail");
                msg.Foreground = Res<SolidColorBrush>("BrushYellow");
                actionRow.Children.Add(MakeBtn(Lang.T("common.ok"), () => Close()));
                return;
            }
            if (!info.HasNew)
            {
                msg.Text = Lang.F("update.latest", "v" + info.Current);
                msg.Foreground = Res<SolidColorBrush>("BrushGreen");
                actionRow.Children.Add(MakeBtn(Lang.T("common.ok"), () => Close()));
                return;
            }
            msg.Text = Lang.F("update.found", "v" + info.Latest, "v" + info.Current);
            msg.Foreground = Res<SolidColorBrush>("BrushAccent");
            actionRow.Children.Add(MakeBtn(Lang.T("update.go"), () =>
            {
                try { Process.Start(new ProcessStartInfo(info.ReleaseUrl) { UseShellExecute = true }); } catch { }
                Close();
            }));
            var later = MakeBtn(Lang.T("update.later"), () => Close());
            later.Margin = new Thickness(8, 0, 0, 0);
            actionRow.Children.Add(later);
        };
    }

    private Button MakeBtn(string text, Action onClick)
    {
        var b = ActBtn(text, (_, __) => onClick());
        b.MinWidth = 96;
        return b;
    }
}
