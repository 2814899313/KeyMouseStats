<#
.SYNOPSIS
    给 Rime（小狼毫 / 鼠须管 / fcitx5-rime / ibus-rime）安装 commit_logger 上屏记录插件。

.DESCRIPTION
    只做三件事，全部可回滚：
      1. 把 lua/commit_logger.lua、lua/commit_logger_filter.lua 复制进 <用户目录>/lua/
      2. 在 <用户目录>/rime.lua 末尾追加两行登记（已存在则跳过）
      3. 在 <用户目录>/<方案>.custom.yaml 的 patch: 下追加处理器与滤镜两行（已存在则跳过）
    改动前会把被改的文件备份到 <用户目录>/_backup_commit_logger_<时间戳>/。
    不会删除任何已有配置，不会覆盖你的词库。

.PARAMETER Schema
    要挂插件的方案名，默认 rime_ice（雾凇拼音）。多个用逗号分隔，例：-Schema rime_ice,double_pinyin

.PARAMETER UserDir
    Rime 用户目录。默认自动探测（%APPDATA%\Rime、~/Library/Rime、fcitx5、ibus）。

.PARAMETER NoDeploy
    装完不自动重新部署。

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-commit-logger.ps1

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install-commit-logger.ps1 -Schema rime_ice,double_pinyin
#>
[CmdletBinding()]
param(
    [string]$Schema = 'rime_ice',
    [string]$UserDir = '',
    [switch]$NoDeploy
)

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$luaSource = Join-Path $scriptDir 'lua'

function Write-Step($t) { Write-Host "  -> $t" -ForegroundColor Gray }
function Write-Ok($t) { Write-Host "  OK $t" -ForegroundColor Green }
function Write-Warn($t) { Write-Host "  !  $t" -ForegroundColor Yellow }
function Write-Bad($t) { Write-Host "  X  $t" -ForegroundColor Red }

function Resolve-UserDir {
    if ($UserDir) { return $UserDir }
    $candidates = @(
        (Join-Path $env:APPDATA 'Rime'),
        (Join-Path $HOME 'Library/Rime'),
        (Join-Path $HOME '.local/share/fcitx5/rime'),
        (Join-Path $HOME '.config/ibus/rime')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) { return $c }
    }
    return $null
}

function Backup-File([string]$path, [string]$backupDir) {
    if (-not (Test-Path $path)) { return }
    $name = Split-Path -Leaf $path
    Copy-Item $path (Join-Path $backupDir $name) -Force
}

function Read-Text([string]$path) {
    if (-not (Test-Path $path)) { return '' }
    return [System.IO.File]::ReadAllText($path)
}

