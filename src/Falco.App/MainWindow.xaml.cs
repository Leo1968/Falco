using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Falco.App.Models;
using Falco.App.Services;
using Falco.App.Views;
using Forms = System.Windows.Forms;

namespace Falco.App;

public partial class MainWindow : Window
{
    private readonly MetricsService _metrics = new();
    private readonly Dictionary<string, object> _pages = new();
    private Forms.NotifyIcon _tray;
    private bool _realExit;

    public MainWindow()
    {
        InitializeComponent();
        FitToWorkArea();
        LoadBrandIcon();
        BuildPages();
        BuildTray();
        SwitchPage("stat");

        _metrics.SnapshotReady += s => Dispatcher.BeginInvoke(() => OnSnapshot(s));
        _metrics.Start();
        ArtworkService.EnsureCollection();   // 后台补足名画库（The Met 开放获取，离线静默跳过）
        TxtStatus.Text = Lang.T("status.running");
        Lang.Changed += () => Dispatcher.BeginInvoke(() =>
        {
            // C# 直接赋值的文案随语言切换刷新（XAML DynamicResource 由 WPF 自动处理）
            TxtStatus.Text = Lang.T("status.running");
            BuildTray();
        });

        // 开发自检：--dev-dialog=settings|about|update|menu 启动后直接打开对应弹层（渲染验证用，仿 --engine-test）
        var dev = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--dev-dialog", StringComparison.Ordinal));
        if (dev != null)
        {
            var what = dev.Split('=').ElementAtOrDefault(1);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                switch (what)
                {
                    case "settings": OpenSettings(); break;
                    case "about": OpenAbout(); break;
                    case "update": OpenUpdate(); break;
                    case "boost": _ = new BoostResultDialog(1.05, 180, 200) { Owner = this }.ShowDialog(); break;
                    case "menu":
                        _badgeMenu = BuildBadgeMenu();
                        _badgeMenu.PlacementTarget = BtnBadge;
                        _badgeMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                        _badgeMenu.StaysOpen = true;   // 无人值守截图：不因失焦自关
                        _badgeMenu.IsOpen = true;
                        break;
                }
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        }
    }

    /// <summary>导航栏语言切换：中 ↔ EN。</summary>
    private void OnToggleLang(object sender, RoutedEventArgs e) => Lang.Toggle();

    /// <summary>导航栏主题切换：暗 ↔ 亮（DynamicResource 即时刷新）。</summary>
    private void OnToggleTheme(object sender, RoutedEventArgs e) => Theme.Toggle();

