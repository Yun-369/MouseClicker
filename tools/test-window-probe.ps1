# 真实输入探针：只操作 tools/test-window.ps1 自己创建的测试窗口。
# 用法：先启动测试窗口，再运行本脚本。
#   pwsh -File .\tools\test-window.ps1
#   pwsh -File .\tools\test-window-probe.ps1
# 验证内容：左键单击、滚轮、真实键盘按键、Unicode 文本、以及多行文本框里
# Enter 恰好插入一个换行、Tab 恰好插入一个制表符（真实 SendInput 路径）。
# 计数采用增量比较（环境里可能存在无关的外部输入，见 README 的说明）。
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class SI {
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Explicit)] public struct UNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public UNION u; }
  [DllImport("user32.dll")] public static extern uint SendInput(uint n, INPUT[] a, int s);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
  [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern IntPtr FindWindowW(IntPtr c,string n);
  [StructLayout(LayoutKind.Sequential)] public struct RC { public int L,T,R,B; }
  [StructLayout(LayoutKind.Sequential)] public struct PT { public int X,Y; }
  [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out RC r);
  [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr h, ref PT p);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
  [DllImport("user32.dll")] public static extern bool PostMessageW(IntPtr h,uint m,IntPtr w,IntPtr l);
  public static PT ToScreen(IntPtr h,int x,int y){ var p=new PT{X=x,Y=y}; ClientToScreen(h,ref p); return p; }
  public static void Mouse(uint flags){ var a=new INPUT[1]; a[0].type=0; a[0].u.mi.dwFlags=flags; SendInput(1,a,Marshal.SizeOf<INPUT>()); }
  public static void Click(int x,int y){ SetCursorPos(x,y); System.Threading.Thread.Sleep(80); Mouse(0x0002); System.Threading.Thread.Sleep(40); Mouse(0x0004); }
  public static void Wheel(int x,int y,int delta){ SetCursorPos(x,y); System.Threading.Thread.Sleep(60); var a=new INPUT[1]; a[0].type=0; a[0].u.mi.mouseData=(uint)delta; a[0].u.mi.dwFlags=0x0800; SendInput(1,a,Marshal.SizeOf<INPUT>()); }
  public static void Key(ushort vk){ var a=new INPUT[2]; a[0].type=1; a[0].u.ki.wVk=vk; a[1].type=1; a[1].u.ki.wVk=vk; a[1].u.ki.dwFlags=2; SendInput(2,a,Marshal.SizeOf<INPUT>()); }
  public static void Text(string s){ foreach(char c in s){ var a=new INPUT[2]; a[0].type=1; a[0].u.ki.wScan=c; a[0].u.ki.dwFlags=4; a[1].type=1; a[1].u.ki.wScan=c; a[1].u.ki.dwFlags=6; SendInput(2,a,Marshal.SizeOf<INPUT>()); System.Threading.Thread.Sleep(30);} }
}
'@
[void][SI]::SetProcessDPIAware()

$countsPath = Join-Path $env:TEMP 'mc-testwindow-counts.txt'
$textPath   = Join-Path $env:TEMP 'mc-testwindow-text.txt'
function Read-Counts {
  for($i=0;$i -lt 20;$i++){
    $raw = Get-Content -LiteralPath $countsPath -ErrorAction SilentlyContinue
    if($raw -and ($raw | Where-Object { $_ -like 'leftClick=*' })){ return $raw }
    Start-Sleep -Milliseconds 100
  }
  return @()
}
function Get-Count($rows,$key){ [int]((($rows | Where-Object { $_ -like "$key=*" }) -replace "$key=",'') -as [int]) }

$h=[IntPtr]::Zero
for($i=0;$i -lt 100 -and $h -eq [IntPtr]::Zero;$i++){ $h=[SI]::FindWindowW([IntPtr]::Zero,'连点器基础输入测试窗口'); Start-Sleep -Milliseconds 100 }
if($h -eq [IntPtr]::Zero){ throw '未找到测试窗口：请先运行 tools/test-window.ps1' }
# 把测试窗口停到左上角，避免被其它置顶窗口挡住点击点（只移动本脚本自己创建的窗口）。
[void][SI]::SetWindowPos($h,[IntPtr](-1),0,0,0,0,0x0001 -bor 0x0040)
Start-Sleep -Milliseconds 400
$rc=New-Object SI+RC; [void][SI]::GetClientRect($h,[ref]$rc)
$o=[SI]::ToScreen($h,0,0)
$padX=$o.X+[int]($rc.R/2); $padY=$o.Y+112+[int](($rc.B-112-94)/2)
$txtX=$o.X+[int]($rc.R/2); $txtY=$o.Y+$rc.B-94+20+37

$base=Read-Counts
$baseLeft=Get-Count $base 'leftClick'; $baseWheel=Get-Count $base 'wheel'; $baseKey=Get-Count $base 'keyDown'

# 真实 Enter / Tab：接着输入 A<Enter>B<Tab>C（字母用 Unicode 输入，Enter/Tab 用真实 VK），
# 检查换行与制表符各恰好一个。先做这一步，避免其它按键干扰输入法状态。
[SI]::Click($txtX,$txtY); Start-Sleep -Milliseconds 150
[SI]::Text('A'); Start-Sleep -Milliseconds 150
[SI]::Key(0x0D); Start-Sleep -Milliseconds 250                   # Enter
[SI]::Text('B'); Start-Sleep -Milliseconds 150
[SI]::Key(0x09); Start-Sleep -Milliseconds 250                   # Tab
[SI]::Text('C'); Start-Sleep -Milliseconds 400
# 夹具把捕获文本写成可读形式：每个换行 -> <CR>，每个制表符 -> <TAB>。
$text2=[IO.File]::ReadAllText($textPath)
$enterOnce = $text2.Contains('A<CR>B') -and -not $text2.Contains('<CR><CR>')
$tabOnce   = $text2.Contains('B<TAB>C') -and -not $text2.Contains('<TAB><TAB>')
$vis = $text2

[SI]::Click($padX,$padY); Start-Sleep -Milliseconds 200
[SI]::Wheel($padX,$padY,120); Start-Sleep -Milliseconds 200
[SI]::Click($txtX,$txtY); Start-Sleep -Milliseconds 250          # 聚焦文本接收框
[SI]::Text('Aa1中文'); Start-Sleep -Milliseconds 200
[SI]::Key(0x42)                                                  # 真实键盘 'B'
Start-Sleep -Milliseconds 500

$after=Read-Counts
$dLeft=(Get-Count $after 'leftClick')-$baseLeft
$dWheel=(Get-Count $after 'wheel')-$baseWheel
$dKey=(Get-Count $after 'keyDown')-$baseKey
$text=[IO.File]::ReadAllText($textPath)
$idx=0; foreach($ch in 'Aa1中文'.ToCharArray()){ $idx=$text.IndexOf($ch,$idx); if($idx -lt 0){ break }; $idx++ }
$marker=($idx -ge 0)

$ok = ($dLeft -ge 1) -and ($dWheel -ge 1) -and ($dKey -ge 1) -and $marker -and $enterOnce -and $tabOnce
Write-Host "delta leftClick=+$dLeft wheel=+$dWheel keyDown=+$dKey textMarker=$marker"
Write-Host "real Enter/Tab: enterOnce=$enterOnce tabOnce=$tabOnce  captured tail=[$vis]"
Write-Host "==== PROBE RESULT: $(if($ok){'PASS'}else{'FAIL'}) ===="
[void][SI]::PostMessageW($h,0x0010,[IntPtr]::Zero,[IntPtr]::Zero)
if(-not $ok){ exit 1 }
