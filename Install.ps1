param([switch]$Uninstall)
. (Join-Path $PSScriptRoot 'Core.ps1')
& (Join-Path $PSScriptRoot 'Remove-LegacyHooks.ps1')
$bin = Join-Path (Get-KiroPetHome) 'bin'
$shim = Join-Path $bin 'kiro-pet.cmd'
$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
$entries = @($userPath -split ';' | Where-Object { $_ -and $_.TrimEnd('\') -ne $bin.TrimEnd('\') })
if ($Uninstall) {
    & (Join-Path $PSScriptRoot 'kiro-pet.ps1') startup off | Out-Null
    if (Get-PetProcess) { Send-PetCommand 'stop' $null | Out-Null }
    if (Test-Path -LiteralPath $shim) { [IO.File]::Delete($shim) }
    [Environment]::SetEnvironmentVariable('Path', ($entries -join ';'), 'User')
    'Removed the command, sign-in startup, and owned hooks. Pet assets and settings were kept.'
} else {
    if (-not (Get-Command pwsh -ErrorAction SilentlyContinue)) { throw 'Install PowerShell 7 before running this development build.' }
    [void][IO.Directory]::CreateDirectory($bin)
    $entryPoint = Join-Path $PSScriptRoot 'kiro-pet.ps1'
    $pwsh = (Get-Command pwsh).Source
    if ($entryPoint -match '["%]') { throw 'Installation paths cannot contain quotes or percent signs.' }
    [IO.File]::WriteAllText($shim, "@echo off`r`n`"$pwsh`" -NoProfile -ExecutionPolicy Bypass -File `"$entryPoint`" %*`r`n")
    [Environment]::SetEnvironmentVariable('Path', (($entries + $bin) -join ';'), 'User')
    $env:Path += ";$bin"
    if (Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Startup')) 'Kiro Pet.cmd')) {
        & (Join-Path $PSScriptRoot 'kiro-pet.ps1') startup on | Out-Null
    }
    "Installed kiro-pet. Open a new terminal to use it. Keep this project folder at $PSScriptRoot."
}