function Write-Text([string]$path, [string]$text) {
    [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

Write-Host ''
Write-Host '== commit_logger 上屏记录插件 ==' -ForegroundColor Cyan
Write-Host ''

# ---------------------------------------------------------------- 0. 前置检查
Write-Step '检查安装包与用户目录'
foreach ($f in @('commit_logger.lua', 'commit_logger_filter.lua')) {
    if (-not (Test-Path (Join-Path $luaSource $f))) {
        Write-Bad "安装包不完整：缺少 lua/$f（请从仓库完整下载，不要只拷脚本）"
        exit 1
    }
}
$root = Resolve-UserDir
if (-not $root) {
    Write-Bad '没找到 Rime 用户目录。请先安装小狼毫/鼠须管，或用 -UserDir 指定。'
    exit 1
}
if (-not (Test-Path $root)) {
    Write-Bad "用户目录不存在：$root"
    exit 1
}
Write-Ok "用户目录 $root"

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $root "_backup_commit_logger_$stamp"
New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
Write-Ok "备份目录 $backupDir"

# ---------------------------------------------------------------- 1. lua 文件
Write-Step '安装 lua 模块'
$luaDir = Join-Path $root 'lua'
if (-not (Test-Path $luaDir)) { New-Item -ItemType Directory -Path $luaDir -Force | Out-Null }
foreach ($f in @('commit_logger.lua', 'commit_logger_filter.lua')) {
    $dst = Join-Path $luaDir $f
    Backup-File $dst $backupDir
    Copy-Item (Join-Path $luaSource $f) $dst -Force
    Write-Ok "lua/$f"
}

# ---------------------------------------------------------------- 2. rime.lua 登记
Write-Step '登记到 rime.lua'
$rimeLua = Join-Path $root 'rime.lua'
$lines = @(
    'commit_logger        = require("commit_logger")',
    'commit_logger_filter = require("commit_logger_filter")'
)
$text = Read-Text $rimeLua
$added = @()
foreach ($line in $lines) {
    if ($text -notmatch [regex]::Escape($line.Split('=')[0].Trim())) { $added += $line }
}
if ($added.Count -eq 0) {
    Write-Ok 'rime.lua 已有登记行'
}
else {
    Backup-File $rimeLua $backupDir
    $eol = "`r`n"
    if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { $text += $eol }
    if ($text.Length -eq 0) { $text = "-- rime.lua`r`n" }
    $text += ($added -join $eol) + $eol
    Write-Text $rimeLua $text
    Write-Ok ("rime.lua 追加 " + $added.Count + " 行（原文件已备份）")
}

# ---------------------------------------------------------------- 3. 方案补丁
Write-Step '写入方案补丁'
$processorLine = '  "engine/processors/+":'
$filterLine = '  "engine/filters/+":'
$processorItem = '    - lua_processor@*commit_logger'
$filterItem = '    - lua_filter@*commit_logger_filter'

$schemas = $Schema.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }
foreach ($s in $schemas) {
    $patchPath = Join-Path $root ($s + '.custom.yaml')
    $existing = Read-Text $patchPath

    if ($existing -and ($existing -match '(?m)^[A-Za-z_]' ) -and ($existing -notmatch '(?m)^patch:\s*$')) {
        Write-Warn "$s.custom.yaml 结构非常规（没有顶层 patch:），跳过自动修改，请按 README 手动合并"
        continue
    }
    # 顶层键里如果 patch: 之后还有别的键，追加会落到错误的层级 —— 宁可不动。
    if ($existing) {
        $topLevel = [regex]::Matches($existing, '(?m)^([A-Za-z_][\w\-]*):')
        if ($topLevel.Count -gt 1) {
            Write-Warn "$s.custom.yaml 里有多个顶层键，跳过自动修改，请按 README 手动合并"
            continue
        }
    }

    $needProcessor = $existing -notmatch [regex]::Escape($processorItem)
    $needFilter = $existing -notmatch [regex]::Escape($filterItem)
    if (-not $needProcessor -and -not $needFilter) {
        Write-Ok "$s.custom.yaml 已有补丁"
        continue
    }

    Backup-File $patchPath $backupDir
    $eol = "`r`n"
    if (-not $existing) { $existing = "patch:$eol" }
    if (-not $existing.EndsWith("`n")) { $existing += $eol }
    if ($needProcessor) {
        $existing += $processorLine + $eol + $processorItem + $eol
    }
    if ($needFilter) {
        $existing += $filterLine + $eol + $filterItem + $eol
    }
    Write-Text $patchPath $existing
    Write-Ok "$s.custom.yaml 已写入（原文件已备份）"
}

# ---------------------------------------------------------------- 4. 重新部署
if ($NoDeploy) {
    Write-Warn '已指定 -NoDeploy，请手动「重新部署」'
}
else {
    Write-Step '尝试自动重新部署'
    $deployer = @(
        (Join-Path $env:ProgramFiles 'Rime\weasel-*\WeaselDeployer.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Rime\weasel-*\WeaselDeployer.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Rime\weasel-*\WeaselDeployer.exe')
    ) | ForEach-Object { Get-ChildItem $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
    if ($deployer) {
        & $deployer.FullName /deploy | Out-Null
        Write-Ok '已调用 WeaselDeployer /deploy'
    }
    else {
        Write-Warn '没找到 WeaselDeployer，请手动：托盘图标右键 → 重新部署'
    }
}

Write-Host ''
Write-Host '== 完成 ==' -ForegroundColor Cyan
Write-Host ''
Write-Host '验证：随手打几个字上屏，然后看用户目录里的 commit_log.csv 是否增长：'
Write-Host ("  " + (Join-Path $root 'commit_log.csv'))
Write-Host ''
Write-Host '表头应为：时间点,字符数,明文,程序,方案,码长,候选序'
Write-Host '出现 commit_log.err.txt 时，里面会写具体原因。'
Write-Host ''
