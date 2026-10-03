# SiNiSistar 2「全异常状态设定」Mod - 自动检测安装脚本
# 自动查找游戏目录（Steam 库 / 非 Steam 版 / 常见盘符），确认后安装。
# 已经装过旧版的话会自动更新：只覆盖有变化的文件，其余原样跳过。
# 也可以直接指定目录：install.ps1 -GameDir "D:\Games\SiNiSistar 2"
# 兼容 Windows PowerShell 5.1，文件编码 UTF-8 BOM。

param(
    [string]$GameDir = ""
)

$ErrorActionPreference = "Stop"

function Find-SteamGameDir {
    $candidates = @()
    $steamPath = $null
    try { $steamPath = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -Name SteamPath -ErrorAction SilentlyContinue).SteamPath } catch { }
    if (-not $steamPath) {
        try { $steamPath = (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -Name InstallPath -ErrorAction SilentlyContinue).InstallPath } catch { }
    }
    if (-not $steamPath -and (Test-Path "C:\Program Files (x86)\Steam")) { $steamPath = "C:\Program Files (x86)\Steam" }
    if ($steamPath) {
        $steamPath = $steamPath -replace "/", "\"
        $libRoots = @()
        $libRoots += Join-Path $steamPath "steamapps"
        $vdf = Join-Path $steamPath "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            $vdfContent = Get-Content $vdf -Raw
            foreach ($m in [regex]::Matches($vdfContent, '"path"\s+"([^"]+)"')) {
                $p = $m.Groups[1].Value -replace "\\\\", "\"
                $libRoots += (Join-Path $p "steamapps")
            }
        }
        foreach ($lib in $libRoots) {
            $g = Join-Path $lib "common\SiNiSistar 2"
            if (Test-Path (Join-Path $g "SiNiSistar2.exe")) { $candidates += $g }
        }
    }
    return $candidates
}

function Find-UninstallEntries {
    $candidates = @()
    $roots = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    foreach ($r in $roots) {
        try {
            $items = Get-ItemProperty $r -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match "SiNiSistar" }
            foreach ($it in $items) {
                $loc = $it.InstallLocation
                if ($loc -and (Test-Path (Join-Path $loc "SiNiSistar2.exe"))) { $candidates += $loc }
            }
        } catch { }
    }
    return $candidates
}

function Find-CommonDrives {
    $candidates = @()
    foreach ($drive in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
        try {
            Get-ChildItem -Path $drive.Root -Directory -ErrorAction SilentlyContinue | Where-Object {
                $_.Name -match "SiNiSistar" -and (Test-Path (Join-Path $_.FullName "SiNiSistar2.exe"))
            } | ForEach-Object { $candidates += $_.FullName }
        } catch { }
    }
    return $candidates
}

function Get-AllCandidates {
    $all = @()
    $all += Find-SteamGameDir
    $all += Find-UninstallEntries
    $all += Find-CommonDrives
    $seen = @{}
    $result = @()
    foreach ($c in $all) {
        $key = $c.ToLower()
        if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; $result += $c }
    }
    return $result
}

function Get-GameVersion($dir) {
    $bf = Join-Path $dir "build_info.txt"
    if (Test-Path $bf) { try { return (Get-Content $bf -Raw).Trim() } catch { return "?" } }
    return "?"
}

# 读一个 dll 的版本号（1.0.9.0 -> 1.0.9）
function Get-DllVersion($dllPath) {
    if (-not (Test-Path $dllPath)) { return $null }
    try {
        $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath)
        $v = $vi.FileVersion
        if ([string]::IsNullOrWhiteSpace($v)) { $v = $vi.ProductVersion }
        if ([string]::IsNullOrWhiteSpace($v)) { return "?" }
        return ($v -replace "\.0$", "")
    } catch {
        return "?"
    }
}

