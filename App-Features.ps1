function Apply-AnimationSettings {
    $script:frameMilliseconds = 180.0 / [double]$script:petSettings.speed
    $animation.Interval = [TimeSpan]::FromMilliseconds($script:frameMilliseconds)
    foreach ($name in $script:clips.Keys) {
        $clip = $script:clips[$name]
        $clip.Loops = if ($name -eq 'wave') { 1 } else {
            [Math]::Max(1, [int][Math]::Ceiling(([double]$script:petSettings.duration * 1000) / ($clip.Columns.Count * $script:frameMilliseconds)))
        }
    }
    $defaults = @{
        idle = @('idle', 'eat', 'sleep')
        working = @('read', 'walk-right', 'walk-left')
        blocked = @('surprise', 'worry')
        error = @('worry', 'surprise')
        done = @('jump', 'excited')
        offline = @('sleep')
    }
    if ($script:sheetRows -eq 11) {
        $defaults.idle += @('turn-right', 'turn-left')
        $defaults.working += @('turn-right', 'turn-left')
    }
    $custom = $script:petSettings.pets[$script:currentPetKey]
    foreach ($status in $defaults.Keys) {
        $playlist = if ($custom -and $custom[$status]) { @($custom[$status]) } else { $defaults[$status] }
        $playlist = @($playlist | Where-Object { $_ -in $script:allAnimations })
        if (-not $playlist.Count) { $playlist = @('idle') }
        $states[$status].Playlist = $playlist
    }
    $script:tick = 0
    $script:clipIndex = 0
    $script:reactions = @()
}

function Save-PetSettings {
    Write-PetJson $settingsPath $script:petSettings
}

function Save-PetPosition {
    if ($Check) { return }
    Write-PetJson (Join-Path $dataRoot 'position.json') @{ left = $window.Left; top = $window.Top }
}

function Restore-PetPosition {
    $positionFile = Join-Path $dataRoot 'position.json'
    if (-not (Test-Path -LiteralPath $positionFile)) { return }
    try {
        $position = Get-Content -LiteralPath $positionFile -Raw | ConvertFrom-Json
        $left = [double]$position.left
        $top = [double]$position.top
        $area = [System.Windows.SystemParameters]::WorkArea
        if ($left -ge $area.Left -and $left -le $area.Right - $window.Width -and $top -ge $area.Top -and $top -le $area.Bottom - $window.Height) {
            $window.Left = $left
            $window.Top = $top
        }
    } catch { Write-Warning 'Could not restore pet position.' }
}

function Update-ConnectionHealth {
    $script:connectionHealth = $script:nativeSnapshot.Health -join "`n"
    if ($script:nativeSnapshot.Recovering) { $script:connectionHealth += "`nRecovering active turns from native records..." }
    $window.ToolTip = $script:connectionHealth
}

function New-PanelWindow([string]$Title, [int]$Width, [int]$Height) {
    $panel = [System.Windows.Window]::new()
    $panel.Title = $Title
    $panel.Width = $Width
    $panel.Height = $Height
    $panel.MinWidth = 420
    $panel.MinHeight = 300
    $panel.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterScreen
    $panel.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI')
    $panel.FontSize = 13
    $panel.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#FFF9F7F3')
    $panel.Topmost = $true
    return $panel
}

function Add-PanelText($Panel, [string]$Text, [switch]$Heading) {
    $textBlock = [System.Windows.Controls.TextBlock]::new()
    $textBlock.Text = $Text
    $textBlock.TextWrapping = [System.Windows.TextWrapping]::Wrap
    $textBlock.Margin = [System.Windows.Thickness]::new(0, 10, 0, 6)
    if ($Heading) { $textBlock.FontWeight = [System.Windows.FontWeights]::SemiBold; $textBlock.FontSize = 15 }
    [void]$Panel.Children.Add($textBlock)
    return $textBlock
}

