Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[M]::SetProcessDPIAware() | Out-Null

function Click([int]$x, [int]$y) {
    [M]::SetCursorPos($x, $y) | Out-Null
    Start-Sleep -Milliseconds 250
    [M]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [M]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
}

# 1. 点溢出箭头
Click 1609 1056
Start-Sleep -Milliseconds 900

# 2. 找溢出面板，再找图标
$root = [System.Windows.Automation.AutomationElement]::RootElement
$nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '鼠标连点器')
$icon = $null
foreach ($cls in @('NotifyIconOverflowWindow', 'TopLevelWindowForOverflowXamlIsland', 'XamlExplorerHostIslandWindow')) {
    $clsCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, $cls)
    $panel = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $clsCond)
    if ($panel -eq $null) { Write-Output "panel $cls : none"; continue }
    $icon = $panel.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    Write-Output "panel $cls : icon=$(if ($icon) {'YES'} else {'no'})"
    if ($icon) { break }
}

if ($icon -eq $null) { Write-Output 'ICON NOT FOUND'; exit 1 }

$r = $icon.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
Write-Output ("icon rect=({0},{1},{2},{3}) center=({4},{5})" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $cx, $cy)

# 3. 右键图标
[M]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 300
[M]::mouse_event(0x0008,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M]::mouse_event(0x0010,0,0,0,[UIntPtr]::Zero)
Write-Output 'right-click sent'
