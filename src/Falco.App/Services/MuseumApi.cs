using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace Falco.App.Services;

/// <summary>
/// 五馆开放获取随机抓取：The Met / Cleveland / AIC / NGA / Rijksmuseum（全部免钥、公有领域）。
/// FetchRandom 随机选馆、馆内随机检索词 + 随机取样，一馆失败自动换下一馆。
/// 返回的 ArtMeta 已含下载好的本地图片文件（存 ArtworkService.Folder）。
/// </summary>
public static class MuseumApi
{
    internal static readonly HttpClient Http = new(new HttpClientHandler
    {
        UseProxy = true,                     // 跟随系统代理（Clash 等）
        AutomaticDecompression = System.Net.DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromSeconds(20),
        // AIC 等馆的 CDN 会 403 无 UA 的 .NET HttpClient，统一带浏览器式 UA
        DefaultRequestHeaders = { UserAgent = { new System.Net.Http.Headers.ProductInfoHeaderValue("Mozilla", "5.0"), new System.Net.Http.Headers.ProductInfoHeaderValue("(Falco/2.2)") } },
    };

    private static readonly string[] Terms = ArtworkService.SearchTerms;
    private static readonly Random _rng = new();
    private static string _lastName;         // 上次成功的馆：排到最后，让五馆轮换更均匀

    public static ArtMeta? FetchRandom()
    {
        var providers = new (string name, Func<string, ArtMeta?> fetch)[]
        {
            ("met", FetchMet),
            ("cleveland", FetchCleveland),
            ("aic", FetchAic),
            ("rijks", FetchRijks),
            ("smk", FetchSmk),
            ("nga", FetchNga),
        };
        var order = providers.OrderBy(_ => _rng.Next()).ToList();
        if (!string.IsNullOrEmpty(_lastName))
        {
            var last = order.FirstOrDefault(p => p.name == _lastName);
            if (last.name != null) { order.Remove(last); order.Add(last); }
        }
        foreach (var p in order)
        {
            try
            {
                var meta = p.fetch(Terms[_rng.Next(Terms.Length)]);
                if (meta != null)
                {
                    _lastName = p.name;
                    AppEnv.Log($"名画抓取（{p.name}）：{meta.Artist}《{meta.Title}》");
                    return meta;
                }
            }
            catch (Exception ex) { AppEnv.Log($"名画抓取失败（{p.name}）：{ex.Message}", "WARN"); }
        }
        return null;
    }

    // ---------- 公共 ----------

    private static string S(JsonElement r, string name)
    {
        try { return r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()?.Trim() ?? "" : ""; }
        catch { return ""; }
    }

    /// <summary>供策展清单（CuratedArt）复用的图片下载。</summary>
    internal static bool DownloadImage(string url, string target) => Download(url, target);

    private static bool Download(string url, string target, string? ua = null)
    {
        HttpRequestMessage req = new(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(ua)) req.Headers.UserAgent.ParseAdd(ua);
        using var resp = Http.SendAsync(req).Result;
        if (!resp.IsSuccessStatusCode) return false;
        using var fs = File.Create(target);
        resp.Content.CopyToAsync(fs).Wait();
        return true;
    }

    private static ArtMeta? Save(string code, string key, string url, string title, string artist,
                                 string year, string style, string museum, string credit, string pageUrl = "", string ua = null)
    {
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title)) return null;
        Directory.CreateDirectory(ArtworkService.Folder);
        var file = $"{code}-{Sanitize(key)}.jpg";
        var target = Path.Combine(ArtworkService.Folder, file);
        if (!Download(url, target, ua) || new FileInfo(target).Length < 2048) return null;   // <2KB 多半是错误页
        return new ArtMeta
        {
            File = file,
            Title = title,
            Artist = string.IsNullOrEmpty(artist) ? Lang.T("art.unknown") : artist,
            Year = year,
            Style = style,
            Museum = museum,
            Credit = credit,
            Source = $"{code}:{key}",
        };
    }

    private static string Sanitize(string k)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in k) sb.Append(char.IsLetterOrDigit(c) || c == '-' ? c : '_');
        var s = sb.ToString();
        return s.Length > 60 ? s[..60] : s;
    }

    private static List<JsonElement> ArrayOf(JsonElement r, string name)
    {
        try { return r.TryGetProperty(name, out var a) && a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : new(); }
        catch { return new(); }
    }

    // ---------- NGA 随机源（复用本地 nga.json.gz 63k CC0 索引；单遍蓄水池采样，零常驻内存） ----------

    /// <summary>词表命中创作者/标题者优先蓄水池采样，无命中则全库随机；NGA IIIF 直取（免钥 CC0，CuratedArt 同源）。</summary>
    private static ArtMeta? FetchNga(string term)
    {
        var bundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "nga.json.gz");
        var tl = term.ToLowerInvariant();

        string? g = null, title = null, creator = null, date = null, medium = null, cls = null;
        int matched = 0, total = 0;
        try
        {
            using var fs = File.OpenRead(bundle);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var doc = JsonDocument.Parse(gz);
            foreach (var o in doc.RootElement.GetProperty("objects").EnumerateArray())
            {
                var og = S(o, "g");
                var ot = S(o, "t");
                if (og.Length == 0 || ot.Length == 0) continue;
                var oc = S(o, "c");
                var hit = oc.Length > 0 && (oc.ToLowerInvariant().Contains(tl) || ot.ToLowerInvariant().Contains(tl));
                if (!hit && matched > 0) continue;      // 已有命中池：只参与命中的抽样
                total++;
                if (hit) matched++;
                var pool = hit ? matched : total;
                if (_rng.Next(pool) == 0)
                { g = og; title = ot; creator = oc; date = S(o, "d"); medium = S(o, "m"); cls = S(o, "l"); }
            }
        }
        catch (Exception ex) { AppEnv.Log($"NGA 索引扫描失败：{ex.Message}", "WARN"); return null; }
        if (g == null || title == null) return null;
        return Save("nga", g, $"https://api.nga.gov/iiif/{g}/full/1600,/0/default.jpg", title,
            string.IsNullOrEmpty(creator) ? "Unknown" : creator, date,
            string.IsNullOrEmpty(medium) ? cls : medium, "National Gallery of Art, Washington", "");
    }

    // ---------- SMK 丹麦国家画廊（api.smk.dk 免钥，public_domain 服务端过滤，image_native 直链 JPEG） ----------

    private sealed record SmkPick(string Acc, string Image, string Title, string Artist, string Year);

    /// <summary>Extract fields while the source JsonDocument is still alive (caller guarantees scope).</summary>
    private static SmkPick? PickSmk(System.Text.Json.JsonElement items, Random rng)
    {
        var list = items.EnumerateArray()
            .Where(it => it.TryGetProperty("image_native", out var im) && im.ValueKind == JsonValueKind.String
                         && !string.IsNullOrEmpty(im.GetString()))
            .ToList();
        if (list.Count == 0) return null;
        var pick = list[rng.Next(list.Count)];

        var title = "";
        if (pick.TryGetProperty("titles", out var titles) && titles.ValueKind == JsonValueKind.Array)
        {
            foreach (var ti in titles.EnumerateArray())
            {
                var lang = S(ti, "language");
                var val = S(ti, "title");
                if (val.Length == 0) continue;
                if (lang.Contains("english") || lang.Contains("engelsk")) { title = val; break; }
                if (title.Length == 0) title = val;
            }
        }
        if (title.Length == 0) return null;

        var artist = "";
        if (pick.TryGetProperty("artist", out var artists) && artists.ValueKind == JsonValueKind.Array)
            artist = string.Join(", ", artists.EnumerateArray().Select(a => a.ValueKind == JsonValueKind.String ? a.GetString() ?? "" : a.ToString()).Where(s => s.Length > 0));

        var year = "";
        if (pick.TryGetProperty("production_date", out var pds) && pds.ValueKind == JsonValueKind.Array && pds.GetArrayLength() > 0)
            year = S(pds[0], "period");

        return new SmkPick(S(pick, "object_number"), S(pick, "image_native"), title, artist, year);
    }

    private static ArtMeta? FetchSmk(string term)
    {
        // fields param is broken server-side (localized variants 500); full records, filter client-side
        var u = $"https://api.smk.dk/api/v1/art/search/?keys={Uri.EscapeDataString(term)}" +
                "&filters=%5Bhas_image%3Atrue%5D%2C%5Bpublic_domain%3Atrue%5D&rows=15&offset=0";
        SmkPick? pick;
        using (var resp = Http.GetAsync(u).Result)
        {
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(resp.Content.ReadAsStringAsync().Result);
            pick = doc.RootElement.TryGetProperty("items", out var items) ? PickSmk(items, _rng) : null;
        }
        if (pick == null)
        {
            // term miss -> keys=* random (still has_image+public_domain filtered)
            var all = Http.GetAsync(u.Replace("keys=" + Uri.EscapeDataString(term), "keys=%2A")).Result;
            using var doc2 = JsonDocument.Parse(all.Content.ReadAsStringAsync().Result);
            pick = doc2.RootElement.TryGetProperty("items", out var items2) ? PickSmk(items2, _rng) : null;
        }
        if (pick == null) return null;
        return Save("smk", pick.Acc, pick.Image, pick.Title,
            string.IsNullOrEmpty(pick.Artist) ? "Unknown" : pick.Artist, pick.Year, "Painting",
            "Statens Museum for Kunst", "");
    }

    // ---------- The Met（collectionapi.metmuseum.org，原实现迁移） ----------

    private static ArtMeta? FetchMet(string term)
    {
        const string api = "https://collectionapi.metmuseum.org/public/collection/v1";
        const string searchApi = api + ".1";   // v1 search 于 2026-10-01 退役（410），v1.1 Elastic 版响应兼容
        var searchJson = Http.GetStringAsync($"{searchApi}/search?q={Uri.EscapeDataString(term)}&hasImages=true").Result;
        using var doc = JsonDocument.Parse(searchJson);
        var ids = doc.RootElement.TryGetProperty("objectIDs", out var arr) && arr.ValueKind == JsonValueKind.Array
            ? arr.EnumerateArray().Select(v => v.GetInt32()).ToList() : new List<int>();
        foreach (var id in ids.OrderBy(_ => _rng.Next()).Take(12))
        {
            try
            {
                var r = JsonDocument.Parse(Http.GetStringAsync($"{api}/objects/{id}").Result).RootElement;
                if (!r.TryGetProperty("isPublicDomain", out var pd) || !pd.GetBoolean()) continue;
                var img = S(r, "primaryImageSmall") is { Length: > 0 } s ? s : S(r, "primaryImage");
                var style = S(r, "classification");
                var period = S(r, "period");
                if (!string.IsNullOrEmpty(period)) style = string.IsNullOrEmpty(style) ? period : $"{style} · {period}";
                var meta = Save("met", id.ToString(), img, S(r, "title"), S(r, "artistDisplayName"),
                                S(r, "objectDate"), style, "The Metropolitan Museum of Art, New York", S(r, "creditLine"));
                if (meta != null) return meta;
            }
            catch { }
        }
        return null;
    }

    // ---------- Cleveland Museum of Art（openaccess-api，cc0 服务端过滤） ----------

    private static ArtMeta? FetchCleveland(string term)
    {
        var url = $"https://openaccess-api.clevelandart.org/api/artworks?q={Uri.EscapeDataString(term)}" +
                  "&cc0=1&has_image=1&limit=25";
        var data = JsonDocument.Parse(Http.GetStringAsync(url).Result).RootElement.GetProperty("data").EnumerateArray().ToList();
        foreach (var r in data.OrderBy(_ => _rng.Next()))
        {
            string img = "";
            try
            {
                var web = r.GetProperty("images").GetProperty("web");
                img = web.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
            }
            catch { }
            string artist = "";
            try { artist = S(r.GetProperty("creators").EnumerateArray().First(), "description"); } catch { }
            var meta = Save("cleveland", S(r, "accession_number"), img, S(r, "title"), artist,
                            S(r, "creation_date"), S(r, "type"), "The Cleveland Museum of Art", S(r, "creditline"),
                            S(r, "url"));
            if (meta != null) return meta;
        }
        return null;
    }

    // ---------- Art Institute of Chicago（api.artic.edu，公有领域 + IIIF） ----------

    private static ArtMeta? FetchAic(string term)
    {
        var url = "https://api.artic.edu/api/v1/artworks?q=" + Uri.EscapeDataString(term) +
                  "&limit=25&fields=id,title,artist_title,date_display,image_id,is_public_domain,credit_line";
        var data = JsonDocument.Parse(Http.GetStringAsync(url).Result).RootElement.GetProperty("data").EnumerateArray().ToList();
        foreach (var r in data.OrderBy(_ => _rng.Next()))
        {
            if (!r.TryGetProperty("is_public_domain", out var pd) || !pd.GetBoolean()) continue;
            var imageId = S(r, "image_id");
            if (string.IsNullOrEmpty(imageId)) continue;
            var meta = Save("aic", S(r, "id"),
                            $"https://www.artic.edu/iiif/2/{imageId}/full/843,/0/default.jpg",
                            S(r, "title"), S(r, "artist_title"), S(r, "date_display"), "Painting",
                            "The Art Institute of Chicago", S(r, "credit_line"),
                            $"https://www.artic.edu/artworks/{S(r, "id")}");
            if (meta != null) return meta;
        }
        return null;
    }

    // ---------- NGA guid 流式直查（供策展清单 CuratedArt；不缓存整表，用完即释） ----------

    /// <summary>NGA 本地索引 id → IIIF guid。按需解压流式查找，不常驻内存。</summary>
    internal static string? NgaGuidById(string id)
    {
        var bundle = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "nga.json.gz");
        if (!File.Exists(bundle)) return null;
        using var fs = File.OpenRead(bundle);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var doc = JsonDocument.Parse(gz);
        foreach (var o in doc.RootElement.GetProperty("objects").EnumerateArray())
            if (S(o, "i") == id && o.TryGetProperty("g", out var g)) return g.GetString();
        return null;
    }

    // ---------- Rijksmuseum（免钥 Data Services，Linked-Art 三跳到 IIIF） ----------

    private static HttpRequestMessage ReqLd(string url)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.ParseAdd("application/ld+json");
        return req;
    }

    private static JsonElement FetchLd(string url)
    {
        var json = Http.SendAsync(ReqLd(url)).Result.EnsureSuccessStatusCode().Content.ReadAsStringAsync().Result;
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static ArtMeta? FetchRijks(string term)
    {
        // 搜索是分面的（无全文 q）：creator 与 title 各查一路并合并
        var ids = new List<string>();
        foreach (var facet in new[] { "creator", "title" })
        {
            try
            {
                var u = $"https://data.rijksmuseum.nl/search/collection?{facet}={Uri.EscapeDataString(term)}&imageAvailable=true";
                using var doc = JsonDocument.Parse(Http.SendAsync(ReqLd(u)).Result.Content.ReadAsStringAsync().Result);
                foreach (var it in ArrayOf(doc.RootElement.Clone(), "orderedItems"))
                {
                    var id = S(it, "id");
                    var m = System.Text.RegularExpressions.Regex.Match(id, @"/(\d+)/?$");
                    if (m.Success) ids.Add(m.Groups[1].Value);
                }
            }
            catch { }
        }
        foreach (var id in ids.Distinct().OrderBy(_ => _rng.Next()).Take(6))
        {
            try
            {
                var obj = FetchLd($"https://id.rijksmuseum.nl/{id}");
                // object → shows[0] → digitally_shown_by[0] → access_point[0] = IIIF 图
                var shows = ArrayOf(obj, "shows");
                if (shows.Count == 0) continue;
                var visual = FetchLd(S(shows[0], "id"));
                var digitals = ArrayOf(visual, "digitally_shown_by");
                if (digitals.Count == 0) continue;
                var digital = FetchLd(S(digitals[0], "id"));
                var points = ArrayOf(digital, "access_point");
                if (points.Count == 0) continue;
                var img = S(points[0], "id").Replace("/full/max/", "/full/1200,/").Replace("/full/full/", "/full/1200,/");
                string title = "", artist = "", year = "";
                // 标题优先取英文标注（AAT 300388277），否则第一条
                var names = ArrayOf(obj, "identified_by");
                foreach (var n in names)
                {
                    var hasEn = ArrayOf(n, "language").Any(l => S(l, "id").Contains("300388277"));
                    if (hasEn && !string.IsNullOrEmpty(S(n, "content"))) { title = S(n, "content"); break; }
                }
                if (string.IsNullOrEmpty(title))
                    foreach (var n in names)
                        if (!string.IsNullOrEmpty(S(n, "content"))) { title = S(n, "content"); break; }
                if (obj.TryGetProperty("produced_by", out var produced) && produced.ValueKind == JsonValueKind.Object)
                {
                    foreach (var carrier in new[] { produced }.Concat(ArrayOf(produced, "part")))
                    {
                        foreach (var actor in ArrayOf(carrier, "carried_out_by"))
                        {
                            foreach (var note in ArrayOf(actor, "notation"))
                                if (S(note, "@language") == "en" && !string.IsNullOrEmpty(S(note, "@value")))
                                { artist = S(note, "@value"); break; }
                            if (string.IsNullOrEmpty(artist)) artist = S(actor, "content");
                            if (!string.IsNullOrEmpty(artist)) break;
                        }
                        if (!string.IsNullOrEmpty(artist)) break;
                    }
                    if (produced.TryGetProperty("timespan", out var ts)) year = S(ts, "begin_of_the_end");
                }
                var meta = Save("rijks", id, img, title, artist, year, "Painting",
                                "Rijksmuseum, Amsterdam", "", $"https://id.rijksmuseum.nl/{id}");
                if (meta != null) return meta;
            }
            catch { }
        }
        return null;
    }
}
