param([string]$Version = '4.7.2')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$toolDir = Join-Path $root '.tools'
New-Item -ItemType Directory -Force -Path $toolDir | Out-Null
$engineDir = Join-Path $toolDir "godot\Godot_v$Version-stable_mono_win64"
if (-not (Test-Path -LiteralPath (Join-Path $engineDir "Godot_v$Version-stable_mono_win64.exe"))) {
    $archivePath = Join-Path $toolDir 'godot.zip'
    Invoke-WebRequest -Uri "https://github.com/godotengine/godot/releases/download/$Version-stable/Godot_v$Version-stable_mono_win64.zip" -OutFile $archivePath
    Expand-Archive -LiteralPath $archivePath -DestinationPath (Join-Path $toolDir 'godot') -Force
}
$templatesDir = Join-Path $toolDir 'templates'
New-Item -ItemType Directory -Force -Path $templatesDir | Out-Null
if (-not (Test-Path -LiteralPath (Join-Path $templatesDir 'windows_release_x86_64.exe'))) {
    $archivePath = Join-Path $toolDir 'templates.zip'
    Invoke-WebRequest -Uri "https://github.com/godotengine/godot/releases/download/$Version-stable/Godot_v$Version-stable_mono_export_templates.tpz" -OutFile $archivePath
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        foreach ($entry in $archive.Entries) {
            if ($entry.FullName -match '^templates/windows_(release|debug)_x86_64(_console)?\.exe$') {
                [System.IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $templatesDir $entry.Name), $true)
            }
        }
    } finally { $archive.Dispose() }
}
Write-Host "Godot $Version is ready in $engineDir"
