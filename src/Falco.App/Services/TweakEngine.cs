using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Falco.App.Services;

/// <summary>sc.exe 封装：查询/启停/配置服务（避免引入 ServiceProcess NuGet 包）。</summary>
public static class ScService
{
    public static string StartType(string name)   // "Disabled" / "Automatic" / "Manual" / null
    {
        var (_, o) = Shell.Run("sc.exe", $"qc {name}");
        foreach (var line in o.Split('\n'))
            if (line.Contains("START_TYPE"))
            {
                if (line.Contains("DISABLED")) return "Disabled";
                if (line.Contains("AUTO_START")) return "Automatic";
                if (line.Contains("DEMAND_START")) return "Manual";
                if (line.Contains("SYSTEM_START")) return "System";
                if (line.Contains("BOOT_START")) return "Boot";
            }
        return null;
    }

    public static string State(string name)       // "Running" / "Stopped" / null
    {
        var (_, o) = Shell.Run("sc.exe", $"query {name}");
        foreach (var line in o.Split('\n'))
            if (line.Contains("STATE"))
                return line.Contains("RUNNING") ? "Running" : "Stopped";
        return null;
    }

    public static bool TryConfig(string name, string startType)   // startType: Disabled/Automatic/Manual
    {
        var (code, o) = Shell.Run("sc.exe", $"config {name} start= {startType.ToLower()}");
        if (code != 0) AppEnv.Log($"sc config {name} 失败：{o}", "WARN");
        return code == 0;
    }

    public static void TryStop(string name) => Shell.Run("sc.exe", $"stop {name}");
    public static void TryStart(string name) => Shell.Run("sc.exe", $"start {name}");
}

/// <summary>优化项修改记录（与 PS 版 tweaks-state.json 的字段名完全一致）。</summary>
public class TweakRec
{
    [JsonPropertyName("path")] public string Path { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("kind")] public string Kind { get; set; }
    [JsonPropertyName("desired")] public string Desired { get; set; }
    [JsonPropertyName("original")] public string Original { get; set; }
    [JsonPropertyName("originalExists")] public bool OriginalExists { get; set; }
    [JsonPropertyName("startMode")] public string StartMode { get; set; }
    [JsonPropertyName("state")] public string State { get; set; }
}

public class TweakDef
{
    public string Id, Name, Category, Risk, Hint;
    public Func<string> Detect;
    public Func<int, int, bool> Apply;   // (ahStart, ahEnd)
    public string DesiredStartType;      // 服务类项的漂移检测
}

public class TweakRow
{
    public string Id, Name, Risk, State;
    public bool Backed, Drifted;
}

/// <summary>
/// 优化引擎：六个优化项的检测/应用/通用还原/漂移检测，
/// 注册表写入带回读验证与自动回滚（V2 验证引擎的 C# 移植），
/// 数据格式与 PS GUI 版互通（tweaks-state.json / Backups）。
/// </summary>
public class TweakEngine
{
    public int PassCount, FailCount;
    private List<TweakRec> _records;

    private readonly List<TweakDef> _table;

