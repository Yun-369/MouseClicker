using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClicker.Actions;
using MouseClicker.Domain;

namespace MouseClicker.Platform;

// Window messages are accepted by ordinary desktop windows; applications that
// read raw input or DirectInput may not respond to these messages.
public sealed class BackgroundInputDriver : IInputDriver
{
    public sealed record TargetIdentity(nint Handle, uint ProcessId, uint ThreadId, long ProcessStartTicks,
        nint FocusHandle, Point ClientAnchor);
    private readonly nint root;
    private readonly uint processId, threadId;
    private readonly long processStartTicks;
    private readonly bool clientCoordinates;
    private readonly Dictionary<ClickButton, nint> buttons = [];
    private readonly Dictionary<(ushort Vk, bool Extended), (nint Target, ushort Scan)> keys = [];
    private Point point;
    private nint lastTarget;
    private readonly nint initialFocus;
    private readonly Point clientAnchor;
    public TargetIdentity Identity => new(root, processId, threadId, processStartTicks, initialFocus, clientAnchor);

    public BackgroundInputDriver(nint window, bool clientCoordinates = false, Point? initialScreenPoint = null, TargetIdentity? expected = null)
    {
        root = GetAncestor(window, 2);
        if (root == 0 || !IsWindow(root) || !IsWindowVisible(root))
            throw new InvalidOperationException("所选窗口已关闭或不可见。");
        threadId = GetWindowThreadProcessId(root, out processId);
        if (threadId == 0 || processId == (uint)Environment.ProcessId)
            throw new InvalidOperationException("请选择其他应用的窗口。");
        using (var process = System.Diagnostics.Process.GetProcessById((int)processId))
            processStartTicks = process.StartTime.ToUniversalTime().Ticks;
        if (expected is not null && (root != expected.Handle || processId != expected.ProcessId ||
            threadId != expected.ThreadId || processStartTicks != expected.ProcessStartTicks))
            throw new InvalidOperationException("后台目标窗口身份已变化，请重新选择。");
        this.clientCoordinates = clientCoordinates;
        var gui = new GuiThreadInfo { Size = (uint)Marshal.SizeOf<GuiThreadInfo>() };
        nint focus = expected?.FocusHandle ?? (GetGUIThreadInfo(threadId, ref gui) ? gui.Focus : 0);
        if (focus == 0) focus = root;
        if (!IsWindow(focus) || (focus != root && !IsChild(root, focus)))
            throw new InvalidOperationException("后台目标的输入控件已变化，请重新选择窗口。");
        initialFocus = lastTarget = focus;
        if (initialScreenPoint is null && expected is not null)
        {
            clientAnchor = point = expected.ClientAnchor;
            return;
        }
        var cursor = initialScreenPoint is { } initial
            ? new NativePoint { X = initial.X, Y = initial.Y }
            : GetCursorPos(out var current) ? current : throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!ScreenToClient(root, ref cursor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法获取目标窗口位置。");
        clientAnchor = point = new Point(cursor.X, cursor.Y);
    }

    public Point? CursorPosition => point;
    public static nint ForegroundWindow => GetForegroundWindow();
    public void ValidatePoint(int x, int y)
    {
        var original = point;
        try
        {
            var p = new NativePoint { X = x, Y = y };
            if (!clientCoordinates && !ScreenToClient(root, ref p))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "无法换算屏幕坐标。");
            point = new Point(p.X, p.Y);
            _ = TargetAtPoint(out _);
        }
        finally { point = original; }
    }

