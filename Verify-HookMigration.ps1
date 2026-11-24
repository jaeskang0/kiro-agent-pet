$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Core.ps1')
$originalHome = $env:KIRO_PET_HOME
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('kiro-pet-migration-check-' + [guid]::NewGuid().ToString('N'))
try {
    $env:KIRO_PET_HOME = Join-Path $temporary 'data'
    $workspace = Join-Path $temporary 'workspace'
    $hookFile = Join-Path $workspace '.kiro\hooks\kiro-pet.json'
    Write-PetJson $hookFile @{
        version = 'v1'; kiroPetOwner = 'kiro-pet'; hooks = @(
            @{ name = 'Kiro Pet Working'; action = @{ command = 'powershell -File "C:\fixture\Publish-Session.ps1" -Status working' } },
            @{ name = 'My existing hook'; action = @{ command = 'echo existing' } }
        )
    }
    Write-PetJson (Join-Path $env:KIRO_PET_HOME 'connections.json') @(@{ workspace = $workspace; hookFile = $hookFile })
    & (Join-Path $PSScriptRoot 'Remove-LegacyHooks.ps1')
    $remaining = Get-Content -LiteralPath $hookFile -Raw | ConvertFrom-Json
    if ($remaining.hooks.Count -ne 1 -or $remaining.hooks[0].name -ne 'My existing hook') { throw 'Migration removed an unrelated hook.' }
    $backups = @(Get-ChildItem -LiteralPath (Join-Path $env:KIRO_PET_HOME 'hook-backups') -Recurse -Filter '*.json')
    if ($backups.Count -ne 1) { throw 'Migration did not preserve a backup.' }
    & (Join-Path $PSScriptRoot 'Remove-LegacyHooks.ps1')
    Write-Output 'Hook migration PASS: backup, removal of owned hooks only, repeat operation.'
} finally {
    $env:KIRO_PET_HOME = $originalHome
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char]92) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('kiro-pet-migration-check-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