    public TweakEngine()
    {
        _table = new List<TweakDef>
        {
        new TweakDef {
            Id = "DO-001", Name = Lang.T("tweak.do001.name"), Category = Lang.T("tweak.cat.net"), Risk = "High",
            Hint = Lang.T("tweak.do001.hint"),
            Detect = () => {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config");
                var v = k?.GetValue("DODownloadMode");
                if (v == null) return Lang.T("opt.state.default");
                return v.ToString() == "0" ? Lang.T("opt.state.optimized") : Lang.F("opt.state.defaultv", v.ToString());
            },
            Apply = (_, _) => OptimizeDeliveryOptimization() },
        new TweakDef {
            Id = "BG-001", Name = Lang.T("tweak.bg001.name"), Category = Lang.T("tweak.cat.net"), Risk = "Low",
            Hint = Lang.T("tweak.bg001.hint"),
            Detect = () => {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Policies\Microsoft\Windows\AppPrivacy");
                return k?.GetValue("LetAppsRunInBackground")?.ToString() == "2" ? Lang.T("opt.state.optimized") : Lang.T("opt.state.default");
            },
            Apply = (_, _) => OptimizeBackgroundApps() },
        new TweakDef {
            Id = "VIS-001", Name = Lang.T("tweak.vis001.name"), Category = Lang.T("tweak.cat.sys"), Risk = "Low",
            Hint = Lang.T("tweak.vis001.hint"),
            Detect = () => {
                using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects");
                return k?.GetValue("VisualFXSetting")?.ToString() == "3" ? Lang.T("opt.state.optimized") : Lang.T("opt.state.default");
            },
            Apply = (_, _) => OptimizeVisualEffects() },
        new TweakDef {
            Id = "AH-001", Name = Lang.T("tweak.ah001.name"), Category = Lang.T("tweak.cat.wu"), Risk = "Low",
            Hint = Lang.T("tweak.ah001.hint"),
            Detect = () => {
                using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings");
                var s = k?.GetValue("ActiveHoursStart"); var e = k?.GetValue("ActiveHoursEnd");
                return s == null ? Lang.T("opt.state.default") : Lang.F("opt.state.set", s?.ToString(), e?.ToString());
            },
            Apply = (s, e) => SetActiveHours(s, e) },
        new TweakDef {
            Id = "SYS-001", Name = Lang.T("tweak.sys001.name"), Category = Lang.T("tweak.cat.svc"), Risk = "High",
            Hint = Lang.T("tweak.sys001.hint"), DesiredStartType = "Disabled",
            Detect = () => ServiceDetect("SysMain"),
            Apply = (_, _) => DisableService("SysMain") },
        new TweakDef {
            Id = "WS-001", Name = Lang.T("tweak.ws001.name"), Category = Lang.T("tweak.cat.svc"), Risk = "High",
            Hint = Lang.T("tweak.ws001.hint"), DesiredStartType = "Disabled",
            Detect = () => ServiceDetect("WSearch"),
            Apply = (_, _) => DisableService("WSearch") },
        };
    }

    // ---------- 状态持久化（兼容 PS 版格式） ----------
    private static Dictionary<string, List<TweakRec>> ReadState()
    {
        try
        {
            if (File.Exists(AppEnv.TweakStateFile))
            {
                var doc = JsonDocument.Parse(File.ReadAllText(AppEnv.TweakStateFile));
                var result = new Dictionary<string, List<TweakRec>>();
                foreach (var p in doc.RootElement.EnumerateObject())
                {
                    var list = new List<TweakRec>();
                    foreach (var e in p.Value.EnumerateArray())
                        list.Add(JsonSerializer.Deserialize<TweakRec>(e.GetRawText()) ?? new TweakRec());
                    result[p.Name] = list;
                }
                return result;
            }
        }
        catch (Exception ex) { AppEnv.Log($"读取优化项状态失败：{ex.Message}", "WARN"); }
        return new Dictionary<string, List<TweakRec>>();
    }

