using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Falco.App.Services;

namespace Falco.App.Views;

public partial class OptimizePage : UserControl
{
    private readonly TweakEngine _engine = new();
    private bool _admin;
    private bool _loading;


    /// <summary>返回状态主页（导航栏只留状态单入口）。</summary>
    private void OnBack(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("stat");

    public OptimizePage()
    {
        InitializeComponent();
        _admin = AppEnv.IsAdmin();
        if (!_admin)
        {
            AdminHint.Text = Lang.T("opt.noadmin");
            AdminHint.Visibility = Visibility.Visible;
        }
        Refresh();
    }

    /// <summary>异步检测（DetectAll 含 12 次 sc.exe 查询，同步 .Result 会卡 UI 线程）。</summary>
    private async void Refresh()
    {
        if (_loading) return;
        _loading = true;
        TweakList.Children.Clear();
        TweakSummary.Text = Lang.T("common.detecting");
        var rows = await Task.Run(() => _engine.DetectAll());
        int optimized = rows.Count(r => r.State.Contains(Lang.T("opt.state.optimized")) || r.State.Contains(Lang.T("opt.state.set.prefix")));
        int backed = rows.Count(r => r.Backed);
        int drifted = rows.Count(r => r.Drifted);
        TweakSummary.Text = Lang.F("opt.summary", optimized, rows.Count, backed) + (drifted > 0 ? " " + Lang.F("opt.drifted", drifted) : "");
        foreach (var r in rows) TweakList.Children.Add(MakeRow(r));
        _loading = false;
    }

    private Border MakeRow(TweakRow r)
    {
        var stateColor = r.Drifted ? "BrushRed" : r.State.Contains(Lang.T("opt.state.optimized")) || r.State.Contains(Lang.T("opt.state.set.prefix")) ? "BrushGreen" : "BrushText2";
        var grid = new Grid { Margin = new Thickness(0, 1, 0, 0) };
        for (int i = 0; i < 5; i++) grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions[0].Width = new GridLength(70);
        grid.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        grid.ColumnDefinitions[2].Width = new GridLength(190);
        grid.ColumnDefinitions[3].Width = new GridLength(70);
        grid.ColumnDefinitions[4].Width = new GridLength(70);

        var id = new TextBlock { Text = r.Id, FontSize = 11, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = r.Drifted ? $"{r.Name} {Lang.T("opt.state.drifted")}" : r.Name, FontSize = 12, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, ToolTip = Lang.F("opt.tooltip", r.Id, r.Name, RiskDisplay(r.Risk), EngineHint(r.Id)) };
        var state = new TextBlock { Text = r.State, FontSize = 11, Foreground = (Brush)FindResource(stateColor), VerticalAlignment = VerticalAlignment.Center };
        var risk = new TextBlock { Text = RiskDisplay(r.Risk), FontSize = 11, Foreground = (Brush)FindResource(r.Risk == "High" ? "BrushRed" : "BrushText2"), VerticalAlignment = VerticalAlignment.Center };

        var apply = new Button { Style = (Style)FindResource("DlgBtn"), Content = Lang.T("opt.apply"), FontSize = 11, Padding = new Thickness(8, 4, 8, 4), Margin = new Thickness(0, 0, 4, 0), IsEnabled = _admin, Tag = r.Id };
        apply.Click += OnApply;
        var revert = new Button { Style = (Style)FindResource("DlgBtn"), Content = Lang.T("opt.revert"), FontSize = 11, Padding = new Thickness(8, 4, 8, 4), IsEnabled = _admin && r.Backed, Tag = r.Id };
        revert.Click += OnRevert;

        Grid.SetColumn(id, 0); Grid.SetColumn(name, 1); Grid.SetColumn(state, 2); Grid.SetColumn(risk, 3);
        var ops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        ops.Children.Add(apply); ops.Children.Add(revert);
        Grid.SetColumn(ops, 4);
        grid.Children.Add(id); grid.Children.Add(name); grid.Children.Add(state); grid.Children.Add(risk); grid.Children.Add(ops);

        return new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 5, 6, 5), Child = grid };
    }

    // 提示语统一取自语言字典（TweakDef.Hint 同源）；Risk 内部值 High/Low → 显示映射
    private static string EngineHint(string id) => Lang.T("tweak." + id.ToLowerInvariant().Replace('-', '.') + ".hint");
    private static string RiskDisplay(string risk) => Lang.T(risk == "High" ? "opt.risk.high" : "opt.risk.low");

    private void OnApply(object sender, RoutedEventArgs e)
    {
        var id = (string)((Button)sender).Tag;
        if (id is "SYS-001" or "WS-001" &&
            MessageBox.Show(Lang.F("opt.highrisk.confirm", Lang.T("tweak." + id.ToLowerInvariant().Replace('-', '.') + ".name")), "Falco", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        int ahStart = 8, ahEnd = 22;
        if (id == "AH-001")
        {
            var dlg = new ActiveHoursDialog { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() != true) return;
            ahStart = dlg.StartHour; ahEnd = dlg.EndHour;
        }
        Busy(Lang.F("opt.applying", id));
        Task.Run(() =>
        {
            var (pass, fail) = _engine.Apply(id, ahStart, ahEnd);
            Dispatcher.BeginInvoke(() =>
            {
                RunResult.Text = Lang.F("opt.apply.done", pass, fail);
                Refresh();
            });
        });
    }

    private void OnRevert(object sender, RoutedEventArgs e)
    {
        var id = (string)((Button)sender).Tag;
        Busy(Lang.F("opt.reverting", id));
        Task.Run(() =>
        {
            _engine.Revert(id);
            Dispatcher.BeginInvoke(() => { RunResult.Text = Lang.F("opt.reverted", id); Refresh(); });
        });
    }

    private void OnSafeRun(object sender, RoutedEventArgs e) => RunBatch(new[] { "DO-001", "BG-001", "VIS-001", "AH-001" }, Lang.T("opt.batch.safe"));

    /// <summary>性能档 = 安全档四项 + SysMain（与批次卡描述一致），整批前确认。</summary>
    private void OnPerfRun(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Lang.F("opt.highrisk.confirm", Lang.T("tweak.sys001.name")),
                "Falco", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        RunBatch(new[] { "DO-001", "BG-001", "VIS-001", "AH-001", "SYS-001" }, Lang.T("opt.batch.perf"));
    }
    private void OnDevRun(object sender, RoutedEventArgs e) => RunBatch(new[] { "DO-001", "VIS-001" }, Lang.T("opt.batch.dev"));

    private void RunBatch(string[] ids, string label)
    {
        if (!_admin) { RunResult.Text = Lang.T("clean.needadmin"); return; }
        Busy(Lang.F("opt.batch.running", label));
        Task.Run(() =>
        {
            int pass = 0, fail = 0;
            foreach (var id in ids) { var (p, f) = _engine.Apply(id); pass += p; fail += f; }
            Dispatcher.BeginInvoke(() =>
            {
                RunResult.Text = Lang.F("opt.batch.done", label, pass, fail);
                Refresh();
            });
        });
    }

    private void OnRefresh(object sender, RoutedEventArgs e) { Busy(Lang.T("common.detecting")); Task.Run(() => Dispatcher.BeginInvoke(Refresh)); }

    private void OnOpenBackup(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = AppEnv.BackupDir, UseShellExecute = true });

    private void OnRevertAll(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(Lang.T("opt.revertall.confirm"), "Falco", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        Busy(Lang.T("opt.revertall.running"));
        Task.Run(() => { _engine.RevertAll(); Dispatcher.BeginInvoke(() => { RunResult.Text = Lang.T("opt.revertall.done"); Refresh(); }); });
    }

    private void OnRunRestore(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(AppEnv.RestoreScript)) { RunResult.Text = Lang.T("opt.noscript"); return; }
        Process.Start(new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{AppEnv.RestoreScript}\"") { UseShellExecute = true, Verb = "runas" });
    }

    private void Busy(string msg) => RunResult.Text = msg;
}

