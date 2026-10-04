using System.IO;

namespace Falco.App.Services;

/// <summary>
/// 策展清单（全球最著名 50 幅画作）：只收录画家卒年 ≤1955 的公有领域作品，图源全部验证可得。
/// 图片路由两类：
///   nga:<NGA索引id>   — 本地 nga.json.gz 索引 → NGA IIIF 直取（免钥、CC0、无需代理）
///   commons:<文件名>  — Wikimedia Commons 公有领域摄影图（卢浮宫/乌菲兹/普拉多等镇馆作；
///                       需可连通 Wikimedia，失败静默跳过，由五馆随机兜底）
/// Commons 文件名已经 API 逐一验证存在；zh 字段为中文展示，En 字段供英文界面。
/// </summary>
internal static class CuratedArt
{
    public sealed record Entry(string Slug, string Artist, string ArtistEn, string Title, string TitleEn,
                               string Year, string Style, string Museum, string MuseumEn, string Route);

    internal static readonly Entry[] Items =
    {
        // ---------- 文艺复兴 ----------
        new("da-vinci-mona-lisa", "列奥纳多·达·芬奇", "Leonardo da Vinci", "蒙娜丽莎", "Mona Lisa", "1503", "文艺复兴", "卢浮宫", "Musée du Louvre, Paris", "commons:Mona Lisa, by Leonardo da Vinci, from C2RMF retouched.jpg"),
        new("da-vinci-lady-ermine", "列奥纳多·达·芬奇", "Leonardo da Vinci", "抱银鼠的女子", "Lady with an Ermine", "c. 1489/1491", "文艺复兴", "恰尔托雷斯基博物馆, 克拉科夫", "Czartoryski Museum, Kraków", "commons:Lady with an Ermine - Leonardo da Vinci - Google Art Project.jpg"),
        new("da-vinci-last-supper", "列奥纳多·达·芬奇", "Leonardo da Vinci", "最后的晚餐", "The Last Supper", "1495/1498", "文艺复兴", "恩宠圣母教堂, 米兰", "Santa Maria delle Grazie, Milan", "commons:The Last Supper - Leonardo Da Vinci - High Resolution 32x16.jpg"),
        new("michelangelo-creation", "米开朗基罗", "Michelangelo", "创造亚当", "The Creation of Adam", "1512", "文艺复兴", "西斯廷礼拜堂", "Sistine Chapel, Vatican", "commons:Michelangelo - Creation of Adam (cropped).jpg"),
        new("raphael-athens", "拉斐尔", "Raphael", "雅典学院", "The School of Athens", "1511", "文艺复兴", "使徒宫, 梵蒂冈", "Apostolic Palace, Vatican", "commons:\"The School of Athens\" by Raffaello Sanzio da Urbino.jpg"),
        new("raphael-sistine-madonna", "拉斐尔", "Raphael", "西斯廷圣母", "The Sistine Madonna", "c. 1513/1514", "文艺复兴", "历代大师画廊, 德累斯顿", "Gemäldegalerie Alte Meister, Dresden", "commons:Raphael - The Sistine Madonna - Google Art Project.jpg"),
        new("botticelli-venus", "桑德罗·波提切利", "Sandro Botticelli", "维纳斯的诞生", "The Birth of Venus", "c. 1485", "文艺复兴", "乌菲兹美术馆", "Uffizi Gallery, Florence", "commons:Sandro Botticelli - La nascita di Venere - Google Art Project - edited.jpg"),
        new("botticelli-primavera", "桑德罗·波提切利", "Sandro Botticelli", "春", "Primavera", "c. 1480", "文艺复兴", "乌菲兹美术馆", "Uffizi Gallery, Florence", "commons:Primavera (Botticelli).jpg"),
        // ---------- 北方文艺复兴 ----------
        new("van-eyck-arnolfini", "扬·凡·艾克", "Jan van Eyck", "阿尔诺芬尼夫妇像", "The Arnolfini Portrait", "1434", "北方文艺复兴", "伦敦国家美术馆", "National Gallery, London", "commons:Van Eyck - Arnolfini Portrait.jpg"),
        new("van-eyck-annunciation", "扬·凡·艾克", "Jan van Eyck", "受胎告知", "The Annunciation", "c. 1434/1436", "北方文艺复兴", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:46"),
        new("durer-selfportrait", "阿尔布雷希特·丢勒", "Albrecht Dürer", "二十八岁自画像", "Self-Portrait at Twenty-Eight", "1500", "北方文艺复兴", "老绘画陈列馆", "Alte Pinakothek, Munich", "commons:Albrecht Dürer - 1500 self-portrait (High resolution and detail).jpg"),
        new("bosch-earthly-delights", "耶罗尼米斯·博斯", "Hieronymus Bosch", "人间乐园", "The Garden of Earthly Delights", "c. 1490/1510", "北方文艺复兴", "普拉多美术馆", "Museo del Prado, Madrid", "commons:The Garden of Earthly Delights by Bosch High Resolution.jpg"),
        new("bruegel-hunters", "老彼得·勃鲁盖尔", "Pieter Bruegel the Elder", "雪中猎人", "Hunters in the Snow", "1565", "北方文艺复兴", "艺术史博物馆", "Kunsthistorisches Museum, Vienna", "commons:Pieter Bruegel the Elder - Hunters in the Snow (Winter) - Google Art Project.jpg"),
        // ---------- 巴洛克 ----------
        new("vermeer-girl-pearl", "约翰内斯·维米尔", "Johannes Vermeer", "戴珍珠耳环的少女", "Girl with a Pearl Earring", "c. 1665", "巴洛克", "毛里茨之家, 海牙", "Mauritshuis, The Hague", "commons:1665 Girl with a Pearl Earring.jpg"),
        new("vermeer-milkmaid", "约翰内斯·维米尔", "Johannes Vermeer", "倒牛奶的女仆", "The Milkmaid", "c. 1658/1660", "巴洛克", "荷兰国立博物馆", "Rijksmuseum, Amsterdam", "commons:Johannes Vermeer - Het melkmeisje - Google Art Project.jpg"),
        new("vermeer-art-painting", "约翰内斯·维米尔", "Johannes Vermeer", "绘画艺术", "The Art of Painting", "c. 1666/1668", "巴洛克", "艺术史博物馆", "Kunsthistorisches Museum, Vienna", "commons:Jan Vermeer - The Art of Painting - Google Art Project.jpg"),
        new("vermeer-balance", "约翰内斯·维米尔", "Johannes Vermeer", "持天平的女人", "Woman Holding a Balance", "c. 1664", "巴洛克", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:1236"),
        new("rembrandt-nightwatch", "伦勃朗", "Rembrandt van Rijn", "夜巡", "The Night Watch", "1642", "巴洛克", "荷兰国立博物馆", "Rijksmuseum, Amsterdam", "commons:La ronda de noche, por Rembrandt van Rijn.jpg"),
        new("caravaggio-musicians", "卡拉瓦乔", "Caravaggio", "乐师", "The Musicians", "c. 1595", "巴洛克", "大都会艺术博物馆", "The Met, New York", "commons:The Musicians MET DP-687-001.jpg"),
        new("caravaggio-matthew", "卡拉瓦乔", "Caravaggio", "圣马太蒙召", "The Calling of Saint Matthew", "1599/1600", "巴洛克", "圣王路易堂, 罗马", "San Luigi dei Francesi, Rome", "commons:Caravaggio, Michelangelo Merisi da - The Calling of Saint Matthew - 1599-1600 (hi res).jpg"),
        new("rubens-daniel", "鲁本斯", "Peter Paul Rubens", "狮穴中的但以理", "Daniel in the Lions' Den", "c. 1614/1616", "巴洛克", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:50298"),
        new("rubens-descent", "鲁本斯", "Peter Paul Rubens", "下十字架", "The Descent from the Cross", "1612/1614", "巴洛克", "圣母主教座堂, 安特卫普", "Cathedral of Our Lady, Antwerp", "commons:Peter Paul Rubens - Descent from the Cross - WGA20212.jpg"),
        new("velazquez-meninas", "迭戈·委拉斯开兹", "Diego Velázquez", "宫娥", "Las Meninas", "1656", "巴洛克", "普拉多美术馆", "Museo del Prado, Madrid", "commons:Las Meninas, by Diego Velázquez, from Prado in Google Earth.jpg"),
        // ---------- 17-19 世纪 ----------
        new("poussin-arcadia", "尼古拉·普桑", "Nicolas Poussin", "阿卡迪亚的牧人", "Et in Arcadia Ego", "1637/1638", "古典主义", "卢浮宫", "Musée du Louvre, Paris", "commons:Nicolas Poussin - Et in Arcadia ego (deuxième version).jpg"),
        new("david-marat", "雅克-路易·大卫", "Jacques-Louis David", "马拉之死", "The Death of Marat", "1793", "新古典主义", "比利时皇家美术博物馆, 布鲁塞尔", "Royal Museums of Fine Arts of Belgium, Brussels", "commons:Death of Marat by David.jpg"),
        new("david-napoleon", "雅克-路易·大卫", "Jacques-Louis David", "拿破仑翻越阿尔卑斯山", "Napoleon Crossing the Alps", "1801", "新古典主义", "美景宫", "Belvedere, Vienna", "commons:Napoleon at the Great St. Bernard - Jacques-Louis David - Google Cultural Institute.jpg"),
        new("gericault-raft", "泰奥多尔·借里柯", "Théodore Géricault", "梅杜萨之筏", "The Raft of the Medusa", "1818/1819", "浪漫主义", "卢浮宫", "Musée du Louvre, Paris", "commons:JEAN LOUIS THÉODORE GÉRICAULT - La Balsa de la Medusa (Museo del Louvre, 1818-19).jpg"),
        new("delacroix-liberty", "欧仁·德拉克罗瓦", "Eugène Delacroix", "自由引导人民", "Liberty Leading the People", "1830", "浪漫主义", "卢浮宫", "Musée du Louvre, Paris", "commons:Eugène Delacroix - La liberté guidant le peuple.jpg"),
        new("constable-haywain", "约翰·康斯太勃尔", "John Constable", "干草车", "The Hay Wain", "1821", "浪漫主义", "伦敦国家美术馆", "National Gallery, London", "commons:John Constable - The Hay Wain (1821).jpg"),
        new("turner-keelmen", "威廉·透纳", "J. M. W. Turner", "月下运煤工", "Keelmen Heaving in Coals by Moonlight", "1835", "浪漫主义", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:1225"),
        new("goya-sabasa", "弗朗西斯科·戈雅", "Francisco Goya", "萨巴莎·加西亚夫人", "Señora Sabasa Garcia", "c. 1806/1811", "浪漫主义", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:95"),
        new("goya-third-may", "弗朗西斯科·戈雅", "Francisco Goya", "1808年5月3日", "The Third of May 1808", "1814", "浪漫主义", "普拉多美术馆", "Museo del Prado, Madrid", "commons:El Tres de Mayo, by Francisco de Goya, from Prado in Google Earth.jpg"),
        // ---------- 印象派前后 ----------
        new("millet-gleaners", "让-弗朗索瓦·米勒", "Jean-François Millet", "拾穗者", "The Gleaners", "1857", "现实主义", "奥赛博物馆", "Musée d'Orsay, Paris", "commons:Jean-François Millet - Gleaners - Google Art Project.jpg"),
        new("millet-angelus", "让-弗朗索瓦·米勒", "Jean-François Millet", "晚祷", "The Angelus", "1857/1859", "现实主义", "奥赛博物馆", "Musée d'Orsay, Paris", "commons:Jean-François Millet - The Angelus - Google Art Project.jpg"),
        new("manet-oldmusician", "爱德华·马奈", "Édouard Manet", "老乐师", "The Old Musician", "1862", "现实主义", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:46637"),
        new("monet-sunrise", "克劳德·莫奈", "Claude Monet", "日出·印象", "Impression, Sunrise", "1872", "印象派", "马摩丹莫奈美术馆", "Musée Marmottan Monet, Paris", "commons:Monet - Impression, Sunrise.jpg"),
        new("degas-danceclass", "埃德加·德加", "Edgar Degas", "舞蹈课", "The Dance Class", "c. 1873", "印象派", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:165300"),
        new("seurat-jatte", "乔治·修拉", "Georges Seurat", "大碗岛的星期天下午", "A Sunday on La Grande Jatte", "1884/1886", "新印象派", "芝加哥艺术学院", "Art Institute of Chicago", "commons:Georges Seurat - A Sunday on La Grande Jatte -- 1884 - Google Art Project.jpg"),
        // ---------- 后印象派与现代 ----------
        new("cezanne-stilllife", "保罗·塞尚", "Paul Cézanne", "苹果与桃子静物", "Still Life with Apples and Peaches", "c. 1905", "后印象派", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:45986"),
        new("vangogh-selfportrait", "文森特·梵高", "Vincent van Gogh", "自画像", "Self-Portrait", "1889", "后印象派", "华盛顿国家美术馆", "National Gallery of Art, Washington", "nga:106382"),
        new("vangogh-starrynight", "文森特·梵高", "Vincent van Gogh", "星月夜", "The Starry Night", "1889", "后印象派", "纽约现代艺术博物馆", "MoMA, New York", "commons:Vincent van Gogh - Starry Night - Google Art Project.jpg"),
        new("vangogh-sunflowers", "文森特·梵高", "Vincent van Gogh", "向日葵", "Sunflowers", "1888", "后印象派", "伦敦国家美术馆", "National Gallery, London", "commons:Vincent Willem van Gogh 127.jpg"),
        new("vangogh-irises", "文森特·梵高", "Vincent van Gogh", "鸢尾花", "Irises", "1889", "后印象派", "盖蒂中心", "Getty Center, Los Angeles", "commons:Irises-Vincent van Gogh.jpg"),
        new("wood-american-gothic", "格兰特·伍德", "Grant Wood", "美国哥特式", "American Gothic", "1930", "地区主义", "芝加哥艺术学院", "Art Institute of Chicago", "commons:Grant Wood - American Gothic - Google Art Project.jpg"),
        new("klimt-kiss", "古斯塔夫·克里姆特", "Gustav Klimt", "吻", "The Kiss", "1908", "维也纳分离派", "美景宫", "Belvedere, Vienna", "commons:The Kiss - Gustav Klimt - Google Cultural Institute.jpg"),
        new("munch-scream", "爱德华·蒙克", "Edvard Munch", "呐喊", "The Scream", "1893", "表现主义", "奥斯陆国家博物馆", "National Museum, Oslo", "commons:The Scream.jpg"),
        new("kandinsky-composition8", "瓦西里·康定斯基", "Wassily Kandinsky", "构成第八号", "Composition 8", "1923", "抽象艺术", "古根海姆美术馆", "Guggenheim Museum, New York", "commons:Kandinsky - Composition 8, July 1923.jpg"),
        new("mondrian-redblue", "皮特·蒙德里安", "Piet Mondrian", "红、蓝、黄的构成 II", "Composition II in Red, Blue, and Yellow", "1930", "抽象艺术", "", "", "commons:Piet Mondriaan, 1930 - Mondrian Composition II in Red, Blue, and Yellow.jpg"),
        new("hokusai-greatwave", "葛饰北斋", "Katsushika Hokusai", "神奈川冲浪里", "The Great Wave off Kanagawa", "c. 1831", "浮世绘", "大都会艺术博物馆", "The Met, New York", "commons:Great Wave off Kanagawa2.jpg"),
        new("matisse-dance", "亨利·马蒂斯", "Henri Matisse", "舞蹈（第二版）", "Dance (II)", "1910", "野兽派", "冬宫博物馆", "Hermitage Museum, St. Petersburg", "commons:La Danse II, par Henri Matisse.jpg"),
    };

    /// <summary>在 ArtworkService.Fill 内同步执行（后台线程）：清退换版条目 → 补齐清单中尚未入库的画作。</summary>
    public static void Fill()
    {
        var validSlugs = Items.Select(i => $"curated:{i.Slug}").ToHashSet();
        ArtworkService.PruneCurated(validSlugs);   // 清单换版：移除不在新清单中的旧策展条目与图片

        var sources = ArtworkService.ReadCatalog()
            .Select(m => m.Source).Where(s => !string.IsNullOrEmpty(s)).ToHashSet();
        foreach (var e in Items)
        {
            if (sources.Contains($"curated:{e.Slug}")) continue;
            var meta = Fetch(e);
            if (meta == null) { AppEnv.Log($"策展画作跳过：{e.Artist}《{e.Title}》（{e.Route}）"); continue; }
            ArtworkService.AppendCatalog(meta);
            sources.Add(meta.Source!);
            AppEnv.Log($"策展画作入库：{meta.Artist}《{meta.Title}》（{meta.Year}）· {meta.Museum}");
            Thread.Sleep(1200);   // 温和限速
        }
    }

    private static ArtMeta? Fetch(Entry e)
    {
        try
        {
            Directory.CreateDirectory(ArtworkService.Folder);
            var file = $"curated-{e.Slug}.jpg";
            var target = Path.Combine(ArtworkService.Folder, file);
            var url = e.Route.StartsWith("nga:", StringComparison.Ordinal)
                ? NgaImageUrl(e.Route[4..])
                : "https://commons.wikimedia.org/wiki/Special:FilePath/" +
                  Uri.EscapeDataString(e.Route["commons:".Length..]) + "?width=1600";
            if (string.IsNullOrEmpty(url)) return null;
            if (!MuseumApi.DownloadImage(url, target) || new FileInfo(target).Length < 4096) return null;
            return new ArtMeta
            {
                File = file,
                Title = e.Title,
                Artist = e.Artist,
                Year = e.Year,
                Style = e.Style,
                Museum = e.Museum,
                Credit = Lang.T("art.credit.openaccess"),
                Source = $"curated:{e.Slug}",
                TitleEn = e.TitleEn,
                ArtistEn = e.ArtistEn,
                MuseumEn = e.MuseumEn,
            };
        }
        catch { return null; }
    }

    /// <summary>NGA 本地索引 id → IIIF 直链（与 MuseumApi.FetchNga 同一数据源）。</summary>
    private static string? NgaImageUrl(string id)
    {
        var guid = MuseumApi.NgaGuidById(id);
        return string.IsNullOrEmpty(guid) ? null : $"https://api.nga.gov/iiif/{guid}/full/1600,/0/default.jpg";
    }
}
