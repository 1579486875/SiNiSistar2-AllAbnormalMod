# SiNiSistar 2「全异常状态设定」Mod - 自动检测安装脚本
# 自动查找游戏目录（Steam 库 / 卸载登记 / 全盘递归 / 常见游戏位置），确认后安装。
# 已经装过旧版的话会自动更新：只覆盖有变化的文件，其余原样跳过。
# 也可以直接指定目录：install.ps1 -GameDir "D:\Games\SiNiSistar 2"
# 手动填的路径会做容错：去引号、去尾空格、填到 exe、填到外层目录、填到 Data 目录都能自动纠正。
# 兼容 Windows PowerShell 5.1，文件编码 UTF-8 BOM。

param(
    [string]$GameDir = "",
    [int]$SearchSeconds = 30,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

# 递归搜索时跳过的目录名（小写比较）——避免钻进系统目录浪费时间
$script:SkipNames = @(
    'windows', 'winsxs', 'windowsapps', 'system volume information', '$recycle.bin',
    'programdata', 'appdata', 'application data', 'local settings',
    'node_modules', '.git', '.svn', '.vs', '.vscode', '.idea',
    'obj', 'bin', 'packages', 'driverstore', 'assembly', 'microsoft.net',
    'installer', 'softwaredistribution', 'temp', 'tmp', 'cache',
    'windows defender', 'common files', 'windows nt', 'wow6432node',
    'fonts', 'inf', 'logs', 'media', 'servicing', 'speech', 'twain_32'
)

# 常见游戏安装位置（相对盘根），优先扫这些能更快命中
# 注意：不要把 'program files' 放进来 —— 对它做限深递归要遍历几万个目录，
# 会把整个搜索时间预算吃光，而游戏极少直接装在它的根下。
$script:HotNames = @('games', 'game', 'steamapps', 'games library', 'gamelibrary',
                     'downloads', 'desktop', 'documents', 'pc games', 'pcgames')

# ------------------------------------------------------------------ 基础工具

# 路径归一：去引号 / 去尾空格 / 正斜杠转反斜杠
function Normalize-Path([string]$p) {
    if ([string]::IsNullOrWhiteSpace($p)) { return "" }
    $p = $p.Trim()
    $p = $p.Trim('"').Trim("'").Trim()
    $p = $p -replace '/', '\'
    $p = $p.TrimEnd('\')
    return $p
}

<#
 判定一个目录是不是 SiNiSistar 2 的游戏目录。
 指纹取自 <游戏名>_Data/app.info（内容是两行：开发商 Uu、游戏名 SiNiSistar2），
 而不是「有 *_Data + GameAssembly.dll 就算」—— 后者会把绝区零之类
 所有 Unity 游戏都误判进来。用 app.info 既能排除别的 Unity 游戏，
 也能认出 exe / Data 目录被改过名的整合版。
 命中任一即可：
   1) 存在 SiNiSistar2_Data 目录
   2) 存在 SiNiSistar2.exe
   3) 某个 *_Data 目录下的 app.info 里写着 SiNiSistar2
#>
function Test-GameDir([string]$dir) {
    if ([string]::IsNullOrWhiteSpace($dir)) { return $false }
    try {
        if (-not (Test-Path -LiteralPath $dir -PathType Container)) { return $false }
    } catch { return $false }

    $items = $null
    try {
        $items = Get-ChildItem -LiteralPath $dir -Force -ErrorAction SilentlyContinue
    } catch { return $false }
    if (-not $items) { return $false }

    $dataDirs = @()
    foreach ($it in $items) {
        if ($it.PSIsContainer) {
            if ($it.Name -ieq 'SiNiSistar2_Data') { return $true }
            if ($it.Name -like '*_Data') { $dataDirs += $it.FullName }
        } elseif ($it.Name -ieq 'SiNiSistar2.exe') {
            return $true
        }
    }

    # 只有在确实看到 *_Data 目录时才去读 app.info（小文件，读一次很快）
    foreach ($d in $dataDirs) {
        $appInfo = Join-Path $d 'app.info'
        if (-not (Test-Path -LiteralPath $appInfo)) { continue }
        try {
            $c = Get-Content -LiteralPath $appInfo -Raw -ErrorAction SilentlyContinue
            if ($c -and ($c -match 'SiNiSistar2')) { return $true }
        } catch { }
    }
    return $false
}

