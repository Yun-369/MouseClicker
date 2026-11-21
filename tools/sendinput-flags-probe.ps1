Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Probe2 {
  [StructLayout(LayoutKind.Sequential)] public struct MSLL { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int DX, DY; public uint MouseData, Flags, Time; public IntPtr Extra; }
  [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT Mouse; }
  [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint Type; public INPUTUNION U; }
  public delegate IntPtr Proc(int code, IntPtr wp, IntPtr lp);
  public static System.Collections.Generic.List<string> Log = new();
  public static IntPtr hook;
  public static IntPtr Callback(int code, IntPtr wp, IntPtr lp) {
    if (code >= 0 && wp == (IntPtr)0x0201) {
      var s = (MSLL)Marshal.PtrToStructure(lp, typeof(MSLL));
      Log.Add(string.Format("down flags=0x{0:X} extra=0x{1:X}", s.Flags, s.Extra.ToInt64()));
    }
    return CallNextHookEx(hook, code, wp, lp);
  }
  [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWindowsHookEx(int id, Proc p, IntPtr mod, uint tid);
  [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int c, IntPtr wp, IntPtr lp);
  [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string n);
  [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint n, INPUT[] inputs, int size);
  public static void Install() { hook = SetWindowsHookEx(14, Callback, GetModuleHandle(null), 0); }
  public static void Inject() {
    var down = new INPUT { Type = 0, U = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = 0x0002 } } };
    SendInput(1, new[]{ down }, Marshal.SizeOf<INPUT>());
    System.Threading.Thread.Sleep(60);
    var up = new INPUT { Type = 0, U = new INPUTUNION { Mouse = new MOUSEINPUT { Flags = 0x0004 } } };
    SendInput(1, new[]{ up }, Marshal.SizeOf<INPUT>());
  }
}
'@
[Probe2]::Install()
Start-Sleep -Milliseconds 300
[Probe2]::Inject()
Start-Sleep -Milliseconds 400
[Probe2]::Inject()
Start-Sleep -Milliseconds 400
foreach ($line in [Probe2]::Log) { Write-Output $line }
