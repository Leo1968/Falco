#requires -version 5.1
<#
.SYNOPSIS
    Falco-CLI - Falco 的命令行版（Mole `mo` 风格子命令）
.DESCRIPTION
    从同目录的 Falco.ps1 运行时提取已验证的引擎函数（备份 / 优化 / 清理 / 白名单），
    与 GUI 版共享 C:\ProgramData\Falco 数据目录与 tweaks-state.json 状态文件。
.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks list
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks apply DO-001,BG-001
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks revert DO-001
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 tweaks backup
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean scan
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 clean run -Items temp,thumbs -Yes
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 purge D:\code -Days 7
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 large C:\ -MinMB 500 -Top 20
    powershell -ExecutionPolicy Bypass -File .\Falco-CLI.ps1 status -Json
#>
param(
    [Parameter(Position = 0)][string]$Command = 'help',
    [Parameter(Position = 1)][string]$Target,
    [Parameter(Position = 2)][string]$Value,
    [switch]$Json,
    [switch]$Yes,
    [string]$Items = 'temp,thumbs',
    [int]$Days = 7,
    [int]$MinMB = 200,
    [int]$Top = 20,
    [int]$AHStart = 8,
    [int]$AHEnd = 22
)

$ErrorActionPreference = 'Stop'

# ---------- 输出辅助 ----------
function Write-Ok { param([string]$m) Write-Host $m -ForegroundColor Green }
function Write-Warn2 { param([string]$m) Write-Host $m -ForegroundColor Yellow }
function Write-Err2 { param([string]$m) Write-Host $m -ForegroundColor Red }
function Write-Dim { param([string]$m) Write-Host $m -ForegroundColor DarkGray }

# ---------- 从 Falco.ps1 提取引擎函数（AST 解析，零复制、与 GUI 同步） ----------
$guiScript = Join-Path $PSScriptRoot 'Falco.ps1'
if (-not (Test-Path -LiteralPath $guiScript)) {
    Write-Err2 "未找到 $guiScript —— CLI 依赖它提供引擎函数。"; exit 1
}
$tokens = $null; $parseErrors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($guiScript, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) { Write-Err2 "Falco.ps1 存在语法错误，无法提取引擎。"; exit 1 }

$wanted = @(
    'Write-Log', 'Get-IsAdmin', 'Get-HardwareInfo',
    'Export-RegistryKey', 'Backup-ServiceState', 'Set-RegValueVerified', 'Set-RegDword', 'Set-RegString',
    'Ensure-RegKey', 'Create-RestoreScript', 'Initialize-Backup',
    'Optimize-DeliveryOptimization', 'Optimize-BackgroundApps', 'Optimize-VisualEffects',
    'Set-WindowsUpdateActiveHours', 'Optimize-SysMainAdvice', 'Optimize-SysMainApply', 'Optimize-WSearchDisable',
    'Measure-FolderMB', 'Clear-TempFiles', 'Clear-WUCache', 'Clear-Recycle', 'Clear-Thumbcache',
    'Measure-PurgeDir', 'Find-PurgeDirs', 'Invoke-PurgeScan',
    'Join-SubPath', 'Get-WhitelistFile', 'Read-WhitelistPatterns', 'Test-Whitelisted', 'Invoke-CacheScan',
    'Read-TweakState', 'Save-TweakState', 'Remove-TweakState', 'Get-TweakTable',
    'Invoke-TweakApply', 'Invoke-TweakUndo', 'Get-TweakDrift', 'Invoke-TweakDetect',
    'Format-Bytes'
)
$loaded = @()
foreach ($name in $wanted) {
    $fn = $ast.Find({ param($a) $a -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $a.Name -eq $name }, $true)
    if ($fn) { . ([scriptblock]::Create($fn.Extent.Text)); $loaded += $name }
}
$missing = @($wanted | Where-Object { $_ -notin $loaded })
if ($missing.Count -gt 0) { Write-Err2 ("引擎函数缺失：{0}" -f ($missing -join ', ')); exit 1 }