# 广度优先在 $root 下找游戏目录，最多 $maxDepth 层；返回第一个命中的目录
function Find-GameUnder {
    param(
        [string]$root,
        [int]$maxDepth = 4,
        [int]$maxChildren = 200,
        [datetime]$deadline = [datetime]::MaxValue
    )
    if ([string]::IsNullOrWhiteSpace($root)) { return $null }
    try {
        if (-not (Test-Path -LiteralPath $root -PathType Container)) { return $null }
    } catch { return $null }

    $queue = New-Object System.Collections.Queue
    $queue.Enqueue(@($root, 0))
    $guard = 0

    while ($queue.Count -gt 0) {
        if ((Get-Date) -gt $deadline) { return $null }
        $guard++
        if ($guard -gt 20000) { return $null }

        $item = $queue.Dequeue()
        $dir = $item[0]
        $depth = [int]$item[1]

        if (Test-GameDir $dir) { return $dir }
        if ($depth -ge $maxDepth) { continue }

        $leaf = ""
        try { $leaf = (Split-Path -Leaf $dir).ToLower() } catch { }
        if ($script:SkipNames -contains $leaf) { continue }

        $subs = $null
        try {
            $subs = Get-ChildItem -LiteralPath $dir -Directory -Force -ErrorAction SilentlyContinue |
                    Select-Object -First $maxChildren
        } catch { continue }
        if (-not $subs) { continue }

        foreach ($s in $subs) {
            $queue.Enqueue(@($s.FullName, $depth + 1))
        }
    }
    return $null
}

<#
 全盘找游戏目录。分两轮，先快后全：
   第一轮：只沿着「名字像游戏」的目录深入（sinistar / シニシスタ / sini 等关键词），
           以及常见游戏位置（Games、Downloads、steamapps…）——大多数机器这一步就命中了。
   第二轮：仍有时间预算的话，再对盘根做一次限深递归兜底。
 全程受 $SearchSeconds 时间预算约束，超时就返回已找到的结果。
#>
function Find-GameDirs {
    param([int]$seconds = 30)

    $results = New-Object System.Collections.Generic.List[string]
    $seen = New-Object System.Collections.Generic.HashSet[string]
    $deadline = (Get-Date).AddSeconds($seconds)

    $roots = @()
    try {
        foreach ($d in (Get-PSDrive -PSProvider FileSystem -ErrorAction SilentlyContinue)) {
            if ($d.Root -and (Test-Path -LiteralPath $d.Root)) { $roots += $d.Root }
        }
    } catch { }

    # 每个盘符：先在根目录的直接子目录里按名字筛
    foreach ($r in $roots) {
        if ((Get-Date) -gt $deadline) { break }
        $subs = $null
        try {
            $subs = Get-ChildItem -LiteralPath $r -Directory -Force -ErrorAction SilentlyContinue
        } catch { continue }
        if (-not $subs) { continue }

        foreach ($s in $subs) {
            if ((Get-Date) -gt $deadline) { break }
            $leaf = $s.Name
            $lower = $leaf.ToLower()

            # 候选 1：名字像这个游戏
            $nameHit = ($lower -match 'sinistar' -or $lower -match 'sini' -or
                        $lower -match 'シニシスタ' -or $lower -match 'sisis')
            # 候选 2：常见的游戏/下载目录
            $hotHit = ($script:HotNames -contains $lower)
            # 候选 3：本身就是游戏目录
            if (Test-GameDir $s.FullName) {
                if ($seen.Add($s.FullName.ToLower())) { [void]$results.Add($s.FullName) }
                continue
            }
            if (-not ($nameHit -or $hotHit)) { continue }
            # 已经有结果了（比如注册表里找到了 Steam 版）：这轮只认名字像这个
            # 游戏的目录，不再逐个翻 Games / Downloads 这类常见大目录，省好几秒。
            if ($results.Count -gt 0 -and -not $nameHit) { continue }

            # 单个目录最多找 4 秒：避免某个特别大的目录把预算全吃掉
            $subDeadline = (Get-Date).AddSeconds(4)
            if ($subDeadline -gt $deadline) { $subDeadline = $deadline }
            $hit = Find-GameUnder -root $s.FullName -maxDepth 5 -deadline $subDeadline
            if ($hit -and $seen.Add($hit.ToLower())) { [void]$results.Add($hit) }
        }
    }

    # 用户目录再扫一轮：很多人把整合包解压在 Downloads / Desktop 里，
    # 路径层级往往有 5~6 层，靠盘根浅扫够不到。
    # 但如果前面已经找到过游戏（Steam 版 / 卸载登记 / 盘根），
    # 这里就只快速看一眼常见解压位置 —— 不必为了多找一个副本让玩家干等半分钟。
    if ((Get-Date) -lt $deadline) {
        $alreadyFound = ($results.Count -gt 0)
        $userRoots = @()
        if (-not $alreadyFound) {
            foreach ($v in @($env:USERPROFILE, $env:PUBLIC)) {
                if ($v) { $userRoots += $v }
            }
        }
        if ($env:USERPROFILE) {
            foreach ($sub in @('Downloads', 'Desktop', 'Documents')) {
                $userRoots += (Join-Path $env:USERPROFILE $sub)
            }
        }
        if ($env:PUBLIC) { $userRoots += (Join-Path $env:PUBLIC 'Desktop') }
        $userDepth = 6
        if ($alreadyFound) { $userDepth = 4 }

        foreach ($u in $userRoots) {
            if ((Get-Date) -gt $deadline) { break }
            if (-not (Test-Path -LiteralPath $u -PathType Container)) { continue }
            if (Test-GameDir $u) {
                if ($seen.Add($u.ToLower())) { [void]$results.Add($u) }
                continue
            }
            # 单个目录最多找 5 秒
            $uDeadline = (Get-Date).AddSeconds(5)
            if ($uDeadline -gt $deadline) { $uDeadline = $deadline }
            $hit = Find-GameUnder -root $u -maxDepth $userDepth -maxChildren 500 -deadline $uDeadline
            if ($hit -and $seen.Add($hit.ToLower())) { [void]$results.Add($hit) }
        }
    }

    # 兜底：前面一无所获时，才对每个盘做一次限深递归（这步最慢，能省则省）
    if ($results.Count -eq 0 -and (Get-Date) -lt $deadline) {
        foreach ($r in $roots) {
            if ((Get-Date) -gt $deadline) { break }
            $hit = Find-GameUnder -root $r -maxDepth 4 -maxChildren 300 -deadline $deadline
            if ($hit -and $seen.Add($hit.ToLower())) { [void]$results.Add($hit) }
        }
    }

    return $results
}

