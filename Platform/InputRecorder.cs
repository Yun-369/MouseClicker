using System.Diagnostics;
using System.Runtime.InteropServices;
using MouseClicker.Domain;
using MouseClicker.Storage;

namespace MouseClicker.Platform;

// Records physical mouse/keyboard input through WH_MOUSE_LL + WH_KEYBOARD_LL.
// Injected events (and events carrying our own SendInput signature) are filtered
// so playback and the click engine never record themselves.
//
// Self-input is also excluded: (1) mouse events whose screen point is over the
// controller window (e.g. the click used to press "停止录制") are dropped;
// (2) the control chord's primary key (start/stop trigger, emergency key) is in
// suppressedVks, and when it fires the Ctrl/Shift/Alt pressed alongside it are
// retroactively pulled back too — but ONLY for that chord. A genuine Ctrl+A in
// the target app is kept, because a normal non-modifier key "commits" pending
// modifiers. Hooks run on the UI thread, which must keep pumping messages.
public sealed class InputRecorder : IDisposable
{
    public readonly record struct ControlChord(ushort Vk, bool Ctrl, bool Alt, bool Shift);
    private const int WhMouseLl = 14, WhKeyboardLl = 13;

    private const uint WmMouseMove = 0x0200;
    private const uint WmLButtonDown = 0x0201, WmLButtonUp = 0x0202;
    private const uint WmRButtonDown = 0x0204, WmRButtonUp = 0x0205;
    private const uint WmMButtonDown = 0x0207, WmMButtonUp = 0x0208;
    private const uint WmMouseWheel = 0x020A;
    private const uint WmKeyDown = 0x0100, WmKeyUp = 0x0101, WmSysKeyDown = 0x0104, WmSysKeyUp = 0x0105;

    private const uint LlmhfInjected = 0x01;   // mouse injected flag
    private const uint LlkhfInjected = 0x10;   // keyboard injected flag
    private const uint LlkhfExtended = 0x01;   // keyboard extended-key flag
    private const uint GaRoot = 2;

    // Generic modifier virtual-key codes and their left/right variants.
    private const ushort VkShift = 0x10, VkControl = 0x11, VkMenu = 0x12;
    private const ushort VkLWin = 0x5B, VkRWin = 0x5C;

    private readonly IReadOnlySet<ushort> suppressed;
    private readonly IReadOnlyList<ControlChord> controlChords;
    private readonly HashSet<ushort> swallowedPrimary = [];
    private readonly int dpi;
    private readonly Rectangle virtualScreen;
    private readonly nint ownerWindow;
    // Test seam: when provided it decides whether a screen point belongs to the
    // controller window; production leaves it null and uses the Win32 hit test.
    private readonly Func<int, int, bool>? ownerHitTest;

    private readonly object gate = new();
    private readonly List<RecordedEvent> events = [];
    // Modifier downs recorded since the last committing event; removed if the next
    // primary key turns out to be a control chord.
    private readonly List<RecordedEvent> pendingModEvents = [];
    private readonly HashSet<ushort> heldMods = [];        // generic mods currently down
    private readonly HashSet<ushort> swallowModUp = [];    // generic mods whose up we drop
    private readonly Stopwatch clock = new();
    private int lastMs;

    private nint mouseHook, keyHook;
    private LowLevelMouseProc? mouseCallback;
    private LowLevelKeyProc? keyCallback;

    public int Count { get { lock (gate) return events.Count; } }
    // Fires (on the UI thread) when the event cap is reached; the UI stops recording.
    public event Action? LimitReached;

    // ownerWindow is the controller top-level window handle; mouse input over it
    // is treated as self-operation and not recorded. Pass 0 to disable. ownerHitTest
    // is a test-only seam that replaces the Win32 hit test.
    public InputRecorder(IReadOnlySet<ushort> suppressedVks, int dpi, Rectangle virtualScreen, nint ownerWindow,
        Func<int, int, bool>? ownerHitTest = null)
    {
        suppressed = suppressedVks;
        controlChords = [];
        this.dpi = dpi;
        this.virtualScreen = virtualScreen;
        this.ownerWindow = ownerWindow;
        this.ownerHitTest = ownerHitTest;
    }

    public InputRecorder(IReadOnlyList<ControlChord> chords, int dpi, Rectangle virtualScreen, nint ownerWindow,
        Func<int, int, bool>? ownerHitTest = null)
        : this(new HashSet<ushort>(), dpi, virtualScreen, ownerWindow, ownerHitTest)
    {
        controlChords = chords;
    }

