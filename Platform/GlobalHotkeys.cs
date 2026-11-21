using System.ComponentModel;
using System.Runtime.InteropServices;

namespace MouseClicker.Platform;

// Global hotkeys via RegisterHotKey. The emergency-stop hotkey and the normal
// start/stop hotkey are managed independently: a conflict on the normal hotkey
// must never unregister an already-working emergency stop (that used to leave a
// running task with no way to stop it).
public sealed class GlobalHotkeys(nint handle) : IDisposable
{
    public const int MessageId = 0x0312;
    public const int ToggleId = 1;
    public const int EmergencyId = 2;
    private const int AlternateToggleId = 3;

    private const uint ModAlt = 0x0001, ModControl = 0x0002, ModShift = 0x0004, ModNoRepeat = 0x4000;
    private const int VkF12 = 0x7B;

    private readonly HashSet<int> registered = [];
    private int currentToggleId = ToggleId;
    private int currentToggleVk;
    private uint currentToggleModifiers;

    public bool IsCurrentToggleId(int id) => ToggleRegistered && id == currentToggleId;

    public bool EmergencyRegistered { get; private set; }
    public bool ToggleRegistered { get; private set; }
    public string? LastError { get; private set; }

    // Idempotently (re)register the fixed emergency stop Ctrl+Shift+F12.
    // Failure here is real (no safety net) and is surfaced to the caller.
    public void EnsureEmergency()
    {
        if (EmergencyRegistered) return;
        Add(EmergencyId, ModControl | ModShift, VkF12);
        EmergencyRegistered = true;
        LastError = null;
    }

    // Register the replacement first. A conflict leaves the old registration
    // and the independent emergency hotkey in place.
    public bool ReplaceToggle(int toggleVk, uint toggleModifiers, out string? error)
    {
        if (ToggleRegistered && toggleVk == currentToggleVk && toggleModifiers == currentToggleModifiers)
        {
            error = null;
            LastError = null;
            return true;
        }
        int candidateId = ToggleRegistered && currentToggleId == ToggleId ? AlternateToggleId : ToggleId;
        try
        {
            Add(candidateId, toggleModifiers, (uint)toggleVk);
            if (ToggleRegistered) Remove(currentToggleId);
            currentToggleId = candidateId;
            currentToggleVk = toggleVk;
            currentToggleModifiers = toggleModifiers;
            ToggleRegistered = true;
            error = null;
            LastError = null;
            return true;
        }
        catch (Win32Exception ex)
        {
            error = ex.Message;
            LastError = ex.Message;
            return false;
        }
    }

    // Hold-to-run mode does not use a registered toggle hotkey.
    public void RemoveToggle()
    {
        Remove(currentToggleId);
        ToggleRegistered = false;
    }

    public bool RegisterRecording(int id,int vk,out string? error)
    {
        if(id<1000)throw new ArgumentOutOfRangeException(nameof(id));
        try{Add(id,ModControl|ModAlt,(uint)vk);error=null;return true;}catch(Win32Exception ex){error=ex.Message;return false;}
    }
    public void RemoveRecordings(){foreach(int id in registered.Where(id=>id>=1000).ToArray())Remove(id);}

    private void Add(int id, uint modifiers, uint vk)
    {
        if (!RegisterHotKey(handle, id, modifiers | ModNoRepeat, vk))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"热键 {Describe(modifiers, vk)} 注册失败，可能已被其他程序占用。");
        registered.Add(id);
    }

    private void Remove(int id)
    {
        if (registered.Remove(id)) UnregisterHotKey(handle, id);
    }

    private static string Describe(uint modifiers, uint vk)
    {
        var parts = new List<string>();
        if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
        if ((modifiers & ModAlt) != 0) parts.Add("Alt");
        if ((modifiers & ModShift) != 0) parts.Add("Shift");
        parts.Add(vk is >= 0x70 and <= 0x7B ? $"F{vk - 0x70 + 1}" : $"0x{vk:X2}");
        return string.Join("+", parts);
    }

    public void Dispose()
    {
        foreach (var id in registered) UnregisterHotKey(handle, id);
        registered.Clear();
        EmergencyRegistered = ToggleRegistered = false;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(nint hwnd, int id);
}
