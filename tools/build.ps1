param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnet)) { throw 'Install a .NET 8 or 9 SDK for development. Players do not need it.' }
$env:PATH = (Split-Path -Parent $dotnet) + ';' + $env:PATH
& (Join-Path $PSScriptRoot 'bootstrap.ps1')
$engine = Join-Path $root '.tools\godot\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64_console.exe'
New-Item -ItemType Directory -Force -Path 'artifacts','dist\Thornwake-Windows' | Out-Null
& $dotnet restore Thornwake.csproj
if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
if (-not $SkipTests) {
    & $dotnet run --project Tests/Thornwake.Core.Tests.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
}
& $dotnet build Thornwake.csproj --no-restore
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $engine --headless --path $root --editor --import --quit *> 'artifacts\import.log'
if ($LASTEXITCODE -ne 0) { throw 'Godot import failed; see artifacts/import.log.' }
& $engine --headless --path $root --export-release 'Windows Desktop' 'dist/Thornwake-Windows/Thornwake.exe' *> 'artifacts\export.log'
if ($LASTEXITCODE -ne 0 -or (Select-String -LiteralPath 'artifacts\export.log' -Pattern 'ERROR:' -Quiet)) { throw 'Export failed; see artifacts/export.log.' }
$release = Join-Path $root 'dist\Thornwake-Windows'
Copy-Item -LiteralPath 'README.md' -Destination (Join-Path $release 'README.md') -Force
if (Test-Path -LiteralPath 'Licenses') { Copy-Item -LiteralPath 'Licenses' -Destination $release -Recurse -Force }
Copy-Item -LiteralPath 'docs' -Destination $release -Recurse -Force
$packageFiles = @('Thornwake.exe','data_Thornwake_windows_x86_64','README.md','docs','Licenses') | ForEach-Object { Join-Path $release $_ }
Compress-Archive -LiteralPath $packageFiles -DestinationPath 'dist\Thornwake-0.8.0-Windows-x64.zip' -Force
$hash = Get-FileHash -LiteralPath 'dist\Thornwake-0.8.0-Windows-x64.zip' -Algorithm SHA256
"$($hash.Hash)  Thornwake-0.8.0-Windows-x64.zip" | Set-Content -LiteralPath 'dist\SHA256SUMS.txt'
Write-Host 'Ready: dist/Thornwake-0.8.0-Windows-x64.zip'





