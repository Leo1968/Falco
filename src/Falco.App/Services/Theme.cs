using System.IO;
using System.Text.Json;
using System.Windows;

namespace Falco.App.Services;

/// <summary>
/// 界面主题：dark / light 双画刷字典（Resources\ThemeDark.xaml / ThemeLight.xaml）。
/// Apply 替换合并字典中的主题项（保留语言字典），全部画刷经 DynamicResource 引用即时刷新；
/// C# 侧取色统一走 TryFindResource（FindBrush），已构建的静态色在下一刷新周期自然更换。
/// </summary>
public static class Theme
{
    private static readonly string ConfigFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "config.json");

    public static string Current { get; private set; } = "dark";
    public static event Action? Changed;

    public static void Initialize()
    {
        Current = ReadConfig() == "light" ? "light" : "dark";
        Apply(Current);
    }

    public static void Toggle()
    {
        Current = Current == "dark" ? "light" : "dark";
        Apply(Current);
        WriteConfig(Current);
        Changed?.Invoke();
    }

    private static void Apply(string theme)
    {
        var app = Application.Current;
        if (app == null) return;
        var uri = new Uri($"Resources/Theme{(theme == "light" ? "Light" : "Dark")}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        // 合并字典：保留语言字典与其它，替换主题字典
        var md = app.Resources.MergedDictionaries;
        var others = md.Where(d => d.Source == null ||
                                   !(d.Source.OriginalString.Contains("ThemeDark") || d.Source.OriginalString.Contains("ThemeLight"))).ToList();
        md.Clear();
        foreach (var d in others) md.Add(d);
        md.Add(dict);
    }

    /// <summary>C# 侧取画刷（当前主题）。</summary>
    public static System.Windows.Media.SolidColorBrush FindBrush(string key)
        => Application.Current?.TryFindResource(key) as System.Windows.Media.SolidColorBrush
           ?? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);

    // ---------- 配置（与 Lang 共用 config.json） ----------

    private static string? ReadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
                if (doc.RootElement.TryGetProperty("theme", out var t) && t.ValueKind == JsonValueKind.String)
                    return t.GetString();
            }
        }
        catch { }
        return null;
    }

    private static void WriteConfig(string theme)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            var dict = new Dictionary<string, object>();
            try
            {
                if (File.Exists(ConfigFile))
                {
                    var prev = JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(ConfigFile));
                    if (prev != null) dict = prev;
                }
            }
            catch { }
            dict["theme"] = theme;
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
