using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClicker.Domain;

namespace MouseClicker.Platform;

// Low-level keyboard hook for the hold-to-run trigger. The callback fires on
// the installing thread (the UI message loop) and must stay lightweight.
//
// Combo semantics:
//   * On the primary key DOWN the required Ctrl/Alt must currently be held;
//     that edge fires Pressed once and latches the primary key.
//   * After latching, releasing the PRIMARY key always fires Released, even if
//     the modifier was let go first (so F8-up after Ctrl-up still stops).
//   * Releasing a REQUIRED modifier while the primary is still held also fires
//     Released ("combo no longer held = stop").
//   * Key auto-repeat never re-fires Pressed.
// The required key/modifiers are an applied snapshot passed in explicitly; the
// hook never reads live UI control values. Injected events carrying our own
// signature are ignored so keyboard playback cannot self-trigger start/stop;
// other injectors are still accepted on purpose.
public sealed class KeyboardHook : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const uint WmKeydown = 0x0100, WmSyskeydown = 0x0104;
    private const uint WmKeyup = 0x0101, WmSyskeyup = 0x0105;
    private const int VkControl = 0x11, VkLControl = 0xA2, VkRControl = 0xA3;
    private const int VkMenu = 0x12, VkLMenu = 0xA4, VkRMenu = 0xA5;
    private const int VkShift = 0x10, VkLShift = 0xA0, VkRShift = 0xA1;
    private const int VkLWin = 0x5B, VkRWin = 0x5C;

    public uint PrimaryVk { get; set; } = 0x77; // F8
    public bool RequireCtrl { get; set; }
    public bool RequireAlt { get; set; }
    public bool RequireShift { get; set; }
    public Action? Pressed;
    public Action? Released;
    public Func<bool>? CanPressSideButton { get; init; }

    private nint hook;
    private MouseSideButtonHook? mouseHook;
    private bool latched;
    private bool primaryDown;

    // Keep the delegate alive for the whole install→uninstall window. A local
    // passed straight to SetWindowsHookEx can be GC'd and silently kill the hook.
    private LowLevelProc? callback;

    public void Install()
    {
        if (hook != 0) return;
        callback = Callback;
        hook = SetWindowsHookEx(WhKeyboardLl, callback, GetModuleHandle(null), 0);
        if (hook == 0)
        {
            callback = null;
            throw new Win32Exception(Marshal.GetLastWin32Error(), "键盘钩子安装失败，长按启动不可用。");
        }
        if (ClickSettings.IsMouseSideButton((int)PrimaryVk))
        {
            try { mouseHook = new MouseSideButtonHook(HandleSideButton); }
            catch { Dispose(); throw; }
        }
    }

    private bool HandleSideButton(int vk, bool down)
    {
        if (vk != PrimaryVk) return false;
        if (!down)
        {
            if (!primaryDown) return false;
            Handle(WmKeyup, (uint)vk);
            return true;
        }
        if (primaryDown) return true;
        if (!ModifiersSatisfied() || CanPressSideButton?.Invoke() == false) return false;
        Handle(WmKeydown, (uint)vk);
        return true;
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var info = Marshal.PtrToStructure<KbdStruct>(lParam);
            // Never react to what this program injects itself.
            if (info.ExtraInfo != InputInjection.Signature)
                Handle((uint)wParam, info.VkCode);
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    private void Handle(uint msg, uint vk)
    {
        bool down = msg is WmKeydown or WmSyskeydown;
        bool up = msg is WmKeyup or WmSyskeyup;

        if (vk == PrimaryVk && up)
        {
            primaryDown = false;
            Release();
            return;
        }
        if (latched && up && ((RequireCtrl && IsControl(vk) && !OtherSideDown(vk, VkLControl, VkRControl))
            || (RequireAlt && IsAlt(vk) && !OtherSideDown(vk, VkLMenu, VkRMenu))
            || (RequireShift && IsShift(vk) && !OtherSideDown(vk, VkLShift, VkRShift)))) Release();
        if (latched && down && ((!RequireCtrl && IsControl(vk)) || (!RequireAlt && IsAlt(vk)) || (!RequireShift && IsShift(vk)) || vk is VkLWin or VkRWin)) Release();
        if (down && vk == PrimaryVk)
        {
            if (primaryDown) return;
            primaryDown = true;
            if (!latched && ModifiersSatisfied()) { latched = true; Pressed?.Invoke(); }
        }
    }

    private void Release()
    {
        if (!latched) return;
        latched = false;
        Released?.Invoke();
    }

    private bool ModifiersSatisfied()
        => RequireCtrl == IsAsyncDown(VkControl) && RequireAlt == IsAsyncDown(VkMenu)
           && RequireShift == IsAsyncDown(VkShift) && !IsAsyncDown(VkLWin) && !IsAsyncDown(VkRWin);

    private static bool IsControl(uint vk) => vk is VkControl or VkLControl or VkRControl;
    private static bool IsAlt(uint vk) => vk is VkMenu or VkLMenu or VkRMenu;
    private static bool IsShift(uint vk) => vk is VkShift or VkLShift or VkRShift;
    private static bool OtherSideDown(uint released, int left, int right)
        => released == left ? IsAsyncDown(right) : released == right && IsAsyncDown(left);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vk);
    private static bool IsAsyncDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public void Dispose()
    {
        mouseHook?.Dispose();
        mouseHook = null;
        latched = false;
        primaryDown = false;
        if (hook != 0) UnhookWindowsHookEx(hook);
        hook = 0;
        callback = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdStruct { public uint VkCode, ScanCode, Flags, Time; public nuint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, LowLevelProc proc, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);

    private delegate nint LowLevelProc(int code, nint wParam, nint lParam);
}
