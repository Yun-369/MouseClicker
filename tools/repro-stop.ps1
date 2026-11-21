Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class M {
  [StructLayout(LayoutKind.Sequential)] public struct MI { public int DX, DY; public uint Data, Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Explicit)] public struct IU { [FieldOffset(0)] public MI Mouse; }
  [StructLayout(LayoutKind.Sequential)] public struct IN { public uint Type; public IU U; }
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, IN[] inputs, int size);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
}
'@
[M]::SetProcessDPIAware() | Out-Null

function Click([int]$cx, [int]$cy) {
    [M]::SetCursorPos($cx, $cy) | Out-Null
    Start-Sleep -Milliseconds 150
    $down = New-Object M+IN; $down.Type = 0; $down.U.Mouse.Flags = 0x0002
    $up   = New-Object M+IN; $up.Type   = 0; $up.U.Mouse.Flags   = 0x0004
    [M]::SendInput(1, @($down), [Runtime.InteropServices.Marshal]::SizeOf([type]"M+IN")) | Out-Null
    Start-Sleep -Milliseconds 60
    [M]::SendInput(1, @($up), [Runtime.InteropServices.Marshal]::SizeOf([type]"M+IN")) | Out-Null
}

$root = [System.Windows.Automation.AutomationElement]::RootElement
$procId = (Get-Process MouseClicker).Id
$pc = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pc)

function FindBtn($name) {
    $c = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $name)
    return $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $c)
}

function ClickEl($el) {
    $r = $el.Current.BoundingRectangle
    Click ([int]($r.X + $r.Width/2)) ([int]($r.Y + $r.Height/2))
}

$b = FindBtn '开始'
if ($b -eq $null) { Write-Output 'start not found'; exit 1 }
ClickEl $b
Write-Output ('clicked at ' + (Get-Date -Format 'HH:mm:ss.fff'))
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 100
    $cur = FindBtn '停止'
    $st = if ($cur) { 'RUNNING' } else { 'idle' }
    if ($i % 5 -eq 0) { Write-Output ("t+{0,4}ms  {1}" -f ($i*100), $st) }
}
$b2 = FindBtn '停止'
if ($b2) { ClickEl $b2; Write-Output 'stop clicked' }
Start-Sleep -Milliseconds 300
Get-Content "$env:LOCALAPPDATA\MouseClicker\logs\run-20260914.log" | Select-Object -Last 4
