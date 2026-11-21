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

function Click($el) {
    $r = $el.Current.BoundingRectangle
    $cx = [int]($r.X + $r.Width / 2); $cy = [int]($r.Y + $r.Height / 2)
    [M]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 150
    [M]::mouse_event(0x0002,0,0,0,[UIntPtr]::Zero); Start-Sleep -Milliseconds 50; [M]::mouse_event(0x0004,0,0,0,[UIntPtr]::Zero)
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$procId = (Get-Process MouseClicker).Id
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)

# set both interval fields to 10000
$editCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = $app.FindAll([System.Windows.Automation.TreeScope]::Descendants, $editCond)
$set = 0
foreach ($e in $edits) {
    if ($e.Current.Name -match '间隔') {
        $v = $e.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
        $v.SetValue('10000')
        $set++
    }
}
Write-Output "interval edits set: $set"

# click start
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '开始')
$start = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($start -eq $null) { Write-Output 'start not found'; exit 1 }
Click $start
Write-Output 'clicked start'
Start-Sleep -Milliseconds 2000

# click stop
$btnCond2 = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '停止')
$stop = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond2)
if ($stop -eq $null) { Write-Output 'stop not found (maybe already idle)'; exit 0 }
Click $stop
Write-Output 'clicked stop'
Start-Sleep -Milliseconds 500
