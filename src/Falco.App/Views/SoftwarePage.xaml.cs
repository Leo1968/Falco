using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Falco.App.Services;

namespace Falco.App.Views;

public partial class SoftwarePage : UserControl
{
    private List<StartupRow> _startups;
    private List<AppRow> _apps;
    private List<ResidueRow> _residue;
    private List<TaskRow> _tasks;
    private AppRow _selected;


    /// <summary>返回状态主页（导航栏只留状态单入口）。</summary>
    private void OnBack(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("stat");

    public SoftwarePage()
    {
        InitializeComponent();
        Loaded += (_, __) => { OnStartupRefresh(null, null); OnAppRefresh(null, null); OnTaskRefresh(null, null); };
    }

    private Border Row(string col1, string col2, string col3, object tag = null, bool hasCheck = false, bool check = false,
                       RoutedEventHandler onClick = null, string tooltip = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        int col = 0;
        if (hasCheck)
        {
            var cb = new CheckBox { IsChecked = check, VerticalAlignment = VerticalAlignment.Center, Tag = tag };
            cb.Click += (_, __) => { if (cb.Tag is ResidueRow rr) rr.Checked = cb.IsChecked == true; };
            Grid.SetColumn(cb, col++);
            grid.Children.Add(cb);
        }
        else
        {
            var rb = new RadioButton { GroupName = "app", VerticalAlignment = VerticalAlignment.Center, Tag = tag };
            rb.Checked += (_, __) => _selected = rb.Tag as AppRow;
            Grid.SetColumn(rb, col++);
            grid.Children.Add(rb);
        }
        var t1 = new TextBlock { Text = col1, FontSize = 11, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = tooltip ?? col1 };
        var t2 = new TextBlock { Text = col2, FontSize = 10, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var t3 = new TextBlock { Text = col3, FontSize = 11, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        Grid.SetColumn(t1, col++); Grid.SetColumn(t2, col++); Grid.SetColumn(t3, col++);
        grid.Children.Add(t1); grid.Children.Add(t2); grid.Children.Add(t3);
        var border = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = grid, Tag = tag };
        if (onClick != null) border.MouseLeftButtonUp += (s, e) => onClick(s, null);
        return border;
    }

    private void OnStartupRefresh(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            _startups = SoftwareService.GetStartups();
            Dispatcher.BeginInvoke(() =>
            {
                StartupList.Children.Clear();
                foreach (var s in _startups)
                    StartupList.Children.Add(StartupRowView(s));
                StartupRes.Text = Lang.F("soft.summary.items", _startups.Count);
            });
        });
    }

