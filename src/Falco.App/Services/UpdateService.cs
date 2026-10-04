using System.Reflection;
using System.Text.Json;

namespace Falco.App.Services;

/// <summary>
/// 检查更新：GitHub Releases 最新一条（gui-v 前缀标签），与当前程序集版本比对。
/// 复用 MuseumApi.Http（带浏览器式 UA、跟随系统代理）；任何失败返回 null（离线静默）。
/// </summary>
public static class UpdateService
{
    public sealed record UpdateInfo(bool HasNew, string Latest, string Current, string ReleaseUrl);

    private const string ReleasesApi = "https://api.github.com/repos/Leo1968/Falco/releases/latest";
    private const string ReleasesPage = "https://github.com/Leo1968/Falco/releases/latest";

    public static async Task<UpdateInfo?> CheckAsync()
    {
        try
        {
            var json = await MuseumApi.Http.GetStringAsync(ReleasesApi);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var latest = tag.StartsWith("gui-v", StringComparison.Ordinal) ? tag["gui-v".Length..] : tag;
            var url = root.TryGetProperty("html_url", out var u) && !string.IsNullOrEmpty(u.GetString())
                ? u.GetString()!
                : ReleasesPage;

            var current = CurrentVersion();
            var hasNew = Version.TryParse(latest, out var lv) && Version.TryParse(current, out var cv) && lv > cv;
            return new UpdateInfo(hasNew, latest, current, url);
        }
        catch { return null; }
    }

    internal static string CurrentVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "0.0.0" : v.ToString(3);
    }
}
