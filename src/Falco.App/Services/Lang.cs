using System.IO;
using System.Text.Json;
using System.Windows;

namespace Falco.App.Services;

/// <summary>
/// 界面语言：zh / en 双资源字典（Resources\LangZh.xaml / LangEn.xaml）。
/// Initialize 在 App 启动时调用；Switch 替换合并字典，WPF DynamicResource 引用自动刷新；
/// C# 代码侧用 T()/F() 取词（已弹出的动态文本在下次刷新周期自然更换）。
/// </summary>
public static class Lang
{
    private static readonly string ConfigFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "config.json");

    public static string Current { get; private set; } = "zh";

    /// <summary>语言切换后触发（C# 侧需要立即重绘的界面可订阅）。</summary>
    public static event Action? Changed;

    public static void Initialize()
    {
        Current = ReadConfig() is "en" ? "en" : "zh";
        Apply(Current);
    }

    public static void Toggle()
    {
        Current = Current == "zh" ? "en" : "zh";
        Apply(Current);
        WriteConfig(Current);
        Changed?.Invoke();
    }

    private static void Apply(string lang)
    {
        var app = Application.Current;
        if (app == null) return;
        var uri = new Uri($"Resources/Lang{(lang == "en" ? "En" : "Zh")}.xaml", UriKind.Relative);
        var dict = new ResourceDictionary { Source = uri };

        // 合并字典第 0 位是语言字典（App.xaml 中先 Merge 语言字典再其它）
        var md = app.Resources.MergedDictionaries;
        var others = md.Where(d => d.Source == null || !(d.Source.OriginalString.Contains("LangZh") || d.Source.OriginalString.Contains("LangEn"))).ToList();
        md.Clear();
        md.Add(dict);
        foreach (var d in others) md.Add(d);
    }

    // ---------- C# 侧取词 ----------

    /// <summary>取词：找不到 key 时回退 zh 字典，再找不到返回 key 本身。</summary>
    public static string T(string key)
    {
        var app = Application.Current;
        if (app == null) return key;
        var v = app.TryFindResource(key);
        if (v is string s && !string.IsNullOrEmpty(s)) return s;
        // 当前语言缺失时尝试另一份（防止运行中切换后的瞬时缺失）
        try
        {
            var uri = new Uri($"Resources/Lang{(Current == "en" ? "Zh" : "En")}.xaml", UriKind.Relative);
            var rd = new ResourceDictionary { Source = uri };
            if (rd[key] is string s2 && !string.IsNullOrEmpty(s2)) return s2;
        }
        catch { }
        return key;
    }

    /// <summary>取词并格式化（{0} 占位）。</summary>
    public static string F(string key, params object[] args) => string.Format(T(key), args);

    // ---------- 配置 ----------

    private static string? ReadConfig()
    {
        try
        {
            if (File.Exists(ConfigFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(ConfigFile));
                if (doc.RootElement.TryGetProperty("lang", out var l) && l.ValueKind == JsonValueKind.String)
                    return l.GetString();
            }
        }
        catch { }
        return null;
    }

    private static void WriteConfig(string lang)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigFile)!);
            string prev = "";
            try { if (File.Exists(ConfigFile)) prev = File.ReadAllText(ConfigFile); } catch { }
            var dict = string.IsNullOrEmpty(prev)
                ? new Dictionary<string, object>()
                : JsonSerializer.Deserialize<Dictionary<string, object>>(prev) ?? new();
            dict["lang"] = lang;
            File.WriteAllText(ConfigFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }
}
