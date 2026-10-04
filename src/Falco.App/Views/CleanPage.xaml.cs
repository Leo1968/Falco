using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Falco.App.Services;

namespace Falco.App.Views;

public partial class CleanPage : UserControl
{
    private readonly List<ScanRow> _sys = new();
    private List<ScanRow> _purge, _cache, _inst;


    /// <summary>返回状态主页（导航栏只留状态单入口）。</summary>
    private void OnBack(object sender, RoutedEventArgs e) => (Window.GetWindow(this) as MainWindow)?.Navigate("stat");

    public CleanPage()
    {
        InitializeComponent();
    }

    // ---------- 通用行构建 ----------
    private Border MakeRow(ScanRow r, Action refresh, string whitelistContext = null)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });

        var cb = new CheckBox { IsChecked = r.Checked, VerticalAlignment = VerticalAlignment.Center, IsEnabled = r.Kind != "protected" };
        cb.Click += (_, __) => { r.Checked = cb.IsChecked == true; };
        var path = new TextBlock { Text = r.Path, FontSize = 11, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = r.Path };
        var note = new TextBlock { Text = r.Note, FontSize = 10, Foreground = (Brush)FindResource("BrushText2"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var size = new TextBlock { Text = r.SizeText, FontSize = 11, Foreground = (Brush)FindResource("BrushText"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };

        Grid.SetColumn(cb, 0); Grid.SetColumn(path, 1); Grid.SetColumn(size, 2); Grid.SetColumn(note, 3);
        grid.Children.Add(cb); grid.Children.Add(path); grid.Children.Add(size); grid.Children.Add(note);

        var border = new Border { Background = Brushes.Transparent, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 4, 6, 4), Child = grid };
        if (whitelistContext != null)
        {
            border.MouseRightButtonUp += (_, __) =>
            {
                if (MessageBox.Show(Lang.F("clean.whitelist.confirm", r.Path) + "\n" + r.Path, "Falco", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                {
                    CleanService.AddWhitelist(whitelistContext == "cache" ? r.Path : r.Path);
                    refresh();
                }
            };
            border.ToolTip = Lang.T("clean.whitelist.tip");
        }
        return border;
    }

    private void Fill(StackPanel host, List<ScanRow> rows, Action refresh = null, string whitelist = null)
    {
        host.Children.Clear();
        foreach (var r in rows) host.Children.Add(MakeRow(r, refresh ?? FillNothing, whitelist));
    }

    private static void FillNothing() { }

    private static string SumText(List<ScanRow> rows)
        => Lang.F("clean.rows.summary", rows.Count, rows.Count(r => r.Checked), rows.Where(r => r.Checked).Sum(r => r.SizeMB).ToString("0.#"));

    // ---------- 系统层 ----------
    private void OnSysScan(object sender, RoutedEventArgs e)
    {
        SysRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            _sys.Clear();
            _sys.Add(new ScanRow { Kind = "temp", Path = Lang.T("clean.row.temp"), Note = Lang.T("clean.row.temp.note"), SizeText = $"{CleanService.TempMB():0.#} MB", SizeMB = CleanService.TempMB(), Checked = true, });
            _sys.Add(new ScanRow { Kind = "wu", Path = Lang.T("clean.row.wu"), Note = Lang.T("clean.row.wu.note"), SizeText = $"{CleanService.WuMB():0.#} MB", SizeMB = CleanService.WuMB(), Checked = true });
            _sys.Add(new ScanRow { Kind = "thumbs", Path = Lang.T("clean.row.thumbs"), Note = Lang.T("clean.row.thumbs.note"), SizeText = $"{CleanService.ThumbsMB():0.#} MB", SizeMB = CleanService.ThumbsMB(), Checked = true });
            _sys.Add(new ScanRow { Kind = "recycle", Path = Lang.T("clean.row.recycle"), Note = Lang.T("clean.row.recycle.note"), SizeText = "—", Checked = false });
            Dispatcher.BeginInvoke(() => { Fill(SysList, _sys); SysRes.Text = SumText(_sys); });
        });
    }

    private void OnSysClean(object sender, RoutedEventArgs e)
    {
        if (!AppEnv.IsAdmin()) { SysRes.Text = Lang.T("clean.needadmin"); return; }
        var freed = _sys.Where(r => r.Checked).Sum(r => r.SizeMB);
        Task.Run(() =>
        {
            foreach (var r in _sys.Where(r => r.Checked))
            {
                try
                {
                    if (r.Kind == "temp") CleanService.CleanTemp();
                    else if (r.Kind == "wu") CleanService.CleanWu();
                    else if (r.Kind == "thumbs") CleanService.CleanThumbs();
                    else if (r.Kind == "recycle") CleanService.CleanRecycle();
                }
                catch (Exception ex) { AppEnv.Log($"清理失败（{r.Path}）：{ex.Message}", "ERROR"); }
            }
            Dispatcher.BeginInvoke(() =>
            {
                SysRes.Text = freed > 0 ? Lang.F("clean.done.freed", freed.ToString("0.#")) : Lang.T("clean.done");
                OnSysScan(null, null);
            });
        });
    }

    // ---------- 构建产物 ----------
    private void OnPurgeScan(object sender, RoutedEventArgs e)
    {
        PurgeRes.Text = Lang.T("clean.scanning.deep");
        var root = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Task.Run(() =>
        {
            _purge = CleanService.ScanPurge(root);
            Dispatcher.BeginInvoke(() => { Fill(PurgeList, _purge); PurgeRes.Text = SumText(_purge); });
        });
    }

    private void OnPurgeClean(object sender, RoutedEventArgs e)
    {
        var picked = _purge?.Where(r => r.Checked).ToList();
        if (picked is not { Count: > 0 }) { PurgeRes.Text = Lang.T("clean.noselect"); return; }
        if (MessageBox.Show(Lang.F("clean.purge.confirm", picked.Count), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var freed = picked.Sum(r => r.SizeMB);
        Task.Run(() =>
        {
            CleanService.CleanPurge(_purge);
            Dispatcher.BeginInvoke(() =>
            {
                PurgeRes.Text = freed > 0 ? Lang.F("clean.done.freed", freed.ToString("0.#")) : Lang.T("clean.deleted.done");
                OnPurgeScan(null, null);
            });
        });
    }

    private void OnPurgeAll(object sender, RoutedEventArgs e) => SetAll(_purge, true, PurgeList, PurgeRes);
    private void OnPurgeNone(object sender, RoutedEventArgs e) => SetAll(_purge, false, PurgeList, PurgeRes);

    private void SetAll(List<ScanRow> rows, bool check, StackPanel host, TextBlock res)
    {
        if (rows == null) return;
        foreach (var r in rows) r.Checked = check && r.Kind == "rebuildable";
        Fill(host, rows);
        res.Text = SumText(rows);
    }

    // ---------- 开发缓存 ----------
    private void OnCacheScan(object sender, RoutedEventArgs e)
    {
        CacheRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            _cache = CleanService.ScanCaches();
            Dispatcher.BeginInvoke(() => { Fill(CacheList, _cache, () => OnCacheScan(null, null), "cache"); CacheRes.Text = SumText(_cache); });
        });
    }

    private void OnCacheClean(object sender, RoutedEventArgs e)
    {
        var picked = _cache?.Where(r => r.Checked && r.SizeMB > 0).ToList();
        if (picked is not { Count: > 0 }) { CacheRes.Text = Lang.T("clean.noselect"); return; }
        if (MessageBox.Show(Lang.F("clean.cache.confirm", picked.Count), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var freed = picked.Sum(r => r.SizeMB);
        Task.Run(() =>
        {
            CleanService.CleanCaches(_cache);
            Dispatcher.BeginInvoke(() =>
            {
                CacheRes.Text = freed > 0 ? Lang.F("clean.done.freed", freed.ToString("0.#")) : Lang.T("clean.done");
                OnCacheScan(null, null);
            });
        });
    }

    // ---------- 安装包 ----------
    private void OnInstScan(object sender, RoutedEventArgs e)
    {
        InstRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            _inst = CleanService.ScanInstallers();
            Dispatcher.BeginInvoke(() => { Fill(InstList, _inst); InstRes.Text = SumText(_inst); });
        });
    }

    /// <summary>追加扫描用户任选的文件夹（顶层，与默认目录去重合并）。</summary>
    private void OnInstFolder(object sender, RoutedEventArgs e)
    {
        using var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Lang.T("clean.inst.otherfolder"),
            ShowNewFolderButton = false
        };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        InstRes.Text = Lang.T("clean.scanning");
        Task.Run(() =>
        {
            var extra = CleanService.ScanInstallers(dlg.SelectedPath);
            Dispatcher.BeginInvoke(() =>
            {
                _inst ??= new List<ScanRow>();
                var known = _inst.Select(r => r.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var added = extra.Where(r => !known.Contains(r.Path)).ToList();
                _inst.AddRange(added);
                Fill(InstList, _inst);
                InstRes.Text = added.Count > 0
                    ? Lang.F("clean.inst.folder.added", added.Count)
                    : Lang.T("clean.inst.folder.none");
            });
        });
    }

    private void OnInstClean(object sender, RoutedEventArgs e)
    {
        var picked = _inst?.Where(r => r.Checked).ToList();
        if (picked is not { Count: > 0 }) { InstRes.Text = Lang.T("clean.noselect"); return; }
        if (MessageBox.Show(Lang.F("clean.inst.confirm", picked.Count), "Falco", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var freed = picked.Sum(r => r.SizeMB);
        Task.Run(() =>
        {
            CleanService.CleanInstallers(_inst);
            Dispatcher.BeginInvoke(() =>
            {
                InstRes.Text = freed > 0 ? Lang.F("clean.done.freed", freed.ToString("0.#")) : Lang.T("clean.deleted.done");
                OnInstScan(null, null);
            });
        });
    }
}
