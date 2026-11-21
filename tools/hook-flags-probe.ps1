Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Probe {
  public struct MSLL { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }
  public delegate IntPtr Proc(int code, IntPtr wp, IntPtr lp);
  public static System.Collections.Generic.List<string> Log = new();
  public static IntPtr hook;
  public static IntPtr Callback(int code, IntPtr wp, IntPtr lp) {
    if (code >= 0 && wp == (IntPtr)0x0201) {
      var s = (MSLL)Marshal.PtrToStructure(lp, typeof(MSLL));
      Log.Add(string.Format("down at ({0},{1}) flags=0x{2:X} extra=0x{3:X}", s.X, s.Y, s.Flags, s.Extra.ToInt64()));
    }
    return CallNextHookEx(hook, code, wp, lp);
  }
  [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint tid);
  [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr wp, IntPtr lp);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string n);
  [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
  public static void Install() { hook = SetWindowsHookEx(14, Callback, GetModuleHandle(null), 0); }
  public static void Inject() {
    mouse_event(2,0,0,0,UIntPtr.Zero); System.Threading.Thread.Sleep(40);
    mouse_event(4,0,0,0,UIntPtr.Zero);
  }
}
'@
[Probe]::Install()
Start-Sleep -Milliseconds 300
[Probe]::Inject()
Start-Sleep -Milliseconds 500
[Probe]::Inject()
Start-Sleep -Milliseconds 500
Write-Output ("hook=0x{0:X}" -f [Probe]::hook.ToInt64())
foreach ($line in [Probe]::Log) { Write-Output $line }