# 容错解析用户手填的路径
function Resolve-GameDir([string]$raw) {
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    $p = Normalize-Path $raw
    if ([string]::IsNullOrWhiteSpace($p)) { return $null }

    # 直接填了 exe 文件 -> 取所在目录
    if ($p -match '\.exe$') {
        try { $p = Split-Path -Path $p -Parent } catch { return $null }
    }
    # 填了 Data 目录 -> 取父目录
    if ($p -match '_Data$') {
        try { $p = Split-Path -Path $p -Parent } catch { return $null }
    }

    if (-not (Test-Path -LiteralPath $p -PathType Container)) { return $null }

    # 1) 本身就是游戏目录
    if (Test-GameDir $p) {
        try { return (Get-Item -LiteralPath $p).FullName } catch { return $p }
    }

    # 2) 填得太深（在游戏目录里面）-> 向上找 3 层
    $up = $p
    for ($i = 0; $i -lt 3; $i++) {
        try { $up = Split-Path -Path $up -Parent } catch { break }
        if ([string]::IsNullOrWhiteSpace($up)) { break }
        if (Test-GameDir $up) {
            try { return (Get-Item -LiteralPath $up).FullName } catch { return $up }
        }
    }

    # 3) 填得太浅（在外面）-> 向下找 4 层
    return Find-GameUnder -root $p -maxDepth 4
}

# ------------------------------------------------------------------ 其它探测来源

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
            try {
                $vdfContent = Get-Content $vdf -Raw
                foreach ($m in [regex]::Matches($vdfContent, '"path"\s+"([^"]+)"')) {
                    $p = $m.Groups[1].Value -replace "\\\\", "\"
                    $libRoots += (Join-Path $p "steamapps")
                }
            } catch { }
        }
        foreach ($lib in $libRoots) {
            try {
                if (-not (Test-Path -LiteralPath $lib)) { continue }
                Get-ChildItem -LiteralPath (Join-Path $lib "common") -Directory -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -match 'SiNiSistar|シニシスタ' } |
                    ForEach-Object { if (Test-GameDir $_.FullName) { $candidates += $_.FullName } }
            } catch { }
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
            $items = Get-ItemProperty $r -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match "SiNiSistar|シニシスタ" }
            foreach ($it in $items) {
                $loc = Normalize-Path $it.InstallLocation
                if ($loc) {
                    $ok = Resolve-GameDir $loc
                    if ($ok) { $candidates += $ok }
                }
            }
        } catch { }
    }
    return $candidates
}

