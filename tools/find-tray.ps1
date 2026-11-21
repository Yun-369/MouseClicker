Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$root = [System.Windows.Automation.AutomationElement]::RootElement

$targets = @('Shell_TrayWnd', 'NotifyIconOverflowWindow', 'TopLevelWindowForOverflowXamlIsland', 'XamlExplorerHostIslandWindow')
foreach ($cls in $targets) {
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ClassNameProperty, $cls)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($win -eq $null) { Write-Output "$cls : not found"; continue }
    $r = $win.Current.BoundingRectangle
    Write-Output "$cls : rect=($([int]$r.X),$([int]$r.Y),$([int]$r.Width),$([int]$r.Height))"

    $nameCond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, '鼠标连点器')
    $icon = $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $nameCond)
    if ($icon -ne $null) {
        $ir = $icon.Current.BoundingRectangle
        Write-Output "  ICON 鼠标连点器 rect=($([int]$ir.X),$([int]$ir.Y),$([int]$ir.Width),$([int]$ir.Height))"
    } else {
        Write-Output "  icon not in this window"
    }
}
