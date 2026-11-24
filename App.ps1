param([switch]$Check)
. (Join-Path $PSScriptRoot 'Core.ps1')
$script:nativeAsync = -not $Check
$dataRoot = Get-KiroPetHome
$petHostName = 'Kiro'
$petHostProcess = 'Kiro'
$petHostExecutable = Join-Path $env:LOCALAPPDATA 'Programs\Kiro\Kiro.exe'
$script:sessions = @()
$script:connectionHealth = 'No workspaces connected.'
$script:petMutex = $null
$script:controlTimer = $null
$script:settingsWindow = $null
$script:sessionWindow = $null
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class KiroPetWindows {
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr window, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] public static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr window);
    private delegate bool EnumWindowProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int length);
    public static IntPtr Find(uint[] processes, string workspace) {
        IntPtr result = IntPtr.Zero;
        EnumWindows((window, parameter) => {
            uint process;
            GetWindowThreadProcessId(window, out process);
            if (!IsWindowVisible(window) || Array.IndexOf(processes, process) < 0) return true;
            var title = new System.Text.StringBuilder(2048);
            GetWindowText(window, title, title.Capacity);
            if (title.Length == 0 || (!string.IsNullOrEmpty(workspace) && title.ToString().IndexOf(workspace, StringComparison.OrdinalIgnoreCase) < 0)) return true;
            result = window;
            return false;
        }, IntPtr.Zero);
        return result;
    }
    public static uint ProcessId() {
        uint process;
        GetWindowThreadProcessId(GetForegroundWindow(), out process);
        return process;
    }
}
'@

. (Join-Path $PSScriptRoot 'Pet-Library.ps1')
. (Join-Path $PSScriptRoot 'App-Features.ps1')
$states = @{
    idle    = @{ Playlist = @('idle', 'eat', 'idle', 'sleep'); Label = 'Taking a little break'; Color = '#FFFFF7DB' }
    working = @{ Playlist = @('read', 'walk-right', 'read', 'walk-left'); Label = "I'm working..."; Color = '#FFE8F2FF' }
    error   = @{ Playlist = @('worry'); Label = 'Something went wrong'; Color = '#FFFFECEC' }
    blocked = @{ Playlist = @('surprise', 'worry'); Label = 'I need you!'; Color = '#FFFFECEC' }
    done    = @{ Playlist = @('jump', 'excited'); Label = 'Done! Take a look'; Color = '#FFF0FADB' }
    offline = @{ Playlist = @('sleep'); Label = 'Check my connection'; Color = '#FFF0EEE8' }
}

$window = [System.Windows.Window]::new()
$window.Title = "$petHostName Pet"
$window.Width = 176
$window.Height = 222
$window.WindowStyle = [System.Windows.WindowStyle]::None
$window.ResizeMode = [System.Windows.ResizeMode]::NoResize
$window.AllowsTransparency = $true
$window.Background = [System.Windows.Media.Brushes]::Transparent
$window.Topmost = $true
$window.ShowInTaskbar = $false
$window.WindowStartupLocation = [System.Windows.WindowStartupLocation]::Manual
$workArea = [System.Windows.SystemParameters]::WorkArea
$window.Left = $workArea.Right - $window.Width - 24
$window.Left -= ($window.Width + 16)
$window.Top = $workArea.Bottom - $window.Height - 24

$stack = [System.Windows.Controls.Grid]::new()
$stack.Width = $window.Width
$stack.Height = $window.Height
$window.Content = $stack

$speech = [System.Windows.Controls.Grid]::new()
$speech.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Center
$speech.VerticalAlignment = [System.Windows.VerticalAlignment]::Top
$speech.Margin = [System.Windows.Thickness]::new(0, 8, 0, 0)
$speech.MaxWidth = 168
$speech.Cursor = [System.Windows.Input.Cursors]::Hand
$shadow = [System.Windows.Media.Effects.DropShadowEffect]::new()
$shadow.Color = [System.Windows.Media.ColorConverter]::ConvertFromString('#FF6B5140')
$shadow.ShadowDepth = 2
$shadow.BlurRadius = 7
$shadow.Opacity = 0.2
$speech.Effect = $shadow
$bodyRow = [System.Windows.Controls.RowDefinition]::new()
$bodyRow.Height = [System.Windows.GridLength]::Auto
[void]$speech.RowDefinitions.Add($bodyRow)
$tailRow = [System.Windows.Controls.RowDefinition]::new()
$tailRow.Height = [System.Windows.GridLength]::new(8)
[void]$speech.RowDefinitions.Add($tailRow)