# ---------- 与 GUI 版一致的数据目录与共享状态 ----------
$BaseDir = Join-Path $env:ProgramData 'Falco'
$BackupDir = Join-Path $BaseDir 'Backups'
$LogFile = Join-Path $BaseDir 'Falco.log'
$StateFile = Join-Path $BaseDir 'state.json'
$RestoreScript = Join-Path $BaseDir 'Restore-Falco.ps1'
New-Item -ItemType Directory -Force -Path $BaseDir, $BackupDir | Out-Null

$Shared = [hashtable]::Synchronized(@{})
$Shared.TweakRecords = (New-Object System.Collections.ArrayList)
$Shared.TweakPass = 0
$Shared.TweakFail = 0

# Write-Log 向 $shared.LogQueue 入队，CLI 中让日志同时落到控制台
$shared.LogQueue = [System.Collections.Queue]::Synchronized((New-Object System.Collections.Queue))

function Show-Log {
    # 排空引擎日志队列并按级别着色输出
    while ($shared.LogQueue.Count -gt 0) {
        $entry = "$($shared.LogQueue.Dequeue())"
        $msg = $entry -replace '^(INFO|WARN|ERROR)\|', ''
        if ($entry -like 'ERROR*') { Write-Err2 "  $msg" }
        elseif ($entry -like 'WARN*') { Write-Warn2 "  $msg" }
        else { Write-Dim "  $msg" }
    }
}

# ---------- 温度（LibreHardwareMonitor 优先，ACPI 热区兜底） ----------
$script:LhmPath = Join-Path $PSScriptRoot 'lib\LibreHardwareMonitor\LibreHardwareMonitorLib.dll'
$script:LhmAvailable = $false
try {
    if (Test-Path -LiteralPath $script:LhmPath) {
        if (-not ('LibreHardwareMonitor.Hardware.Computer' -as [type])) { Add-Type -Path $script:LhmPath }
        if (-not ('FalcoLhmVisitor' -as [type])) {
            Add-Type -TypeDefinition @'
using LibreHardwareMonitor.Hardware;
public sealed class FalcoLhmVisitor : IVisitor {
    public void VisitComputer(IComputer computer) { computer.Traverse(this); }
    public void VisitHardware(IHardware hardware) {
        hardware.Update();
        foreach (IHardware sub in hardware.SubHardware) sub.Accept(this);
    }
    public void VisitSensor(ISensor sensor) { }
    public void VisitParameter(IParameter parameter) { }
}
'@ -ReferencedAssemblies $script:LhmPath
        }
        $script:LhmAvailable = $true
    }
} catch { $script:LhmAvailable = $false }

function Get-FalcoTemps {
    $cpu = $null; $gpu = $null
    if ($script:LhmAvailable) {
        try {
            $computer = [LibreHardwareMonitor.Hardware.Computer]::new()
            $computer.IsCpuEnabled = $true; $computer.IsGpuEnabled = $true; $computer.IsMotherboardEnabled = $true
            $computer.Open()
            $visitor = [FalcoLhmVisitor]::new()
            $computer.Traverse($visitor)
            function Get-Sensors($hw) {
                foreach ($s in @($hw.Sensors)) {
                    if ($s.SensorType.ToString() -eq 'Temperature' -and $null -ne $s.Value) {
                        [pscustomobject]@{ Name = [string]$s.Name; V = [double]$s.Value }
                    }
                }
                foreach ($sub in @($hw.SubHardware)) { if ($null -ne $sub) { Get-Sensors $sub } }
            }
            $all = @(Get-Sensors $computer)
            $cpuSensor = $all | Where-Object { $_.Name -like 'CPU Package*' -or $_.Name -like 'Core (Max)*' -or $_.Name -like 'Core #1*' -or $_.Name -like 'Tctl*' -or $_.Name -like 'Tdie*' } | Select-Object -First 1
            if (-not $cpuSensor) { $cpuSensor = $all | Where-Object { $_.Name -like '*CPU*' } | Select-Object -First 1 }
            if ($cpuSensor) { $cpu = $cpuSensor.V }
            $gpuSensor = $all | Where-Object { $_.Name -like 'GPU Core*' -or $_.Name -like 'GPU*' } | Select-Object -First 1
            if ($gpuSensor) { $gpu = $gpuSensor.V }
        } catch { }
    }
    if ($null -eq $cpu) {
        try {
            $t = Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop |
                Measure-Object -Property CurrentTemperature -Maximum
            if ($null -ne $t.Maximum) { $cpu = [math]::Round($t.Maximum / 10 - 273.15, 1) }
        } catch { }
    }
    [pscustomobject]@{ Cpu = $cpu; Gpu = $gpu }
}

