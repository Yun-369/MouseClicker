Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Mouse {
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extra);
}
'@

$root = [System.Windows.Automation.AutomationElement]::RootElement
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '鼠标连点器')
$found = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
Write-Output ("search done, count=" + $found.Count)
if ($found.Count -eq 0) { exit 1 }

$el = $found[0]
$r = $el.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
Write-Output ("rect=({0},{1},{2},{3}) center=({4},{5})" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $cx, $cy)

[Mouse]::SetCursorPos($cx, $cy) | Out-Null
Start-Sleep -Milliseconds 300
[Mouse]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero)
Start-Sleep -Milliseconds 60
[Mouse]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero)
Write-Output "right-click sent"