function Show-PetSettings {
    if ($script:settingsWindow) { [void]$script:settingsWindow.Activate(); return }
    $script:settingsWindow = New-PanelWindow 'Kiro Pet settings' 550 720
    $scroll = [System.Windows.Controls.ScrollViewer]::new()
    $scroll.VerticalScrollBarVisibility = 'Auto'
    $panel = [System.Windows.Controls.StackPanel]::new()
    $panel.Margin = [System.Windows.Thickness]::new(22)
    $scroll.Content = $panel
    $script:settingsWindow.Content = $scroll
    [void](Add-PanelText $panel "Animation for $script:currentPetName" -Heading)
    [void](Add-PanelText $panel 'Changes preview immediately on the pet and save automatically.')
    $script:speedLabel = Add-PanelText $panel ('Playback speed: {0:0.00}x' -f $script:petSettings.speed)
    $script:speedSlider = [System.Windows.Controls.Slider]::new()
    $script:speedSlider.Minimum = 0.1
    $script:speedSlider.Maximum = 2
    $script:speedSlider.TickFrequency = 0.05
    $script:speedSlider.IsSnapToTickEnabled = $true
    $script:speedSlider.Value = $script:petSettings.speed
    $script:speedSlider.Add_ValueChanged({
        param($sender, $event)
        $script:petSettings.speed = [Math]::Round($sender.Value, 2)
        $script:speedLabel.Text = 'Playback speed: {0:0.00}x' -f $script:petSettings.speed
        Apply-AnimationSettings
        Save-PetSettings
        Show-PetFrame
    })
    [void]$panel.Children.Add($script:speedSlider)
    [void](Add-PanelText $panel '0.10x is ten times slower than normal playback.')
    $script:durationLabel = Add-PanelText $panel "Repeat an action for: $($script:petSettings.duration) seconds"
    $script:durationSlider = [System.Windows.Controls.Slider]::new()
    $script:durationSlider.Minimum = 5
    $script:durationSlider.Maximum = 120
    $script:durationSlider.TickFrequency = 1
    $script:durationSlider.IsSnapToTickEnabled = $true
    $script:durationSlider.Value = $script:petSettings.duration
    $script:durationSlider.Add_ValueChanged({
        param($sender, $event)
        $script:petSettings.duration = [Math]::Round($sender.Value)
        $script:durationLabel.Text = "Repeat an action for: $($script:petSettings.duration) seconds"
        Apply-AnimationSettings
        Save-PetSettings
    })
    [void]$panel.Children.Add($script:durationSlider)
    [void](Add-PanelText $panel 'A full animation loop finishes before the next action. Task status changes take effect immediately.')
    $motion = [System.Windows.Controls.CheckBox]::new()
    $motion.Content = 'Reduced motion (static character)'
    $motion.Margin = [System.Windows.Thickness]::new(0, 12, 0, 10)
    $motion.IsChecked = $script:petSettings.reducedMotion
    $motion.Add_Click({
        param($sender, $event)
        $script:petSettings.reducedMotion = [bool]$sender.IsChecked
        Apply-AnimationSettings
        Save-PetSettings
        Show-PetFrame
    })
    [void]$panel.Children.Add($motion)
    $startup = [System.Windows.Controls.CheckBox]::new()
    $startup.Content = 'Start Kiro Pet at sign-in'
    $startup.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    $startup.IsChecked = Test-Path -LiteralPath (Join-Path ([Environment]::GetFolderPath('Startup')) 'Kiro Pet.cmd')
    $startup.Add_Click({
        param($sender, $event)
        try {
            $value = if ($sender.IsChecked) { 'on' } else { 'off' }
            & (Join-Path $PSScriptRoot 'kiro-pet.ps1') startup $value | Out-Null
        } catch { [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'Start at sign-in') }
    })
    [void]$panel.Children.Add($startup)
    [void](Add-PanelText $panel 'Actions by status' -Heading)
    [void](Add-PanelText $panel 'Choose one or more actions for each status. Assignments are saved separately for each character.')
    $script:actionBoxes = @{}
    foreach ($status in @('idle', 'working', 'blocked', 'error', 'done')) {
        $caption = @{ idle = 'Idle'; working = 'Working'; blocked = 'Needs input'; error = 'Blocked / error'; done = 'Done (unread)' }[$status]
        [void](Add-PanelText $panel $caption -Heading)
        $wrap = [System.Windows.Controls.WrapPanel]::new()
        $script:actionBoxes[$status] = @()
        foreach ($action in $script:allAnimations) {
            $box = [System.Windows.Controls.CheckBox]::new()
            $box.Content = $action
            $box.Margin = [System.Windows.Thickness]::new(0, 3, 14, 7)
            $box.Tag = $status
            $box.IsChecked = $action -in $states[$status].Playlist
            $box.Add_Click({
                param($sender, $event)
                $statusKey = [string]$sender.Tag
                $selected = @($script:actionBoxes[$statusKey] | Where-Object IsChecked | ForEach-Object { [string]$_.Content })
                if (-not $selected.Count) { $sender.IsChecked = $true; return }
                if (-not $script:petSettings.pets.ContainsKey($script:currentPetKey)) { $script:petSettings.pets[$script:currentPetKey] = @{} }
                $script:petSettings.pets[$script:currentPetKey][$statusKey] = $selected
                Apply-AnimationSettings
                Save-PetSettings
                Start-PetReaction @([string]$sender.Content)
            })
            $script:actionBoxes[$status] += $box
            [void]$wrap.Children.Add($box)
        }
        [void]$panel.Children.Add($wrap)
    }
    $reset = [System.Windows.Controls.Button]::new()
    $reset.Content = 'Restore defaults for this character'
    $reset.Padding = [System.Windows.Thickness]::new(12, 8, 12, 8)
    $reset.Margin = [System.Windows.Thickness]::new(0, 16, 0, 0)
    $reset.Add_Click({
        $script:petSettings.pets.Remove($script:currentPetKey)
        $script:petSettings.speed = 0.6
        $script:petSettings.duration = 14.0
        Apply-AnimationSettings
        Save-PetSettings
        $script:settingsWindow.Close()
        Show-PetSettings
    })
    [void]$panel.Children.Add($reset)
    $script:settingsWindow.Add_Closed({ $script:settingsWindow = $null })
    [void]$script:settingsWindow.Show()
    [void]$script:settingsWindow.Activate()
}

