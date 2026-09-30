param([string]$Label='source')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$backupPath=Join-Path $taskRoot ('backups\Thornwake-'+$Label+'-'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'.zip')
$sourceFiles=Get-ChildItem -LiteralPath $taskRoot -File -Recurse | Where-Object { $_.FullName.Substring($taskRoot.Length+1) -notmatch '^(\.godot|\.tools|backups|artifacts|dist|ArtKit|\.git)[\\/]|[\\/](bin|obj)[\\/]' }
Add-Type -AssemblyName System.IO.Compression
$zip=[IO.Compression.ZipFile]::Open($backupPath,[IO.Compression.ZipArchiveMode]::Create)
$manifest=@()
try { foreach($file in $sourceFiles) { $relative=$file.FullName.Substring($taskRoot.Length+1).Replace('\','/'); [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$file.FullName,$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null; $manifest+= [pscustomobject]@{Path=$relative;SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash} } } finally {$zip.Dispose()}
$zip=[IO.Compression.ZipFile]::OpenRead($backupPath)
try { foreach($entry in $manifest) { $stream=$zip.GetEntry($entry.Path).Open(); try { $sha=[Security.Cryptography.SHA256]::Create(); $actual=[BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-',''); $sha.Dispose(); if($actual -ne $entry.SHA256){throw ('Backup verification failed: '+$entry.Path)} } finally {$stream.Dispose()} } } finally {$zip.Dispose()}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath ($backupPath+'.manifest.json')
Write-Output ('Verified '+$manifest.Count+' source files: '+$backupPath)
