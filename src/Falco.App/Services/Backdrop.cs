using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Shell;

namespace Falco.App.Services;

/// <summary>
/// Win11 Mica 系统背板（Apple 玻璃的 Windows 官方对应物）：壁纸染色垫底，卡片浮于其上。
/// 仅 Win11 22H2+（build 22621）与真实桌面会话可用；RDP/旧系统 DwmSetWindowAttribute
/// 返回失败，静默回退纯色背景。不设 AllowsTransparency（会破坏 DWM 背板与硬件渲染路径），
/// 经 WindowChrome 全窗口玻璃框（GlassFrameThickness=-1）承接背板。
/// </summary>
public static class Backdrop
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int USE_IMMERSIVE_DARK_MODE = 20;
    private const int SYSTEMBACKDROP_TYPE = 38;
    private const int DWMSBT_MAINWINDOW = 1;   // Mica

    private static IntPtr _hwnd;
    private static bool _applied;

    /// <summary>SourceInitialized 后调用；成功返回 true。失败恢复原背景与系统边框。</summary>
    public static bool TryApplyMica(Window window)
    {
        try
        {
            var chrome = new WindowChrome
            {
                GlassFrameThickness = new Thickness(-1),   // 一体化玻璃框：DWM 背板透入整个窗口
                CaptionHeight = 34,
                ResizeBorderThickness = new Thickness(8),
                CornerRadius = new CornerRadius(0),
            };
            WindowChrome.SetWindowChrome(window, chrome);
            // 全透明表面在 KB5121794 后被合成器跳过（内容不绘制）；1-alpha 强制合成
            window.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 0, 0, 0));

            _hwnd = new WindowInteropHelper(window).EnsureHandle();
            var backdrop = DWMSBT_MAINWINDOW;
            if (DwmSetWindowAttribute(_hwnd, SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int)) != 0)
            {
                Restore(window);
                return false;
            }
            _applied = true;
            ApplyDarkMode(Theme.Current == "dark");

            // 主题切换同步标题栏/Mica 深浅
            Theme.Changed -= OnThemeChanged;
            Theme.Changed += OnThemeChanged;
            return true;
        }
        catch
        {
            Restore(window);
            return false;
        }
    }

    private static void OnThemeChanged() => ApplyDarkMode(Theme.Current == "dark");

    private static void ApplyDarkMode(bool dark)
    {
        if (!_applied) return;
        var v = dark ? 1 : 0;
        DwmSetWindowAttribute(_hwnd, USE_IMMERSIVE_DARK_MODE, ref v, sizeof(int));
    }

    private static void Restore(Window window)
    {
        _applied = false;
        WindowChrome.SetWindowChrome(window, null);
        window.SetResourceReference(Window.BackgroundProperty, "BrushWindow");
    }
}