    private static void WriteState(Dictionary<string, List<TweakRec>> state)
    {
        File.WriteAllText(AppEnv.TweakStateFile,
            JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
    }

    // ---------- 对外操作 ----------
    public List<TweakRow> DetectAll()
    {
        var state = ReadState();
        return _table.Select(t => new TweakRow
        {
            Id = t.Id, Name = t.Name, Risk = t.Risk,
            State = SafeDetect(t),
            Backed = state.ContainsKey(t.Id),
            Drifted = t.DesiredStartType != null && state.ContainsKey(t.Id)
                      && ScService.StartType(ServiceNameOf(t.Id)) != t.DesiredStartType
        }).ToList();
    }

    private static string ServiceNameOf(string tweakId) => tweakId switch { "SYS-001" => "SysMain", "WS-001" => "WSearch", _ => null };

    private static string SafeDetect(TweakDef t)
    {
        try { return t.Detect(); }
        catch (Exception ex) { return Lang.F("opt.state.detectfail", ex.Message); }
    }

    /// <summary>应用一个优化项（先全量备份，捕获记录入 state）。</summary>
    public (int pass, int fail) Apply(string id, int ahStart = 8, int ahEnd = 22)
    {
        var t = _table.FirstOrDefault(x => x.Id == id);
        if (t == null) { AppEnv.Log($"未知优化项：{id}", "ERROR"); return (0, 0); }
        PassCount = 0; FailCount = 0;
        _records = new List<TweakRec>();
        AppEnv.Log($"应用优化项：{t.Name}（{id}）");
        BackupAll();
        try { t.Apply(ahStart, ahEnd); }
        catch (Exception ex) { AppEnv.Log($"优化项执行异常：{ex.Message}", "ERROR"); FailCount++; }
        if (_records.Count > 0)
        {
            var state = ReadState();
            state[id] = _records;
            WriteState(state);
            AppEnv.Log($"优化项修改记录已保存：{id}");
        }
        return (PassCount, FailCount);
    }

    /// <summary>通用还原：按记录逐条恢复（服务恢复启动类型，注册表回写原值或移除新增）。</summary>
    public void Revert(string id)
    {
        var t = _table.FirstOrDefault(x => x.Id == id);
        if (t == null) return;
        var state = ReadState();
        if (!state.ContainsKey(id)) { AppEnv.Log($"「{t.Name}」没有修改记录，无需还原。", "WARN"); return; }
        AppEnv.Log($"开始还原：{t.Name}（{id}）");
        foreach (var r in state[id])
        {
            try
            {
                if (r.Kind == "service")
                {
                    if (!string.IsNullOrEmpty(r.StartMode))
                        ScService.TryConfig(r.Name, r.StartMode == "Auto" ? "Automatic" : r.StartMode);
                    if (r.State == "Running" && ScService.State(r.Name) != "Running") ScService.TryStart(r.Name);
                    AppEnv.Log($"服务已还原：{r.Name}（启动类型 {r.StartMode}）");
                }
                else if (r.OriginalExists)
                {
                    var kind = r.Kind == "String" ? RegistryValueKind.String : RegistryValueKind.DWord;
                    RegWrite(r.Path, r.Name, kind, r.Kind == "String" ? r.Original : int.TryParse(r.Original, out var i) ? i : 0);
                    AppEnv.Log($"注册表已还原：{r.Path}\\{r.Name} = {r.Original}");
                }
                else
                {
                    using var k = OpenKey(r.Path, writable: true);
                    k?.DeleteValue(r.Name, throwOnMissingValue: false);
                    AppEnv.Log($"已移除新增的注册表值：{r.Path}\\{r.Name}");
                }
            }
            catch (Exception ex) { AppEnv.Log($"还原失败（{r.Name}）：{ex.Message}", "ERROR"); }
        }
        state.Remove(id);
        WriteState(state);
        AppEnv.Log($"还原完成：{t.Name}");
    }

    public void RevertAll() => DetectAll().Where(r => r.Backed).Select(r => r.Id).ToList().ForEach(Revert);

    // ---------- 修改实现（与 PS 版等值） ----------
    private static RegistryKey OpenKey(string path, bool writable)
    {
        var hive = path.StartsWith("HKLM:") ? Registry.LocalMachine : Registry.CurrentUser;
        var sub = path[(path.IndexOf(':') + 1)..].TrimStart('\\');
        return writable ? hive.CreateSubKey(sub) : hive.OpenSubKey(sub);
    }

    private bool RegWrite(string path, string name, RegistryValueKind kind, object value)
    {
        using var k = OpenKey(path, true);
        k.SetValue(name, value, kind);
        var read = k.GetValue(name);
        if ($"{read}" == $"{value}") { PassCount++; AppEnv.Log($"验证 PASS：{path}\\{name} = {value}"); return true; }
        FailCount++; AppEnv.Log($"验证 FAIL：{path}\\{name}", "ERROR"); return false;
    }

    /// <summary>验证引擎：写前记旧值 → 写入 → 回读 → 失败重试一次 → 仍失败回滚。</summary>
    private bool RegVerified(string path, string name, RegistryValueKind kind, object value)
    {
        object old = null; var oldExists = false;
        using (var rk = OpenKey(path, false))
            if (rk != null) { old = rk.GetValue(name); oldExists = old != null; }
        var ok = RegWrite(path, name, kind, value);
        if (!ok)
        {
            AppEnv.Log($"验证未通过，重试一次：{path}\\{name}", "WARN");
            ok = RegWrite(path, name, kind, value);
        }
        if (ok)
        {
            _records.Add(new TweakRec { Path = path, Name = name, Kind = kind.ToString(), Desired = $"{value}", Original = oldExists ? $"{old}" : null, OriginalExists = oldExists });
            return true;
        }
        if (oldExists) { using var k = OpenKey(path, true); k.SetValue(name, old, kind); }
        else { using var k = OpenKey(path, true); k.DeleteValue(name, false); }
        AppEnv.Log($"验证 FAIL，已回滚：{path}\\{name}", "ERROR");
        return false;
    }

    private bool OptimizeDeliveryOptimization()
    {
        const string key = @"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config";
        BackupRegKey(key, "deliveryoptimization-before.reg");
        var ok = RegVerified(key, "DODownloadMode", RegistryValueKind.DWord, 0);
        AppEnv.Log("传递优化：DODownloadMode = 0（HTTP 直连，禁止 Internet P2P）");
        // DoSvc 保持可用（仅限制 P2P），与 PS 版一致
        if (ScService.TryConfig("DoSvc", "Automatic")) PassCount++;
        else FailCount++;
        return ok;
    }

    private bool OptimizeBackgroundApps()
    {
        const string key = @"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy";
        BackupRegKey(key, "appprivacy-before.reg");
        AppEnv.Log("后台应用策略：禁止商店应用在后台运行（部分应用通知可能受影响）。");
        return RegVerified(key, "LetAppsRunInBackground", RegistryValueKind.DWord, 2);
    }

    private bool OptimizeVisualEffects()
    {
        const string key = @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects";
        BackupRegKey(key, "visualeffects-before.reg");
        var ok = RegVerified(key, "VisualFXSetting", RegistryValueKind.DWord, 3);
        const string desk = @"HKCU:\Control Panel\Desktop";
        BackupRegKey(desk, "desktop-before.reg");
        ok &= RegVerified(desk, "MenuShowDelay", RegistryValueKind.String, "50");
        AppEnv.Log("视觉效果：调整为性能优先；菜单响应延迟 50ms。");
        return ok;
    }

    private bool SetActiveHours(int start, int end)
    {
        if (start is < 0 or > 23 || end is < 0 or > 23 || start == end) { AppEnv.Log("活动时间参数无效。", "ERROR"); return false; }
        const string key = @"HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
        BackupRegKey(key, "windowsupdate-before.reg");
        var ok = RegVerified(key, "ActiveHoursStart", RegistryValueKind.DWord, start);
        ok &= RegVerified(key, "ActiveHoursEnd", RegistryValueKind.DWord, end);
        AppEnv.Log($"Windows Update 活动时间：{start}:00 - {end}:00（不会关闭更新）。");
        return ok;
    }

    private static string ServiceDetect(string name)
        => ScService.StartType(name) == null ? Lang.T("opt.state.noservice") : ScService.StartType(name) == "Disabled" ? Lang.T("opt.state.optimized") : Lang.T("opt.state.default");

    private bool DisableService(string name)
    {
        BackupService(name);
        try
        {
            if (ScService.State(name) == "Running") ScService.TryStop(name);
            ScService.TryConfig(name, "Disabled");
        }
        catch (Exception ex) { AppEnv.Log($"无法设置 {name}：{ex.Message}", "WARN"); FailCount++; return false; }
        var ok = ScService.StartType(name) == "Disabled";
        if (ok) { PassCount++; AppEnv.Log($"验证 PASS：{name} 启动类型 = Disabled（可用恢复脚本还原）。"); }
        else { FailCount++; AppEnv.Log($"验证 FAIL：{name} 启动类型实际为 {ScService.StartType(name)}。", "ERROR"); }
        return ok;
    }

    // ---------- 备份（与 PS 版目录结构一致） ----------
    public void BackupAll()
    {
        BackupRegKey(@"HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config", "deliveryoptimization-auto.reg");
        BackupRegKey(@"HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy", "appprivacy-auto.reg");
        BackupRegKey(@"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "visualeffects-auto.reg");
        BackupRegKey(@"HKCU:\Control Panel\Desktop", "desktop-auto.reg");
        BackupRegKey(@"HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance", "maintenance-auto.reg");
        foreach (var s in new[] { "DoSvc", "SysMain", "WSearch", "wuauserv" }) BackupService(s);
    }

    public static void BackupRegKey(string key, string fileName)
    {
        var native = key.Replace("HKLM:", "HKEY_LOCAL_MACHINE").Replace("HKCU:", "HKEY_CURRENT_USER");
        using var rk = OpenKey(key, false);
        if (rk == null) { AppEnv.Log($"注册表项不存在，跳过备份：{native}"); return; }
        var (code, _) = Shell.Run("reg.exe", $"export \"{native}\" \"{Path.Combine(AppEnv.BackupDir, fileName)}\" /y");
        AppEnv.Log(code == 0 ? $"注册表已备份：{native}" : $"注册表备份失败：{native}", code == 0 ? "INFO" : "WARN");
    }

    public void BackupService(string name)
    {
        try
        {
            var rec = new TweakRec { Kind = "service", Name = name, StartMode = ScService.StartType(name), State = ScService.State(name) };
            File.WriteAllText(Path.Combine(AppEnv.BackupDir, $"service-{name}.json"),
                JsonSerializer.Serialize(rec, new JsonSerializerOptions { WriteIndented = true }));
            AppEnv.Log($"服务状态已备份：{name}");
        }
        catch (Exception ex) { AppEnv.Log($"服务备份失败：{name}：{ex.Message}", "WARN"); }
    }
}