    public void Start()
    {
        if (mouseHook != 0 || keyHook != 0) return;
        lock (gate)
        {
            events.Clear();
            pendingModEvents.Clear();
            heldMods.Clear();
            swallowModUp.Clear();
            swallowedPrimary.Clear();
            lastMs = 0;
        }
        clock.Restart();

        mouseCallback = MouseCallback;
        keyCallback = KeyCallback;
        mouseHook = SetWindowsHookEx(WhMouseLl, mouseCallback, GetModuleHandle(null), 0);
        keyHook = SetWindowsHookEx(WhKeyboardLl, keyCallback, GetModuleHandle(null), 0);
        if (mouseHook == 0 || keyHook == 0)
        {
            int err = Marshal.GetLastWin32Error();
            Dispose();
            throw new System.ComponentModel.Win32Exception(err, "录制钩子安装失败，无法开始录制。");
        }
    }

    public RecordingDocument Stop(string name)
    {
        clock.Stop();
        RecordedEvent[] copy;
        lock (gate) copy = [.. events];
        return new RecordingDocument
        {
            SchemaVersion = RecordingSchema.CurrentVersion,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            CoordinateSpace = "virtualDesktopPhysicalPixels",
            Dpi = dpi,
            VirtualLeft = virtualScreen.Left,
            VirtualTop = virtualScreen.Top,
            VirtualWidth = virtualScreen.Width,
            VirtualHeight = virtualScreen.Height,
            Events = [.. copy]
        };
    }

    private void Add(RecordedEvent e)
    {
        bool hitLimit = false;
        lock (gate)
        {
            if (events.Count >= RecordingSchema.MaxEvents) return;
            events.Add(e);
            hitLimit = events.Count == RecordingSchema.MaxEvents;
        }
        if (hitLimit) LimitReached?.Invoke();
    }

    private RecordedEvent Build(string type, int x = 0, int y = 0, string? button = null, int delta = 0,
        ushort vk = 0, ushort scan = 0, bool extended = false)
    {
        int now = (int)clock.ElapsedMilliseconds;
        int delay = Math.Max(0, now - lastMs);
        lastMs = now;
        return new RecordedEvent
        {
            Type = type, DelayMs = delay, TimeMs = now, X = x, Y = y, Button = button, Delta = delta,
            Vk = vk, Scan = scan, Extended = extended
        };
    }

    // A real (non-modifier) event proves the pending modifiers were genuine input
    // (e.g. Ctrl+A): keep them and stop tracking.
    private void CommitModifiers()
    {
        lock (gate) pendingModEvents.Clear();
    }

    // The control chord's primary key fired: pull back the modifiers pressed for it
    // and remember to swallow their matching key-up events.
    private void CancelPendingModifiers()
    {
        lock (gate)
        {
            if (pendingModEvents.Count == 0) return;
            events.RemoveAll(e =>
            {
                foreach (var p in pendingModEvents) if (ReferenceEquals(p, e)) return true;
                return false;
            });
            foreach (var p in pendingModEvents) swallowModUp.Add(p.Vk);
            pendingModEvents.Clear();
            // Removing events leaves gaps in DelayMs. Rebuild every retained event's
            // DelayMs from its ORIGINAL TimeMs so normal actions keep their exact
            // occurrence time (the removed chord's duration folds into the gap of the
            // next retained event instead of shifting later actions).
            RecomputeDelaysFromTimeLocked();
        }
    }

    // DelayMs[i] = TimeMs[i] - TimeMs[previous retained] (first event: from 0).
    private void RecomputeDelaysFromTimeLocked()
    {
        var rebuilt = RecordingTimeline.RebuildDelaysFromTime(events);
        events.Clear();
        events.AddRange(rebuilt);
    }

    private bool IsOverOwnerWindow(int x, int y)
    {
        if (ownerHitTest is not null) return ownerHitTest(x, y);
        if (ownerWindow == 0) return false;
        var hit = WindowFromPoint(new PointStruct { X = x, Y = y });
        if (hit == 0) return false;
        var root = GetAncestor(hit, GaRoot);
        if (root == 0) root = hit;
        return root == ownerWindow;
    }

