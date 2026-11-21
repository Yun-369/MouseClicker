# MouseClicker basic input test window
# This script is a verification fixture. It does not belong to the product UI and does not
# It only emits temporary counters and coordinates for isolated input checks.
# TelemetryDir (optional): directory for the mc-testwindow-*.txt telemetry files.
# Standalone runs default to %TEMP%; the acceptance drivers pass a per-round directory so
# two fixtures never share counters/geometry.
param([string]$TelemetryDir = '')
Add-Type @"
using System;
using System.IO;
using System.Windows.Forms;
public sealed class KeySpy : IMessageFilter {
  public static string KeyLogPath = "";
  public static string CharLogPath = "";
  // r7 diagnostics: every mouse button message RETRIEVED by this fixture's pump,
  // with client coordinates - proves whether a synthetic click reached the window.
  public static string MouseLogPath = "";
  static void LogKey(ushort vk, bool extended, bool up, int scan) {
    // r9: log the SCAN code too - injected keys may be delivered with a layout-
    // derived VK__none_ (0xE5/0xE7) on this Windows build, but lParam carries the scan.
    try { File.AppendAllText(KeyLogPath, vk + " " + (extended ? 1 : 0) + " " + (up ? 1 : 0) + " " + scan + Environment.NewLine); } catch { }
  }
  public static void LogChar(int codePoint) {
    try { File.AppendAllText(CharLogPath, codePoint + Environment.NewLine); } catch { }
  }
  static void LogMouse(uint msg, int x, int y) {
    try { File.AppendAllText(MouseLogPath, msg.ToString("X") + " " + x + "," + y + Environment.NewLine); } catch { }
  }
  public bool PreFilterMessage(ref Message m) {
    if (m.Msg == 0x0100 || m.Msg == 0x0101 || m.Msg == 0x0104 || m.Msg == 0x0105) {
      ushort vk = (ushort)m.WParam.ToInt64();
      bool extended = (((long)m.LParam >> 24) & 1) != 0;
      int scan = (((int)m.LParam >> 16) & 0xFF);
      LogKey(vk, extended, m.Msg == 0x0101 || m.Msg == 0x0105, scan);
    }
    if (m.Msg == 0x0102) LogChar((int)m.WParam.ToInt64());
    if (m.Msg == 0x0201 || m.Msg == 0x0202 || m.Msg == 0x0204 || m.Msg == 0x0205 || m.Msg == 0x0207 || m.Msg == 0x0208 || m.Msg == 0x020A) {
      int lp = (int)m.LParam.ToInt64();
      LogMouse((uint)m.Msg, (short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF));
    }
    return false;
  }
}
"@ -ReferencedAssemblies System.Windows.Forms,System.Windows.Forms.Primitives

# pwsh 7.6: Add-Type -ReferencedAssemblies compiles the spy but no longer brings
# WinForms into the session type system - load it explicitly so the fixture works
# on every pwsh version.
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# per-run telemetry directory: standalone default = %TEMP%, acceptance passes its
# own directory so parallel/sequential fixtures never share files
$tdir = if ([string]::IsNullOrWhiteSpace($TelemetryDir)) { $env:TEMP } else { $TelemetryDir }
if (!(Test-Path -LiteralPath $tdir)) { New-Item -ItemType Directory -Force -Path $tdir | Out-Null }
[System.Windows.Forms.Application]::SetHighDpiMode([System.Windows.Forms.HighDpiMode]::PerMonitorV2)
$form = New-Object System.Windows.Forms.Form
$form.Text = '连点器基础输入测试窗口'
$form.ClientSize = New-Object System.Drawing.Size(620, 420)
$form.TopMost = $true
$form.StartPosition = 'CenterScreen'
$form.KeyPreview = $true

$keyPath = Join-Path $tdir 'mc-testwindow-keys.txt'
$charPath = Join-Path $tdir 'mc-testwindow-chars.txt'
$mousePath = Join-Path $tdir 'mc-testwindow-mouse.txt'
[KeySpy]::KeyLogPath = $keyPath
[KeySpy]::CharLogPath = $charPath
[KeySpy]::MouseLogPath = $mousePath
Set-Content -LiteralPath $keyPath -Value '' -Encoding ascii
Set-Content -LiteralPath $charPath -Value '' -Encoding ascii
Set-Content -LiteralPath $mousePath -Value '' -Encoding ascii
$keySpy = New-Object KeySpy
[System.Windows.Forms.Application]::AddMessageFilter($keySpy)

