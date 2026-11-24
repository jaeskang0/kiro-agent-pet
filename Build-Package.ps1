param([string]$Version = '0.2.1-dev')
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^[a-zA-Z0-9.-]+$') { throw 'Version must contain only letters, numbers, dots, and hyphens.' }
$files = @(
    'App.ps1', 'App-Features.ps1', 'Core.ps1', 'NativeSessions.cs', 'Remove-LegacyHooks.ps1',
    'Pet-Library.ps1', 'Import-CodexPet.py', 'kiro-pet.ps1', 'kiro-pet.cmd',
    'Install.ps1', 'Verify.ps1', 'Verify-NativeSessions.ps1', 'Verify-HookMigration.ps1', 'Verify-LiveCli.ps1', 'Native-Fixtures.ps1', 'Verify-Imports.py', 'README.md', 'THIRD-PARTY.md', 'LICENSE', 'requirements.txt', 'assets\spark.png'
)
$destination = Join-Path $PSScriptRoot 'dist'
[void][IO.Directory]::CreateDirectory($destination)
$archive = Join-Path $destination "kiro-pet-$Version-windows.zip"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$temporary = "$archive.$([guid]::NewGuid().ToString('N')).tmp"
$zip = [IO.Compression.ZipFile]::Open($temporary, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in $files) {
        $path = Join-Path $PSScriptRoot $file
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing package file: $file" }
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $path, $file.Replace('\', '/'))
    }
} finally { $zip.Dispose() }
[IO.File]::Move($temporary, $archive, $true)
$digest = (Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$archive.sha256", "$digest  $(Split-Path -Leaf $archive)`n")
"Development package: $archive"
'Includes original Spark artwork. Excludes user pets, settings, logs, and personal paths. Requires PowerShell 7.4+; Python/Pillow are needed for imports.'