    private nint MouseCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var m = Marshal.PtrToStructure<MouseHookStruct>(lParam);
            bool injected = (m.Flags & LlmhfInjected) != 0 || m.ExtraInfo == InputInjection.Signature;
            bool selfWindow = IsOverOwnerWindow(m.X, m.Y);
            if (!injected && !selfWindow)
            {
                uint msg = (uint)wParam;
                switch (msg)
                {
                    case WmMouseMove: Add(Build(RecordingEventType.MouseMove, m.X, m.Y)); break;
                    case WmLButtonDown: Add(Build(RecordingEventType.MouseDown, m.X, m.Y, "left")); break;
                    case WmLButtonUp: Add(Build(RecordingEventType.MouseUp, m.X, m.Y, "left")); break;
                    case WmRButtonDown: Add(Build(RecordingEventType.MouseDown, m.X, m.Y, "right")); break;
                    case WmRButtonUp: Add(Build(RecordingEventType.MouseUp, m.X, m.Y, "right")); break;
                    case WmMButtonDown: Add(Build(RecordingEventType.MouseDown, m.X, m.Y, "middle")); break;
                    case WmMButtonUp: Add(Build(RecordingEventType.MouseUp, m.X, m.Y, "middle")); break;
                    case WmMouseWheel:
                        short wheel = (short)((m.MouseData >> 16) & 0xFFFF);
                        Add(Build(RecordingEventType.MouseWheel, m.X, m.Y, delta: wheel));
                        break;
                }
                // Real target-app mouse input commits any held modifiers (Ctrl+click).
                CommitModifiers();
            }
        }
        return CallNextHookEx(mouseHook, code, wParam, lParam);
    }

    private nint KeyCallback(int code, nint wParam, nint lParam)
    {
        if (code >= 0)
        {
            var k = Marshal.PtrToStructure<KeyHookStruct>(lParam);
            bool injected = (k.Flags & LlkhfInjected) != 0 || k.ExtraInfo == InputInjection.Signature;
            uint msg = (uint)wParam;
            bool down = msg is WmKeyDown or WmSysKeyDown;
            bool up = msg is WmKeyUp or WmSysKeyUp;
            if (injected || !(down || up)) return CallNextHookEx(keyHook, code, wParam, lParam);

            ushort vk = (ushort)k.VkCode;
            ushort scan = (ushort)k.ScanCode;
            bool extended = (k.Flags & LlkhfExtended) != 0;
            bool matchesChord = down && controlChords.Any(c => c.Vk == vk
                && !heldMods.Contains(VkLWin) && !heldMods.Contains(VkRWin)
                && c.Ctrl == ModifierDown(VkControl) && c.Alt == ModifierDown(VkMenu) && c.Shift == ModifierDown(VkShift));
            if (suppressed.Contains(vk) || matchesChord || swallowedPrimary.Contains(vk))
            {
                if (down) { swallowedPrimary.Add(vk); CancelPendingModifiers(); }
                if (up) swallowedPrimary.Remove(vk);
                return CallNextHookEx(keyHook, code, wParam, lParam);
            }

            if (IsModifier(vk))
            {
                ushort generic = GenericMod(vk);
                if (down)
                {
                    lock (gate)
                    {
                        // Ignore auto-repeat of an already-held modifier.
                        if (!heldMods.Add(vk)) return CallNextHookEx(keyHook, code, wParam, lParam);
                    }
                    var ev = Build(RecordingEventType.KeyDown, vk: vk, scan: scan, extended: extended);
                    Add(ev);
                    lock (gate) pendingModEvents.Add(ev);
                }
                else
                {
                    bool swallow;
                    lock (gate)
                    {
                        swallow = swallowModUp.Remove(vk);
                        heldMods.Remove(vk);
                        pendingModEvents.RemoveAll(p => p.Vk == vk);
                    }
                    if (!swallow) Add(Build(RecordingEventType.KeyUp, vk: vk, scan: scan, extended: extended));
                }
            }
            else
            {
                // Genuine non-modifier key: its modifiers were real, keep them.
                CommitModifiers();
                Add(Build(down ? RecordingEventType.KeyDown : RecordingEventType.KeyUp,
                    vk: vk, scan: scan, extended: extended));
            }
        }
        return CallNextHookEx(keyHook, code, wParam, lParam);
    }

    private static bool IsModifier(ushort vk)
        => vk is VkShift or VkControl or VkMenu or VkLWin or VkRWin or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5;

    private bool ModifierDown(ushort generic) => heldMods.Any(vk => GenericMod(vk) == generic);

    private static ushort GenericMod(ushort vk) => vk switch
    {
        0xA0 or 0xA1 => VkShift,
        0xA2 or 0xA3 => VkControl,
        0xA4 or 0xA5 => VkMenu,
        _ => vk
    };

    public void Dispose()
    {
        if (mouseHook != 0) { UnhookWindowsHookEx(mouseHook); mouseHook = 0; }
        if (keyHook != 0) { UnhookWindowsHookEx(keyHook); keyHook = 0; }
        mouseCallback = null;
        keyCallback = null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointStruct { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseHookStruct { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyHookStruct { public uint VkCode, ScanCode, Flags, Time; public nuint ExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookEx(int idHook, Delegate proc, nint module, uint threadId);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]
    private static extern nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? name);
    [DllImport("user32.dll")]
    private static extern nint WindowFromPoint(PointStruct point);
    [DllImport("user32.dll")]
    private static extern nint GetAncestor(nint hwnd, uint flags);

    private delegate nint LowLevelMouseProc(int code, nint wParam, nint lParam);
    private delegate nint LowLevelKeyProc(int code, nint wParam, nint lParam);
}