$bubble = [System.Windows.Controls.Border]::new()
$bubble.CornerRadius = [System.Windows.CornerRadius]::new(17)
$bubble.BorderThickness = [System.Windows.Thickness]::new(1.25)
$bubble.BorderBrush = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#FF40342C')
$bubble.Padding = [System.Windows.Thickness]::new(12, 7, 12, 8)
$bubble.MinWidth = 142
$bubbleText = [System.Windows.Controls.StackPanel]::new()
$label = [System.Windows.Controls.TextBlock]::new()
$label.FontFamily = [System.Windows.Media.FontFamily]::new('Segoe UI')
$label.FontSize = 12
$label.FontWeight = [System.Windows.FontWeights]::SemiBold
$label.Foreground = $bubble.BorderBrush
$label.TextAlignment = [System.Windows.TextAlignment]::Left
[void]$bubbleText.Children.Add($label)
$details = [System.Windows.Controls.TextBlock]::new()
$details.FontFamily = $label.FontFamily
$details.FontSize = 10
$details.Foreground = [System.Windows.Media.BrushConverter]::new().ConvertFromString('#FF6D6258')
$details.TextAlignment = [System.Windows.TextAlignment]::Left
$details.TextTrimming = [System.Windows.TextTrimming]::CharacterEllipsis
$details.MaxWidth = 140
$details.Margin = [System.Windows.Thickness]::new(0, 3, 0, 0)
[void]$bubbleText.Children.Add($details)
$bubble.Child = $bubbleText
[void]$speech.Children.Add($bubble)

$tail = [System.Windows.Shapes.Path]::new()
$tail.Data = [System.Windows.Media.Geometry]::Parse('M 1,0 L 9,8 L 17,0')
$tail.Stroke = $bubble.BorderBrush
$tail.StrokeThickness = 1
$tail.Width = 18
$tail.Height = 10
$tail.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Center
$tail.Margin = [System.Windows.Thickness]::new(0, -2, 0, 0)
[System.Windows.Controls.Grid]::SetRow($tail, 1)
[void]$speech.Children.Add($tail)
[void]$stack.Children.Add($speech)

$picture = [System.Windows.Controls.Image]::new()
$picture.Width = 132
$picture.Height = 143
$picture.Stretch = [System.Windows.Media.Stretch]::Uniform
$picture.SnapsToDevicePixels = $true
$picture.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Center
$picture.VerticalAlignment = [System.Windows.VerticalAlignment]::Bottom
$picture.Margin = [System.Windows.Thickness]::new(0, 0, 0, 6)
[void]$stack.Children.Add($picture)

$script:state = 'idle'
$script:petHovered = $false
$script:target = $null
$script:tick = 0
$script:clipIndex = 0
$script:reactions = @()
$script:polling = $false
$script:liveAgents = @()
$script:agentCounts = @{}
$script:acknowledged = @{}
$script:petCatalog = @()
$script:petSettings = Get-PetSettings
$settingsPath = Join-Path $dataRoot 'settings.json'
$selectionPath = Join-Path $dataRoot 'selected-pet.json'
$acknowledgementPath = Join-Path $dataRoot 'acknowledged.json'
if (-not $Check -and (Test-Path -LiteralPath $acknowledgementPath)) {
    try {
        $saved = Get-Content -LiteralPath $acknowledgementPath -Raw | ConvertFrom-Json -AsHashtable
        if ($saved -isnot [System.Collections.IDictionary]) { throw 'Expected an object.' }
        $script:acknowledged = $saved
    } catch {
        Write-Warning "Could not load completion acknowledgements: $($_.Exception.Message)"
    }
}