    /// <summary>启动项行：名称（含禁用标记）| 命令 | 位置 | 启停 + 删除。HKLM 项需管理员。</summary>
    private FrameworkElement StartupRowView(StartupRow s)
    {
        var needAdmin = (s.Kind == "hklm" || s.Kind == "hklm32") && !AppEnv.IsAdmin();
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var name = new TextBlock
        {
            Text = s.Enabled ? s.Name : s.Name + " · " + Lang.T("soft.startup.disabled"),
            FontSize = 11,
            Foreground = (Brush)FindResource(s.Enabled ? "BrushText" : "BrushText2"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = s.Name
        };
        var cmd = new TextBlock { Text = s.Command, FontSize = 10, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = s.Command };
        var loc = new TextBlock { Text = s.Location, FontSize = 10, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(name, 0); Grid.SetColumn(cmd, 1); Grid.SetColumn(loc, 2);
        grid.Children.Add(name); grid.Children.Add(cmd); grid.Children.Add(loc);

        var ops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var toggle = new Button
        {
            Style = (Style)FindResource("DlgBtn"),
            Content = Lang.T(s.Enabled ? "soft.startup.disable" : "soft.startup.enable"),
            FontSize = 10, Padding = new Thickness(8, 3, 8, 3), Margin = new Thickness(0, 0, 6, 0),
            IsEnabled = !needAdmin
        };
        toggle.Click += (_, __) =>
        {
            try { SoftwareService.SetStartupEnabled(s, !s.Enabled); }
            catch (Exception ex) { StartupRes.Text = Lang.F("checkup.readfail", ex.Message); return; }
            OnStartupRefresh(null, null);
        };
        var del = new Button
        {
            Style = (Style)FindResource("DlgBtn"),
            Content = Lang.T("soft.startup.delete"),
            FontSize = 10, Padding = new Thickness(8, 3, 8, 3),
            Foreground = (Brush)FindResource("BrushRed"),
            IsEnabled = !needAdmin
        };
        del.Click += (_, __) =>
        {
            if (MessageBox.Show(Lang.F("soft.startup.delete.confirm", s.Name), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try { SoftwareService.DeleteStartup(s); }
            catch (Exception ex) { StartupRes.Text = Lang.F("checkup.readfail", ex.Message); return; }
            OnStartupRefresh(null, null);
        };
        if (needAdmin)
        {
            var tip = Lang.T("soft.startup.needadmin");
            toggle.ToolTip = tip; del.ToolTip = tip;
        }
        ops.Children.Add(toggle); ops.Children.Add(del);
        Grid.SetColumn(ops, 3); grid.Children.Add(ops);

        return new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = grid };
    }

    // ---------- 计划任务（第三方开机自启，只读 + 禁用） ----------

    private void OnTaskRefresh(object sender, RoutedEventArgs e)
    {
        TasksRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            _tasks = SoftwareService.GetLogonTasks();
            Dispatcher.BeginInvoke(() =>
            {
                TaskList.Children.Clear();
                foreach (var t in _tasks)
                    TaskList.Children.Add(TaskRowView(t));
                TasksRes.Text = Lang.F("soft.tasks.summary", _tasks.Count);
            });
        });
    }

    private FrameworkElement TaskRowView(TaskRow t)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });

        var stateBrush = t.State == "Disabled" ? (Brush)FindResource("BrushText2") : (Brush)FindResource("BrushGreen");
        var name = new TextBlock { Text = t.Name, FontSize = 11, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t.Path };
        var act = new TextBlock { Text = t.Action, FontSize = 10, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t.Action };
        var st = new TextBlock { Text = t.State, FontSize = 10, Foreground = stateBrush, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(name, 0); Grid.SetColumn(act, 1); Grid.SetColumn(st, 2);
        grid.Children.Add(name); grid.Children.Add(act); grid.Children.Add(st);

        var ops = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var dis = new Button
        {
            Style = (Style)FindResource("DlgBtn"),
            Content = Lang.T("soft.tasks.disable"),
            FontSize = 10, Padding = new Thickness(8, 3, 8, 3),
            IsEnabled = t.State != "Disabled"
        };
        dis.Click += (_, __) =>
        {
            try { SoftwareService.DisableTask(t.Path); }
            catch (Exception ex) { TasksRes.Text = Lang.F("checkup.readfail", ex.Message); return; }
            OnTaskRefresh(null, null);
        };
        ops.Children.Add(dis);
        Grid.SetColumn(ops, 3); grid.Children.Add(ops);

        return new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = grid };
    }

    private void OnAppRefresh(object sender, RoutedEventArgs e)
    {
        AppRes.Text = Lang.T("soft.loading");
        Task.Run(() =>
        {
            _apps = SoftwareService.GetInstalledApps();
            Dispatcher.BeginInvoke(() =>
            {
                AppList.Children.Clear();
                foreach (var a in _apps.Take(80))
                    AppList.Children.Add(Row(a.Name, a.Version, a.SizeText, a));
                AppRes.Text = Lang.F("soft.apps.summary", _apps.Count);
            });
        });
    }

    private void OnUninstall(object sender, RoutedEventArgs e)
    {
        if (_selected == null) { AppRes.Text = Lang.T("soft.selectfirst"); return; }
        if (MessageBox.Show(Lang.F("soft.uninstall.confirm", _selected.Name), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        SoftwareService.Uninstall(_selected);
        AppRes.Text = Lang.F("soft.uninstall.launched", _selected.Name);
    }

    private void OnResidueScan(object sender, RoutedEventArgs e)
    {
        ResidueRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            _residue = SoftwareService.ScanResidue();
            Dispatcher.BeginInvoke(() =>
            {
                ResidueList.Children.Clear();
                foreach (var r in _residue)
                    ResidueList.Children.Add(Row(r.Path, r.Note, "", r, hasCheck: true, check: r.Checked, tooltip: r.Path));
                ResidueRes.Text = Lang.F("soft.residue.summary", _residue.Count, _residue.Count(x => x.Checked));
            });
        });
    }

    private void OnResidueClean(object sender, RoutedEventArgs e)
    {
        var picked = _residue?.Where(r => r.Checked).ToList();
        if (picked is not { Count: > 0 }) { ResidueRes.Text = Lang.T("clean.noselect"); return; }
        if (MessageBox.Show(Lang.F("soft.residue.confirm", picked.Count), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Task.Run(() =>
        {
            foreach (var r in picked)
            {
                try
                {
                    if (Directory.Exists(r.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(r.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    else if (File.Exists(r.Path))
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(r.Path,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    AppEnv.Log($"残留已删除（回收站）：{r.Path}");
                }
                catch (Exception ex) { AppEnv.Log($"残留删除失败 {r.Path}：{ex.Message}", "ERROR"); }
            }
            Dispatcher.BeginInvoke(() => { ResidueRes.Text = Lang.T("clean.done"); OnResidueScan(null, null); });
        });
    }
}
