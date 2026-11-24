param([Parameter(Mandatory)][string]$SourceRoot)
. (Join-Path $PSScriptRoot 'Core.ps1')
$selection = Get-Content -LiteralPath (Join-Path $SourceRoot 'selected-pet-kiro.json') -Raw | ConvertFrom-Json
$key = [string]$selection.key
if ($key -notmatch '^[a-z0-9][a-z0-9-]*$') { throw 'Invalid pet key in the old selection.' }
$destination = Join-Path (Get-KiroPetHome) "pets\$key"
[void][IO.Directory]::CreateDirectory($destination)
if ($key -eq 'usagi') {
    Copy-Item -LiteralPath (Join-Path $SourceRoot 'spritesheet.png') -Destination (Join-Path $destination 'spritesheet.png')
    $bounds = Get-Content -LiteralPath (Join-Path $SourceRoot 'usagi-bounds.json') -Raw | ConvertFrom-Json
    Write-PetJson (Join-Path $destination 'pet.json') @{
        key = 'usagi'; displayName = 'Usagi'; spriteVersionNumber = 1; highestVisiblePixel = $bounds.highestVisiblePixel
        localPersonalUse = $true
    }
} else {
    foreach ($file in @('pet.json', 'spritesheet.png')) {
        Copy-Item -LiteralPath (Join-Path $SourceRoot "pets\$key\$file") -Destination (Join-Path $destination $file)
    }
}
Write-PetJson (Join-Path (Get-KiroPetHome) 'selected-pet.json') @{ key = $key; displayName = $selection.displayName }
$oldSettings = Get-Content -LiteralPath (Join-Path $SourceRoot 'pet-settings-kiro.json') -Raw | ConvertFrom-Json
$interval = @{ veryslow = 420; slow = 300; normal = 180; fast = 120 }[$oldSettings.animationSpeed]
if ($interval) {
    $settings = Get-PetSettings
    $settings.speed = [Math]::Round(180.0 / $interval, 2)
    Write-PetJson (Join-Path (Get-KiroPetHome) 'settings.json') $settings
}
"Migrated $($selection.displayName) and playback speed to local Kiro Pet data. Source files were not changed."
