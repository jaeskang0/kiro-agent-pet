function New-NativeFixture([string]$Root, [string]$Id, [string]$Workspace, [string]$Source = 'ide') {
    $directory = Join-Path $Root "sessions\group\$Id"
    [void][IO.Directory]::CreateDirectory($directory)
    Write-PetJson (Join-Path $directory 'session.json') @{
        schemaVersion = '1.0.0'; dataModelVersion = 1; id = $Id
        title = $Id; workspacePaths = @($Workspace); status = 'idle'
    }
    Write-PetJson (Join-Path $Root "session-leases\$Id.lease") @{
        pid = $PID; parentPid = $PID; acquiredAt = [DateTime]::UtcNow.ToString('o')
        instanceId = [guid]::NewGuid().ToString(); clientName = $(if ($Source -eq 'ide') { 'kiro-ide' } else { 'kiro-cli' })
    }
    [IO.File]::WriteAllText((Join-Path $directory 'messages.jsonl'), '')
    return $directory
}

function Add-NativeFixtureEvent([string]$Directory, [string]$Type, [string]$Execution = 'turn-a', [hashtable]$Extra = @{}, [string]$Child) {
    $payload = @{ type = $Type; executionId = $Execution; turnId = $Execution }
    foreach ($key in $Extra.Keys) { $payload[$key] = $Extra[$key] }
    $record = @{ timestamp = [DateTime]::UtcNow.ToString('o'); payload = $payload } | ConvertTo-Json -Depth 10 -Compress
    $file = if ($Child) { Join-Path $Directory "sub-executions\$Child.jsonl" } else { Join-Path $Directory 'messages.jsonl' }
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $file))
    [IO.File]::AppendAllText($file, "$record`n", [Text.UTF8Encoding]::new($false))
}