function Refresh-SessionPicker {
    if (-not $script:sessionWindow) { return }
    $selectedKey = if ($script:sessionList.SelectedItem) { $script:sessionList.SelectedItem.Tag } else { $null }
    $script:sessionList.Items.Clear()
    $priority = @{ waiting = 5; error = 4; done = 3; working = 2; idle = 1; closed = 0 }
    foreach ($session in @($script:sessions | Sort-Object -Property @{ Expression = { $priority[$_.status] }; Descending = $true })) {
        $statusText = if ($session.status -in @('done', 'error') -and $script:acknowledged[$session.key] -eq $session.completionId) { 'Checked' } else {
            @{ waiting = 'Needs input'; error = 'Blocked'; done = 'Done'; working = 'Working'; idle = 'Idle'; closed = 'Closed'; recovering = 'Recovering status...' }[$session.status]
        }
        if ($session.disconnected) { $statusText += ' (disconnected)' }
        elseif ($session.closed) { $statusText += ' (closed)' }
        $item = [System.Windows.Controls.ListBoxItem]::new()
        $item.Tag = $session.key
        $id = $session.sessionId.Substring(0, [Math]::Min(8, $session.sessionId.Length))
        $title = if ($session.title) { $session.title } else { $id }
        $item.Content = "$title`n$(Split-Path -Leaf $session.workspace) · $($session.source.ToUpperInvariant()) · $id · $statusText"
        $item.Padding = [System.Windows.Thickness]::new(8)
        [void]$script:sessionList.Items.Add($item)
        if ($item.Tag -eq $selectedKey) { $script:sessionList.SelectedItem = $item }
    }
    $script:pickerHealth.Text = $script:connectionHealth
}

function Show-SessionPicker {
    if ($script:sessionWindow) { Refresh-SessionPicker; [void]$script:sessionWindow.Activate(); return }
    $script:sessionWindow = New-PanelWindow 'Kiro Pet sessions' 580 440
    $panel = [System.Windows.Controls.DockPanel]::new()
    $panel.Margin = [System.Windows.Thickness]::new(18)
    $script:pickerHealth = [System.Windows.Controls.TextBlock]::new()
    $script:pickerHealth.TextWrapping = 'Wrap'
    $script:pickerHealth.Margin = [System.Windows.Thickness]::new(0, 0, 0, 10)
    [System.Windows.Controls.DockPanel]::SetDock($script:pickerHealth, 'Top')
    [void]$panel.Children.Add($script:pickerHealth)
    $buttons = [System.Windows.Controls.StackPanel]::new()
    $buttons.Orientation = 'Horizontal'
    $buttons.Margin = [System.Windows.Thickness]::new(0, 12, 0, 0)
    [System.Windows.Controls.DockPanel]::SetDock($buttons, 'Bottom')
    foreach ($labelText in @('Open workspace', 'Mark completion checked', 'Refresh')) {
        $button = [System.Windows.Controls.Button]::new()
        $button.Content = $labelText
        $button.Padding = [System.Windows.Thickness]::new(9, 7, 9, 7)
        $button.Margin = [System.Windows.Thickness]::new(0, 0, 8, 0)
        $button.Add_Click({
            param($sender, $event)
            if ($sender.Content -eq 'Refresh') { Update-PetStatus; Refresh-SessionPicker; return }
            $selected = $script:sessionList.SelectedItem
            if (-not $selected) { return }
            if ($sender.Content -eq 'Open workspace') {
                $script:target = [string]$selected.Tag
                Open-AgentHostFromPet
            } else {
                Update-AgentSnapshot $script:liveAgents ([string]$selected.Tag)
                Refresh-SessionPicker
            }
        })
        [void]$buttons.Children.Add($button)
    }
    [void]$panel.Children.Add($buttons)
    $script:sessionList = [System.Windows.Controls.ListBox]::new()
    [void]$panel.Children.Add($script:sessionList)
    $script:sessionWindow.Content = $panel
    $script:sessionWindow.Add_Closed({ $script:sessionWindow = $null })
    Refresh-SessionPicker
    [void]$script:sessionWindow.Show()
    [void]$script:sessionWindow.Activate()
}

