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
    Start-Sleep -Milliseconds 200
    [M]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 60; [M]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$procId = (Get-Process MouseClicker).Id
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '准星取点')
$btn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($btn -eq $null) { Write-Output 'button not found'; exit 1 }
$r = $btn.Current.BoundingRectangle
$cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
Write-Output ("pick button center: ({0},{1})" -f $cx, $cy)
Click $cx $cy
Write-Output 'clicked pick button'
Start-Sleep -Milliseconds 800

# now click screen center to pick a point on the overlay
Click 960 540
Write-Output 'clicked overlay at (960,540)'
