#requires -version 5.1
<#
.SYNOPSIS
    Falco-CLI - Falco 的命令行版（Mole `mo` 风格子命令）
.DESCRIPTION
    引擎函数内嵌（备份 / 优化 / 清理 / 白名单），
    与 GUI 付费版共享 C:\ProgramData\Falco 数据目录与 tweaks-state.json 状态文件。
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

# ---------- 引擎函数（自 Falco.ps1 内嵌，与 GUI 版同一套已验证逻辑） ----------
function Write-Log {
    param([string]$Message, [ValidateSet('INFO','WARN','ERROR')][string]$Level = 'INFO')
    $line = "[{0}] [{1}] {2}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $Level, $Message
    try { Add-Content -LiteralPath $LogFile -Value $line -Encoding UTF8 } catch {}
    $shared.LogQueue.Enqueue("$Level|$Message")
}
function Get-IsAdmin {
    $id = [Security.Principal.WindowsIdentity]::GetCurrent()
    $p = New-Object Security.Principal.WindowsPrincipal($id)
    return $p.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
function Get-HardwareInfo {
    $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
    $os = Get-CimInstance Win32_OperatingSystem
    $ramGB = [math]::Round($os.TotalVisibleMemorySize / 1MB, 1)
    $systemDrive = $env:SystemDrive
    $diskType = "未知"
    try {
        $part = Get-Partition -DriveLetter $systemDrive.TrimEnd(':') -ErrorAction Stop
        $disk = Get-Disk -Number $part.DiskNumber -ErrorAction Stop
        $media = (Get-PhysicalDisk -ErrorAction Stop | Where-Object { $_.DeviceId -eq $disk.Number } | Select-Object -First 1).MediaType
        if ($media) { $diskType = [string]$media } else { $diskType = "未识别" }
    } catch {}
    [pscustomobject]@{
        CPU = $cpu.Name
        Cores = $cpu.NumberOfCores
        Threads = $cpu.NumberOfLogicalProcessors
        RAM_GB = $ramGB
        SystemDisk = $systemDrive
        DiskType = $diskType
        Windows = $os.Caption
        Build = $os.BuildNumber
    }
}
function Export-RegistryKey {
    param([string]$Key, [string]$FileName)
    $file = Join-Path $BackupDir $FileName
    $nativeKey = $Key -replace '^HKLM:', 'HKEY_LOCAL_MACHINE' -replace '^HKCU:', 'HKEY_CURRENT_USER'
    if (-not (Test-Path -LiteralPath $Key -ErrorAction SilentlyContinue)) {
        Write-Log "注册表项不存在，跳过备份（后续写入时将创建）：$nativeKey" 'INFO'
        return
    }
    & reg.exe export $nativeKey $file /y | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Log "注册表已备份：$nativeKey" }
    else { Write-Log "注册表备份失败：$nativeKey" 'WARN' }
}
function Backup-ServiceState {
    param([string]$ServiceName)
    $svc = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($svc) {
        $obj = [pscustomobject]@{ Name = $svc.Name; StartMode = $svc.StartMode; State = $svc.State }
        $obj | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $BackupDir "service-$ServiceName.json") -Encoding UTF8
        Write-Log "服务状态已备份：$ServiceName"
        if ($Shared -and $Shared.TweakCurrentId) {
            # V2-2：服务原始状态入记录，供通用还原
            [void]$Shared.TweakRecords.Add(@{ kind = 'service'; name = [string]$svc.Name; startMode = [string]$svc.StartMode; state = [string]$svc.State })
        }
    }
}
function Set-RegValueVerified {
    # V2-1 验证引擎：写入前捕获旧值 → 写入 → 回读验证 → 重试一次 → 仍失败回滚为旧值
    param([string]$Path, [string]$Name, [string]$Kind, $Value)
    Ensure-RegKey -Path $Path
    $old = $null
    $oldExists = $false
    try {
        $old = (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name
        $oldExists = $true
    } catch {}
    New-ItemProperty -Path $Path -Name $Name -PropertyType $Kind -Value $Value -Force | Out-Null
    $read = $null
    try { $read = (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name } catch {}
    if ("$read" -ne "$Value") {
        Write-Log ("验证未通过（回读 {0} ≠ 期望 {1}），重试一次：{2}\{3}" -f $read, $Value, $Path, $Name) 'WARN'
        New-ItemProperty -Path $Path -Name $Name -PropertyType $Kind -Value $Value -Force | Out-Null
        try { $read = (Get-ItemProperty -Path $Path -Name $Name -ErrorAction Stop).$Name } catch {}
    }
    if ("$read" -eq "$Value") {
        if ($Shared -and $Shared.TweakCurrentId) {
            # V2-2：记录期望值与原始值，供通用还原与漂移检测使用
            [void]$Shared.TweakRecords.Add(@{ path = $Path; name = $Name; kind = $Kind; desired = "$Value"; original = "$old"; originalExists = $oldExists })
        }
        if ($Shared) { $Shared.TweakPass = [int]$Shared.TweakPass + 1 }
        Write-Log ("验证 PASS：{0}\{1} = {2}" -f $Path, $Name, $Value)
        return $true
    }
    if ($oldExists) {
        New-ItemProperty -Path $Path -Name $Name -PropertyType $Kind -Value $old -Force | Out-Null
        Write-Log ("验证 FAIL，已回滚为修改前的值 {0}：{1}\{2}" -f $old, $Path, $Name) 'ERROR'
    } else {
        Remove-ItemProperty -Path $Path -Name $Name -ErrorAction SilentlyContinue
        Write-Log ("验证 FAIL，已回滚（移除新增项）：{0}\{1}" -f $Path, $Name) 'ERROR'
    }
    if ($Shared) { $Shared.TweakFail = [int]$Shared.TweakFail + 1 }
    return $false
}
function Set-RegDword {
    param([string]$Path, [string]$Name, [int]$Value)
    return (Set-RegValueVerified -Path $Path -Name $Name -Kind DWord -Value $Value)
}
function Set-RegString {
    param([string]$Path, [string]$Name, [string]$Value)
    return (Set-RegValueVerified -Path $Path -Name $Name -Kind String -Value $Value)
}
function Ensure-RegKey {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace($Path)) { throw '注册表路径为空。' }
    if (Test-Path -LiteralPath $Path -ErrorAction SilentlyContinue) { return }
    $parent = Split-Path -Path $Path -Parent
    if ($parent -and $parent -ne $Path -and -not (Test-Path -LiteralPath $parent -ErrorAction SilentlyContinue)) {
        Ensure-RegKey -Path $parent
    }
    New-Item -Path $Path -Force -ErrorAction Stop | Out-Null
}
function Create-RestoreScript {
$restore = @'
#requires -version 5.1
# Falco 自动生成的恢复脚本
$ErrorActionPreference = "Continue"
$BaseDir = Join-Path $env:ProgramData "Falco"
$BackupDir = Join-Path $BaseDir "Backups"

function Import-Reg($file) {
    if (Test-Path $file) {
        Write-Host "恢复注册表：$file"
        & reg.exe import $file | Out-Null
    }
}
function Restore-Service($name) {
    $f = Join-Path $BackupDir "service-$name.json"
    if (Test-Path $f) {
        $s = Get-Content $f -Raw | ConvertFrom-Json
        $mode = switch ($s.StartMode) {
            "Auto" { "Automatic" }
            "Manual" { "Manual" }
            "Disabled" { "Disabled" }
            default { $null }
        }
        if ($mode) { Set-Service -Name $name -StartupType $mode -ErrorAction SilentlyContinue }
        if ($s.State -eq "Running") { Start-Service -Name $name -ErrorAction SilentlyContinue }
    }
}

Write-Host ""
Write-Host "=== Falco 恢复工具 ===" -ForegroundColor Cyan
Write-Host "备份目录：$BackupDir"
Write-Host ""

Get-ChildItem $BackupDir -Filter *.reg -ErrorAction SilentlyContinue | ForEach-Object {
    Import-Reg $_.FullName
}

"SysMain","WSearch","DoSvc" | ForEach-Object { Restore-Service $_ }

Write-Host ""
Write-Host "恢复操作已执行。建议重启 Windows。" -ForegroundColor Green
Read-Host "按 Enter 退出"
'@
    Set-Content -LiteralPath $RestoreScript -Value $restore -Encoding UTF8
    Write-Log "恢复脚本已生成：$RestoreScript"
}
function Initialize-Backup {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    Write-Log "开始创建备份：$stamp"

    Export-RegistryKey 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config' "deliveryoptimization-$stamp.reg"
    Export-RegistryKey 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' "appprivacy-$stamp.reg"
    Export-RegistryKey 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' "visualeffects-$stamp.reg"
    Export-RegistryKey 'HKCU:\Control Panel\Desktop' "desktop-$stamp.reg"
    Export-RegistryKey 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Schedule\Maintenance' "maintenance-$stamp.reg"

    "DoSvc","SysMain","WSearch","wuauserv" | ForEach-Object { Backup-ServiceState $_ }

    $startup = Get-CimInstance Win32_StartupCommand |
        Select-Object Name, Command, Location, User | ConvertTo-Json -Depth 3
    $startup | Set-Content -LiteralPath (Join-Path $BackupDir "startup-$stamp.json") -Encoding UTF8

    @{ BackupTime = (Get-Date).ToString('o'); Hardware = (Get-HardwareInfo) } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $StateFile -Encoding UTF8

    Create-RestoreScript
}
function Optimize-DeliveryOptimization {
    $key = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config'
    Export-RegistryKey $key "deliveryoptimization-before.reg"
    # 0 = HTTP only，禁止 Internet P2P 上传
    Set-RegDword $key 'DODownloadMode' 0
    Write-Log "传递优化：DODownloadMode = 0（HTTP 直连，禁止 Internet P2P）"
    try {
        Set-Service -Name DoSvc -StartupType Automatic -ErrorAction Stop
        $svc = Get-Service -Name DoSvc -ErrorAction SilentlyContinue
        if ($svc -and "$($svc.StartType)" -eq 'Automatic') {
            if ($Shared) { $Shared.TweakPass = [int]$Shared.TweakPass + 1 }
            Write-Log "验证 PASS：DoSvc 启动类型 = Automatic（保持可用，仅限制 P2P）。"
        } else {
            if ($Shared) { $Shared.TweakFail = [int]$Shared.TweakFail + 1 }
            Write-Log ("验证 FAIL：DoSvc 启动类型实际为 {0}，可能被组策略强制。" -f $(if ($svc) { $svc.StartType } else { '未知' })) 'ERROR'
        }
    } catch {
        Write-Log "无法设置 DoSvc：$($_.Exception.Message)" 'WARN'
    }
}
function Optimize-BackgroundApps {
    $key = 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy'
    Export-RegistryKey $key "appprivacy-before.reg"
    # 2 = Force Deny
    Set-RegDword $key 'LetAppsRunInBackground' 2
    Write-Log "后台应用策略：禁止商店应用在后台运行（部分应用通知可能受影响）。"
}
function Optimize-VisualEffects {
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects'
    Export-RegistryKey $key "visualeffects-before.reg"
    Set-RegDword $key 'VisualFXSetting' 3

    $desktop = 'HKCU:\Control Panel\Desktop'
    Export-RegistryKey $desktop "desktop-before.reg"
    Set-RegString $desktop 'MenuShowDelay' '50'
    Write-Log "视觉效果：调整为性能优先；菜单响应延迟 50ms。"
}
function Set-WindowsUpdateActiveHours {
    param([int]$Start = 8, [int]$End = 22)
    if ($Start -lt 0 -or $Start -gt 23 -or $End -lt 0 -or $End -gt 23) { throw "活动时间必须为 0-23。" }
    if ($Start -eq $End) { throw "开始与结束时间不能相同。" }
    $key = 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings'
    Export-RegistryKey $key "windowsupdate-before.reg"
    Set-RegDword $key 'ActiveHoursStart' $Start
    Set-RegDword $key 'ActiveHoursEnd' $End
    Write-Log "Windows Update 活动时间：$Start:00 - $End:00（不会关闭更新）。"
}
function Optimize-SysMainAdvice {
    $hw = Get-HardwareInfo
    $svc = Get-Service -Name SysMain -ErrorAction SilentlyContinue
    if (-not $svc) { Write-Log "SysMain 服务不存在，无需处理。" 'WARN'; return }
    if ($hw.RAM_GB -le 8 -or $hw.DiskType -match 'HDD') {
        Write-Log ("检测结果：RAM={0}GB / 磁盘={1}。建议禁用 SysMain 以降低磁盘压力（代价是应用启动稍慢）。" -f $hw.RAM_GB, $hw.DiskType)
    } else {
        Write-Log ("检测结果：RAM={0}GB / 磁盘={1}。建议保留 SysMain（SSD + 大内存收益更大）。" -f $hw.RAM_GB, $hw.DiskType)
    }
}
function Optimize-SysMainApply {
    Backup-ServiceState "SysMain"
    $svc = Get-Service -Name SysMain -ErrorAction SilentlyContinue
    if (-not $svc) { Write-Log "SysMain 不存在，跳过。" 'WARN'; return }
    Stop-Service SysMain -Force -ErrorAction SilentlyContinue
    Set-Service SysMain -StartupType Disabled
    $svc = Get-Service -Name SysMain -ErrorAction SilentlyContinue
    if ($svc -and "$($svc.StartType)" -eq 'Disabled') {
        if ($Shared) { $Shared.TweakPass = [int]$Shared.TweakPass + 1 }
        Write-Log '验证 PASS：SysMain 启动类型 = Disabled（可用恢复脚本还原）。'
    } else {
        if ($Shared) { $Shared.TweakFail = [int]$Shared.TweakFail + 1 }
        Write-Log ("验证 FAIL：SysMain 启动类型实际为 {0}，可能被组策略强制。" -f $(if ($svc) { $svc.StartType } else { '未知' })) 'ERROR'
    }
}
function Optimize-WSearchDisable {
    Backup-ServiceState "WSearch"
    Stop-Service WSearch -Force -ErrorAction SilentlyContinue
    Set-Service WSearch -StartupType Disabled
    $svc = Get-Service -Name WSearch -ErrorAction SilentlyContinue
    if ($svc -and "$($svc.StartType)" -eq 'Disabled') {
        if ($Shared) { $Shared.TweakPass = [int]$Shared.TweakPass + 1 }
        Write-Log '验证 PASS：WSearch 启动类型 = Disabled（开始菜单搜索将变慢，可用恢复脚本还原）。'
    } else {
        if ($Shared) { $Shared.TweakFail = [int]$Shared.TweakFail + 1 }
        Write-Log ("验证 FAIL：WSearch 启动类型实际为 {0}，可能被组策略强制。" -f $(if ($svc) { $svc.StartType } else { '未知' })) 'ERROR'
    }
}
function Measure-FolderMB {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return 0 }
    $sum = (Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue |
        Measure-Object -Property Length -Sum).Sum
    if (-not $sum) { return 0 }
    return [math]::Round($sum / 1MB, 1)
}
function Clear-TempFiles {
    $paths = @($env:TEMP, "$env:WINDIR\Temp")
    $freed = 0.0
    foreach ($p in $paths) {
        if (Test-Path -LiteralPath $p) {
            Get-ChildItem -LiteralPath $p -Force -ErrorAction SilentlyContinue |
                Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
        }
    }
    Write-Log "临时文件清理完成。"
    return $freed
}
function Clear-WUCache {
    $dl = "$env:WINDIR\SoftwareDistribution\Download"
    Write-Log "停止 wuauserv / bits 服务…"
    Stop-Service wuauserv, bits -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $dl) {
        Get-ChildItem -LiteralPath $dl -Force -ErrorAction SilentlyContinue |
            Remove-Item -Force -Recurse -ErrorAction SilentlyContinue
    }
    Write-Log "正在重新启动 wuauserv / bits 服务…"
    Set-Service wuauserv, bits -StartupType Automatic -ErrorAction SilentlyContinue
    Start-Service bits, wuauserv -ErrorAction SilentlyContinue
    Write-Log "Windows Update 下载缓存清理完成（未完成的更新会自动重新下载）。"
}
function Clear-Recycle {
    try {
        Clear-RecycleBin -Force -ErrorAction Stop
        Write-Log "回收站已清空。"
    } catch {
        Write-Log "回收站清理：$($_.Exception.Message)" 'WARN'
    }
}
function Clear-Thumbcache {
    $dir = Join-Path $env:LOCALAPPDATA 'Microsoft\Windows\Explorer'
    Write-Log "重启资源管理器以释放缓存文件…"
    Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Get-ChildItem -LiteralPath $dir -Filter 'thumbcache_*.db' -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $dir -Filter 'iconcache_*.db' -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Start-Process explorer.exe
    Write-Log "缩略图与图标缓存已重建。"
}
function Measure-PurgeDir {
    # 单次遍历同时统计：总大小 / 文件数 / 最近写入时间 / 是否含密钥文件
    param([string]$Path)
    $bytes = [double]0
    $cnt = 0
    $latest = [datetime]::MinValue
    $hasKey = $false
    $err = $false
    try {
        $files = Get-ChildItem -LiteralPath $Path -Recurse -Force -File -ErrorAction SilentlyContinue
        foreach ($f in $files) {
            $cnt++
            $bytes += $f.Length
            if ($f.LastWriteTime -gt $latest) { $latest = $f.LastWriteTime }
            if (-not $hasKey -and ($f.Name -match '\.(pem|key|pfx|p12|jks|keystore)$' -or $f.Name -like 'id_rsa*' -or $f.Name -like 'id_ed25519*')) { $hasKey = $true }
        }
    } catch { $err = $true }
    return [pscustomobject]@{ Bytes = $bytes; Files = $cnt; Latest = $latest; HasKey = $hasKey; Err = $err }
}
function Find-PurgeDirs {
    # 带剪枝的遍历：命中产物目录后不再下钻；跳过 junction/符号链接防止绕环
    param([string]$Root)
    $always = @('node_modules','.next','.nuxt','.turbo','venv','.venv','__pycache__','.gradle')
    $projOnly = @('target','build','dist','.build','bin','obj')   # 泛化名称，仅在项目目录内命中
    $skip = @('appdata','.git','$recycle.bin','windows','program files','program files (x86)','programdata','system volume information','recovery','msocache','intel','perflogs')
    $markers = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
    @('package.json','cargo.toml','pom.xml','build.gradle','settings.gradle','go.mod','composer.json','pubspec.yaml','tsconfig.json','cmakelists.txt','makefile','pyproject.toml') |
        ForEach-Object { [void]$markers.Add($_) }

    $hits = New-Object System.Collections.Generic.List[string]
    $stack = New-Object System.Collections.Generic.Stack[string]
    $stack.Push($Root)
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        $children = $null
        try { $children = Get-ChildItem -LiteralPath $dir -Directory -Force -ErrorAction SilentlyContinue } catch {}
        if (-not $children) { continue }

        # 泛化名称只在其父目录确为项目（含标记文件）时命中，避免误伤同名普通目录
        $hasProjChild = $false
        foreach ($c in $children) { if ($projOnly -contains $c.Name.ToLowerInvariant()) { $hasProjChild = $true; break } }
        $isProject = $false
        if ($hasProjChild) {
            try {
                foreach ($f in (Get-ChildItem -LiteralPath $dir -File -Force -ErrorAction SilentlyContinue)) {
                    if ($markers.Contains($f.Name) -or $f.Name -match '\.(csproj|fsproj|vbproj|sln)$') { $isProject = $true; break }
                }
            } catch {}
        }

        foreach ($c in $children) {
            if (($c.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $n = $c.Name.ToLowerInvariant()
            if ($skip -contains $n) { continue }
            if ($always -contains $n) { $hits.Add($c.FullName); continue }
            if ($isProject -and ($projOnly -contains $n)) { $hits.Add($c.FullName); continue }
            $stack.Push($c.FullName)
        }
    }
    return $hits
}
function Invoke-PurgeScan {
    param([string]$Root, [int]$ActiveDays = 7)
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        Write-Log "扫描目录不存在或不是文件夹：$Root" 'ERROR'
        $Shared.PurgeStamp = (Get-Date).Ticks
        return
    }
    $threshold = (Get-Date).AddDays(-$ActiveDays)
    Write-Log "开始扫描构建产物（$ActiveDays 天内有写入视为活跃）：$Root"
    $dirs = @(Find-PurgeDirs $Root)
    Write-Log "遍历完成，共发现 $($dirs.Count) 个候选目录，正在测量大小与保护检查…"

    $groups = @{}
    $skTotal = 0; $skActive = 0; $skKey = 0; $skGit = 0; $skErr = 0
    $i = 0
    foreach ($d in $dirs) {
        $i++
        $Shared.PurgeProgress = ("扫描中 {0}/{1}：{2}" -f $i, $dirs.Count, $d)
        $m = Measure-PurgeDir $d
        $reason = $null
        if ($m.Err) { $reason = '无法测量'; $skErr++ }
        elseif (Test-Path -LiteralPath (Join-Path $d '.git')) { $reason = '嵌套仓库'; $skGit++ }
        elseif ($m.HasKey) { $reason = '含密钥文件'; $skKey++ }
        elseif ($m.Latest -gt $threshold) { $reason = '活跃中'; $skActive++ }

        $parent = Split-Path -Parent $d
        if (-not $groups.ContainsKey($parent)) {
            $groups[$parent] = @{ Dirs = (New-Object System.Collections.Generic.List[string]); SkipCounts = @{}; Bytes = [double]0; Latest = [datetime]::MinValue }
        }
        $g = $groups[$parent]
        if ($reason) {
            $skTotal++
            Write-Log ("跳过（{0}）：{1}" -f $reason, $d)
            if (-not $g.SkipCounts.ContainsKey($reason)) { $g.SkipCounts[$reason] = 0 }
            $g.SkipCounts[$reason]++
            continue
        }
        $g.Dirs.Add($d)
        $g.Bytes += $m.Bytes
        if ($m.Latest -gt $g.Latest) { $g.Latest = $m.Latest }
    }

    $rows = New-Object System.Collections.Generic.List[object]
    $delCount = 0; $totalBytes = [double]0
    foreach ($k in @($groups.Keys)) {
        $g = $groups[$k]
        if ($g.Dirs.Count -eq 0 -and $g.SkipCounts.Count -eq 0) { continue }
        $r = New-Object PurgeRow
        $r.Project = $k
        $r.CountText = "$($g.Dirs.Count) 个"
        if ($g.SkipCounts.Count -gt 0) {
            $skSum = ($g.SkipCounts.Values | Measure-Object -Sum).Sum
            $r.CountText += "（另跳过 $skSum）"
            $r.Note = '跳过：' + (($g.SkipCounts.GetEnumerator() | ForEach-Object { "$($_.Key)×$($_.Value)" }) -join '、')
        }
        $r.SizeMB = [math]::Round($g.Bytes / 1MB, 1)
        $r.SizeText = Format-Bytes $g.Bytes
        $r.LastModText = if ($g.Latest -gt [datetime]::MinValue) { $g.Latest.ToString('yyyy-MM-dd') } else { '—' }
        $r.Checked = ($g.Dirs.Count -gt 0)
        $r.DirList = $g.Dirs
        $rows.Add($r)
        $delCount += $g.Dirs.Count
        $totalBytes += $g.Bytes
    }
    $sorted = @($rows | Sort-Object SizeMB -Descending)
    $Shared.PurgeRows = $sorted
    if ($sorted.Count -eq 0) {
        $Shared.PurgeSummary = "未发现可清理的构建产物。"
    } else {
        $Shared.PurgeSummary = ("发现 {0} 个项目、{1} 个可重建目录，可释放 {2}；已跳过 {3} 个（活跃 {4} / 密钥 {5} / 嵌套仓库 {6} / 无法测量 {7}）。" -f $sorted.Count, $delCount, (Format-Bytes $totalBytes), $skTotal, $skActive, $skKey, $skGit, $skErr)
    }
    $Shared.Remove('PurgeProgress')
    $Shared.PurgeStamp = (Get-Date).Ticks
    Write-Log $Shared.PurgeSummary
}
function Join-SubPath {
    param([string]$Base, [string[]]$Parts)
    foreach ($p in $Parts) { $Base = Join-Path $Base $p }
    return $Base
}
function Get-WhitelistFile {
    $dir = Join-SubPath $env:USERPROFILE @('.config', 'falco')
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    return (Join-Path $dir 'whitelist.txt')
}
function Read-WhitelistPatterns {
    $f = Get-WhitelistFile
    if (-not (Test-Path -LiteralPath $f)) {
        Set-Content -LiteralPath $f -Value '# Falco 白名单：每行一个路径模式，支持 * 通配，不区分大小写，# 开头为注释' -Encoding UTF8
        return @()
    }
    return @(Get-Content -LiteralPath $f | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}
function Test-Whitelisted {
    param([string]$Path, [string[]]$Patterns)
    foreach ($p in $Patterns) { if ($Path -like $p) { return $true } }
    return $false
}
function Invoke-CacheScan {
    $ld = $env:LOCALAPPDATA
    $ud = $env:USERPROFILE
    $chromeUd = Join-SubPath $ld @('Google', 'Chrome', 'User Data')
    $edgeUd = Join-SubPath $ld @('Microsoft', 'Edge', 'User Data')
    $cats = @(
        @{ Name = 'Chrome 浏览器缓存';  Base = $chromeUd; Guard = @('chrome');
           Paths = @((Join-SubPath $chromeUd @('*', 'Cache')), (Join-SubPath $chromeUd @('*', 'Code Cache')), (Join-SubPath $chromeUd @('*', 'GPUCache'))) }
        @{ Name = 'Edge 浏览器缓存';    Base = $edgeUd;   Guard = @('msedge');
           Paths = @((Join-SubPath $edgeUd @('*', 'Cache')), (Join-SubPath $edgeUd @('*', 'Code Cache')), (Join-SubPath $edgeUd @('*', 'GPUCache'))) }
        @{ Name = 'Firefox 浏览器缓存'; Base = (Join-SubPath $ld @('Mozilla', 'Firefox', 'Profiles')); Guard = @('firefox');
           Paths = @((Join-SubPath $ld @('Mozilla', 'Firefox', 'Profiles', '*', 'cache2'))) }
        @{ Name = 'npm 缓存';   Base = (Join-SubPath $ld @('npm-cache')); Guard = @('npm');    Paths = @((Join-SubPath $ld @('npm-cache'))) }
        @{ Name = 'pnpm store'; Base = (Join-SubPath $ld @('pnpm', 'store')); Guard = @('pnpm'); Paths = @((Join-SubPath $ld @('pnpm', 'store'))) }
        @{ Name = 'pip 缓存';   Base = (Join-SubPath $ld @('pip', 'cache')); Guard = @('pip');  Paths = @((Join-SubPath $ld @('pip', 'cache'))) }
        @{ Name = 'NuGet HTTP 缓存'; Base = (Join-SubPath $ld @('NuGet', 'v3-cache')); Guard = @('nuget', 'dotnet');
           Paths = @((Join-SubPath $ld @('NuGet', 'v3-cache'))) }
        @{ Name = 'Gradle 缓存'; Base = (Join-SubPath $ud @('.gradle', 'caches')); Guard = @('gradle', 'java');
           Paths = @((Join-SubPath $ud @('.gradle', 'caches'))) }
    )
    $wl = @(Read-WhitelistPatterns)
    $rows = New-Object System.Collections.Generic.List[object]
    $delBytes = [double]0; $skipRun = 0; $skipWl = 0
    $i = 0
    foreach ($cat in $cats) {
        $i++
        $Shared.CacheProgress = ("扫描中 {0}/{1}：{2}" -f $i, $cats.Count, $cat.Name)
        $r = New-Object CacheRow
        $r.Name = $cat.Name
        $r.Path = $cat.Base
        $r.DirList = New-Object System.Collections.Generic.List[string]

        $dirs = @()
        foreach ($p in $cat.Paths) {
            try { $dirs += @(Get-Item -Path $p -Force -ErrorAction SilentlyContinue | Where-Object { $_.PSIsContainer }) } catch {}
        }
        $dirs = @($dirs | Sort-Object -Property FullName -Unique)
        if ($dirs.Count -eq 0) {
            $r.Status = '未安装或无缓存'
            $r.SizeText = '—'
            $rows.Add($r)
            continue
        }

        # 程序运行中则整类跳过（跳过要给理由）
        $running = @()
        foreach ($g in $cat.Guard) { if (Get-Process -Name $g -ErrorAction SilentlyContinue) { $running += $g } }
        if ($running.Count -gt 0) {
            $skipRun++
            $r.Status = "已跳过：$($running -join '、') 正在运行"
            $r.SizeText = '—'
            Write-Log ("{0} 整类跳过（{1} 正在运行，关闭后重扫即可）" -f $cat.Name, ($running -join '、'))
            $rows.Add($r)
            continue
        }

        $bytesAll = [double]0; $bytesDel = [double]0; $wlHits = 0
        foreach ($d in $dirs) {
            # 复用构建产物的精确度量（字节级，避免 MB 舍入把小缓存算成 0）
            $m = (Measure-PurgeDir $d.FullName).Bytes
            $bytesAll += $m
            if (Test-Whitelisted -Path $d.FullName -Patterns $wl) { $wlHits++; continue }
            $r.DirList.Add($d.FullName)
            $bytesDel += $m
        }
        if ($wlHits -gt 0) { $skipWl++ }
        $r.SizeMB = [math]::Round($bytesAll / 1MB, 1)
        $r.DelMB = [math]::Round($bytesDel / 1MB, 1)
        $r.SizeText = Format-Bytes $bytesAll
        if ($r.DirList.Count -eq 0) {
            $r.Status = if ($wlHits -gt 0) { "白名单已保护 ×$wlHits" } else { '不可清理' }
        } else {
            $r.Checked = $true
            $r.Status = if ($wlHits -gt 0) { "可清理（白名单已排除 ×$wlHits）" } else { '可清理' }
            $delBytes += $bytesDel
        }
        $rows.Add($r)
    }
    $Shared.CacheRows = $rows.ToArray()   # 不用 @($rows)：pwsh 7.6 下 @() 包裹 List[object] 变量会抛"Argument types do not match"
    $Shared.CacheSummary = ("共 {0} 类；可清理约 {1}；跳过 {2} 类（程序运行中）、{3} 处白名单保护。" -f $rows.Count, (Format-Bytes $delBytes), $skipRun, $skipWl)
    $Shared.Remove('CacheProgress')
    $Shared.CacheStamp = (Get-Date).Ticks
    Write-Log $Shared.CacheSummary
}
function Read-TweakState {
    # 读取 tweaks-state.json → hashtable（id → 记录数组）；无文件返回空表
    param([string]$BaseDir)
    $all = @{}
    $f = Join-Path $BaseDir 'tweaks-state.json'
    if (Test-Path -LiteralPath $f) {
        try {
            $obj = Get-Content -LiteralPath $f -Raw | ConvertFrom-Json
            foreach ($p in $obj.PSObject.Properties) { $all[$p.Name] = $p.Value }
        } catch { Write-Log "读取优化项状态文件失败：$($_.Exception.Message)" 'WARN' }
    }
    return $all
}
function Save-TweakState {
    # 把本次 Apply 捕获的记录（$Shared.TweakRecords）合并进 tweaks-state.json
    param([string]$Id, [string]$BaseDir)
    $all = Read-TweakState $BaseDir
    $all[$Id] = $Shared.TweakRecords.ToArray()
    $all | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BaseDir 'tweaks-state.json') -Encoding UTF8
    $Shared.TweakRecords = (New-Object System.Collections.ArrayList)
    Write-Log "优化项修改记录已保存：$Id"
}
function Remove-TweakState {
    param([string]$Id, [string]$BaseDir)
    $all = Read-TweakState $BaseDir
    if (-not $all.ContainsKey($Id)) { return }
    $all.Remove($Id)
    $all | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $BaseDir 'tweaks-state.json') -Encoding UTF8
}
function Get-TweakTable {
    # 优化项定义表：Detect/Apply 为四件套中的两件；Undo 由修改记录通用还原，无需逐项编写
    return @(
        @{ Id = 'DO-001'; Name = '限制传递优化 P2P 上传'; Category = '网络与后台'; Risk = '低'
           Hint = '下载改为 HTTP 直连，不再向 Internet 上传更新数据'
           Detect = {
               $v = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DeliveryOptimization\Config' -ErrorAction SilentlyContinue).DODownloadMode
               if ($null -eq $v) { '默认' } elseif ("$v" -eq '0') { '已优化' } else { "默认（值 = $v）" }
           }
           Apply = { Optimize-DeliveryOptimization } }
        @{ Id = 'BG-001'; Name = '禁止商店应用后台运行'; Category = '网络与后台'; Risk = '低'
           Hint = '部分应用的后台通知可能受影响'
           Detect = {
               $v = (Get-ItemProperty -Path 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy' -ErrorAction SilentlyContinue).LetAppsRunInBackground
               if ("$v" -eq '2') { '已优化' } else { '默认' }
           }
           Apply = { Optimize-BackgroundApps } }
        @{ Id = 'VIS-001'; Name = '视觉效果性能优先'; Category = '系统体验'; Risk = '低'
           Hint = '降低动画与透明特效，菜单响应 50ms'
           Detect = {
               $v = (Get-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects' -ErrorAction SilentlyContinue).VisualFXSetting
               if ("$v" -eq '3') { '已优化' } else { '默认' }
           }
           Apply = { Optimize-VisualEffects } }
        @{ Id = 'AH-001'; Name = 'Windows Update 活动时间'; Category = 'Windows Update'; Risk = '低'
           Hint = '控制自动重启时段，不关闭更新'
           Detect = {
               $p = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\WindowsUpdate\UX\Settings' -ErrorAction SilentlyContinue
               if ($null -eq $p.ActiveHoursStart) { '默认' } else { '已设置 {0}:00 - {1}:00' -f $p.ActiveHoursStart, $p.ActiveHoursEnd }
           }
           Apply = {
               $p = $Shared.AHParams
               if ($p) { Set-WindowsUpdateActiveHours $p.Start $p.End } else { Write-Log '缺少活动时间参数。' 'WARN' }
           } }
        @{ Id = 'SYS-001'; Name = '禁用 SysMain'; Category = '服务管理'; Risk = '高'; DesiredStartType = 'Disabled'
           Hint = '应用预加载能力下降，大型应用启动可能变慢'
           Detect = {
               $svc = Get-Service -Name SysMain -ErrorAction SilentlyContinue
               if ($svc -and "$($svc.StartType)" -eq 'Disabled') { '已优化' } elseif ($svc) { '默认' } else { '服务不存在' }
           }
           Apply = { Optimize-SysMainApply } }
        @{ Id = 'WS-001'; Name = '禁用 Windows Search'; Category = '服务管理'; Risk = '高'; DesiredStartType = 'Disabled'
           Hint = '开始菜单/文件搜索将变慢'
           Detect = {
               $svc = Get-Service -Name WSearch -ErrorAction SilentlyContinue
               if ($svc -and "$($svc.StartType)" -eq 'Disabled') { '已优化' } elseif ($svc) { '默认' } else { '服务不存在' }
           }
           Apply = { Optimize-WSearchDisable } }
    )
}
function Invoke-TweakApply {
    param([string]$Id)
    $t = $null
    foreach ($x in (Get-TweakTable)) { if ($x.Id -eq $Id) { $t = $x; break } }
    if (-not $t) { Write-Log "未知优化项：$Id" 'ERROR'; return }
    Write-Log "应用优化项：$($t.Name)（$Id）"
    $Shared.TweakCurrentId = $Id
    $Shared.TweakRecords = (New-Object System.Collections.ArrayList)
    try { & $t.Apply }
    finally { $Shared.TweakCurrentId = $null }
    if ($Shared.TweakRecords.Count -gt 0) { Save-TweakState -Id $Id -BaseDir $BaseDir }
    Write-Log "优化项应用完成：$($t.Name)"
}
function Invoke-TweakUndo {
    # 通用还原：按 tweaks-state.json 中的原始记录逐条恢复，无需逐项编写 Undo
    param([string]$Id)
    $t = $null
    foreach ($x in (Get-TweakTable)) { if ($x.Id -eq $Id) { $t = $x; break } }
    if (-not $t) { Write-Log "未知优化项：$Id" 'ERROR'; return }
    $all = Read-TweakState $BaseDir
    if (-not $all.ContainsKey($Id)) { Write-Log "「$($t.Name)」没有修改记录，无需还原。" 'WARN'; return }
    Write-Log "开始还原：$($t.Name)（$Id）"
    $recs = @($all[$Id])
    foreach ($r in $recs) {
        try {
            if ("$($r.kind)" -eq 'service') {
                $mode = switch ("$($r.startMode)") { 'Auto' { 'Automatic' } 'Manual' { 'Manual' } 'Disabled' { 'Disabled' } default { $null } }
                if ($mode) { Set-Service -Name $r.name -StartupType $mode -ErrorAction SilentlyContinue }
                if ("$($r.state)" -eq 'Running') { Start-Service -Name $r.name -ErrorAction SilentlyContinue }
                Write-Log "服务已还原：$($r.name)（启动类型 $mode）"
            } elseif ("$($r.originalExists)" -eq 'True') {
                if ("$($r.kind)" -eq 'String') { Set-RegValueVerified -Path $r.path -Name $r.name -Kind String -Value "$($r.original)" | Out-Null }
                else { Set-RegValueVerified -Path $r.path -Name $r.name -Kind DWord -Value ([int]"$($r.original)") | Out-Null }
                Write-Log "注册表已还原：$($r.path)\$($r.name) = $($r.original)"
            } else {
                Remove-ItemProperty -Path $r.path -Name $r.name -ErrorAction SilentlyContinue
                Write-Log "已移除新增的注册表值：$($r.path)\$($r.name)"
            }
        } catch {
            Write-Log "还原失败（$($r.name)）：$($_.Exception.Message)" 'ERROR'
        }
    }
    Remove-TweakState -Id $Id -BaseDir $BaseDir
    Write-Log "还原完成：$($t.Name)"
}
function Get-TweakDrift {
    # V2-3 漂移判定（记录级，只读）：期望值 vs 当前值，任一记录不符即漂移
    param($Records, [string]$DesiredStartType)
    foreach ($r in @($Records)) {
        if ("$($r.kind)" -eq 'service') {
            if ($DesiredStartType) {
                $svc = Get-Service -Name $r.name -ErrorAction SilentlyContinue
                if (-not $svc -or "$($svc.StartType)" -ne $DesiredStartType) { return $true }
            }
        } else {
            $cur = $null
            try { $cur = (Get-ItemProperty -Path $r.path -Name $r.name -ErrorAction Stop).$($r.name) } catch {}
            if ("$cur" -ne "$($r.desired)") { return $true }
        }
    }
    return $false
}
function Invoke-TweakDetect {
    # 逐项检测当前状态 → 状态列表回填；只读操作，非管理员可用
    $rows = New-Object System.Collections.Generic.List[object]
    $state = Read-TweakState $BaseDir
    $driftNames = New-Object System.Collections.Generic.List[string]
    foreach ($t in (Get-TweakTable)) {
        $st = '未知'
        try { $st = [string](& $t.Detect) } catch { $st = '检测失败' }
        $r = New-Object TweakRow
        $r.Id = $t.Id
        $r.Name = $t.Name
        $r.Category = $t.Category
        $r.Risk = $t.Risk
        $r.HasState = $state.ContainsKey($t.Id)
        # V2-3 漂移检测：已应用但当前值 ≠ 期望值 → 被系统改回
        if ($r.HasState -and (Get-TweakDrift -Records @($state[$t.Id]) -DesiredStartType $t.DesiredStartType)) {
            $r.Status = "⚠ 已漂移：$st"
            [void]$driftNames.Add($t.Name)
        } else {
            $r.Status = $st
        }
        $rows.Add($r)
    }
    $Shared.TweakRows = $rows.ToArray()
    $Shared.TweakStamp = (Get-Date).Ticks
    if ($driftNames.Count -gt 0) {
        $Shared.TweakSummary = ("检测完成：{0} 项已漂移（{1}），可点「应用」重新生效。" -f $driftNames.Count, ($driftNames -join '、'))
        Write-Log ("漂移检测：{0}" -f $Shared.TweakSummary) 'WARN'
    } else {
        $Shared.TweakSummary = '检测完成：无漂移'
        Write-Log ("优化项状态检测完成：{0} 项。" -f $rows.Count)
    }
}
function Format-Bytes {
    param([double]$Bytes)
    if ($Bytes -lt 1KB) { return "{0:N0} B" -f $Bytes }
    if ($Bytes -lt 1MB) { return "{0:N0} KB" -f ($Bytes / 1KB) }
    if ($Bytes -lt 1GB) { return "{0:N1} MB" -f ($Bytes / 1MB) }
    return "{0:N2} GB" -f ($Bytes / 1GB)
}


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
