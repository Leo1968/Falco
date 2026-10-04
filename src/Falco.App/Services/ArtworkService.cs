using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Falco.App.Services;

/// <summary>单幅画作元数据（catalog.json 条目）。</summary>
public class ArtMeta
{
    [JsonPropertyName("file")] public string File { get; set; }
    [JsonPropertyName("artist")] public string Artist { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("year")] public string Year { get; set; } = "";
    [JsonPropertyName("style")] public string Style { get; set; } = "";
    [JsonPropertyName("museum")] public string Museum { get; set; } = "";
    [JsonPropertyName("credit")] public string Credit { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";

    // 英文界面展示用（策展清单条目携带；旧条目/五馆条目为空时回退中文字段）
    [JsonPropertyName("titleEn")] public string? TitleEn { get; set; }
    [JsonPropertyName("artistEn")] public string? ArtistEn { get; set; }
    [JsonPropertyName("museumEn")] public string? MuseumEn { get; set; }
}

/// <summary>
/// 名画自动抓取：The Met 开放获取 API（免钥、CC0）。
/// 后台补足画作库（目标 12 幅在线画作），图片与元数据存入
/// C:\ProgramData\Falco\Masterpieces\（met-*.jpg + catalog.json）。
/// 离线或失败静默降级为本地已有图片。
/// </summary>
public static class ArtworkService
{
    public static readonly string Folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Falco", "Masterpieces");
    private static readonly string CatalogFile = Path.Combine(Folder, "catalog.json");

    private const int TargetOnline = 12;          // 在线画作保有量

    // 检索词轮换：按流派/画家铺开，保证多样性（MuseumApi 五馆共用）
    internal static readonly string[] SearchTerms =
    {
        "Rembrandt", "Johannes Vermeer", "Vincent van Gogh", "Claude Monet",
        "Edgar Degas", "Pierre-Auguste Renoir", "Caravaggio", "J.M.W. Turner",
        "Paul Cézanne", "Eugène Delacroix", "El Greco", "Katsushika Hokusai",
        "Winslow Homer", "Mary Cassatt", "Gustave Courbet", "Camille Pissarro",
        "Nicolas Poussin", "Jan Steen", "Frans Hals", "Berthe Morisot",
    };

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };

    // ---------- catalog ----------
    public static List<ArtMeta> ReadCatalog()
    {
        try
        {
            if (File.Exists(CatalogFile))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(CatalogFile));
                var list = new List<ArtMeta>();
                foreach (var e in doc.RootElement.EnumerateArray())
                    list.Add(JsonSerializer.Deserialize<ArtMeta>(e.GetRawText()) ?? new ArtMeta());
                return list;
            }
        }
        catch { }
        return new List<ArtMeta>();
    }

    private static void WriteCatalog(List<ArtMeta> catalog)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CatalogFile, JsonSerializer.Serialize(catalog, _json));
        }
        catch { }
    }

    public static ArtMeta Lookup(string file)
        => ReadCatalog().FirstOrDefault(m => string.Equals(m.File, file, StringComparison.OrdinalIgnoreCase));

    /// <summary>追加一条画作元数据（换画时在线抓取后调用）。同 Source 幂等。</summary>
    public static void AppendCatalog(ArtMeta meta)
    {
        try
        {
            var catalog = ReadCatalog();
            if (catalog.Any(m => m.Source == meta.Source)) return;
            catalog.Add(meta);
            Directory.CreateDirectory(Folder);
            File.WriteAllText(CatalogFile, JsonSerializer.Serialize(catalog, _json));
        }
        catch { }
    }

    /// <summary>策展清单换版：移除不在新清单中的策展条目（catalog 行 + 图片文件）。</summary>
    internal static void PruneCurated(HashSet<string> validSources)
    {
        var catalog = ReadCatalog();
        var stale = catalog.Where(m => m.Source?.StartsWith("curated:", StringComparison.Ordinal) == true
                                       && !validSources.Contains(m.Source)).ToList();
        if (stale.Count == 0) return;
        foreach (var m in stale)
        {
            catalog.Remove(m);
            try { File.Delete(Path.Combine(Folder, m.File)); } catch { }
            AppEnv.Log($"策展换版移除：{m.File}");
        }
        WriteCatalog(catalog);
    }

    // ---------- 抓取 ----------

    /// <summary>后台补足画作库；由 MainWindow 启动时调用一次。</summary>
    public static void EnsureCollection()
    {
        var t = new Thread(() =>
        {
            try { Fill(); }
            catch (Exception ex) { AppEnv.Log($"名画库抓取终止：{ex.Message}", "WARN"); }
        })
        { IsBackground = true, Name = "FalcoArt" };
        t.Start();
    }

    private static void Fill()
    {
        CuratedArt.Fill();   // 策展清单优先（48 幅代表作，失败项静默跳过）

        var catalog = ReadCatalog();
        var haveIds = catalog.Select(m => m.Source).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        // 五馆多源补库：MuseumApi 每次随机选一馆抓一张（Met/Cleveland/AIC/NGA/Rijksmuseum）
        while (catalog.Count(c => !string.IsNullOrEmpty(c.Source)) < TargetOnline)
        {
            var meta = MuseumApi.FetchRandom();
            if (meta == null) { AppEnv.Log("五馆均未取到画作，稍后再试", "WARN"); return; }
            if (haveIds.Contains(meta.Source)) continue;
            catalog.Add(meta);
            haveIds.Add(meta.Source);
            WriteCatalog(catalog);
            AppEnv.Log($"名画入库：{meta.Artist}《{meta.Title}》（{meta.Year}）· {meta.Museum}");
            Thread.Sleep(1500);   // 温和限速
        }
    }
}