# ---------- 健康度评分（与 GUI 版同一套扣分权重） ----------
function Get-HealthSummary {
    $cpu = 0.0
    try { $cpu = [double](Get-CimInstance Win32_Processor | Select-Object -First 1).LoadPercentage } catch { }
    if ($cpu -eq 0) { try { $cpu = [double]((Get-Counter '\Processor(_Total)\% Processor Time' -ErrorAction Stop).CounterSamples.CookedValue) } catch { } }
    $os = Get-CimInstance Win32_OperatingSystem
    $memPct = [math]::Round((1 - $os.FreePhysicalMemory / $os.TotalVisibleMemorySize) * 100, 0)
    $disk = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$env:SystemDrive'"
    $df = [math]::Round($disk.FreeSpace / 1GB, 1)
    $dt = [math]::Round($disk.Size / 1GB, 1)
    $dp = if ($dt -gt 0) { [math]::Round(($dt - $df) / $dt * 100, 0) } else { 0 }
    $temps = Get-FalcoTemps

    $score = 100.0
    $note = ''
    if ($df -lt 50) { $score -= (50 - $df) / 30 * 15 }
    if ($dp -gt 80) { $score -= ($dp - 80) / 20 * 10 }
    if ($df -lt 10) { $note = '磁盘空间严重不足' }
    elseif ($df -lt 20) { $note = '磁盘空间不足' }
    elseif ($dp -ge 90) { $note = '磁盘占用过高' }
    elseif ($dp -ge 80) { $note = '磁盘占用偏高' }
    $score -= [math]::Max(0, ($memPct - 70)) / 25 * 15
    if ($memPct -ge 85 -and $note -eq '') { $note = '内存压力较高' }
    $score -= [math]::Max(0, ($cpu - 55)) / 35 * 15
    if ($cpu -gt 70 -and $note -eq '') { $note = 'CPU 负载较高' }
    if ($null -ne $temps.Cpu -and $temps.Cpu -ge 80) { $score -= 10; if ($note -eq '') { $note = '温度偏高' } }
    $boot = $null
    try { $boot = $os.LastBootUpTime } catch { }
    if ($boot) {
        $span = (Get-Date) - $boot
        if ($span.TotalHours -gt 168) { $score -= 5; if ($note -eq '') { $note = '长时间未重启' } }
        $uptime = "{0:N0} 小时" -f [int]$span.TotalHours
        if ($span.TotalHours -ge 48) { $uptime = "{0:N1} 天" -f $span.TotalDays }
    } else { $uptime = '—' }
    $score = [math]::Max(5, [math]::Min(100, [math]::Round($score, 0)))
    $word = if ($score -ge 85) { '很好' } elseif ($score -ge 70) { '良好' } elseif ($score -ge 50) { '一般' } else { '需要优化' }

    [pscustomobject]@{
        Score = [int]$score; Word = $word; Note = $note
        CpuPct = [int]$cpu; MemPct = [int]$memPct
        DiskFreeGB = $df; DiskTotalGB = $dt; DiskPct = [int]$dp
        CpuTemp = $temps.Cpu; GpuTemp = $temps.Gpu
        Uptime = $uptime
    }
}