$textPath = Join-Path $tdir 'mc-testwindow-text.txt'
$textRectPath = Join-Path $tdir 'mc-testwindow-rect.txt'
# r8: per-click ordered coordinates (button + client x,y) for sequence checks
$clicksPath = Join-Path $tdir 'mc-testwindow-clicks.txt'
Set-Content -LiteralPath $clicksPath -Value '' -Encoding ascii
function Log-Click($button, $x, $y) {
  try { [IO.File]::AppendAllText($clicksPath, "$button $x $y" + [Environment]::NewLine) } catch { }
}
$padRectPath = Join-Path $tdir 'mc-testwindow-padrect.txt'
$countsPath = Join-Path $tdir 'mc-testwindow-counts.txt'
Set-Content -LiteralPath $textPath -Value '' -Encoding utf8

$statsPanel = New-Object System.Windows.Forms.Panel
$statsPanel.Dock = 'Top'; $statsPanel.Height = 112
$form.Controls.Add($statsPanel)
$stats = New-Object System.Windows.Forms.Label
$stats.Bounds = New-Object System.Drawing.Rectangle(8, 6, 470, 100)
$stats.Font = New-Object System.Drawing.Font('Consolas', 10)
$statsPanel.Controls.Add($stats)
$reset = New-Object System.Windows.Forms.Button
$reset.Text = '清零'; $reset.Bounds = New-Object System.Drawing.Rectangle(520, 8, 84, 28); $reset.TabStop = $false
$statsPanel.Controls.Add($reset)

$inputHost = New-Object System.Windows.Forms.Panel
$inputHost.Dock = 'Bottom'; $inputHost.Height = 94
$form.Controls.Add($inputHost)
$inputLabel = New-Object System.Windows.Forms.Label
$inputLabel.Dock = 'Top'; $inputLabel.Height = 20; $inputLabel.Text = '文本接收框（点击聚焦后由宏输入）：'
$inputBox = New-Object System.Windows.Forms.TextBox
$inputBox.Multiline = $true; $inputBox.AcceptsTab = $true; $inputBox.AcceptsReturn = $true
$inputBox.Dock = 'Fill'; $inputBox.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 10)
$inputHost.Controls.Add($inputBox); $inputHost.Controls.Add($inputLabel)

$pad = New-Object System.Windows.Forms.Panel
$pad.Dock = 'Fill'; $pad.BorderStyle = 'FixedSingle'
$hint = New-Object System.Windows.Forms.Label
$hint.Dock = 'Fill'; $hint.TextAlign = 'MiddleCenter'; $hint.Font = New-Object System.Drawing.Font('Microsoft YaHei UI', 12)
$hint.Text = "在此区域点击 / 按住拖拽 / 滚轮`n（左中右键与键盘都会被统计）"
$pad.Controls.Add($hint); $form.Controls.Add($pad)