function Set-CurrentPet([string]$key, [switch]$Remember) {
    $pet = $script:petCatalog | Where-Object Key -eq $key | Select-Object -First 1
    if (-not $pet) { throw "Pet $key is not installed." }
    if ($script:settingsWindow -and $key -ne $script:currentPetKey) { $script:settingsWindow.Close() }
    $graphics = Read-PetGraphics $pet
    $script:frames = $graphics.Frames
    $script:clips = $graphics.Clips
    $script:allAnimations = $graphics.Names
    $script:sheetRows = $graphics.Rows
    $script:currentPetKey = $pet.Key
    $script:currentPetName = $pet.DisplayName
    $script:highestVisiblePixel = $graphics.HighestVisiblePixel
    $spriteScale = [Math]::Min($picture.Width / 192, $picture.Height / 208)
    $spriteTop = $window.Height - $picture.Height - $picture.Margin.Bottom
    $reservedSpeechHeight = 58
    $bubbleClearance = 6
    $bubbleTop = [Math]::Max(8, $spriteTop + $script:highestVisiblePixel * $spriteScale - $reservedSpeechHeight - $bubbleClearance)
    $speech.Margin = [System.Windows.Thickness]::new(0, $bubbleTop, 0, 0)
    $states.idle.Playlist = @('idle', 'eat', 'idle', 'sleep')
    $states.working.Playlist = @('read', 'walk-right', 'read', 'walk-left')
    if ($pet.Version -eq 2) {
        $states.idle.Playlist += @('turn-right', 'turn-left')
        $states.working.Playlist += @('turn-right', 'turn-left')
    }
    $script:tick = 0
    $script:clipIndex = 0
    $script:reactions = @()
    Apply-AnimationSettings
    Show-PetFrame
    if ($Remember -and -not $Check) {
        $savedSelection = @{ key = $pet.Key; displayName = $pet.DisplayName } | ConvertTo-Json
        [System.IO.File]::WriteAllText("$selectionPath.tmp", $savedSelection)
        [System.IO.File]::Move("$selectionPath.tmp", $selectionPath, $true)
    }
}

function Import-PetPack([string]$source) {
    $pythonExecutable = (Get-Command python -ErrorAction Stop).Source
    $result = & $pythonExecutable (Join-Path $PSScriptRoot 'Import-CodexPet.py') $source --library-root (Join-Path $dataRoot 'pets') 2>&1
    if ($LASTEXITCODE -ne 0) { throw ($result -join "`n") }
    $imported = ($result -join "`n") | ConvertFrom-Json
    $script:petCatalog = @(Get-PetCatalog $dataRoot)
    Set-CurrentPet $imported.key -Remember
}