# ---------- 子命令实现 ----------
function Invoke-StatusCmd {
    $h = Get-HealthSummary
    if ($Json) { $h | ConvertTo-Json; return }
    $scoreColor = if ($h.Score -ge 85) { 'Green' } elseif ($h.Score -ge 70) { 'Yellow' } else { 'Red' }
    Write-Host ''
    Write-Host '  Falco 系统状态' -ForegroundColor White
    Write-Host '  ─────────────────────────────────────────────' -ForegroundColor DarkGray
    Write-Host '   健康度  ' -NoNewline
    Write-Host ("{0}" -f $h.Score) -ForegroundColor $scoreColor -NoNewline
    Write-Host (" {0}   {1}" -f $h.Word, $h.Note) -ForegroundColor DarkGray
    Write-Host ('   CPU     {0}%   (温度 {1})' -f $h.CpuPct, $(if ($null -ne $h.CpuTemp) { "$([int]$h.CpuTemp)°C" } else { '—' }))
    Write-Host ('   内存    {0}% 已使用' -f $h.MemPct)
    Write-Host ('   磁盘    {0} GB 可用 / {1} GB（已用 {2}%）' -f $h.DiskFreeGB, $h.DiskTotalGB, $h.DiskPct)
    Write-Host ('   GPU     温度 {0}' -f $(if ($null -ne $h.GpuTemp) { "$([int]$h.GpuTemp)°C" } else { '—' }))
    Write-Host ('   开机    已运行 {0}' -f $h.Uptime)
    Write-Host ('   优化项  {0}' -f $(
        $t = Get-TweakTable; $st = Read-TweakState $BaseDir
        $applied = @($t | Where-Object { $st.ContainsKey($_.Id) }).Count
        '{0}/{1} 已应用' -f $applied, $t.Count
    ))
    Write-Host ''
    if (-not (Get-IsAdmin)) { Write-Warn2 '  当前为非管理员模式：tweaks apply/revert 与部分清理需要管理员权限。' ; Write-Host '' }
}

function Invoke-TweaksCmd {
    param([string]$Action, [string]$Ids)
    switch ($Action) {
        'list' {
            $state = Read-TweakState $BaseDir
            $rows = foreach ($t in (Get-TweakTable)) {
                $detect = (& $t.Detect)
                $applied = $state.ContainsKey($t.Id)
                [pscustomobject]@{ Id = $t.Id; Name = $t.Name; Risk = $t.Risk; State = $detect; Backed = $applied; Hint = $t.Hint }
            }
            if ($Json) { $rows | ConvertTo-Json; return }
            $rows | Format-Table Id, Name, Risk, State, Backed -AutoSize | Out-Host
            Write-Dim "  应用：tweaks apply <Id[,Id…]>    还原：tweaks revert <Id|all>    备份：tweaks backup"
        }
        'backup' {
            if (-not (Get-IsAdmin)) { Write-Err2 '需要管理员权限。'; exit 2 }
            Initialize-Backup
            Write-Ok "备份完成：$BackupDir"
        }
        'apply' {
            if (-not (Get-IsAdmin)) { Write-Err2 '应用优化项需要管理员权限。'; exit 2 }
            if ([string]::IsNullOrWhiteSpace($Ids)) { Write-Err2 '缺少优化项 Id。示例：tweaks apply DO-001 或 tweaks apply safe'; exit 1 }
            $map = @{ safe = 'DO-001,BG-001,VIS-001,AH-001'; performance = 'DO-001,BG-001,VIS-001,AH-001'; dev = '' }
            if ($map.ContainsKey($Ids.ToLower())) { $Ids = $map[$Ids.ToLower()] }
            $ids = @($Ids -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ })
            if (-not $Yes) {
                $highRisk = @($ids | Where-Object { $_ -in @('SYS-001', 'WS-001') })
                if ($highRisk.Count -gt 0) {
                    Write-Warn2 "包含高风险项（$($highRisk -join ', ')）：禁用服务可能影响系统响应。加 -Yes 确认继续。"
                    exit 1
                }
            }
            Initialize-Backup
            Show-Log
            $Shared.AHParams = @{ Start = $AHStart; End = $AHEnd }
            foreach ($id in $ids) { Invoke-TweakApply -Id $id; Show-Log }
            Write-Ok ("完成：验证通过 {0} 项，失败 {1} 项。" -f $Shared.TweakPass, $Shared.TweakFail)
        }
        'revert' {
            if (-not (Get-IsAdmin)) { Write-Err2 '还原优化项需要管理员权限。'; exit 2 }
            if ([string]::IsNullOrWhiteSpace($Ids)) { Write-Err2 '缺少优化项 Id，或使用 all。'; exit 1 }
            $ids = if ($Ids -eq 'all') { @(Read-TweakState $BaseDir).Keys } else { @($Ids -split ',' | ForEach-Object { $_.Trim() }) }
            foreach ($id in $ids) { Invoke-TweakUndo -Id $id }
            Write-Ok '还原完成。'
        }
        default { Write-Err2 "未知子命令：tweaks $Action（可用：list / backup / apply / revert）"; exit 1 }
    }
}

