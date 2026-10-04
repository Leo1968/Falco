using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Falco.App.Services;

namespace Falco.App.Views;

public partial class AnalyzePage : UserControl
{
    private string _folderRoot;


    /// <summary>返回状态主页（导航栏只留状态单入口）。</summary>
    private void OnBack(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("stat");

    public AnalyzePage()
    {
        InitializeComponent();
        _folderRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Loaded += (_, __) => { OnSvcRefresh(null, null); OnFolderScan(null, null); };
    }

    private static TextBlock Cell(string text, int width, bool star = false, bool right = false)
        => new()
        {
            Text = text,
            FontSize = 11,
            Foreground = (Brush)Application.Current.TryFindResource("BrushText"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = text,
            TextAlignment = right ? TextAlignment.Right : TextAlignment.Left,
        };

    private static Border Row(string c1, string c2, string c3, string tooltip = null, MouseButtonEventHandler onDoubleClick = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        var t1 = Cell(c1, 0); t1.FontSize = 11;
        var t2 = Cell(c2, 0, right: true); t2.Foreground = (Brush)Application.Current.TryFindResource("BrushText2");
        var t3 = Cell(c3, 0, right: true);
        Grid.SetColumn(t1, 0); Grid.SetColumn(t2, 1); Grid.SetColumn(t3, 2);
        grid.Children.Add(t1); grid.Children.Add(t2); grid.Children.Add(t3);
        var b = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = grid, ToolTip = tooltip ?? c1, Cursor = onDoubleClick != null ? Cursors.Hand : Cursors.Arrow };
        if (onDoubleClick != null) b.AddHandler(Control.MouseDoubleClickEvent, onDoubleClick);
        return b;
    }

    private void OnSvcRefresh(object sender, RoutedEventArgs e)
    {
        Task.Run(() =>
        {
            var hw = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "hardware.json");
            var info = Lang.T("ana.hw.fail");
            try
            {
                if (File.Exists(hw))
                {
                    var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(hw));
                    var r = doc.RootElement;
                    info = Lang.F("ana.hw.info", r.GetProperty("CPU").GetString(), r.GetProperty("RAM_GB").GetDouble().ToString("0.#"), r.GetProperty("Windows").GetString(), r.GetProperty("Build").GetString(), r.GetProperty("DiskType").GetString());
                }
            }
            catch { }
            var svcs = AnalyzeService.GetKeyServices();
            Dispatcher.BeginInvoke(() =>
            {
                InfoText.Text = info;
                SvcList.Children.Clear();
                foreach (var (name, state, startType) in svcs)
                {
                    var color = state == "Running" ? "BrushGreen" : state == "Stopped" ? "BrushText2" : "BrushRed";
                    var row = Row(name, $"{state} · {startType}", "",
                        tooltip: Lang.F("ana.svc.tooltip", name, state, startType));
                    ((TextBlock)((Grid)row.Child).Children[1]).Foreground =
                        (Brush)FindResource(color);
                    SvcList.Children.Add(row);
                }
            });
        });
    }

    private void OnFolderScan(object sender, RoutedEventArgs e)
    {
        FolderTitle.Text = Lang.F("ana.folders.title.tpl", _folderRoot);
        FolderRes.Text = Lang.T("ana.calculating");
        var root = _folderRoot;
        Task.Run(() =>
        {
            var rows = AnalyzeService.ScanFolders(root);
            Dispatcher.BeginInvoke(() =>
            {
                FolderList.Children.Clear();
                foreach (var r in rows)
                    FolderList.Children.Add(Row(r.Name, Lang.F("ana.items", r.Files), r.SizeText, tooltip: r.FullPath,
                        onDoubleClick: (_, __) => { _folderRoot = r.FullPath; OnFolderScan(null, null); }));
                FolderRes.Text = Lang.F("ana.folders.summary", rows.Count);
            });
        });
    }

    private void OnFolderUp(object sender, RoutedEventArgs e)
    {
        var parent = Path.GetDirectoryName(_folderRoot);
        if (!string.IsNullOrEmpty(parent)) { _folderRoot = parent; OnFolderScan(null, null); }
    }

    private void OnBigUser(object sender, RoutedEventArgs e) => ScanBig(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
    private void OnBigAll(object sender, RoutedEventArgs e) => ScanBig(Path.GetPathRoot(Environment.SystemDirectory));

    private void ScanBig(string root)
    {
        BigRes.Text = Lang.T("ana.scanning");
        Task.Run(() =>
        {
            var files = AnalyzeService.ScanBigFiles(root, 200);
            Dispatcher.BeginInvoke(() =>
            {
                BigList.Children.Clear();
                foreach (var f in files)
                    BigList.Children.Add(Row(f.Path, f.Modified, $"{f.SizeMB:0} MB", tooltip: f.Path));
                BigRes.Text = Lang.F("ana.bigfiles.summary", files.Count, root);
            });
        });
    }

    private void OnOpenBackup(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = AppEnv.BackupDir, UseShellExecute = true });

    private void OnOpenLog(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = "notepad.exe", Arguments = AppEnv.LogFile, UseShellExecute = true });

    private void OnOpenData(object sender, RoutedEventArgs e)
        => Process.Start(new ProcessStartInfo { FileName = AppEnv.BaseDir, UseShellExecute = true });
}
