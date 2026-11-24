$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Core.ps1')
$refs = @(Get-ChildItem -LiteralPath (Join-Path $PSHOME 'ref') -Filter '*.dll' | ForEach-Object FullName)
Add-Type -Path (Join-Path $PSScriptRoot 'NativeSessions.cs') -ReferencedAssemblies $refs
$workspace = Join-Path ([IO.Path]::GetTempPath()) ('kiro-pet-live-cli-' + [guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($workspace)
$reader = [KiroPet.NativeReader]::new((Join-Path $env:USERPROFILE '.kiro'), (Join-Path $workspace 'unused-cache.json'), $false)
$process = $null
try {
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command kiro-cli -ErrorAction Stop).Source
    $start.WorkingDirectory = $workspace
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    foreach ($argument in @('chat', '--v3', '--trust-workspace', '--no-interactive', 'Reply with exactly OK. Do not use any tools.')) {
        $start.ArgumentList.Add($argument)
    }
    $process = [Diagnostics.Process]::Start($start)
    $working = $false
    $done = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(45)
    while ([DateTime]::UtcNow -lt $deadline) {
        $session = $reader.ReadNow().Sessions | Where-Object workspace -eq $workspace | Select-Object -First 1
        if ($session.status -eq 'working' -and -not $process.HasExited) { $working = $true }
        if ($session.status -eq 'done') { $done = $session }
        if ($process.HasExited -and $done) { break }
        Start-Sleep -Milliseconds 100
    }
    if (-not $process.WaitForExit(5000)) { throw 'CLI smoke task did not exit.' }
    $stdout = $process.StandardOutput.ReadToEnd()
    $stderr = $process.StandardError.ReadToEnd()
    if ($process.ExitCode -ne 0) { throw "CLI task failed: $stderr" }
    if ($stdout.Trim() -ne 'OK' -or -not $working -or -not $done) { throw 'Native reader did not observe the real Working and Done sequence.' }
    $nativeRoot = Join-Path $env:USERPROFILE '.kiro\sessions'
    $eventsFile = @(Get-ChildItem -LiteralPath $nativeRoot -Directory | ForEach-Object {
        $file = Join-Path $_.FullName "$($done.sessionId)\messages.jsonl"
        if (Test-Path -LiteralPath $file) { $file }
    })[0]
    $events = @(Get-Content -LiteralPath $eventsFile | ForEach-Object { $_ | ConvertFrom-Json })
    if (@($events | Where-Object { $_.payload.type -eq 'ContextualHookInvoked' }).Count) { throw 'The supposedly hook-free test invoked a hook.' }
    if ($done.source -ne 'cli') { throw 'Native CLI source was misidentified.' }
    Write-Output 'Real CLI PASS: compiled reader observed Working before exit, Done with native turn identity, automatic discovery, no hooks.'
} finally {
    $reader.Dispose()
    if ($process -and -not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
    if ($process) { $process.Dispose() }
    $resolved = [IO.Path]::GetFullPath($workspace)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char]92) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('kiro-pet-live-cli-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