function Invoke-CleanCmd {
    param([string]$Action)
    switch ($Action) {
        'scan' {
            $temp = (Measure-FolderMB $env:TEMP) + (Measure-FolderMB "$env:WINDIR\Temp")
            $wu = Measure-FolderMB "$env:WINDIR\SoftwareDistribution\Download"
            $thumbs = 0.0
            $tdir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
            if (Test-Path -LiteralPath $tdir) {
                $thumbs = [math]::Round((Get-ChildItem -LiteralPath $tdir -Force -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match '^(thumb|icon)cache_.*\.db$' } |
                    Measure-Object -Property Length -Sum).Sum / 1MB, 1)
            }
            $rows = @(
                [pscustomobject]@{ Item = 'temp'; Desc = '临时文件'; MB = [math]::Round($temp, 1) }
                [pscustomobject]@{ Item = 'wu'; Desc = 'Windows Update 缓存'; MB = [math]::Round($wu, 1) }
                [pscustomobject]@{ Item = 'thumbs'; Desc = '缩略图/图标缓存'; MB = [math]::Round($thumbs, 1) }
            )
            if ($Json) { $rows | ConvertTo-Json; return }
            $rows | Format-Table Item, Desc, MB -AutoSize | Out-Host
            Write-Dim "  清理：clean run -Items temp,wu,thumbs -Yes"
        }
        'run' {
            $items = @($Items -split ',' | ForEach-Object { $_.Trim().ToLower() } | Where-Object { $_ })
            if ($items -contains 'recycle' -and -not $Yes) {
                Write-Warn2 '清空回收站不可恢复：请加 -Yes 确认。'
                exit 1
            }
            foreach ($it in $items) {
                switch ($it) {
                    'temp' { Clear-TempFiles | Out-Null; Write-Ok '✓ 临时文件已清理' }
                    'wu' {
                        if (-not (Get-IsAdmin)) { Write-Err2 '✗ WU 缓存清理需要管理员权限'; continue }
                        Clear-WUCache; Write-Ok '✓ Windows Update 缓存已清理'
                    }
                    'thumbs' { Clear-Thumbcache; Write-Ok '✓ 缩略图/图标缓存已重建' }
                    'recycle' { Clear-Recycle; Write-Ok '✓ 回收站已清空' }
                    default { Write-Warn2 "✗ 未知清理项：$it（可用 temp,wu,thumbs,recycle）" }
                }
            }
        }
        default { Write-Err2 "未知子命令：clean $Action（可用：scan / run）"; exit 1 }
    }
}