function Refresh-PetMenu {
    $script:petCatalog = @(Get-PetCatalog $dataRoot)
    $changePet.Items.Clear()
    foreach ($pet in $script:petCatalog) {
        $choice = [System.Windows.Controls.MenuItem]::new()
        $choice.Header = $pet.DisplayName
        $choice.Tag = $pet.Key
        $choice.IsCheckable = $true
        $choice.IsChecked = $pet.Key -eq $script:currentPetKey
        $choice.Add_Click({
            param($sender, $event)
            try { Set-CurrentPet ([string]$sender.Tag) -Remember }
            catch { [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'Change pet') }
        })
        [void]$changePet.Items.Add($choice)
    }
    [void]$changePet.Items.Add([System.Windows.Controls.Separator]::new())
    $zipOption = [System.Windows.Controls.MenuItem]::new()
    $zipOption.Header = 'Import ZIP...'
    $zipOption.Add_Click({
        $picker = [Microsoft.Win32.OpenFileDialog]::new()
        $picker.Title = 'Choose a Codex pet pack'
        $picker.Filter = 'Codex pet packs (*.codex-pet.zip;*.zip)|*.codex-pet.zip;*.zip'
        if ($picker.ShowDialog($window)) {
            try { Import-PetPack $picker.FileName }
            catch { [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'Import pet') }
        }
    })
    [void]$changePet.Items.Add($zipOption)
    $folderOption = [System.Windows.Controls.MenuItem]::new()
    $folderOption.Header = 'Import folder...'
    $folderOption.Add_Click({
        Add-Type -AssemblyName System.Windows.Forms
        $picker = [System.Windows.Forms.FolderBrowserDialog]::new()
        $picker.Description = 'Choose the pet folder containing pet.json'
        try {
            if ($picker.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {
                try { Import-PetPack $picker.SelectedPath }
                catch { [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'Import pet') }
            }
        } finally { $picker.Dispose() }
    })
    [void]$changePet.Items.Add($folderOption)
}

function Get-AgentIdentity($agent) {
    if ($agent.name) { return [string]$agent.name }
    $program = switch ([string]$agent.agent) {
        'kiro-cli' { 'Kiro CLI' }
        'kiro-ide' { 'Kiro IDE' }
        'codex' { 'Codex' }
        'claude-code' { 'Claude Code' }
        default { [string]$agent.agent }
    }
    if (-not $program) { $program = [string]$agent.address }
    if ($agent.where) { return "$program · $($agent.where)" }
    return $program
}

function Update-BubbleVisibility {
    $speech.Visibility = if ($script:state -ne 'idle' -or $script:petHovered) {
        [System.Windows.Visibility]::Visible
    } else {
        [System.Windows.Visibility]::Collapsed
    }
}

function Set-PetState([string]$nextState, [string]$terminal) {
    $statusChanged = $script:state -ne $nextState
    if ($statusChanged) {
        $script:state = $nextState
        $script:tick = 0
        $script:clipIndex = 0
        $script:reactions = @()
    }
    $script:target = $terminal
    $stateInfo = $states[$script:state]
    $count = $script:agentCounts[$nextState]
    $label.Text = $stateInfo.Label + $(if ($count -gt 1) { " ($count)" } else { '' })
    $selectedAgent = $script:liveAgents | Where-Object address -eq $terminal | Select-Object -First 1
    $identity = if ($selectedAgent) { Get-AgentIdentity $selectedAgent } else { $null }
    $details.Text = if ($identity) { $identity } elseif ($nextState -eq 'offline') { 'Double-click to open' } else { '' }
    $details.Visibility = if ($details.Text) { [System.Windows.Visibility]::Visible } else { [System.Windows.Visibility]::Collapsed }
    $bubble.Background = [System.Windows.Media.BrushConverter]::new().ConvertFromString($stateInfo.Color)
    $tail.Fill = $bubble.Background
    $speech.ToolTip = if ($terminal) {
        "$identity`nDouble-click to open this workspace. Use Sessions to check individual completions."
    } else { "Double-click to open $petHostName" }
    Update-BubbleVisibility
    if ($statusChanged -and $frames) {
        Show-PetFrame
        if (-not $Check -and $animation.IsEnabled) {
            $animation.Stop()
            $animation.Start()
        }
    }
}

function Save-PetAcknowledgements {
    if ($Check) { return }
    $temporaryPath = "$acknowledgementPath.tmp"
    [System.IO.File]::WriteAllText($temporaryPath, ($script:acknowledged | ConvertTo-Json))
    [System.IO.File]::Move($temporaryPath, $acknowledgementPath, $true)
}

function Update-AgentSnapshot([object[]]$agents, [string]$viewedAgent) {
    if ($viewedAgent) {
        $completed = $script:liveAgents | Where-Object { $_.address -eq $viewedAgent -and $_.status -in @('done', 'error') } | Select-Object -First 1
        if ($completed) {
            $script:acknowledged[$completed.address] = $completed.turn
            Save-PetAcknowledgements
        }
    }
    Update-PetStatus
}

function Open-AgentHostFromPet {
    $selected = $script:sessions | Where-Object key -eq $script:target | Select-Object -First 1
    if ($selected.source -eq 'cli') {
        [void][System.Windows.MessageBox]::Show("Return to your CLI terminal to review this session. You can acknowledge its completion from the Sessions list.", 'Kiro Pet')
        return
    }
    try {
        $workspace = if ($selected) { $selected.workspace } else { @(Get-PetConnections)[0].workspace }
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = $petHostExecutable
        $start.UseShellExecute = $true
        if ($workspace) { $start.ArgumentList.Add([string]$workspace) }
        [Diagnostics.Process]::Start($start) | Out-Null
    } catch {
        [void][System.Windows.MessageBox]::Show($_.Exception.Message, 'Kiro Pet')
    }
}

function Start-PetReaction([string[]]$playlist) {
    $script:reactions = @($playlist)
    $script:tick = 0
}

function Show-PetFrame {
    $playlist = $states[$script:state].Playlist
    $clipName = if ($script:reactions.Count) { $script:reactions[0] } else { $playlist[$script:clipIndex] }
    $clip = $clips[$clipName]
    $picture.Source = $frames[$clip.Row][$clip.Columns[$script:tick % $clip.Columns.Count]]
    if ($script:petSettings.reducedMotion) { return }
    $script:tick++
    $repeatCount = if ($script:reactions.Count) { 1 } else { $clip.Loops }
    if ($script:tick -ge ($clip.Columns.Count * $repeatCount)) {
        $script:tick = 0
        if ($script:reactions.Count) {
            $script:reactions = @($script:reactions | Select-Object -Skip 1)
        } else {
            $script:clipIndex = ($script:clipIndex + 1) % $playlist.Count
        }
    }
}

function Update-PetStatus {
    if ($script:polling) { return }
    $script:polling = $true
    try {
        $script:sessions = @(Get-PetSessions)
        $summary = Get-SessionSummary $script:sessions $script:acknowledged
        $script:agentCounts = @{
            blocked = $summary.counts.waiting; error = $summary.counts.error
            done = $summary.counts.done; working = $summary.counts.working; idle = $summary.counts.idle
        }
        $script:liveAgents = @($script:sessions | ForEach-Object {
            [pscustomobject]@{
                address = $_.key; status = $_.status; turn = $_.completionId
                name = $(if ($_.title) { "$($_.title) · $(Split-Path -Leaf $_.workspace)" } else { $null })
                agent = "kiro-$($_.source)"; where = (Split-Path -Leaf $_.workspace) + ' · ' + $_.sessionId.Substring(0, [Math]::Min(8, $_.sessionId.Length))
            }
        })
        $next = if ($summary.status -eq 'waiting') { 'blocked' } else { $summary.status }
        $target = if ($summary.target) { $summary.target.key } else { $null }
        Set-PetState $next $target
        if ($summary.counts.working -gt 0 -and $next -ne 'working') {
            $details.Text = "$($details.Text) · $($summary.counts.working) working"
        }
        Update-ConnectionHealth
        $pickerVersion = "$($script:nativeSnapshot.Version)|$($script:nativeSnapshot.Health -join '|')"
        if ($script:sessionWindow -and $pickerVersion -ne $script:lastPickerVersion) {
            Refresh-SessionPicker
            $script:lastPickerVersion = $pickerVersion
        }
    } catch {
        $script:connectionHealth = $_.Exception.Message
        Set-PetState 'offline' $null
    } finally { $script:polling = $false }
}

function Update-PetWindowStatus { Update-PetStatus }
function Update-KiroStatusIfChanged { Update-PetStatus }

$animation = [System.Windows.Threading.DispatcherTimer]::new()
$animation.Interval = [TimeSpan]::FromMilliseconds(300)
$animation.Add_Tick({ Show-PetFrame })

$statusTimer = [System.Windows.Threading.DispatcherTimer]::new()
$statusTimer.Interval = [TimeSpan]::FromSeconds(2)
$statusTimer.Add_Tick({ Update-PetWindowStatus })
$kiroStatusTimer = $null
$kiroStatusTimer = [System.Windows.Threading.DispatcherTimer]::new()
$kiroStatusTimer.Interval = [TimeSpan]::FromMilliseconds(200)
$kiroStatusTimer.Add_Tick({ Update-KiroStatusIfChanged })

$window.Add_MouseMove({
    param($sender, $event)
    $onPet = $event.GetPosition($picture)
    $overPet = $onPet.X -ge 0 -and $onPet.X -le $picture.ActualWidth -and
        $onPet.Y -ge 0 -and $onPet.Y -le $picture.ActualHeight
    $overSpeech = $false
    if ($speech.Visibility -eq [System.Windows.Visibility]::Visible) {
        $onSpeech = $event.GetPosition($speech)
        $petTop = $picture.TranslatePoint([System.Windows.Point]::new(0, 0), $speech).Y
        $hoverHeight = [Math]::Max($speech.ActualHeight, $petTop)
        $overSpeech = $onSpeech.X -ge 0 -and $onSpeech.X -le $speech.ActualWidth -and
            $onSpeech.Y -ge 0 -and $onSpeech.Y -le $hoverHeight
    }
    if ($script:petHovered -ne ($overPet -or $overSpeech)) {
        $script:petHovered = $overPet -or $overSpeech
        Update-BubbleVisibility
    }
})
$window.Add_MouseLeave({
    $script:petHovered = $false
    Update-BubbleVisibility
})

$window.Add_MouseLeftButtonDown({
    param($sender, $event)
    if ($event.ClickCount -eq 2) {
        Open-AgentHostFromPet
    } else {
        $window.DragMove()
        Save-PetPosition
        Start-PetReaction @('wave')
    }
})

$menu = [System.Windows.Controls.ContextMenu]::new()
$changePet = [System.Windows.Controls.MenuItem]::new()
$changePet.Header = 'Change pet'
[void]$menu.Items.Add($changePet)
$settingsMenu = [System.Windows.Controls.MenuItem]::new()
$settingsMenu.Header = 'Settings...'
$settingsMenu.Add_Click({ Show-PetSettings })
[void]$menu.Items.Add($settingsMenu)
$sessionsMenu = [System.Windows.Controls.MenuItem]::new()
$sessionsMenu.Header = 'Sessions...'
$sessionsMenu.Add_Click({ Show-SessionPicker })
[void]$menu.Items.Add($sessionsMenu)
$healthMenu = [System.Windows.Controls.MenuItem]::new()
$healthMenu.Header = 'Connection health...'
$healthMenu.Add_Click({ [void][System.Windows.MessageBox]::Show($script:connectionHealth, 'Kiro Pet connections') })
[void]$menu.Items.Add($healthMenu)
$menu.Add_Opened({ Refresh-PetMenu })
$preview = [System.Windows.Controls.MenuItem]::new()
$preview.Header = 'Play all animations'
$preview.Add_Click({ Start-PetReaction $allAnimations })
[void]$menu.Items.Add($preview)
$quit = [System.Windows.Controls.MenuItem]::new()
$quit.Header = "Quit $petHostName Pet"
$quit.Add_Click({ $window.Close() })
[void]$menu.Items.Add($quit)
$window.ContextMenu = $menu
$window.Add_Closed({
    Save-PetPosition
    Stop-PetControl
    $animation.Stop()
    $statusTimer.Stop()
    if ($kiroStatusTimer) { $kiroStatusTimer.Stop() }
    if ($script:openTimer) { $script:openTimer.Stop() }
})

$script:petCatalog = @(Get-PetCatalog $dataRoot)
$initialPet = 'spark'
if (Test-Path -LiteralPath $selectionPath) {
    try {
        $saved = Get-Content -LiteralPath $selectionPath -Raw | ConvertFrom-Json
        if ($saved.key -in $script:petCatalog.Key) { $initialPet = $saved.key }
    } catch { Write-Warning 'The saved pet selection is invalid; using Spark.' }
}
Set-CurrentPet $initialPet
Update-PetStatus

Restore-PetPosition
if ($Check) {
    foreach ($candidate in $script:petCatalog) { Set-CurrentPet $candidate.Key; Show-PetFrame }
    Write-Output "Renderer PASS: $($script:petCatalog.Count) pets; state=$script:state; speed=$($script:petSettings.speed)"
    return
}
Start-PetControl
$statusTimer.Start()
if ($kiroStatusTimer) { $kiroStatusTimer.Start() }
$animation.Start()
$application = [System.Windows.Application]::new()
$application.ShutdownMode = [System.Windows.ShutdownMode]::OnExplicitShutdown
$window.Add_Closed({ $application.Shutdown() })
try { [void]$application.Run($window) } finally { Stop-PetControl }
