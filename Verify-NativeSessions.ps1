$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Core.ps1')
. (Join-Path $PSScriptRoot 'Native-Fixtures.ps1')
if (-not ('KiroPet.NativeReader' -as [type])) {
    $refs = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
    Add-Type -Path (Join-Path $PSScriptRoot 'NativeSessions.cs') -ReferencedAssemblies $refs
}
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('kiro-pet-native-check-' + [guid]::NewGuid().ToString('N'))
$reader = $null
$restored = $null
try {
    $workspace = Join-Path $temporary 'workspace'
    [void][IO.Directory]::CreateDirectory($workspace)
    $root = Join-Path $temporary 'kiro'
    $cache = Join-Path $temporary 'cache.json'
    $a = New-NativeFixture $root 'sess_chat-a' $workspace
    $b = New-NativeFixture $root 'sess_chat-b' $workspace 'cli'
    Add-NativeFixtureEvent $a 'turn_start' 'execution-a'
    Add-NativeFixtureEvent $b 'turn_start' 'execution-b'
    $reader = [KiroPet.NativeReader]::new($root, $cache, $true)
    $snapshot = $reader.ReadNow()
    Assert ($snapshot.Sessions.Count -eq 2) 'Native discovery did not find two sessions.'
    Assert (@($snapshot.Sessions | Where-Object status -eq 'working').Count -eq 2) 'Startup did not recover both running turns.'
    Assert (@($snapshot.Sessions | Where-Object source -eq 'cli').Count -eq 1) 'CLI source was not identified.'
    Add-NativeFixtureEvent $a 'turn_end' 'wrong-execution' @{ stopReason = 'end_turn' }
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-a').status -eq 'working') 'An old execution finished a new turn.'
    Add-NativeFixtureEvent $a 'pending_interaction' 'execution-a' @{ toolCallId = 'approval' }
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-a').status -eq 'waiting') 'Approval was not detected.'
    Add-NativeFixtureEvent $a 'interaction_resolved' 'execution-a' @{ toolCallId = 'approval'; outcome = 'accepted' }
    Add-NativeFixtureEvent $a 'turn_end' 'execution-a' @{ stopReason = 'end_turn' }
    $snapshot = $reader.ReadNow()
    $done = $snapshot.Sessions | Where-Object sessionId -eq 'sess_chat-a'
    Assert ($done.status -eq 'done' -and $done.completionId -eq 'execution-a:execution-a') 'Completion identity is not native and stable.'
    $summary = Get-SessionSummary $snapshot.Sessions @{}
    Assert ($summary.status -eq 'done' -and $summary.counts.working -eq 1) 'Multi-session priority failed.'
    $ack = @{ $done.key = $done.completionId }
    Assert ((Get-SessionSummary $snapshot.Sessions $ack).status -eq 'working') 'Checking one completion did not reveal the other working session.'
    $restored = [KiroPet.NativeReader]::new($root, $cache, $false)
    $again = $restored.ReadNow()
    Assert (($again.Sessions | Where-Object sessionId -eq 'sess_chat-a').completionId -eq $done.completionId) 'Restart created a different completion.'
    $restored.Dispose()
    Add-NativeFixtureEvent $a 'turn_start' 'execution-next'
    Add-NativeFixtureEvent $a 'turn_end' 'execution-next' @{ stopReason = 'end_turn' }
    Assert ((Get-SessionSummary $reader.ReadNow().Sessions $ack).status -eq 'done') 'A new completion stayed acknowledged.'
    Add-NativeFixtureEvent $b 'sub_agent_start' 'execution-b' @{ parentExecutionId = 'execution-b'; subSessionId = 'child-1' }
    Add-NativeFixtureEvent $b 'pending_interaction' 'execution-b' @{ toolCallId = 'child-approval' } 'child-1'
    $snapshot = $reader.ReadNow()
    Assert (($snapshot.Sessions | Where-Object sessionId -eq 'sess_chat-b').status -eq 'waiting') 'Delegated approval was missed.'
    Add-NativeFixtureEvent $b 'turn_end' 'execution-b' @{ stopReason = 'end_turn' } 'child-1'
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-b').status -eq 'waiting') 'Child completion finished its parent.'
    Add-NativeFixtureEvent $b 'interaction_resolved' 'execution-b' @{ toolCallId = 'child-approval'; outcome = 'accepted' } 'child-1'
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-b').status -eq 'working') 'Delegated approval resolution did not restore Working.'
    Add-NativeFixtureEvent $b 'turn_end' 'execution-b' @{ stopReason = 'failed' }
    Assert ((Get-SessionSummary $reader.ReadNow().Sessions @{}).status -eq 'error') 'Terminal failure priority failed.'
    $savedLease = Get-Content -LiteralPath (Join-Path $root 'session-leases\sess_chat-b.lease') -Raw | ConvertFrom-Json -AsHashtable
    $savedLease.acquiredAt = '2000-01-01T00:00:00Z'
    Write-PetJson (Join-Path $root 'session-leases\sess_chat-b.lease') $savedLease
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-b').disconnected) 'A recycled process ID kept a stale lease alive.'
    $failure = $reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-b'
    Assert ((Get-SessionSummary @($failure) @{}).status -eq 'error') 'A closed task failure stopped requiring review.'
    Assert ((Get-SessionSummary @($failure) @{ $failure.key = $failure.completionId }).status -eq 'idle') 'A task failure could not be acknowledged.'
    [IO.File]::Delete((Join-Path $root 'session-leases\sess_chat-a.lease'))
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-a').status -eq 'done') 'Closing lost unread Done.'
    Write-Output 'Native sessions PASS: automatic IDE/CLI discovery, startup recovery, turn identity, priorities, acknowledgement, delegated approvals, errors, stale leases.'

    $rotation = New-NativeFixture $root 'sess_rotation' $workspace
    Add-NativeFixtureEvent $rotation 'turn_start' 'rotate-a'
    Start-Sleep -Milliseconds 1050
    $reader.ReadNow() | Out-Null
    [IO.File]::WriteAllText((Join-Path $rotation 'messages.jsonl'), '')
    Add-NativeFixtureEvent $rotation 'turn_start' 'rotate-b'
    Add-NativeFixtureEvent $rotation 'turn_end' 'rotate-b' @{ stopReason = 'end_turn' }
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_rotation').completionId -eq 'rotate-b:rotate-b') 'Truncated event file did not recover.'
    Write-Output 'Rotation PASS: new turn reconstructed after truncation.'
    $partial = New-NativeFixture $root 'sess_partial' $workspace
    Add-NativeFixtureEvent $partial 'turn_start' 'partial-a'
    $record = '{"timestamp":"' + [DateTime]::UtcNow.ToString('o') + '","payload":{"type":"turn_end","executionId":"partial-a","turnId":"partial-a","stopReason":"end_turn"}}' + "`n"
    $bytes = [Text.Encoding]::UTF8.GetBytes($record)
    $file = Join-Path $partial 'messages.jsonl'
    $stream = [IO.File]::Open($file, [IO.FileMode]::Append)
    $stream.Write($bytes, 0, $bytes.Length - 5)
    $stream.Dispose()
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_partial').status -ne 'done') 'An incomplete record completed a turn.'
    $stream = [IO.File]::Open($file, [IO.FileMode]::Append)
    $stream.Write($bytes, $bytes.Length - 5, 5)
    $stream.Dispose()
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_partial').status -eq 'done') 'Completed partial record was not consumed.'

    $utf = New-NativeFixture $root 'sess_utf' $workspace
    $prefix = '{"timestamp":"' + [DateTime]::UtcNow.ToString('o') + '","payload":{"type":"assistant","content":"'
    $emoji = [char]0xD83D + [string][char]0xDC08
    $padding = 65533 - [Text.Encoding]::UTF8.GetByteCount($prefix)
    $line = $prefix + ('a' * $padding) + $emoji + ('b' * 70000) + '"}}' + "`n"
    [IO.File]::WriteAllText((Join-Path $utf 'messages.jsonl'), $line, [Text.UTF8Encoding]::new($false))
    Add-NativeFixtureEvent $utf 'turn_start' 'unicode-a'
    Add-NativeFixtureEvent $utf 'turn_end' 'unicode-a' @{ stopReason = 'end_turn' }
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_utf').status -eq 'done') 'Split UTF-8 broke event reading.'
    Write-Output 'Native boundary PASS: incomplete JSON and UTF-8 split across read buffers.'

    $unsupported = New-NativeFixture $root 'sess_unsupported' $workspace
    $metadataPath = Join-Path $unsupported 'session.json'
    $metadata = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json -AsHashtable
    $metadata.dataModelVersion = 99
    Write-PetJson $metadataPath $metadata
    Start-Sleep -Milliseconds 1050
    $unsupportedSnapshot = $reader.ReadNow()
    Assert (@($unsupportedSnapshot.Health | Where-Object { $_ -like '*Unsupported*' }).Count -gt 0) 'Unsupported schema was not reported.'
    Assert (@($unsupportedSnapshot.Sessions | Where-Object sessionId -eq 'sess_unsupported').Count -eq 0) 'Unsupported schema produced an agent status.'
    $restored = [KiroPet.NativeReader]::new($root, $cache, $false)
    Assert (($restored.ReadNow().Sessions | Where-Object sessionId -eq 'sess_chat-a').status -eq 'done') 'Restart lost a closed session completion.'
    Write-Output 'Recovery PASS: schema diagnostics and closed-session unread completion after restart.'
    $fast = New-NativeFixture $root 'sess_fast' $workspace 'cli'
    $fastLeasePath = Join-Path $root 'session-leases\sess_fast.lease'
    $fastLease = Get-Content -LiteralPath $fastLeasePath -Raw | ConvertFrom-Json -AsHashtable
    $fastLease.acquiredAt = '2000-01-01T00:00:00Z'
    Write-PetJson $fastLeasePath $fastLease
    Add-NativeFixtureEvent $fast 'turn_start' 'fast-a'
    Add-NativeFixtureEvent $fast 'turn_end' 'fast-a' @{ stopReason = 'end_turn' }
    Start-Sleep -Milliseconds 1050
    $fastSession = $reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_fast'
    Assert ($fastSession.status -eq 'done' -and $fastSession.closed) 'A task finishing between discovery polls was lost.'
    Write-Output 'Short task PASS: completion recovered after its host closed before discovery.'
    $old = New-NativeFixture $root 'sess_old-owner' $workspace
    $record = '{"timestamp":"2000-01-01T00:00:00Z","payload":{"type":"turn_start","executionId":"abandoned","turnId":"abandoned"}}' + "`n"
    [IO.File]::WriteAllText((Join-Path $old 'messages.jsonl'), $record, [Text.UTF8Encoding]::new($false))
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_old-owner').status -eq 'idle') 'An abandoned turn from a previous owner appeared Working.'
    Add-NativeFixtureEvent $old 'turn_start' 'new-owner'
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_old-owner').status -eq 'working') 'The current owner could not start new work.'
    Write-Output 'Ownership PASS: old unfinished turns ignored; current runtime work recovered.'
    $blank = New-NativeFixture $root 'sess_blank' $workspace
    [IO.File]::Delete((Join-Path $blank 'messages.jsonl'))
    Start-Sleep -Milliseconds 1050
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_blank').status -eq 'idle') 'A new blank chat was not Idle.'
    Add-NativeFixtureEvent $blank 'turn_start' 'blank-first-turn'
    Assert (($reader.ReadNow().Sessions | Where-Object sessionId -eq 'sess_blank').status -eq 'working') 'The first prompt in a blank chat was missed.'
    Write-Output 'Idle PASS: blank chat rests and its first native turn starts Working.'
} finally {
    if ($reader) { $reader.Dispose() }
    if ($restored) { $restored.Dispose() }
    $resolved = [IO.Path]::GetFullPath($temporary)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char]92) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('kiro-pet-native-check-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
