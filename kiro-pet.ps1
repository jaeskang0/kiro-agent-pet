param(
    [Parameter(Position = 0)][ValidateSet('start', 'stop', 'show', 'hide', 'select', 'settings', 'status', 'sessions', 'pets', 'connections', 'startup', 'import', 'help')][string]$Command = 'help',
    [Parameter(Position = 1)][string]$Value,
    [string]$Workspace
)
. (Join-Path $PSScriptRoot 'Core.ps1')

function Start-KiroPet {
    if (Get-PetProcess) { return 'Kiro Pet is already running.' }
    $pwsh = (Get-Command pwsh -ErrorAction Stop).Source
    $app = Join-Path $PSScriptRoot 'App.ps1'
    $data = Get-KiroPetHome
    [void][IO.Directory]::CreateDirectory($data)
    Start-Process -FilePath $pwsh -WindowStyle Hidden -ArgumentList @(
        '-NoProfile', '-STA', '-ExecutionPolicy', 'Bypass', '-File', ('"' + $app + '"')
    ) -RedirectStandardOutput (Join-Path $data 'app.stdout.log') -RedirectStandardError (Join-Path $data 'app.stderr.log') | Out-Null
    $deadline = [DateTime]::UtcNow.AddSeconds(12)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Get-PetProcess) { return 'Kiro Pet started.' }
        Start-Sleep -Milliseconds 100
    }
    throw "Kiro Pet did not start. Check $(Join-Path $data 'app.stderr.log')."
}

switch ($Command) {
    'start' { Start-KiroPet }
    { $_ -in @('stop', 'hide') } { Send-PetCommand $Command $null }
    { $_ -in @('show', 'settings') } {
        if (-not (Get-PetProcess)) { Start-KiroPet | Out-Null }
        Send-PetCommand $Command $null
    }
    'select' {
        if (-not $Value) { throw 'Use kiro-pet select <pet name or key>.' }
        if (-not (Get-PetProcess)) { Start-KiroPet | Out-Null }
        Send-PetCommand 'select' $Value
    }
    'import' {
        if (-not $Value) { throw 'Use kiro-pet import <ZIP or folder>.' }
        if (-not (Get-PetProcess)) { Start-KiroPet | Out-Null }
        Send-PetCommand 'import' $Value
    }
    'status' {
        $sessions = @(Get-PetSessions)
        @{ running = [bool](Get-PetProcess); dataRoot = Get-KiroPetHome; discovery = 'automatic'; sessions = $sessions.Count; health = $script:nativeSnapshot.Health } | ConvertTo-Json
    }
    'sessions' { @(Get-PetSessions) | Select-Object title, sessionId, source, workspace, status, closed, disconnected | Format-Table }
    'pets' {
        . (Join-Path $PSScriptRoot 'Pet-Library.ps1')
        Get-PetCatalog (Get-KiroPetHome) | Select-Object Key, DisplayName | Format-Table
    }
    'connections' { Get-PetConnections | Select-Object workspace, sessions, source | Format-Table }
    'startup' {
        if ($Value -notin @('on', 'off', 'status')) { throw 'Use kiro-pet startup on|off|status.' }
        $startup = Join-Path ([Environment]::GetFolderPath('Startup')) 'Kiro Pet.cmd'
        if ($Value -eq 'on') {
            $pwsh = (Get-Command pwsh).Source
            $cli = Join-Path $PSScriptRoot 'kiro-pet.ps1'
            if ($cli -match '["%]') { throw 'Startup paths cannot contain quotes or percent signs.' }
            [IO.File]::WriteAllText($startup, "@echo off`r`nstart `"`" /b `"$pwsh`" -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File `"$cli`" start`r`n")
            'Start at sign-in enabled.'
        } elseif ($Value -eq 'off') {
            if (Test-Path -LiteralPath $startup) { [IO.File]::Delete($startup) }
            'Start at sign-in disabled.'
        } else { "Start at sign-in: $(Test-Path -LiteralPath $startup)" }
    }
    'help' {
        @'
Kiro Pet for Windows
  kiro-pet start|stop|show|hide|settings|status
  kiro-pet select <name-or-key>
  kiro-pet import <zip-or-folder>
  kiro-pet pets|sessions|connections
  kiro-pet startup on|off|status
Sessions are discovered automatically. No workspace hooks are needed.
Hide keeps tracking running. Stop exits. Startup is off by default.
'@
    }
}
