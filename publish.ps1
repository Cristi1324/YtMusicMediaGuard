$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$stage = Join-Path $root "dist\stage"
$package = Join-Path $root "dist\YtMusicMediaGuard"
$zip = Join-Path $root "dist\YtMusicMediaGuard-win-x64.zip"

if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $package) { Remove-Item $package -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item $package -ItemType Directory | Out-Null

dotnet publish (Join-Path $root "src\YtMusicMediaGuard\YtMusicMediaGuard.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $stage `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Copy-Item (Join-Path $stage "YtMusicMediaGuard.exe") $package
Copy-Item (Join-Path $root "extension") (Join-Path $package "extension") -Recurse
Copy-Item (Join-Path $root "README.md") $package
Compress-Archive -Path $package -DestinationPath $zip
Write-Output "Created $zip"
