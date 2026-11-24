#requires -Version 7.4
$ErrorActionPreference = 'Stop'

function Get-KiroPetHome {
    if ($env:KIRO_PET_HOME) { return [IO.Path]::GetFullPath($env:KIRO_PET_HOME) }
    return Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'KiroPet'
}

function Write-PetJson([string]$Path, $Value) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    $temporary = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    try {
        [IO.File]::WriteAllText($temporary, ($Value | ConvertTo-Json -Depth 16), [Text.UTF8Encoding]::new($false))
        for ($attempt = 0; $attempt -lt 5; $attempt++) {
            try {
                [IO.File]::Move($temporary, $Path, $true)
                break
            } catch [IO.IOException], [UnauthorizedAccessException] {
                if ($attempt -eq 4) { throw "Could not update $Path`: $($_.Exception.Message)" }
                Start-Sleep -Milliseconds 40
            }
        }
    } finally { if (Test-Path -LiteralPath $temporary) { [IO.File]::Delete($temporary) } }
}

function Get-PetSettings {
    $defaults = @{ speed = 0.6; duration = 14.0; reducedMotion = $false; pets = @{} }
    $path = Join-Path (Get-KiroPetHome) 'settings.json'
    if (Test-Path -LiteralPath $path) {
        $saved = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in $defaults.Keys.Clone()) {
            if ($saved.ContainsKey($key)) { $defaults[$key] = $saved[$key] }
        }
    }
    if ($defaults.speed -lt 0.1 -or $defaults.speed -gt 2) { throw 'Playback speed must be 0.1 to 2.' }
    if ($defaults.duration -lt 5 -or $defaults.duration -gt 120) { throw 'Action duration must be 5 to 120 seconds.' }
    return $defaults
}

function Get-PetConnections {
    Get-PetSessions | Group-Object workspace | ForEach-Object {
        [pscustomobject]@{ workspace = $_.Name; sessions = $_.Count; source = 'native'; key = $_.Group[0].workspaceKey }
    }
}

function Get-WorkspaceKey([string]$Workspace) {
    $normalized = [IO.Path]::GetFullPath($Workspace).TrimEnd('\').ToLowerInvariant()
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))
    return [Convert]::ToHexString($hash).Substring(0, 16).ToLowerInvariant()
}

function Get-PetSessions {
    if (-not ('KiroPet.NativeReader' -as [type])) {
        $references = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' -File | ForEach-Object FullName)
        Add-Type -Path (Join-Path $PSScriptRoot 'NativeSessions.cs') -ReferencedAssemblies $references
    }
    if (-not $script:nativeReader) {
        $nativeHome = if ($env:KIRO_NATIVE_HOME) { $env:KIRO_NATIVE_HOME } else {
            Join-Path $env:USERPROFILE '.kiro'
        }
        $script:nativeReader = [KiroPet.NativeReader]::new($nativeHome, (Join-Path (Get-KiroPetHome) 'native-sessions.json'), [bool]$script:nativeAsync)
    }
    $script:nativeSnapshot = if ($script:nativeAsync) { $script:nativeReader.Poll() } else { $script:nativeReader.ReadNow() }
    return @($script:nativeSnapshot.Sessions)
}

function Get-SessionSummary([object[]]$Sessions, [hashtable]$Acknowledged) {
    $priority = @{ waiting = 5; error = 4; done = 3; working = 2; idle = 1; closed = 0 }
    $visible = @($Sessions | Where-Object {
        $_.status -in @('waiting', 'error', 'done', 'working', 'idle') -and (
        ($_.status -in @('done', 'error') -and $Acknowledged[$_.key] -ne $_.completionId) -or
        ($_.status -notin @('done', 'error', 'closed') -and -not $_.disconnected -and -not $_.closed))
    } | Sort-Object -Stable -Property @{ Expression = { $priority[$_.status] }; Descending = $true }, timestamp)
    $chosen = $visible | Select-Object -First 1
    $counts = @{}
    foreach ($status in $priority.Keys) { $counts[$status] = @($visible | Where-Object status -eq $status).Count }
    return @{ status = $(if ($chosen) { $chosen.status } else { 'idle' }); target = $chosen; counts = $counts; sessions = $visible }
}

function Get-PetProcess {
    $file = Join-Path (Get-KiroPetHome) 'running.json'
    if (-not (Test-Path -LiteralPath $file)) { return $null }
    try {
        $record = Get-Content -LiteralPath $file -Raw | ConvertFrom-Json
        $process = Get-Process -Id $record.pid -ErrorAction SilentlyContinue
        if ($process -and $process.StartTime.ToUniversalTime().Ticks.ToString() -eq [string]$record.started) {
            return $process
        }
    } catch {}
    return $null
}

function Send-PetCommand([string]$Command, [string]$Value) {
    if (-not (Get-PetProcess)) { throw 'Kiro Pet is not running. Use kiro-pet start.' }
    $id = [guid]::NewGuid().ToString('N')
    $root = Join-Path (Get-KiroPetHome) 'commands'
    $response = Join-Path $root "$id.reply"
    Write-PetJson (Join-Path $root "$id.json") @{ command = $Command; value = $Value }
    $deadline = [DateTime]::UtcNow.AddSeconds(8)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $response) {
            $reply = Get-Content -LiteralPath $response -Raw | ConvertFrom-Json
            [IO.File]::Delete($response)
            if (-not $reply.ok) { throw $reply.error }
            return $reply.message
        }
        Start-Sleep -Milliseconds 100
    }
    throw 'Kiro Pet did not answer within eight seconds.'
}
