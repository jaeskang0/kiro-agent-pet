. (Join-Path $PSScriptRoot 'Core.ps1')
$connectionsPath = Join-Path (Get-KiroPetHome) 'connections.json'
if (-not (Test-Path -LiteralPath $connectionsPath)) { return }
$oldConnections = @(Get-Content -LiteralPath $connectionsPath -Raw | ConvertFrom-Json)
foreach ($connection in $oldConnections) {
    $workspace = [IO.Path]::GetFullPath([string]$connection.workspace)
    $file = Join-Path $workspace '.kiro\hooks\kiro-pet.json'
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { continue }
    $document = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
    if ($document.kiroPetOwner -ne 'kiro-pet' -or $document.version -ne 'v1') { continue }
    $owned = @($document.hooks | Where-Object {
        $_.name -like 'Kiro Pet *' -and $_.action.command -match '[\\/]Publish-Session\.ps1"'
    })
    if (-not $owned.Count) { continue }
    $remaining = @($document.hooks | Where-Object { $_ -notin $owned })
    $backup = Join-Path (Get-KiroPetHome) ('hook-backups\' + (Get-WorkspaceKey $workspace))
    [void][IO.Directory]::CreateDirectory($backup)
    Copy-Item -LiteralPath $file -Destination (Join-Path $backup ('kiro-pet-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff') + '.json'))
    if ($remaining.Count) { Write-PetJson $file @{ version = 'v1'; hooks = $remaining } }
    else { [IO.File]::Delete($file) }
    "Removed $($owned.Count) legacy Kiro Pet hooks from $workspace; unrelated hooks preserved."
}
[IO.File]::Move($connectionsPath, (Join-Path (Get-KiroPetHome) ('connections-legacy-' + [guid]::NewGuid().ToString('N') + '.json')))
