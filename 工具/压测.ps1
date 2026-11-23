# =====================================================================
#  极端场景压测 —— 自动部分
#  说明：面板交互需要真按键（keybd_event 对 Unity Input System 无效），
#        所以本脚本只覆盖「不需要按键」的自动化项：
#          A. 长时间挂机（默认 10 分钟）—— 查内存泄漏
#          B. 反复冷启动（默认 5 轮）—— 查启动过程是否稳定、是否有残留
#        需要按键的交互项见 压测清单.md。
#
#  用法：
#    pwsh -File 压测.ps1                  # 默认 A 10 分钟 + B 5 轮
#    pwsh -File 压测.ps1 -IdleMinutes 30  # 挂机 30 分钟
#    pwsh -File 压测.ps1 -SkipCold        # 只跑挂机
# =====================================================================

param(
    [int]$IdleMinutes = 10,
    [int]$ColdRounds  = 5,
    [switch]$SkipIdle,
    [switch]$SkipCold
)

$ErrorActionPreference = 'Continue'

$g    = 'C:\Users\asus\Downloads\SiNiSistar2_v1_2_0.zip\SiNiSistar2_v1_2_0\WIN'
$exe  = Join-Path $g 'SiNiSistar2.exe'
$log  = Join-Path $g 'MelonLoader\Latest.log'
$out  = 'C:\Users\asus\AppData\Local\Temp\dsh_stress'
if (-not (Test-Path $out)) { New-Item -ItemType Directory -Path $out -Force | Out-Null }

function Stop-Game {
    Get-Process -Name 'SiNiSistar2' -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 4
}

function Get-LogErrors {
    if (-not (Test-Path $log)) { return @() }
    Get-Content $log -Encoding UTF8 |
        Where-Object { $_ -match '\[ERROR\]|Exception|Warning.*AllAbnormalMod' } |
        Select-Object -First 20
}

# ---------------------------------------------------------------- A. 长时间挂机
if (-not $SkipIdle) {
    Write-Host ""
    Write-Host ("=== A. idle stress: " + $IdleMinutes + " minutes ===")
    if (Test-Path $log) { Remove-Item $log -Force }

    $p = Start-Process -FilePath $exe -WorkingDirectory $g -PassThru
    Start-Sleep -Seconds 55
    if ($p.HasExited) { Write-Host ("  [!] exited early, code=" + $p.ExitCode) }
    else {
        $rows = @()
        $prevCpu = $p.TotalProcessorTime
        $rounds = [int]($IdleMinutes * 6)      # 每 10 秒一次
        for ($i = 1; $i -le $rounds; $i++) {
            Start-Sleep -Seconds 10
            try { $p.Refresh() } catch { break }
            if ($p.HasExited) { Write-Host ("  [!] crashed at " + ($i*10) + "s"); break }
            $cpu = $p.TotalProcessorTime
            $d = ($cpu - $prevCpu).TotalMilliseconds
            $prevCpu = $cpu
            $rows += [pscustomobject]@{
                sec = $i * 10
                cpuPct = [math]::Round($d / 10000.0 * 100, 2)
                workMB = [math]::Round($p.WorkingSet64 / 1MB, 1)
                privMB = [math]::Round($p.PrivateMemorySize64 / 1MB, 1)
                gcMB   = [math]::Round([GC]::GetTotalMemory($false) / 1MB, 1)
                threads = $p.Threads.Count
                handles = $p.HandleCount
            }
            if ($i % 6 -eq 0) {
                Write-Host ("  t=" + ($i*10) + "s  work=" + $rows[-1].workMB + "MB  cpu=" + $rows[-1].cpuPct + "%  threads=" + $rows[-1].threads + "  handles=" + $rows[-1].handles)
            }
        }

        Write-Host ""
        if ($rows.Count -ge 6) {
            $first = $rows | Select-Object -Skip 3 | Select-Object -First 1
            $last  = $rows[-1]
            $spanMin = ($last.sec - $first.sec) / 60.0
            $rate = [math]::Round(($last.workMB - $first.workMB) / [math]::Max(0.1, $spanMin), 2)
            Write-Host ("  working set: " + $first.workMB + " -> " + $last.workMB + " MB   (" + $rate + " MB/min)")
            Write-Host ("  threads    : " + $first.threads + " -> " + $last.threads)
            Write-Host ("  handles    : " + $first.handles + " -> " + $last.handles)
            Write-Host ("  avg cpu    : " + [math]::Round(($rows | Measure-Object cpuPct -Average).Average, 2) + "%")
            Write-Host ""
            if ($rate -gt 3) { Write-Host "  [结论] 内存持续增长 >3MB/min —— 需要查泄漏" -ForegroundColor Yellow }
            elseif ($rate -gt 1) { Write-Host "  [结论] 有轻微增长，多为 Unity 资源缓存，建议再跑一轮确认" -ForegroundColor Yellow }
            else { Write-Host "  [结论] 内存平稳，无泄漏迹象" -ForegroundColor Green }
        }
        $rows | Export-Csv (Join-Path $out 'idle.csv') -NoTypeInformation -Encoding UTF8

        $errs = Get-LogErrors
        if ($errs) { Write-Host "  [日志异常]"; $errs | ForEach-Object { Write-Host ("    " + $_) } }
        else { Write-Host "  [日志] 无 error / 无 exception" -ForegroundColor Green }
    }
    Stop-Game
}

# ---------------------------------------------------------------- B. 反复冷启动
if (-not $SkipCold) {
    Write-Host ""
    Write-Host ("=== B. cold start x" + $ColdRounds + " ===")
    $okCount = 0
    for ($i = 1; $i -le $ColdRounds; $i++) {
        if (Test-Path $log) { Remove-Item $log -Force }
        $p = Start-Process -FilePath $exe -WorkingDirectory $g -PassThru
        Start-Sleep -Seconds 50
        if ($p.HasExited) {
            Write-Host ("  round " + $i + ": FAILED (exit " + $p.ExitCode + ")")
        } else {
            $ok = $false
            $ver = ''
            if (Test-Path $log) {
                $hit = Select-String -Path $log -Pattern 'AllAbnormalMod v([\d\.]+)' -Encoding UTF8 | Select-Object -First 1
                if ($hit) { $ver = $hit.Matches[0].Groups[1].Value; $ok = $true }
            }
            $panel = ''
            if (Test-Path $log) {
                $ph = Select-String -Path $log -Pattern 'Panel built on overlay canvas: (.+)$' -Encoding UTF8 | Select-Object -First 1
                if ($ph) { $panel = $ph.Matches[0].Groups[1].Value }
            }
            Write-Host ("  round " + $i + ": mod v" + $ver + "  " + $panel)
            if ($ok) { $okCount++ }
        }
        Stop-Game
    }
    Write-Host ""
    if ($okCount -eq $ColdRounds) { Write-Host ("  [结论] " + $ColdRounds + "/" + $ColdRounds + " 全部正常加载") -ForegroundColor Green }
    else { Write-Host ("  [结论] 仅 " + $okCount + "/" + $ColdRounds + " 正常，需排查") -ForegroundColor Yellow }
}

Write-Host ""
Write-Host ("results -> " + $out)
Write-Host "### done"
