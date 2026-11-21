using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseClicker.Domain;
using MouseClicker.Platform;

namespace MouseClicker.UI;

internal sealed class HotkeyCaptureForm : Form
{
    private const string Instructions = "按下键盘组合或鼠标侧键 1 / 2。";
    private const string KeyHint = "字母、数字和空格需搭配 Ctrl 或 Alt。";
    private delegate nint HookProc(int code, nint message, nint data);
    private readonly HookProc callback;
    private readonly Label feedback;
    private readonly HashSet<int> rejectedKeys = [];
    private nint hook;
    private MouseSideButtonHook? mouseHook;
    private bool waitingForRelease;
    private bool lostFocus, cancelRequested, finishing;
    public int Vk { get; private set; }
    public bool Ctrl { get; private set; }
    public bool Alt { get; private set; }
    public bool Shift { get; private set; }

    public HotkeyCaptureForm()
    {
        Text = "修改启停快捷键";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 142);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        Font = new Font("Microsoft YaHei UI", 9.5f);
        BackColor = Color.FromArgb(247, 253, 248);
        ForeColor = Color.FromArgb(26, 52, 36);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16, 10, 16, 10),
            ColumnCount = 1, RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Text = Instructions }, 0, 0);
        feedback = new Label { Dock = DockStyle.Fill, Text = KeyHint, ForeColor = Color.FromArgb(66, 94, 76) };
        layout.Controls.Add(feedback, 0, 1);
        var cancel = new Button
        {
            Text = "取消（Esc）", Width = 104, Anchor = AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom,
            FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(215, 237, 221), UseVisualStyleBackColor = false
        };
        cancel.FlatAppearance.BorderColor = Color.FromArgb(164, 195, 173);
        cancel.Click += (_, _) => RequestCancel();
        layout.Controls.Add(cancel, 0, 2);
        Controls.Add(layout);

        callback = OnKey;
        Shown += (_, _) =>
        {
            MainForm.NativeTheme.ApplyDarkTitleBar(Handle);
            try
            {
                hook = SetWindowsHookEx(13, callback, GetModuleHandle(null), 0);
                if (hook == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
                mouseHook = new MouseSideButtonHook(OnSideButton);
            }
            catch (Win32Exception ex)
            {
                RemoveHooks();
                MessageBox.Show(this, ex.Message, "无法捕获快捷键");
                Finish(DialogResult.Cancel);
            }
        };
    }

    protected override void OnDeactivate(EventArgs e)
    {
        if (waitingForRelease) lostFocus = true;
        base.OnDeactivate(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) RemoveHooks();
        base.Dispose(disposing);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        RemoveHooks();
        base.OnFormClosed(e);
    }

    private void RemoveHooks()
    {
        mouseHook?.Dispose();
        mouseHook = null;
        if (hook != 0) UnhookWindowsHookEx(hook);
        hook = 0;
    }

    private bool IsForeground => IsHandleCreated && GetForegroundWindow() == Handle;

    private bool OnSideButton(int vk, bool down)
    {
        if (waitingForRelease && vk == Vk)
        {
            if (!down) CompleteCapture();
            return true;
        }
        if (!down || waitingForRelease || finishing || !IsForeground || Down(0x5B) || Down(0x5C))
            return false;
        BeginCapture(vk, Down(0x11), Down(0x12), Down(0x10));
        return true;
    }

    private nint OnKey(int code, nint message, nint data)
    {
        if (code >= 0 && (uint)message is 0x100 or 0x104 or 0x101 or 0x105)
        {
            var key = Marshal.PtrToStructure<KeyInfo>(data);
            bool down = (uint)message is 0x100 or 0x104;
            if (key.Extra != InputInjection.Signature && HandleKey((int)key.Vk, down))
                return 1;
        }
        return CallNextHookEx(hook, code, message, data);
    }

    private bool HandleKey(int vk, bool down)
    {
        if (waitingForRelease && !ClickSettings.IsMouseSideButton(Vk) && vk == Vk)
        {
            if (!down) CompleteCapture();
            return true;
        }
        if (!down) return rejectedKeys.Remove(vk);
        if (rejectedKeys.Contains(vk)) return true;
        if (!IsForeground || finishing || vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or >= 0xA0 and <= 0xA5)
            return false;

        bool ctrl = Down(0x11), alt = Down(0x12), shift = Down(0x10);
        if (vk == 0x7B && ctrl && shift && !alt) return false;
        if (vk == 0x09 && !ctrl && !alt) return false;
        if (vk == 0x1B)
        {
            if (!waitingForRelease)
            {
                Vk = vk;
                waitingForRelease = true;
            }
            else if (Vk != vk) rejectedKeys.Add(vk);
            RequestCancel();
            return true;
        }
        if (!ctrl && !alt && !shift && vk is 0x0D or 0x20 && ActiveControl is Button)
            return false;
        if (waitingForRelease || Down(vk)) return false;
        if (Down(0x5B) || Down(0x5C) || !ClickSettings.IsAllowedHotkey(vk, ctrl, alt))
        {
            rejectedKeys.Add(vk);
            BeginInvoke(() => feedback.Text = "这个组合不可用。\n" + KeyHint);
            return true;
        }
        BeginCapture(vk, ctrl, alt, shift);
        return true;
    }

    private void BeginCapture(int vk, bool ctrl, bool alt, bool shift)
    {
        Vk = vk; Ctrl = ctrl; Alt = alt; Shift = shift;
        waitingForRelease = true;
        lostFocus = false;
        BeginInvoke(() => feedback.Text = $"已检测到 {MainForm.HotkeyName(vk, ctrl, alt, shift)}，松开主键确认。");
    }

    private void CompleteCapture()
    {
        waitingForRelease = false;
        if (cancelRequested) Finish(DialogResult.Cancel);
        else if (lostFocus || !IsForeground) BeginInvoke(() => feedback.Text = KeyHint);
        else Finish(DialogResult.OK);
    }

    private void RequestCancel()
    {
        if (!waitingForRelease) { Finish(DialogResult.Cancel); return; }
        cancelRequested = true;
        BeginInvoke(() => feedback.Text = "松开当前按键后取消。");
    }

    private void Finish(DialogResult result)
    {
        if (finishing) return;
        finishing = true;
        BeginInvoke(() => { DialogResult = result; Close(); });
    }

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    [StructLayout(LayoutKind.Sequential)] private struct KeyInfo { public uint Vk, Scan, Flags, Time; public nuint Extra; }
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
