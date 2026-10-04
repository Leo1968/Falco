using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Falco.App.Views;

/// <summary>
/// 星舰发射动态图标（Boost 结果弹窗）：银色不锈钢舰体 + 闪烁尾焰 + 轻微浮动，
/// 参考 SpaceX 星舰发射插画。自绘矢量，无外部素材，明暗主题通用。
/// </summary>
public class StarshipIcon : Canvas
{
    public StarshipIcon()
    {
        Width = 120; Height = 132;

        var flame = MkFlame();
        var rocket = MkRocket();
        Children.Add(flame);
        Children.Add(rocket);

        // 火焰闪烁：以喷口为锚点纵向缩放 + 呼吸透明度
        var flicker = new Storyboard();
        flicker.Children.Add(Anim(flame, "RenderTransform.ScaleX", 0.85, 1.12, 340));
        flicker.Children.Add(Anim(flame, "RenderTransform.ScaleY", 0.78, 1.18, 300));
        flicker.Children.Add(Anim(flame, "Opacity", 0.82, 1.0, 260));
        flicker.Begin(this, true);

        // 舰体轻微浮动（模拟引擎推力抖动）
        var bob = new Storyboard();
        bob.Children.Add(Anim(rocket, "RenderTransform.Y", -1.2, 1.2, 700));
        bob.Begin(this, true);
    }

    private static DoubleAnimation Anim(UIElement target, string path, double from, double to, int ms)
    {
        var a = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(ms)))
        {
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, new PropertyPath(path));
        return a;
    }

    // ---------- 舰体 ----------

    private static UIElement MkRocket()
    {
        var c = new Canvas { RenderTransform = new TranslateTransform(0, 0) };

        var body = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xF4, 0xF7, 0xF9), 0.15),
                new GradientStop(Color.FromRgb(0xC6, 0xCF, 0xD7), 0.55),
                new GradientStop(Color.FromRgb(0x93, 0xA0, 0xAB), 1.0),
            }
        };

        // 舰体（含头锥轮廓）
        c.Children.Add(MkPath("M60,6 C67,12 73,24 74,38 L74,84 L46,84 L46,38 C47,24 53,12 60,6 Z", body, "#59636D"));
        // 深色头锥
        c.Children.Add(MkPath("M60,6 C64,10 68,17 70,26 L50,26 C52,17 56,10 60,6 Z", "#333B45", null));
        // 前翼
        c.Children.Add(MkPath("M47,30 L37,44 L47,44 Z", "#454E59", null));
        c.Children.Add(MkPath("M73,30 L83,44 L73,44 Z", "#454E59", null));
        // 后襟翼
        c.Children.Add(MkPath("M47,58 L34,86 L47,86 Z", "#B7C1C9", "#59636D"));
        c.Children.Add(MkPath("M73,58 L86,86 L73,86 Z", "#B7C1C9", "#59636D"));
        // 舯部焊缝
        c.Children.Add(MkLine(47, 52, 73, 52));
        c.Children.Add(MkLine(47, 68, 73, 68));
        // 尾裙 + 三喷口
        c.Children.Add(MkRect(47.5, 84, 25, 9, "#39414B"));
        c.Children.Add(MkRect(50, 93, 6, 6, "#2F3640"));
        c.Children.Add(MkRect(57, 93, 6, 6, "#2F3640"));
        c.Children.Add(MkRect(64, 93, 6, 6, "#2F3640"));
        return c;
    }

    // ---------- 尾焰 ----------

    private static UIElement MkFlame()
    {
        var c = new Canvas
        {
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new Point(0.5, 0),
        };
        // 喷口辉光
        c.Children.Add(MkEllipse(44, 92, 32, 16, Color.FromRgb(0xFF, 0xD5, 0x4F), 0.35));
        // 三层尾焰（外橙 / 中黄 / 芯白）
        c.Children.Add(MkPath("M50,100 C52,112 57,121 60,127 C63,121 68,112 70,100 Z", "#FF9E2C", null, 0.95));
        c.Children.Add(MkPath("M54,100 C56,109 58.5,115 60,119 C61.5,115 64,109 66,100 Z", "#FFC93C", null, 0.95));
        c.Children.Add(MkPath("M57,100 C58,106 59.5,110 60,112 C60.5,110 62,106 63,100 Z", "#FFF7E6", null, 1.0));
        return c;
    }

    // ---------- 小工具 ----------

    private static Path MkPath(string data, Brush fill, string? stroke, double opacity = 1.0)
    {
        var p = new Path
        {
            Data = Geometry.Parse(data),
            Fill = fill,
            Opacity = opacity,
        };
        if (stroke != null) { p.Stroke = new SolidColorBrush((Color)ColorConverter.ConvertFromString(stroke)); p.StrokeThickness = 1; }
        return p;
    }

    private static Path MkPath(string data, string fillHex, string? stroke, double opacity = 1.0)
        => MkPath(data, new SolidColorBrush((Color)ColorConverter.ConvertFromString(fillHex)), stroke, opacity);

    private static Line MkLine(double x1, double y1, double x2, double y2) => new()
    {
        X1 = x1, Y1 = y1, X2 = x2, Y2 = y2,
        Stroke = new SolidColorBrush(Color.FromRgb(0x9D, 0xA8, 0xB1)),
        StrokeThickness = 0.8,
    };

    private static System.Windows.Shapes.Rectangle MkRect(double x, double y, double w, double h, string fill)
    {
        var r = new System.Windows.Shapes.Rectangle
        {
            Width = w, Height = h,
            RadiusX = 1.5, RadiusY = 1.5,
            Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(fill)),
        };
        Canvas.SetLeft(r, x); Canvas.SetTop(r, y);
        return r;
    }

    private static System.Windows.Shapes.Ellipse MkEllipse(double x, double y, double w, double h, Color fill, double opacity)
    {
        var e = new System.Windows.Shapes.Ellipse
        {
            Width = w, Height = h,
            Fill = new SolidColorBrush(fill), Opacity = opacity,
        };
        Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
        return e;
    }
}