function Get-AllCandidates {
    $all = @()
    $all += Find-SteamGameDir
    $all += Find-UninstallEntries
    $all += Find-GameDirs -seconds $SearchSeconds
    $seen = @{}
    $result = @()
    foreach ($c in $all) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        $key = $c.ToLower()
        if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; $result += $c }
    }
    return $result
}

# ------------------------------------------------------------------ 安装

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
        try { if ([version]$vnum -lt [version]"1.2.0") { Write-Host "  [注意] 游戏版本较旧，如异常请升级到 1.3.1" -ForegroundColor Yellow } } catch { }
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

# ------------------------------------------------------------------ 主流程

Write-Host ""
Write-Host "=== SiNiSistar 2「全异常状态设定」Mod 安装器 ===" -ForegroundColor Cyan

# 允许直接指定游戏目录（高级用法，也方便自动化测试）
if ($GameDir) {
    $resolved = Resolve-GameDir $GameDir
    if ($resolved) {
        if ($DryRun) { Write-Host "解析结果: $resolved" -ForegroundColor Green }
        else { Install-Into $resolved }
    } else {
        Write-Host "在指定的目录里没找到游戏：$GameDir" -ForegroundColor Red
        Write-Host "请填「包含 SiNiSistar2.exe 的那个文件夹」。" -ForegroundColor Yellow
    }
    return
}

Write-Host "正在自动查找游戏目录（最多 $SearchSeconds 秒）..." -ForegroundColor DarkGray
$candidates = @(Get-AllCandidates)

function Select-And-Install($list) {
    if ($list.Count -eq 1) {
        Write-Host ""
        Write-Host "已自动找到游戏目录：" -ForegroundColor Green
        Install-Into $list[0]
        return $true
    }
    Write-Host ""
    Write-Host "找到多个游戏目录，请选择要安装的目录：" -ForegroundColor Green
    for ($i = 0; $i -lt $list.Count; $i++) {
        $ver = Get-GameVersion $list[$i]
        Write-Host "  [$($i+1)] $($list[$i])  (版本: $ver)"
    }
    $sel = Read-Host "输入编号 (1-$($list.Count))，直接回车取消"
    $idx = 0
    if ([int]::TryParse($sel, [ref]$idx) -and $idx -ge 1 -and $idx -le $list.Count) {
        Install-Into $list[$idx-1]
        return $true
    }
    return $false
}

$done = $false

if ($candidates.Count -gt 0) {
    $done = Select-And-Install $candidates
}

# 没找到、或者选择时取消了 -> 让玩家手填，填错可以重填
if (-not $done) {
    Write-Host ""
    if ($candidates.Count -eq 0) {
        Write-Host "没能自动找到游戏目录。麻烦手动填一下。" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "怎么填（任选一种都行）：" -ForegroundColor Cyan
    Write-Host "  1. 打开游戏文件夹，找到 SiNiSistar2.exe"
    Write-Host "     右键它 -> 打开文件所在的位置 -> 点地址栏复制路径"
    Write-Host "  2. 或者直接把「有 SiNiSistar2.exe 的那个文件夹」拖进这个窗口"
    Write-Host "     （拖进来如果带引号也没关系，会自动去掉）"
    Write-Host "  3. 填上一级文件夹也可以，会自动往下找。"
    Write-Host ""
    Write-Host "  注意：不要填 exe 文件本身以外的无关目录；填 SiNiSistar2.exe 也能认。" -ForegroundColor DarkGray
    Write-Host ""

    for ($attempt = 1; $attempt -le 5; $attempt++) {
        $manual = Read-Host "请粘贴或拖入游戏目录（直接回车退出）"
        if ([string]::IsNullOrWhiteSpace($manual)) {
            Write-Host "已取消安装。" -ForegroundColor Yellow
            break
        }
        $resolved = Resolve-GameDir $manual
        if ($resolved) {
            Install-Into $resolved
            $done = $true
            break
        }
        Write-Host "这个路径里没找到游戏。再试一次吧（还能试 $(5 - $attempt) 次）：" -ForegroundColor Red
        Write-Host "  你填的是：$manual" -ForegroundColor DarkGray
    }

    if (-not $done) {
        Write-Host ""
        Write-Host "安装取消。可以加群或留言告诉我你的游戏装在哪，我来适配。" -ForegroundColor Yellow
    }
}
