param([switch]$Live)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$originalHome = $env:KIRO_PET_HOME
$originalNativeHome = $env:KIRO_NATIVE_HOME
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("kiro-pet-check-" + [guid]::NewGuid().ToString('N'))
$env:KIRO_PET_HOME = Join-Path $testRoot 'data'
$env:KIRO_NATIVE_HOME = Join-Path $testRoot 'kiro'
. (Join-Path $root 'Core.ps1')
. (Join-Path $root 'Native-Fixtures.ps1')
function Assert($Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
function Publish([string]$Id, [string]$Status) {
    $directory = Join-Path $env:KIRO_NATIVE_HOME "sessions\group\sess_$Id"
    if ($Status -eq 'working') { Add-NativeFixtureEvent $directory 'turn_start' $Id }
    elseif ($Status -eq 'done') { Add-NativeFixtureEvent $directory 'turn_end' $Id @{ stopReason = 'end_turn' } }
}
try {
    $workspace = Join-Path $testRoot 'workspace'
    [void][IO.Directory]::CreateDirectory($workspace)
    & pwsh -NoProfile -File (Join-Path $root 'Verify-NativeSessions.ps1')
    Assert ($LASTEXITCODE -eq 0) 'Native session checks failed.'
    New-NativeFixture $env:KIRO_NATIVE_HOME 'sess_chat-a' $workspace | Out-Null
    New-NativeFixture $env:KIRO_NATIVE_HOME 'sess_chat-b' $workspace 'cli' | Out-Null
    Publish 'chat-a' 'working'
    Publish 'chat-b' 'working'
    Assert (@(Get-PetSessions).Count -eq 2) 'Two chats overwrote each other.'
    Publish 'chat-a' 'done'
    $sessions = @(Get-PetSessions)
    $summary = Get-SessionSummary $sessions @{}
    Assert ($summary.status -eq 'done' -and $summary.counts.working -eq 1) 'Done hid the working session record.'
    $done = $summary.target
    $ack = @{ $done.key = $done.completionId }
    $summary = Get-SessionSummary @(Get-PetSessions) $ack
    Assert ($summary.status -eq 'working') 'Acknowledging Done did not reveal Working.'
    Write-Output 'Native UI fixtures ready: one unread completion and one working CLI session.'
    Write-PetJson (Join-Path $env:KIRO_PET_HOME 'settings.json') @{ speed = 0.1; duration = 60; reducedMotion = $false; pets = @{ spark = @{ working = @('read') } } }
    $settings = Get-PetSettings
    Assert ($settings.speed -eq 0.1 -and $settings.duration -eq 60 -and $settings.pets.spark.working[0] -eq 'read') 'Settings did not persist.'
    Write-Output 'Settings PASS: slow playback, action duration, per-character mapping.'
    & pwsh -NoProfile -STA -File (Join-Path $root 'App.ps1') -Check
    Assert ($LASTEXITCODE -eq 0) 'Renderer check failed.'
    if ($Live) {
        & (Join-Path $root 'kiro-pet.ps1') start
        $first = (Get-PetProcess).Id
        & (Join-Path $root 'kiro-pet.ps1') start
        Assert ((Get-PetProcess).Id -eq $first) 'Repeated start created another app.'
        & (Join-Path $root 'kiro-pet.ps1') hide
        Assert ([bool](Get-PetProcess)) 'Hide exited the app.'
        & (Join-Path $root 'kiro-pet.ps1') show
        & (Join-Path $root 'kiro-pet.ps1') select spark
        Add-Type -AssemblyName UIAutomationClient
        Add-Type -AssemblyName UIAutomationTypes
        Publish 'chat-b' 'done'
        Start-Sleep -Milliseconds 500
        Send-PetCommand 'sessions' $null | Out-Null
        $pickerCondition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$first),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Kiro Pet sessions')
        )
        $picker = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $pickerCondition)
        Assert ($null -ne $picker) 'Session picker did not appear.'
        $items = $picker.FindAll([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::ListItem))
        $sessionItem = @($items | Where-Object { $_.Current.Name -match 'chat-a' })[0]
        Assert ($null -ne $sessionItem) 'Session picker did not list chat-a.'
        $sessionItem.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $checkButton = $picker.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Mark completion checked'))
        $checkButton.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 200
        $actualAck = Get-Content -LiteralPath (Join-Path $env:KIRO_PET_HOME 'acknowledged.json') -Raw | ConvertFrom-Json -AsHashtable
        Assert ($actualAck.Count -eq 1) 'Session picker acknowledged more than one completion.'
        $remainingDone = Get-SessionSummary @(Get-PetSessions) $actualAck
        Assert ($remainingDone.status -eq 'done' -and $remainingDone.target.sessionId -eq 'sess_chat-b') 'Picker did not leave the other completion unread.'
        Write-Output 'Live picker PASS: actual selection and acknowledgement leave other completions unread.'
        $picker.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).Close()
        & (Join-Path $root 'kiro-pet.ps1') settings
        $condition = [System.Windows.Automation.AndCondition]::new(
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::ProcessIdProperty, [int]$first),
            [System.Windows.Automation.PropertyCondition]::new([System.Windows.Automation.AutomationElement]::NameProperty, 'Kiro Pet settings')
        )
        $settingsWindow = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        Assert ($null -ne $settingsWindow) 'Settings window did not appear.'
        $sliderCondition = [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Slider
        )
        $sliders = $settingsWindow.FindAll([System.Windows.Automation.TreeScope]::Descendants, $sliderCondition)
        Assert ($sliders.Count -eq 2) 'Speed and duration sliders are missing.'
        $range = $sliders[0].GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
        $range.SetValue(0.15)
        $range = $sliders[1].GetCurrentPattern([System.Windows.Automation.RangeValuePattern]::Pattern)
        $range.SetValue(45)
        Start-Sleep -Milliseconds 200
        $actual = Get-PetSettings
        Assert ($actual.speed -eq 0.15 -and $actual.duration -eq 45) 'Operating the sliders did not save actual settings.'
        Write-Output 'Live settings PASS: actual speed and duration sliders persist changes.'
        $petProcess = Get-PetProcess
        & (Join-Path $root 'kiro-pet.ps1') stop
        $petProcess | Wait-Process -Timeout 10
        $deadline = [DateTime]::UtcNow.AddSeconds(5)
        while ((Get-PetProcess) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
        Assert (-not (Get-PetProcess)) 'Stop did not exit.'
        Write-Output 'Live commands PASS: duplicate prevention, hide/show, select, settings, graceful stop.'
    }
    Assert (-not (Test-Path -LiteralPath (Join-Path $workspace '.kiro\hooks'))) 'Verification installed workspace hooks.'
    Write-Output 'Automatic discovery PASS: UI operated without workspace hooks.'
} finally {
    $remainingProcess = Get-PetProcess
    if ($remainingProcess) {
        try { Send-PetCommand 'stop' $null | Out-Null; $remainingProcess | Wait-Process -Timeout 10 } catch {}
    }
    $env:KIRO_PET_HOME = $originalHome
    $env:KIRO_NATIVE_HOME = $originalNativeHome
    if ($script:nativeReader) { $script:nativeReader.Dispose(); $script:nativeReader = $null }
    $resolved = [IO.Path]::GetFullPath($testRoot)
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    if ($resolved.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path -Leaf $resolved).StartsWith('kiro-pet-check-')) {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
