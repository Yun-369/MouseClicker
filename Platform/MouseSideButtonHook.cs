using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClicker.Domain;

namespace MouseClicker.Platform;

internal sealed class MouseSideButtonHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint XButtonDown = 0x020B, XButtonUp = 0x020C;
    private readonly HookProc callback;
    private nint hook;

    // A true return consumes this edge instead of forwarding it to the active app.
    public MouseSideButtonHook(Func<int, bool, bool> handleButton)
    {
        callback = (code, message, data) =>
        {
            uint msg = (uint)message;
            if (code >= 0 && msg is XButtonDown or XButtonUp)
            {
                var input = Marshal.PtrToStructure<MouseInput>(data);
                int vk = (input.MouseData >> 16) switch
                {
                    1 => ClickSettings.MouseSide1,
                    2 => ClickSettings.MouseSide2,
                    _ => 0
                };
                if (vk != 0 && input.ExtraInfo != InputInjection.Signature && handleButton(vk, msg == XButtonDown))
                    return 1;
            }
            return CallNextHookEx(hook, code, message, data);
        };
        hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
        if (hook == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "鼠标侧键钩子安装失败。");
    }

    public void Dispose()
    {
        if (hook == 0) return;
        UnhookWindowsHookEx(hook);
        hook = 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }

    private delegate nint HookProc(int code, nint message, nint data);
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
}
