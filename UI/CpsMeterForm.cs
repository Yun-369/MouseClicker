using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseClicker.UI;

// CPS meter: counts physical left-button presses system-wide via a low-level
// mouse hook. Injected events are excluded.
public sealed class CpsMeterForm : Form
{
    private const int WhMouseLl = 14;
    private const uint WmLbuttondown = 0x0201;
    // Mouse injected flag is 0x01 (0x10 is the keyboard variant).
    private const uint LlmhfInjected = 0x01;

    private nint hook;
    // Keep the hook delegate alive for the whole install→uninstall window.
    private LowLevelProc? callback;
    private bool counting;
    private readonly object gate = new();
    private readonly List<long> clicks = [];
    private readonly Label live = new() { AutoSize = true };
    private readonly Button toggle = new() { Text = "开始统计", AutoSize = true };
    private readonly Button reset = new() { Text = "清零", AutoSize = true };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 100 };

    public CpsMeterForm()
    {
        Text = "CPS 测速（统计全系统真实左键点击）";
        ClientSize = new Size(430, 190);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Microsoft YaHei UI", 10);
        var actions = new FlowLayoutPanel { AutoSize = true, Location = new Point(20, 120) };
        actions.Controls.AddRange([toggle, reset]);
        Controls.Add(live);
        Controls.Add(actions);
        live.Location = new Point(20, 20);

        toggle.Click += (_, _) =>
        {
            if (!counting)
            {
                if (hook == 0) Install();
                counting = true;
                toggle.Text = "停止统计";
            }
            else
            {
                counting = false;
                toggle.Text = "开始统计";
            }
        };
        reset.Click += (_, _) => { lock (gate) clicks.Clear(); };
        refresh.Tick += (_, _) => UpdateStats();
        FormClosing += (_, _) => { refresh.Stop(); Uninstall(); };

        refresh.Start();
        UpdateStats();
    }

    private void Install()
    {
        callback = Callback;
        hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
        if (hook == 0)
        {
            callback = null;
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), "鼠标钩子安装失败。");
        }
    }

    private void Uninstall()
    {
        if (hook != 0) UnhookWindowsHookEx(hook);
        hook = 0;
        callback = null;
    }

    private void UpdateStats()
    {
        long now = Stopwatch.GetTimestamp();
        int last1s, last5s, total;
        lock (gate)
        {
            clicks.RemoveAll(t => now - t > 6 * Stopwatch.Frequency);
            last1s = clicks.Count(t => now - t <= Stopwatch.Frequency);
            last5s = clicks.Count(t => now - t <= 5 * Stopwatch.Frequency);
            total = clicks.Count;
        }
        live.Text = $"状态：{(counting ? "统计中，请连点鼠标左键" : "未开始")}\n\n"
                    + $"最近 1 秒：{last1s} CPS\n"
                    + $"最近 5 秒平均：{last5s / 5d:F1} CPS\n"
                    + $"6 秒内累计：{total} 次";
    }

    private nint Callback(int code, nint wParam, nint lParam)
    {
        if (code >= 0 && counting && wParam == WmLbuttondown)
        {
            var info = Marshal.PtrToStructure<MsllStruct>(lParam);
            if ((info.Flags & LlmhfInjected) == 0)
                lock (gate) clicks.Add(Stopwatch.GetTimestamp());
        }
        return CallNextHookEx(hook, code, wParam, lParam);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsllStruct { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }

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