function Start-PetControl {
    $hash = Get-WorkspaceKey $dataRoot
    $script:petMutex = [Threading.Mutex]::new($false, "Local\KiroPetApp-$hash")
    $script:ownsPetMutex = $false
    try { $script:ownsPetMutex = $script:petMutex.WaitOne(0) } catch [Threading.AbandonedMutexException] { $script:ownsPetMutex = $true }
    if (-not $script:ownsPetMutex) { throw 'Kiro Pet is already running.' }
    $running = @{ pid = $PID; started = (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks.ToString() }
    Write-PetJson (Join-Path $dataRoot 'running.json') $running
    $script:controlTimer = [System.Windows.Threading.DispatcherTimer]::new()
    $script:controlTimer.Interval = [TimeSpan]::FromMilliseconds(100)
    $script:controlTimer.Add_Tick({ Receive-PetCommands })
    $script:controlTimer.Start()
    $helper = [System.Windows.Interop.WindowInteropHelper]::new($window)
    $script:petHandle = $helper.EnsureHandle()
    $script:hotkeySource = [System.Windows.Interop.HwndSource]::FromHwnd($script:petHandle)
    $script:hotkeyHook = [System.Windows.Interop.HwndSourceHook]{
        param($handle, $message, $wParam, $lParam, [ref]$handled)
        if ($message -eq 0x0312 -and $wParam.ToInt32() -eq 1) {
            $window.Show()
            Show-SessionPicker
            $handled.Value = $true
        }
        return [IntPtr]::Zero
    }
    $script:hotkeySource.AddHook($script:hotkeyHook)
    $script:hotkeyRegistered = [KiroPetWindows]::RegisterHotKey($script:petHandle, 1, 0x4009, 0x50)
    if (-not $script:hotkeyRegistered) { Write-Warning 'Windows+Alt+P is already in use. Pet commands still work.' }
}

function Receive-PetCommands {
    $root = Join-Path $dataRoot 'commands'
    if (-not (Test-Path -LiteralPath $root)) { return }
    foreach ($file in Get-ChildItem -LiteralPath $root -Filter '*.json' -File) {
        $stop = $false
        try {
            $request = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
            switch ($request.command) {
                'stop' { $stop = $true }
                'hide' { $window.Hide() }
                'show' { $window.Show(); [void]$window.Activate() }
                'settings' { Show-PetSettings }
                'sessions' { Show-SessionPicker }
                'select' {
                    $matches = @($script:petCatalog | Where-Object { $_.Key -eq $request.value -or $_.DisplayName -eq $request.value -or $_.Key -like "$($request.value)-*" })
                    if ($matches.Count -ne 1) { throw 'Pet name is missing or ambiguous. Use kiro-pet pets for exact keys.' }
                    if ($script:settingsWindow) { $script:settingsWindow.Close() }
                    Set-CurrentPet $matches[0].Key -Remember
                }
                'import' {
                    if ($script:settingsWindow) { $script:settingsWindow.Close() }
                    Import-PetPack $request.value
                }
                default { throw 'Unknown pet command.' }
            }
            Write-PetJson (Join-Path $root "$($file.BaseName).reply") @{ ok = $true; message = "Kiro Pet: $($request.command)" }
        } catch {
            Write-PetJson (Join-Path $root "$($file.BaseName).reply") @{ ok = $false; error = $_.Exception.Message }
        } finally { [IO.File]::Delete($file.FullName) }
        if ($stop) { $window.Close(); return }
    }
}

function Stop-PetControl {
    if ($script:controlTimer) { $script:controlTimer.Stop() }
    if ($script:nativeReader) { $script:nativeReader.Dispose() }
    if ($script:hotkeyRegistered) {
        [void][KiroPetWindows]::UnregisterHotKey($script:petHandle, 1)
        $script:hotkeyRegistered = $false
    }
    if ($script:settingsWindow) { $script:settingsWindow.Close() }
    if ($script:sessionWindow) { $script:sessionWindow.Close() }
    if ($script:ownsPetMutex) {
        $running = Join-Path $dataRoot 'running.json'
        if (Test-Path -LiteralPath $running) { [IO.File]::Delete($running) }
        $script:petMutex.ReleaseMutex()
        $script:ownsPetMutex = $false
    }
    if ($script:petMutex) { $script:petMutex.Dispose(); $script:petMutex = $null }
}
