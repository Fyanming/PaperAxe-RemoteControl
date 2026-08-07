$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'src\ZhifaRemote'
$issues = @()

function Test-Pattern($name, $pattern, $files) {
    $hits = $files | Select-String -Pattern $pattern -AllMatches
    if ($hits) {
        foreach ($hit in $hits) {
            $issues += "$name : $($hit.Path):$($hit.LineNumber)"
        }
    }
}

$csFiles = Get-ChildItem $src -Recurse -Filter *.cs
$xamlFiles = Get-ChildItem $src -Recurse -Filter *.xaml

# 1. 未完成的标记
Test-Pattern '待办/未实现' 'TODO|FIXME|NotImplementedException' $csFiles

# 2. XAML 动画必须使用 GPU 友好属性（transform/opacity），不允许直接动画布局属性
$badAnim = $xamlFiles | Select-String -Pattern 'Storyboard\.TargetProperty="\((Left|Top|Width|Height|Margin)\)"'
if ($badAnim) {
    foreach ($hit in $badAnim) { $issues += "布局属性动画 : $($hit.Path):$($hit.LineNumber)" }
}

# 3. 网络消息大小边界
$proto = Get-Content (Join-Path $src 'Services\Protocol.cs') -Raw
if ($proto -notmatch 'MaxMessageSize = 16 \* 1024 \* 1024') {
    $issues += '协议层缺少消息大小上限'
}

# 4. 服务层异步 void 事件处理必须包裹异常
$svcFiles = Get-ChildItem (Join-Path $src 'Services') -Filter *.cs
$asyncVoid = $svcFiles | Select-String -Pattern 'async void'
foreach ($hit in $asyncVoid) {
    $lines = Get-Content $hit.Path
    $start = [Math]::Max(0, $hit.LineNumber - 1)
    $end = [Math]::Min($lines.Count, $start + 18)
    $body = ($lines[$start..($end - 1)] -join "`n")
    if ($body -notmatch 'try') {
        $issues += "async void 缺少异常保护 : $($hit.Path):$($hit.LineNumber)"
    }
}

# 5. 文件分块大小固定且有界
$fileTransfer = Get-Content (Join-Path $src 'Services\FileTransferService.cs') -Raw
if ($fileTransfer -notmatch 'ChunkSize = 256 \* 1024') {
    $issues += '文件分块大小未按预期定义'
}

if ($issues.Count -eq 0) {
    Write-Output '✅ 经静态扫描，无潜在 Bug'
}
else {
    Write-Output ('发现 ' + $issues.Count + ' 个静态扫描问题：')
    $issues | ForEach-Object { Write-Output "  - $_" }
    exit 1
}
