using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClicker.Actions;
using MouseClicker.Domain;

namespace MouseClicker.Platform;

public sealed class WindowsInputDriver : IInputDriver
{
    public Point? CursorPosition => GetCursorPos(out var p) ? new Point(p.X,p.Y) : null;
    [StructLayout(LayoutKind.Sequential)] private struct CursorPoint { public int X,Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out CursorPoint point);
    private const uint InputMouse = 0, InputKeyboard = 1;
    private const uint MouseMove = 0x0001, MouseAbsolute = 0x8000, MouseVirtualDesk = 0x4000, MouseWheel = 0x0800;
    private const uint KeyfKeyUp = 0x0002;
    private const uint KeyfExtended = 0x0001;
    private const uint KeyfUnicode = 0x0004;
    private const uint MapvkVkToVsc = 0;

    private readonly HashSet<ClickButton> heldButtons = [];
    // Track (vk, extended) separately: numpad Enter and the main Enter share VK
    // 0x0D and differ only by the extended flag, so vk alone is not a unique key.
    private readonly HashSet<(ushort vk, bool extended)> heldKeys = [];
    private readonly object sendGate = new();

    public void MoveTo(int x, int y)
        => Send(CreateMove(x, y));

    private static Input CreateMove(int x, int y)
    {
        var bounds = SystemInformation.VirtualScreen;
        if (!bounds.Contains(x, y)) throw new ArgumentException("固定坐标不在当前虚拟桌面范围内。");
        return CreateMouse(new MouseInput
        {
            Dx = (int)Math.Round((x - bounds.Left) * 65535d / Math.Max(1, bounds.Width - 1)),
            Dy = (int)Math.Round((y - bounds.Top) * 65535d / Math.Max(1, bounds.Height - 1)),
            Flags = MouseMove | MouseAbsolute | MouseVirtualDesk
        });
    }

    public void ButtonDown(ClickButton button)
    {
        SendMouse(new MouseInput { Flags = ButtonFlags(button, true) });
        lock (sendGate) heldButtons.Add(button);
    }

    public void ButtonUp(ClickButton button)
    {
        SendMouse(new MouseInput { Flags = ButtonFlags(button, false) });
        lock (sendGate) heldButtons.Remove(button);
    }

    public void Click(ClickButton button, Point? position, CancellationToken cancellationToken = default)
    {
        var down = CreateMouse(new MouseInput { Flags = ButtonFlags(button, true) });
        var up = CreateMouse(new MouseInput { Flags = ButtonFlags(button, false) });
        Input[] inputs = position is { } point ? [CreateMove(point.X, point.Y), down, up] : [down, up];

        cancellationToken.ThrowIfCancellationRequested();
        uint sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent == inputs.Length) return;