/// <summary>AH-001 活动时间输入：小时 0–23，起止不同。</summary>
public class ActiveHoursDialog : FalcoDialog
{
    public int StartHour { get; private set; } = 8;
    public int EndHour { get; private set; } = 22;

    private readonly TextBox _start = new();
    private readonly TextBox _end = new();
    private readonly TextBlock _err;

    public ActiveHoursDialog() : base(Lang.T("opt.ah.title"), 380, 220)
    {
        var fg = Res<SolidColorBrush>("BrushText");
        var fg2 = Res<SolidColorBrush>("BrushText2");
        var sp = new StackPanel { Margin = new Thickness(24, 18, 24, 14) };
        AddTxt(sp, Lang.T("opt.ah.intro"), 11, fg2, mb: 12);

        var line = new StackPanel { Orientation = Orientation.Horizontal };
        var sl = Txt(Lang.T("opt.ah.start"), 12, fg);
        sl.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(sl);
        _start.Text = "8"; _start.Width = 60; _start.Height = 28;
        _start.Background = Res<SolidColorBrush>("BrushCardAlt"); _start.BorderBrush = Res<SolidColorBrush>("BrushBorder2");
        _start.Foreground = fg; _start.CaretBrush = fg;
        _start.VerticalContentAlignment = VerticalAlignment.Center;
        _start.Margin = new Thickness(8, 0, 16, 0);
        line.Children.Add(_start);
        var el = Txt(Lang.T("opt.ah.end"), 12, fg);
        el.VerticalAlignment = VerticalAlignment.Center;
        line.Children.Add(el);
        _end.Text = "22"; _end.Width = 60; _end.Height = 28;
        _end.Background = Res<SolidColorBrush>("BrushCardAlt"); _end.BorderBrush = Res<SolidColorBrush>("BrushBorder2");
        _end.Foreground = fg; _end.CaretBrush = fg;
        _end.VerticalContentAlignment = VerticalAlignment.Center;
        _end.Margin = new Thickness(8, 0, 0, 0);
        line.Children.Add(_end);
        sp.Children.Add(line);

        _err = Txt("", 11, Res<SolidColorBrush>("BrushRed"));
        _err.Margin = new Thickness(0, 10, 0, 0);
        sp.Children.Add(_err);

        var ok = ActBtn(Lang.T("common.ok"), (_, __) =>
        {
            if (!int.TryParse(_start.Text.Trim(), out var s) || !int.TryParse(_end.Text.Trim(), out var e2)
                || s is < 0 or > 23 || e2 is < 0 or > 23 || s == e2)
            { _err.Text = Lang.T("opt.ah.invalid"); return; }
            StartHour = s; EndHour = e2;
            DialogResult = true;
        });
        ok.MinWidth = 96;
        ok.Margin = new Thickness(0, 12, 0, 0);
        sp.Children.Add(ok);
        Content = sp;
    }
}