    /// <summary>Win11 Mica 系统背板（壁纸染色玻璃底，卡片浮于其上）；Win11 22H2 以下/RDP 自动回退纯色。</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Backdrop.TryApplyMica(this);
    }

    /// <summary>窗口高度按屏幕工作区自适应：小屏（笔记本 768/900 高）自动缩小默认高并贴底显示，避免默认开窗即超屏。</summary>
    private void FitToWorkArea()
    {
        try
        {
            var wa = SystemParameters.WorkArea;
            var maxH = wa.Height - 24;
            if (Height > maxH)
            {
                Height = Math.Max(MinHeight, maxH);
                Top = Math.Max(0, (wa.Height - Height) / 2);
            }
            if (Width > wa.Width - 24)
            {
                Width = Math.Max(MinWidth, wa.Width - 24);
                Left = Math.Max(0, (wa.Width - Width) / 2);
            }
        }
        catch { }
    }

    private void LoadBrandIcon()
    {
        // 图标经 Tag 进模板（模板内 x:Name 代码后不可见），同时作为隼徽菜单的触发按钮
        BtnBadge.Tag = Services.BrandAsset.LoadFromIco();
    }

    // ---------- 隼徽菜单（设置 / 关于 / 检查更新） ----------

    private System.Windows.Controls.Primitives.Popup? _badgeMenu;

    /// <summary>菜单已开时按下徽章 = 关闭（吞掉本次点击，避免关了又开）。</summary>
    private void OnBadgePreviewDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_badgeMenu?.IsOpen == true)
        {
            _badgeMenu.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnBadgeMenu(object sender, RoutedEventArgs e)
    {
        AppEnv.Log("隼徽：Click 触发");
        // 关键：延迟到按钮释放自身鼠标捕获之后再弹——Click 里同步开 StaysOpen=false 弹层，
        // 其捕获会被按钮随后的 ReleaseMouseCapture 踩掉，表现为菜单"即开即关/没反应"。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_badgeMenu?.IsOpen == true) return;
            _badgeMenu = BuildBadgeMenu();
            _badgeMenu.PlacementTarget = BtnBadge;
            _badgeMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            _badgeMenu.StaysOpen = false;   // 点外部/点完菜单项自动关闭
            _badgeMenu.IsOpen = true;
            AppEnv.Log($"隼徽：菜单已开（IsOpen={_badgeMenu.IsOpen}）");
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    /// <summary>菜单每次打开时重建：文案随 Lang.T 取当前语言。</summary>
    private System.Windows.Controls.Primitives.Popup BuildBadgeMenu()
    {
        var text = Res<SolidColorBrush>("BrushText");
        var text2 = Res<SolidColorBrush>("BrushText2");
        var items = new StackPanel();

        void Item(string glyph, string label, Action onClick)
        {
            var icon = new TextBlock
            {
                Text = ((char)Convert.ToInt32(glyph, 16)).ToString(),
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 15,
                Foreground = text2
            };
            var cap = new TextBlock { Text = label, FontSize = 12.5, Foreground = text, Margin = new Thickness(12, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(icon);
            row.Children.Add(cap);

            var b = new Button
            {
                Content = row,
                Cursor = System.Windows.Input.Cursors.Hand,
                Focusable = false,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent,
                Foreground = text,
                FontFamily = new FontFamily("Microsoft YaHei UI, Segoe UI"),
                Padding = new Thickness(14, 9, 26, 9),
                Margin = new Thickness(0, 0, 0, 2)
            };
            var tpl = new ControlTemplate(typeof(Button));
            var f = new FrameworkElementFactory(typeof(Border), "bd");
            f.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
            f.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));
            f.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            var cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.ContentProperty, new TemplateBindingExtension(Button.ContentProperty));
            cp.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
            cp.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            f.AppendChild(cp);
            tpl.VisualTree = f;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            // DynamicResource 引用主题语义键（亮暗两态都对），禁止 16 进制字面量
            hover.Setters.Add(new Setter(Border.BackgroundProperty, new System.Windows.DynamicResourceExtension("BrushCardAltHover")));
            tpl.Triggers.Add(hover);
            b.Template = tpl;
            b.Click += (_, __) => { _badgeMenu!.IsOpen = false; onClick(); };
            items.Children.Add(b);
        }

        Item("E713", Lang.T("menu.settings"), OpenSettings);
        Item("E946", Lang.T("menu.about"), OpenAbout);
        Item("E895", Lang.T("menu.update"), OpenUpdate);

        var menuBorder = new Border
        {
            Background = Res<SolidColorBrush>("BrushCard"),
            BorderBrush = Res<SolidColorBrush>("BrushCardBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(6),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18, ShadowDepth = 2, Direction = 270, Opacity = 0.35
            }
        };
        menuBorder.Child = items;

        return new System.Windows.Controls.Primitives.Popup
        {
            Child = menuBorder,
            AllowsTransparency = true,
            PopupAnimation = System.Windows.Controls.Primitives.PopupAnimation.Fade
        };
    }

    private T? Res<T>(string key) where T : class => (T?)TryFindResource(key);

    private void OpenSettings()
    {
        AppEnv.Log("隼徽：打开设置");
        var dlg = new Views.SettingsDialog { Owner = this };
        dlg.ShowDialog();
        AppEnv.Log($"隼徽：设置对话框关闭（结果 {dlg.IsActive}）");
    }

    private void OpenAbout()
    {
        AppEnv.Log("隼徽：打开关于");
        var dlg = new Views.AboutDialog { Owner = this };
        dlg.ShowDialog();
        AppEnv.Log("隼徽：关于对话框关闭");
    }

    private void OpenUpdate()
    {
        AppEnv.Log("隼徽：打开检查更新");
        var dlg = new Views.UpdateDialog { Owner = this };
        dlg.ShowDialog();
        AppEnv.Log("隼徽：检查更新对话框关闭");
    }


    private void BuildPages()
    {
        // 五个页面全部保留（功能不下架）；标题栏只暴露「状态」单入口，其余经状态页宫格 Navigate() 进入
        _pages["stat"] = new StatusPage(_metrics);
        _pages["clean"] = new CleanPage();
        _pages["soft"] = new SoftwarePage();
        _pages["opt"] = new OptimizePage();
        _pages["ana"] = new AnalyzePage();
        Wire("stat", NavStat);
    }

    private void Wire(string key, Button btn)
        => btn.Click += (_, __) => SwitchPage(key);

    /// <summary>供状态页快速操作/体检对话框跳转页面。</summary>
    public void Navigate(string key) => SwitchPage(key);

    private void SwitchPage(string key)
    {
        PageHost.Content = _pages[key];
        // 仅「状态」胶囊参与高亮：在功能子页时导航栏不高亮，子页自带「返回」
        var active = key == "stat";
        NavStat.Background = active ? (Brush)FindResource("BrushAccent") : null;
        NavStat.Foreground = active ? new SolidColorBrush(Color.FromRgb(0x1A, 0x14, 0x09)) : (Brush)FindResource("BrushText2");
        NavStat.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
    }

    private void OnSnapshot(MetricSnapshot s)
    {
        // M1：状态条只做心跳；状态页自行消费快照
        TxtStatus.Text = Lang.F("status.snapshot", (int)s.Cpu, (int)s.MemPct, (int)s.DiskFreeGB, s.ProcCount);
    }

    // ---------- 托盘 ----------
    private void BuildTray()
    {
        _tray = new Forms.NotifyIcon { Text = "Falco", Visible = true };
        try
        {
            var icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "falco.ico");
            if (File.Exists(icoPath)) _tray.Icon = new System.Drawing.Icon(icoPath, 16, 16);
            else _tray.Icon = System.Drawing.SystemIcons.Application;
        }
        catch { _tray.Icon = System.Drawing.SystemIcons.Application; }

        _tray.DoubleClick += (_, __) => ShowMain();

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(Lang.T("tray.open"), null, (_, __) => ShowMain());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Lang.T("tray.quit"), null, (_, __) => { _realExit = true; Close(); });
        _tray.ContextMenuStrip = menu;
    }

    private void ShowMain()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_realExit)
        {
            // 与 PS 版一致：关窗默认最小化到托盘继续监控
            e.Cancel = true;
            Hide();
            Services.MemoryTrim.Trim(force: true);   // 托盘化即归还工作集
            return;
        }
        _metrics.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        base.OnClosing(e);
    }
}