        int error = Marshal.GetLastWin32Error();
        // A partial batch may have sent Down without Up. ReleaseAll will retry it.
        if (sent == inputs.Length - 1)
            lock (sendGate) heldButtons.Add(button);
        throw InputError(error);
    }

    public void KeyDown(ushort vk, bool extended = false, ushort scan = 0)
    {
        extended |= LegacyExtended(vk, extended, scan);
        SendKeyboard(vk, false, extended, scan);
        lock (sendGate) heldKeys.Add((vk, extended));
    }

    public void KeyUp(ushort vk, bool extended = false, ushort scan = 0)
    {
        // Match whichever down is held: prefer the exact tuple, then either flag.
        bool useExt = extended;
        lock (sendGate)
        {
            if (!heldKeys.Contains((vk, extended)))
                useExt = heldKeys.Any(k => k.vk == vk && k.extended) ? true : extended;
        }
        SendKeyboard(vk, true, useExt, scan);
        lock (sendGate) heldKeys.Remove((vk, useExt));
    }

    public void Wheel(int delta, int x, int y)
    {
        // Position over the target first, then notch the wheel.
        MoveTo(x, y);
        SendMouse(new MouseInput { MouseData = (uint)delta, Flags = MouseWheel });
    }

    // One UTF-16 code unit via KEYEVENTF_UNICODE (Vk=0, Scan=codeUnit). Layout/IME
    // independent; always paired by the caller and therefore not tracked as held.
    public void UnicodeKey(ushort codeUnit, bool down)
    {
        Send(new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyInput
                {
                    Vk = 0, Scan = codeUnit,
                    Flags = KeyfUnicode | (down ? 0u : KeyfKeyUp),
                    Time = 0, ExtraInfo = InputInjection.Signature
                }
            }
        });
    }

    public void ReleaseAll()
    {
        List<Exception> errors = [];
        ClickButton[] buttons;
        (ushort vk, bool extended)[] keys;
        lock (sendGate)
        {
            buttons = [.. heldButtons];
            keys = [.. heldKeys];
        }
        foreach (var button in buttons)
        {
            try { ButtonUp(button); } catch (Exception ex) { errors.Add(ex); }
        }
        foreach (var key in keys)
        {
            try { KeyUp(key.vk, key.extended); } catch (Exception ex) { errors.Add(ex); }
        }
        if (errors.Count > 0) throw new AggregateException("未能释放模拟按键。", errors);
    }

    private static uint ButtonFlags(ClickButton button, bool down) => (button, down) switch
    {
        (ClickButton.Left, true) => 0x0002,
        (ClickButton.Left, false) => 0x0004,
        (ClickButton.Right, true) => 0x0008,
        (ClickButton.Right, false) => 0x0010,
        (ClickButton.Middle, true) => 0x0020,
        (ClickButton.Middle, false) => 0x0040,
        _ => throw new ArgumentOutOfRangeException(nameof(button))
    };

    private static void SendMouse(MouseInput mouse)
        => Send(CreateMouse(mouse));

    private static Input CreateMouse(MouseInput mouse)
    {
        mouse.ExtraInfo = InputInjection.Signature;
        return new Input { Type = InputMouse, Data = new InputUnion { Mouse = mouse } };
    }

    private static void SendKeyboard(ushort vk, bool up, bool extended, ushort scan)
    {
        // Prefer the captured scan code; legacy recordings have none, so derive it.
        if (scan == 0) scan = (ushort)MapVirtualKey(vk, MapvkVkToVsc);
        uint flags = (up ? KeyfKeyUp : 0u) | (extended ? KeyfExtended : 0u);
        Send(new Input
        {
            Type = InputKeyboard,
            Data = new InputUnion
            {
                Keyboard = new KeyInput { Vk = vk, Scan = scan, Flags = flags, Time = 0, ExtraInfo = InputInjection.Signature }
            }
        });
    }

    // Legacy v1 files carry no extended flag/scan. Infer it for keys whose VK is
    // unambiguously extended. Numpad Enter shares VK 0x0D with the main Enter and
    // cannot be inferred — that case requires a recording made by the new build.
    internal static bool LegacyExtended(ushort vk, bool extended, ushort scan)
    {
        if (extended || scan != 0) return extended;
        return vk is 0xA3 or 0xA5 or 0x5B or 0x5C or 0x2C or 0x5F or 0x90
            or (>= 0x21 and <= 0x28) or 0x2D or 0x2E;
    }

    private static void Send(Input input)
    {
        var inputs = new[] { input };
        if (SendInput(1, inputs, InputSize) != 1)
            throw InputError(Marshal.GetLastWin32Error());
    }

    private static Win32Exception InputError(int error)
        => new(error, "输入发送失败，请检查目标窗口权限是否高于本程序。");

    private static readonly int InputSize = Marshal.SizeOf<Input>();

    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public InputUnion Data; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyInput Keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx, Dy;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyInput
    {
        public ushort Vk, Scan;
        public uint Flags, Time;
        public nuint ExtraInfo;
        // INPUT for keyboard has no padding on x64 (union matches mouse size).
    }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