    private void ValidateTarget()
    {
        if (!IsWindow(root) || GetWindowThreadProcessId(root, out uint pid) != threadId || pid != processId)
            throw new InvalidOperationException("后台目标窗口已关闭或发生变化。");
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)processId);
            if (process.StartTime.ToUniversalTime().Ticks != processStartTicks)
                throw new InvalidOperationException("后台目标进程已变化。");
        }
        catch (ArgumentException) { throw new InvalidOperationException("后台目标进程已退出。"); }
    }

    private nint TargetAtPoint(out Point local)
    {
        ValidateTarget();
        if (!GetClientRect(root, out var rect) || point.X < 0 || point.Y < 0 ||
            point.X >= rect.Right || point.Y >= rect.Bottom)
            throw new InvalidOperationException("点击位置不在目标窗口客户区内。");
        nint child = ChildWindowFromPointEx(root, new NativePoint { X = point.X, Y = point.Y }, 7);
        if (child == 0) child = root;
        var native = new NativePoint { X = point.X, Y = point.Y };
        if (child != root && (!ClientToScreen(root, ref native) || !ScreenToClient(child, ref native)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法换算目标坐标。");
        local = new Point(native.X, native.Y);
        return child;
    }

    private void ValidateReceiver(nint target)
    {
        if (!IsWindow(target) || (target != root && !IsChild(root, target)) || GetWindowThreadProcessId(target, out uint pid) == 0 || pid != processId)
            throw new InvalidOperationException("输入接收控件已关闭或改变。");
    }

    private Point PointFor(nint target)
    {
        ValidateTarget();
        ValidateReceiver(target);
        var p = new NativePoint { X = point.X, Y = point.Y };
        if (target != root && (!ClientToScreen(root, ref p) || !ScreenToClient(target, ref p)))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法换算输入接收控件坐标。");
        return new Point(p.X, p.Y);
    }

    private static nint Position(Point p) => (nint)(uint)((ushort)p.X | ((uint)(ushort)p.Y << 16));
    private static void Post(nint hwnd, uint message, nuint wParam, nint lParam)
    {
        if (SendMessageTimeout(hwnd, message, wParam, lParam, 0x0003, 500, out _) == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "后台输入发送失败。");
    }

    private void PostKey(nint target, uint message, nuint wParam, nint lParam)
    {
        ValidateReceiver(target);
        Post(target, message, wParam, lParam);
    }

    public void MoveTo(int x, int y)
    {
        ValidateTarget();
        var p = new NativePoint { X = x, Y = y };
        if (!clientCoordinates && !ScreenToClient(root, ref p))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "无法换算屏幕坐标。");
        point = new Point(p.X, p.Y);
        var target = buttons.Count != 0 ? buttons.Values.First() : TargetAtPoint(out _);
        var local = PointFor(target);
        if (buttons.Count == 0) lastTarget = target;
        Post(target, 0x0200, MouseFlags(), Position(local));
    }

    private nuint MouseFlags()
    {
        uint value = 0;
        if (buttons.ContainsKey(ClickButton.Left)) value |= 1;
        if (buttons.ContainsKey(ClickButton.Right)) value |= 2;
        if (buttons.ContainsKey(ClickButton.Middle)) value |= 0x10;
        if (keys.Keys.Any(k => k.Vk is 0x10 or 0xA0 or 0xA1)) value |= 4;
        if (keys.Keys.Any(k => k.Vk is 0x11 or 0xA2 or 0xA3)) value |= 8;
        return value;
    }

    public void ButtonDown(ClickButton button)
    {
        lastTarget = TargetAtPoint(out var local);
        uint msg = button switch { ClickButton.Left => 0x0201, ClickButton.Right => 0x0204, _ => 0x0207 };
        buttons.Add(button, lastTarget);
        try { Post(lastTarget, msg, MouseFlags(), Position(local)); }
        catch { buttons.Remove(button); throw; }
    }

    public void ButtonUp(ClickButton button)
    {
        var target = buttons.TryGetValue(button, out var held) ? held : TargetAtPoint(out _);
        var local = PointFor(target);
        uint msg = button switch { ClickButton.Left => 0x0202, ClickButton.Right => 0x0205, _ => 0x0208 };
        buttons.Remove(button);
        try { Post(target, msg, MouseFlags(), Position(local)); }
        catch { if (held != 0) buttons[button] = held; throw; }
    }

    public void KeyDown(ushort vk, bool extended = false, ushort scan = 0)
    {
        ValidateTarget();
        var keyId = (vk, extended);
        // 鼠标移动可以改变下一次按键的目标，已按住的键仍归首次接收的控件。
        bool repeat = keys.TryGetValue(keyId, out var held);
        var target = repeat ? held.Target : lastTarget;
        if (scan == 0) scan = repeat ? held.Scan : (ushort)MapVirtualKey(vk, 0);
        uint flags = 1u | ((uint)scan << 16) | (extended ? 0x01000000u : 0) | (repeat ? 0x40000000u : 0);
        PostKey(target, 0x0100, vk, (nint)flags);
        if (!repeat) keys.Add(keyId, (target, scan));
        if (keys.Keys.Any(k => k.Vk is 0x11 or 0x12 or 0xA2 or 0xA3 or 0xA4 or 0xA5)) return;
        if (vk is 0x08 or 0x09 or 0x0D)
        {
            PostKey(target, 0x0102, vk, (nint)1);
            return;
        }
        var state = new byte[256];
        foreach (var key in keys.Keys) state[key.Vk] = 0x80;
        if (keys.Keys.Any(k => k.Vk is 0xA0 or 0xA1)) state[0x10] = 0x80;
        if ((GetKeyState(0x14) & 1) != 0) state[0x14] = 1;
        var chars = new System.Text.StringBuilder(8);
        int count = ToUnicodeEx(vk, scan, state, chars, chars.Capacity, 4, GetKeyboardLayout(threadId));
        for (int i = 0; i < count && i < chars.Length; i++)
            PostKey(target, 0x0102, chars[i], (nint)1);
    }

    public void KeyUp(ushort vk, bool extended = false, ushort scan = 0)
    {
        ValidateTarget();
        var keyId = (vk, extended);
        bool wasHeld = keys.TryGetValue(keyId, out var held);
        var target = wasHeld ? held.Target : lastTarget;
        if (scan == 0) scan = wasHeld ? held.Scan : (ushort)MapVirtualKey(vk, 0);
        PostKey(target, 0x0101, vk, (nint)(0xC0000001u | ((uint)scan << 16) | (extended ? 0x01000000u : 0)));
        keys.Remove(keyId);
    }

    public void UnicodeKey(ushort codeUnit, bool down)
    {
        ValidateTarget();
        if (down) PostKey(lastTarget, 0x0102, codeUnit, (nint)1);
    }

    public void Wheel(int delta, int x, int y)
    {
        MoveTo(x, y);
        var p = new NativePoint { X = point.X, Y = point.Y };
        if (!ClientToScreen(root, ref p)) throw new Win32Exception(Marshal.GetLastWin32Error());
        Post(lastTarget, 0x020A, MouseFlags() | ((nuint)(ushort)delta << 16), Position(new Point(p.X, p.Y)));
    }

    public void ReleaseAll()
    {
        var errors = new List<Exception>();
        foreach (var b in buttons.Keys.ToArray()) try { ButtonUp(b); } catch (Exception ex) { errors.Add(ex); }
        foreach (var k in keys.Keys.ToArray()) try { KeyUp(k.Vk, k.Extended); } catch (Exception ex) { errors.Add(ex); }
        if (errors.Count != 0) throw new AggregateException("后台按键释放失败。", errors);
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct GuiThreadInfo
    {
        public uint Size, Flags;
        public nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        public Rect CaretRect;
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint hwnd, uint flags);
    [DllImport("user32.dll")] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] private static extern bool IsChild(nint parent, nint child);
    [DllImport("user32.dll")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetClientRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ClientToScreen(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool ScreenToClient(nint hwnd, ref NativePoint point);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint ChildWindowFromPointEx(nint hwnd, NativePoint point, uint flags);
    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", SetLastError = true)] private static extern nint SendMessageTimeout(nint hwnd, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint mapType);
    [DllImport("user32.dll")] private static extern short GetKeyState(int vk);
    [DllImport("user32.dll")] private static extern nint GetKeyboardLayout(uint threadId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint vk, uint scan, byte[] state, System.Text.StringBuilder output, int size, uint flags, nint layout);
}