function Invoke-PurgeCmd {
    if ([string]::IsNullOrWhiteSpace($Target)) { Write-Err2 '用法：purge <扫描根目录> [-Days 7]'; exit 1 }
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { Write-Err2 "目录不存在：$Target"; exit 1 }
    $threshold = (Get-Date).AddDays(-$Days)
    $dirs = @(Find-PurgeDirs -Root $Target)
    if ($dirs.Count -eq 0) { Write-Ok '未发现可清理的构建产物目录。'; return }

    $rows = @()
    $skipped = 0
    foreach ($d in $dirs) {
        $m = Measure-PurgeDir $d
        $reason = $null
        if ($m.Err) { $reason = '无法测量'; $skipped++ }
        elseif (Test-Path -LiteralPath (Join-Path $d '.git')) { $reason = '嵌套仓库'; $skipped++ }
        elseif ($m.HasKey) { $reason = '含密钥文件'; $skipped++ }
        elseif ($m.Latest -gt $threshold) { $reason = '活跃中'; $skipped++ }
        if ($reason) { Write-Dim ("  ⚠ 跳过（{0}）：{1}" -f $reason, $d); continue }
        $rows += [pscustomobject]@{ Path = $d; SizeMB = [math]::Round($m.Bytes / 1MB, 1); LastMod = if ($m.Latest -gt [datetime]::MinValue) { $m.Latest.ToString('yyyy-MM-dd') } else { '—' } }
    }
    if ($Json) { $rows | ConvertTo-Json; return }
    if ($rows.Count -eq 0) { Write-Ok '所有候选目录均被安全规则跳过。'; return }
    $rows | Sort-Object SizeMB -Descending | Format-Table SizeMB, LastMod, Path -AutoSize | Out-Host
    $total = ($rows | Measure-Object SizeMB -Sum).Sum
    Write-Dim ("  共 {0} 个可重建目录，可释放约 {1:N1} MB；另有 {2} 个被安全规则跳过。v1 仅扫描，删除请在 GUI 清理页进行。" -f $rows.Count, $total, $skipped)
}

function Invoke-LargeCmd {
    if ([string]::IsNullOrWhiteSpace($Target)) { Write-Err2 '用法：large <扫描目录> [-MinMB 200] [-Top 20]'; exit 1 }
    if (-not (Test-Path -LiteralPath $Target -PathType Container)) { Write-Err2 "目录不存在：$Target"; exit 1 }
    $min = $MinMB * 1MB
    $files = Get-ChildItem -LiteralPath $Target -Recurse -File -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Length -ge $min } |
        Sort-Object Length -Descending |
        Select-Object -First $Top
    $rows = foreach ($f in $files) {
        [pscustomobject]@{ Path = $f.FullName; SizeMB = [math]::Round($f.Length / 1MB, 1); Modified = $f.LastWriteTime.ToString('yyyy-MM-dd') }
    }
    if ($Json) { $rows | ConvertTo-Json; return }
    if (@($rows).Count -eq 0) { Write-Ok "未发现 ≥${MinMB}MB 的文件。"; return }
    $rows | Format-Table Path, SizeMB, Modified -AutoSize | Out-Host
}

function Invoke-HelpCmd {
    Write-Host ''
    Write-Host '  Falco CLI —— Windows 优化与清理命令行' -ForegroundColor White
    Write-Host '  ───────────────────────────────────────────────────────' -ForegroundColor DarkGray
    Write-Host '   status                  系统状态与健康度（-Json 输出 JSON）'
    Write-Host '   tweaks list             优化项清单与当前状态'
    Write-Host '   tweaks backup           备份注册表与服务（修改前自动执行）'
    Write-Host '   tweaks apply <Id|safe>  应用优化项（safe = 低风险四项）'
    Write-Host '   tweaks revert <Id|all>  按备份记录还原'
    Write-Host '   clean scan              扫描可清理空间（-Json）'
    Write-Host '   clean run -Items …      执行清理（temp,wu,thumbs,recycle；需 -Yes）'
    Write-Host '   purge <目录>            扫描构建产物（-Days 7）'
    Write-Host '   large <目录>            大文件扫描（-MinMB 200 -Top 20）'
    Write-Host ''
    Write-Dim '  与 GUI 共享 C:\ProgramData\Falco 数据目录；所有修改前自动备份。'
    Write-Host ''
}

# ---------- 分发 ----------
switch ($Command.ToLower()) {
    'status' { Invoke-StatusCmd }
    'tweaks' { Invoke-TweaksCmd -Action $Target -Ids $Value }
    'clean' { Invoke-CleanCmd -Action $Target }
    'purge' { Invoke-PurgeCmd }
    'large' { Invoke-LargeCmd }
    'help' { Invoke-HelpCmd }
    default { Write-Err2 "未知命令：$Command"; Invoke-HelpCmd; exit 1 }
}
