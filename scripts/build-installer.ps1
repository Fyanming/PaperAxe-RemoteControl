$ErrorActionPreference = 'Stop'

$root = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$iscc = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$publishDir = [System.IO.Path]::GetFullPath((Join-Path $root 'dist\publish'))
$distDir = [System.IO.Path]::GetFullPath((Join-Path $root 'dist'))

if (-not $publishDir.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Invalid publish directory: $publishDir"
}
if (-not (Test-Path $iscc)) {
    throw "Inno Setup 6 not found: $iscc"
}

if (Test-Path $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Path $distDir -Force | Out-Null

$csproj = Join-Path $root 'src\ZhifaRemote\ZhifaRemote.csproj'
dotnet publish $csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw 'dotnet publish failed'
}

& $iscc (Join-Path $root 'installer\installer.iss')
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup compilation failed'
}

$installer = Join-Path $distDir '纸伐局域网远控-安装程序-x64.exe'
Write-Output "Installer created: $installer"