$c = [ordered]@{ leftClick=0; midClick=0; rightClick=0; leftDown=0; leftUp=0; midDown=0; midUp=0; rightDown=0; rightUp=0; wheel=0; keyDown=0; keyUp=0 }
$heldButtons = New-Object System.Collections.Generic.HashSet[string]
$heldKeys = New-Object System.Collections.Generic.HashSet[System.Windows.Forms.Keys]
$script:lastWheelTick = -1000
function Write-TextCapture {
  try {
    $value = $inputBox.Text -replace "`r?`n", '<CR>' -replace "`t", '<TAB>'
    [IO.File]::WriteAllText($textPath, $value, (New-Object System.Text.UTF8Encoding($false)))
  } catch { }   # a concurrent reader must never break the input handlers
}
function Write-TextRect {
  try { Write-TextRectCore } catch { }
  try { Write-PadRectCore } catch { }
}
function Write-TextRectCore {
  if ($inputBox.IsHandleCreated) {
    $rect = $inputBox.RectangleToScreen([System.Drawing.Rectangle]::new(0,0,$inputBox.Width,$inputBox.Height))
    Set-Content -LiteralPath $textRectPath -Value ("{0},{1},{2},{3}" -f $rect.Left,$rect.Top,$rect.Right,$rect.Bottom) -Encoding ascii
  }
}
function Write-PadRectCore {
  # r7: the CLICK PAD rectangle (the area that actually counts clicks).
  if ($pad.IsHandleCreated) {
    $rect = $pad.RectangleToScreen([System.Drawing.Rectangle]::new(0,0,$pad.Width,$pad.Height))
    Set-Content -LiteralPath $padRectPath -Value ("{0},{1},{2},{3}" -f $rect.Left,$rect.Top,$rect.Right,$rect.Bottom) -Encoding ascii
  }
}
function Update-Stats {
  try { Update-StatsCore } catch { }   # telemetry is best-effort under concurrent readers
}
function Update-StatsCore {
  $held = if ($heldButtons.Count) { $heldButtons -join ',' } else { 'none' }
  $stats.Text = @"
完整单击  左:$($c.leftClick) 中:$($c.midClick) 右:$($c.rightClick)
按下      左:$($c.leftDown)  中:$($c.midDown) 右:$($c.rightDown)
松开      左:$($c.leftUp)    中:$($c.midUp)   右:$($c.rightUp)
滚轮:$($c.wheel)   键盘按下:$($c.keyDown) 松开:$($c.keyUp)
当前按住鼠标: $held
"@
  $modCtrl = [int]$heldKeys.Contains([System.Windows.Forms.Keys]::ControlKey)
  $modAlt = [int]$heldKeys.Contains([System.Windows.Forms.Keys]::Menu)
  $modShift = [int]$heldKeys.Contains([System.Windows.Forms.Keys]::ShiftKey)
  @(
    "leftClick=$($c.leftClick)", "midClick=$($c.midClick)", "rightClick=$($c.rightClick)",
    "leftDown=$($c.leftDown)", "midDown=$($c.midDown)", "rightDown=$($c.rightDown)",
    "leftUp=$($c.leftUp)", "midUp=$($c.midUp)", "rightUp=$($c.rightUp)",
    "wheel=$($c.wheel)", "keyDown=$($c.keyDown)", "keyUp=$($c.keyUp)",
    "held=$held", "modCtrl=$modCtrl", "modAlt=$modAlt", "modShift=$modShift"
  ) | Set-Content -LiteralPath $countsPath -Encoding ascii
}
function On-MouseDown($event) {
  switch ($event.Button) {
    'Left' { $c.leftDown++; [void]$heldButtons.Add('左') }
    'Middle' { $c.midDown++; [void]$heldButtons.Add('中') }
    'Right' { $c.rightDown++; [void]$heldButtons.Add('右') }
  }
  Log-Click (('down-' + $event.Button).ToLower()) $event.X $event.Y
  Update-Stats
}
function On-MouseUp($event) {
  switch ($event.Button) {
    'Left' { $c.leftUp++; $c.leftClick++; [void]$heldButtons.Remove('左') }
    'Middle' { $c.midUp++; $c.midClick++; [void]$heldButtons.Remove('中') }
    'Right' { $c.rightUp++; $c.rightClick++; [void]$heldButtons.Remove('右') }
  }
  Log-Click (('up-' + $event.Button).ToLower()) $event.X $event.Y
  Update-Stats
}
function On-MouseWheel($event) {
  $now = [Environment]::TickCount
  if ($now - $script:lastWheelTick -ge 30) { $c.wheel++ }
  $script:lastWheelTick = $now
  Update-Stats
}
foreach ($surface in @($form,$statsPanel,$pad,$hint)) {
  $surface.Add_MouseDown({ param($s,$e) On-MouseDown $e })
  $surface.Add_MouseUp({ param($s,$e) On-MouseUp $e })
  $surface.Add_MouseWheel({ param($s,$e) On-MouseWheel $e })
}
$form.Add_KeyDown({ param($s,$e) if ($heldKeys.Add($e.KeyCode)) { $c.keyDown++ }; Update-Stats })
$form.Add_KeyUp({ param($s,$e) [void]$heldKeys.Remove($e.KeyCode); $c.keyUp++; Update-Stats })
$inputBox.Add_TextChanged({ Write-TextCapture })
$reset.Add_Click({
  foreach ($key in $c.Keys) { $c[$key] = 0 }
  $heldButtons.Clear(); $heldKeys.Clear(); $script:lastWheelTick = -1000
  $inputBox.Clear(); Write-TextCapture
  Set-Content -LiteralPath $keyPath -Value '' -Encoding ascii
  Set-Content -LiteralPath $charPath -Value '' -Encoding ascii
  Set-Content -LiteralPath $clicksPath -Value '' -Encoding ascii
  Update-Stats
})
$form.Add_Shown({ Write-TextRect })
$form.Add_SizeChanged({ Write-TextRect })
$form.Add_LocationChanged({ Write-TextRect })
$timer = New-Object System.Windows.Forms.Timer
$timer.Interval = 400
$timer.Add_Tick({ Write-TextRect })
$timer.Start()
Update-Stats
Write-Host 'basic input test window open'
[System.Windows.Forms.Application]::Run($form)
# Resource release on close: stop the telemetry timer, unhook the message filter and
# dispose the form so the hosting pwsh process can exit promptly.
$timer.Stop(); $timer.Dispose()
[System.Windows.Forms.Application]::RemoveMessageFilter($keySpy)
$form.Dispose()
Write-Host 'basic input test window closed' 





