param([int]$Seconds = 90)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Core.ps1')
$refs = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'NativeSessions.cs') -ReferencedAssemblies $refs
$workspace = Join-Path ([IO.Path]::GetTempPath()) 'kiro-pet-ide-native-check'
if (Test-Path -LiteralPath (Join-Path $workspace '.kiro\hooks')) { throw 'IDE test workspace contains hooks.' }
$reader = [KiroPet.NativeReader]::new((Join-Path $env:USERPROFILE '.kiro'), (Join-Path $workspace 'unused-cache.json'), $false)
$lastState = $null
$waiting = $false
$workingAfterWait = $false
$done = $null
$observations = @()
[void][IO.Directory]::CreateDirectory((Join-Path $PSScriptRoot 'investigation'))
try {
    $deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
    Write-Output 'IDE native observer ready.'
    while ([DateTime]::UtcNow -lt $deadline) {
        $session = $reader.ReadNow().Sessions | Where-Object workspace -eq $workspace | Select-Object -First 1
        if ($session -and $session.status -ne $lastState) {
            $lastState = $session.status
            $observations += [pscustomobject]@{ status = $session.status; sessionId = $session.sessionId; event = $session.event; completionId = $session.completionId }
            $observations | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'investigation\live-ide-observations.json') -Encoding utf8
            Write-Output "IDE native: $($session.status)"
            if ($session.status -eq 'waiting') { $waiting = $true }
            if ($session.status -eq 'working' -and $waiting) { $workingAfterWait = $true }
            if ($session.status -eq 'done') { $done = $session; break }
        }
        Start-Sleep -Milliseconds 100
    }
    if (-not $done -or -not $waiting -or -not $workingAfterWait) { throw 'The complete native IDE approval sequence was not observed.' }
    if ($done.source -ne 'ide') { throw 'IDE session source was misidentified.' }
    $eventsFile = @(Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.kiro\sessions') -Directory | ForEach-Object {
        $file = Join-Path $_.FullName "$($done.sessionId)\messages.jsonl"
        if (Test-Path -LiteralPath $file) { $file }
    })[0]
    $events = @(Get-Content -LiteralPath $eventsFile | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($events | Where-Object { $_.payload.type -eq 'ContextualHookInvoked' }).Count) { throw 'The test IDE session invoked a hook.' }
    Write-Output 'Real IDE PASS: native Working, approval Waiting, resolution Working, Done; no hooks invoked.'
} finally { $reader.Dispose() }
