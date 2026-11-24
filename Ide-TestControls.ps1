Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
if (-not ('KiroIdeTestInput' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class KiroIdeTestInput {
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
}
'@
}
function Get-TestIdeWindow {
    $windows = @([System.Windows.Automation.AutomationElement]::RootElement.FindAll(
        [System.Windows.Automation.TreeScope]::Children, [System.Windows.Automation.Condition]::TrueCondition
    ) | Where-Object { $_.Current.Name -match 'kiro-pet-ide-native-check.*Kiro' })
    if ($windows.Count -ne 1) { throw 'Expected exactly one isolated Kiro test window.' }
    return $windows[0]
}
function Click-TestIdeControl($Control) {
    $window = Get-TestIdeWindow
    $window.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern).SetWindowVisualState(
        [System.Windows.Automation.WindowVisualState]::Normal
    )
    [void][KiroIdeTestInput]::SetForegroundWindow([IntPtr]$window.Current.NativeWindowHandle)
    $rect = $Control.Current.BoundingRectangle
    if ($rect.Width -le 0 -or $rect.Height -le 0 -or $Control.Current.IsOffscreen) { throw 'Test control is not visible.' }
    [void][KiroIdeTestInput]::SetCursorPos([int]($rect.X + $rect.Width / 2), [int]($rect.Y + $rect.Height / 2))
    [KiroIdeTestInput]::mouse_event(2,0,0,0,[UIntPtr]::Zero)
    [KiroIdeTestInput]::mouse_event(4,0,0,0,[UIntPtr]::Zero)
}
function Send-TestIdeKeys([string]$Keys) {
    $window = Get-TestIdeWindow
    if ([KiroIdeTestInput]::GetForegroundWindow() -ne [IntPtr]$window.Current.NativeWindowHandle) {
        throw 'Test IDE window is not foreground; keyboard input was not sent.'
    }
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}