# 按需同步：文件大小一样就跳过，只复制真正有变化的。
# 更新时通常只有 AllAbnormalMod.dll 变了，几秒钟就能装完。
function Sync-Tree($srcDir, $dstDir) {
    $copied = 0
    $skipped = 0
    if (-not (Test-Path $dstDir)) { New-Item -ItemType Directory -Path $dstDir -Force | Out-Null }
    foreach ($f in (Get-ChildItem $srcDir -Recurse -File -ErrorAction SilentlyContinue)) {
        $rel = $f.FullName.Substring($srcDir.Length).TrimStart("\")
        $target = Join-Path $dstDir $rel
        $targetParent = Split-Path $target -Parent
        if (-not (Test-Path $targetParent)) { New-Item -ItemType Directory -Path $targetParent -Force | Out-Null }
        $needCopy = $true
        if (Test-Path $target) {
            try { if ((Get-Item $target).Length -eq $f.Length) { $needCopy = $false } } catch { }
        }
        if ($needCopy) { Copy-Item $f.FullName $target -Force; $copied++ } else { $skipped++ }
    }
    return @{ Copied = $copied; Skipped = $skipped }
}

# 早期安装器有个毛病：目标目录已存在时会把 MelonLoader 整个塞进去，
# 结果在 MelonLoader\MelonLoader\ 下留下一整套运行时文件（几十 MB 垃圾，
# 而且真正该更新的依赖根本没更新到）。这里认出来就清掉。
function Remove-NestedResidue($gameDir) {
    $nested = Join-Path $gameDir "MelonLoader\MelonLoader"
    if (-not (Test-Path (Join-Path $nested "net6\MelonLoader.dll"))) { return $false }
    try {
        Remove-Item $nested -Recurse -Force -ErrorAction Stop
        return $true
    } catch {
        return $false
    }
}

function Install-Into($gameDir) {
    $srcRoot = Join-Path $PSScriptRoot "ModData"
    $newVer = Get-DllVersion (Join-Path $srcRoot "Mods\AllAbnormalMod.dll")
    $oldVer = Get-DllVersion (Join-Path $gameDir "Mods\AllAbnormalMod.dll")
    $isUpdate = ($null -ne $oldVer)

    Write-Host ""
    Write-Host "==========================================================" -ForegroundColor Cyan
    Write-Host "  游戏目录: $gameDir" -ForegroundColor Cyan
    $ver = Get-GameVersion $gameDir
    if ($ver -ne "?") {
        Write-Host "  游戏版本: $ver" -ForegroundColor Cyan
        $vnum = $ver -replace "[^0-9.]", ""
        if ([string]::IsNullOrEmpty($vnum)) { $vnum = "1.0" }
        try { if ([version]$vnum -lt [version]"1.3.0") { Write-Host "  [注意] 旧版本，如游戏异常请升级到 1.3.1" -ForegroundColor Yellow } } catch { }
    }
    if (-not $isUpdate) {
        Write-Host "  Mod 状态: 没装过，将全新安装 v$newVer" -ForegroundColor Green
    } elseif ($oldVer -eq $newVer) {
        Write-Host "  Mod 状态: 已装 v$oldVer（与安装包同版本，将校验并修复缺失文件）" -ForegroundColor Green
    } else {
        Write-Host "  Mod 状态: 已装 v$oldVer  ->  将更新到 v$newVer" -ForegroundColor Green
    }
    Write-Host "==========================================================" -ForegroundColor Cyan

    # 早期安装器留下的嵌套目录先清掉：它不但占地方，还会让后面的同步白做
    if (Remove-NestedResidue $gameDir) {
        Write-Host "  [清理] 发现旧版安装器留下的嵌套目录 MelonLoader\MelonLoader\，已删除" -ForegroundColor Yellow
    }

    Write-Host "[1/3] 同步 MelonLoader 运行框架（含离线 IL2CPP 依赖）..."
    # version.dll 有 11 MB，同版本重复运行时没必要再写一遍：大小一致就跳过
    $verSrc = Join-Path $srcRoot "version.dll"
    $verDst = Join-Path $gameDir "version.dll"
    if (-not (Test-Path $verDst) -or (Get-Item $verDst).Length -ne (Get-Item $verSrc).Length) {
        Copy-Item $verSrc $verDst -Force
    }
    $r1 = Sync-Tree (Join-Path $srcRoot "MelonLoader") (Join-Path $gameDir "MelonLoader")
    Write-Host "      写入 $($r1.Copied) 个，跳过 $($r1.Skipped) 个已是最新的"
    Write-Host "[2/3] 安装 Mod 插件..."
    $modDir = Join-Path $gameDir "Mods"
    if (-not (Test-Path $modDir)) { New-Item -ItemType Directory -Path $modDir -Force | Out-Null }
    Copy-Item (Join-Path $srcRoot "Mods\AllAbnormalMod.dll") (Join-Path $modDir "AllAbnormalMod.dll") -Force
    Write-Host "[3/3] 安装简体中文语言包..."
    $lcDir = Join-Path $gameDir "Mod\Localize\ZH_CN"
    if (-not (Test-Path $lcDir)) { New-Item -ItemType Directory -Path $lcDir -Force | Out-Null }
    $r3 = Sync-Tree (Join-Path $srcRoot "Localize\ZH_CN") $lcDir
    Write-Host "      写入 $($r3.Copied) 个，跳过 $($r3.Skipped) 个已是最新的"
    Write-Host ""
    if ($isUpdate) {
        if ($oldVer -eq $newVer) { Write-Host "校验完成！当前版本 v$newVer" -ForegroundColor Green }
        else { Write-Host "更新完成！v$oldVer  ->  v$newVer" -ForegroundColor Green }
        Write-Host "  说明：屏蔽清单 Mods\AllAbnormalMod_blocked.txt、控制台设置等都会原样保留。" -ForegroundColor DarkGray
    } else {
        Write-Host "安装完成！" -ForegroundColor Green
    }
    Write-Host "  (AllAbnormalMod by 大赢经直插白皮赢道&汐蓝)" -ForegroundColor DarkGray
    Write-Host "  1) 从 Steam（或你的游戏启动器）正常启动游戏"
    Write-Host "     （安装包已内置 IL2CPP 离线依赖，首次启动不需要从 GitHub 下载任何东西）"
    Write-Host "  2) 游戏内：设置 - 语言 - 简体中文"
    Write-Host "  3) 游戏中任意界面按 Home 或 Delete 打开 Mod 面板（图鉴里也会自动出现，无需通关解锁）"
    Write-Host "     W/S 选择   A/D 调等级   鼠标左键/右键点行切换   Home 或 Delete 再按一下关闭面板"
    Write-Host "     面板顶部或边框按住左键可以拖动面板"
    Write-Host "  4) （可选）想让游戏更流畅、不显示黑色控制台窗口："
    Write-Host "     用记事本打开 <游戏目录>\UserData\Loader.cfg，把 hide_console = false 改成 true"
    Write-Host "     （日志仍会写入 MelonLoader\Latest.log）"
    Write-Host ""
}

Write-Host ""
Write-Host "=== SiNiSistar 2「全异常状态设定」Mod 安装器 ===" -ForegroundColor Cyan

# 允许直接指定游戏目录（高级用法，也方便自动化测试）
if ($GameDir) {
    if (Test-Path (Join-Path $GameDir "SiNiSistar2.exe")) {
        Install-Into $GameDir
    } else {
        Write-Host "指定的目录里没有 SiNiSistar2.exe：$GameDir" -ForegroundColor Red
    }
    return
}

$candidates = @(Get-AllCandidates)

if ($candidates.Count -eq 0) {
    Write-Host "未自动找到游戏目录。请手动输入游戏路径（例如 D:\Games\SiNiSistar 2）：" -ForegroundColor Yellow
    $manual = Read-Host "游戏路径"
    if ($manual -and (Test-Path (Join-Path $manual "SiNiSistar2.exe"))) {
        Install-Into $manual
    } else {
        Write-Host "路径无效或不存在，安装取消。" -ForegroundColor Red
    }
} elseif ($candidates.Count -eq 1) {
    Write-Host "已自动找到游戏目录：" -ForegroundColor Green
    Install-Into $candidates[0]
} else {
    Write-Host "找到多个游戏目录，请选择要安装的目录：" -ForegroundColor Green
    for ($i = 0; $i -lt $candidates.Count; $i++) {
        $ver = Get-GameVersion $candidates[$i]
        Write-Host "  [$($i+1)] $($candidates[$i])  (版本: $ver)"
    }
    $sel = Read-Host "输入编号 (1-$($candidates.Count))"
    $idx = 0
    if (-not [int]::TryParse($sel, [ref]$idx) -or $idx -lt 1 -or $idx -gt $candidates.Count) {
        Write-Host "无效输入，安装取消。" -ForegroundColor Red
    } else {
        Install-Into $candidates[$idx-1]
    }
}