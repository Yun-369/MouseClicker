Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class W {
  [DllImport("user32.dll")] public static extern bool EnumWindows(Func<IntPtr, IntPtr, bool> cb, IntPtr lp);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
'@

# Invoke the pick button
$root = [System.Windows.Automation.AutomationElement]::RootElement
$procId = (Get-Process MouseClicker).Id
$pidCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $procId)
$app = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $pidCond)
$btnCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '准星取点')
$btn = $app.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $btnCond)
if ($btn -eq $null) { Write-Output 'button not found'; exit 1 }
$invoke = $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
$invoke.Invoke()
Write-Output 'invoked'
Start-Sleep -Milliseconds 900

# List visible windows of the process
$found = 0
[W]::EnumWindows({ param($h, $lp)
    [uint32]$winPid = 0
    [W]::GetWindowThreadProcessId($h, [ref]$winPid) | Out-Null
    if ($winPid -eq $procId -and [W]::IsWindowVisible($h)) {
        $sb = New-Object System.Text.StringBuilder 256
        [W]::GetWindowText($h, $sb, 256) | Out-Null
        $cls = New-Object System.Text.StringBuilder 256
        [W]::GetClassName($h, $cls, 256) | Out-Null
        $r = New-Object W+RECT
        [W]::GetWindowRect($h, [ref]$r) | Out-Null
        Write-Output ("win: '{0}' class='{1}' rect=({2},{3})-({4},{5})" -f $sb.ToString(), $cls.ToString(), $r.L, $r.T, $r.R, $r.B)
        $script:found++
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
Write-Output ("visible windows: " + $found)
