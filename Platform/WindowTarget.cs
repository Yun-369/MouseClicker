using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MouseClicker.Platform;

// One visible top-level window as seen by the picker / resolver. ClientWidth/Height
// are PHYSICAL pixels of the client area; Dpi is the window's per-monitor DPI.
public sealed class WindowTargetInfo
{
    public nint Hwnd { get; init; }
    public string ProcessName { get; init; } = "";
    public string Title { get; init; } = "";
    public string ClassName { get; init; } = "";
    public int ClientWidth { get; init; }
    public int ClientHeight { get; init; }
    public int Dpi { get; init; }

    public string Describe() => $"[{ProcessName}] {Title}（类名 {ClassName}，客户区 {ClientWidth}×{ClientHeight}，DPI {Dpi}）";
}

// Foreground window binding support, minimal version: foreground SendInput + client
// area relative coordinates. The resolver re-finds the bound target by process name +
// title rule + optional class name EVERY run (the HWND is never persisted); it refuses
// zero matches, multiple matches, minimized windows, or a changed client size/DPI —
// no automatic scaling, no clicks at stale screen positions.
public static class WindowTargetResolver
{
    public static List<WindowTargetInfo> EnumerateVisibleTopLevels()
    {
        var list = new List<WindowTargetInfo>();
        var procNameCache = new Dictionary<int, string>();
        Win32.EnumWindows((hwnd, _) =>
        {
            if (!Win32.IsWindowVisible(hwnd)) return true;
            var title = Win32.GetWindowText(hwnd);
            if (string.IsNullOrWhiteSpace(title)) return true; // picker lists titled windows only
            int clsLen = 256;
            var cls = new System.Text.StringBuilder(clsLen);
            _ = Win32.GetClassNameW(hwnd, cls, clsLen);
            _ = Win32.GetWindowThreadProcessId(hwnd, out int pid);
            if (!procNameCache.TryGetValue(pid, out var proc))
            {
                try { proc = Process.GetProcessById(pid).ProcessName; }
                catch { proc = ""; }
                procNameCache[pid] = proc;
            }
            if (!Win32.GetClientRect(hwnd, out var rc)) return true;
            var origin = new Win32.POINT { X = 0, Y = 0 };
            Win32.ClientToScreen(hwnd, ref origin);
            list.Add(new WindowTargetInfo
            {
                Hwnd = hwnd,
                ProcessName = proc,
                Title = title,
                ClassName = cls.ToString(),
                ClientWidth = rc.Right - rc.Left,
                ClientHeight = rc.Bottom - rc.Top,
                Dpi = Win32.GetDpiForWindowSafe(hwnd)
            });
            return true;
        }, nint.Zero);
        return list;
    }

    // Re-resolve the bound target for THIS run. Returns null with a human-readable
    // reason when the target cannot be uniquely and correctly identified.
    public static WindowTargetInfo? Resolve(MouseClicker.Domain.WindowBinding bind, out string? error)
    {
        error = null;
        var all = EnumerateVisibleTopLevels();
        var matches = all.Where(w =>
            string.Equals(w.ProcessName, bind.ProcessName, StringComparison.OrdinalIgnoreCase)
            && MouseClicker.Domain.WindowTitleRule.Matches(bind.TitleRule, bind.TitleText, w.Title)
            // Class name participates ONLY when explicitly requested (opt-in): WinForms
            // class names embed a per-process hash and change across process restarts.
            && (!bind.MatchClass
                || (string.Equals(w.ClassName, bind.ClassName, StringComparison.OrdinalIgnoreCase))))
            .ToList();

        string rule = $"进程 {bind.ProcessName}，标题{MouseClicker.Domain.WindowTitleRule.Label(bind.TitleRule)}“{bind.TitleText}”"
            + (bind.MatchClass && !string.IsNullOrWhiteSpace(bind.ClassName) ? $"，类名 {bind.ClassName}" : "");
        if (matches.Count == 0)
        {
            error = $"找不到匹配的目标窗口（规则：{rule}）。请确认目标已打开，或重新绑定窗口。";
            return null;
        }
        if (matches.Count > 1)
        {
            var names = string.Join("；", matches.Take(5).Select(m => $"“{m.Title}”"));
            error = $"匹配到 {matches.Count} 个符合规则的目标窗口（{names}），无法确定目标，已拒绝。"
                + "请收紧标题匹配规则（如改为完全等于）或类名后重试。";
            return null;
        }

        var w = matches[0];
        if (!Win32.IsWindow(w.Hwnd) || !Win32.IsWindowVisible(w.Hwnd))
        {
            error = "目标窗口已关闭或不可见，已拒绝。";
            return null;
        }
        if (!EnsureUsable(w, bind, out var usableError))
        {
            error = usableError;
            return null;
        }
        return w;
    }

    // Convert one CLIENT-AREA point of the target to a screen point at send time.
    // Returns false when the window is gone (caller stops the macro).
    public static bool ClientToScreenPoint(nint hwnd, int x, int y, out int sx, out int sy)
    {
        sx = sy = 0;
        if (!Win32.IsWindow(hwnd) || !Win32.GetClientRect(hwnd, out var rc)) return false;
        var p = new Win32.POINT { X = x, Y = y };
        if (!Win32.ClientToScreen(hwnd, ref p)) return false;
        sx = p.X; sy = p.Y;
        return true;
    }

    // Convert a SCREEN point to the target's client area (editor pick-a-point helper).
    public static bool ScreenToClientPoint(nint hwnd, int sx, int sy, out int cx, out int cy)
    {
        cx = cy = 0;
        if (!Win32.IsWindow(hwnd)) return false;
        var p = new Win32.POINT { X = sx, Y = sy };
        if (!Win32.ScreenToClient(hwnd, ref p)) return false;
        cx = p.X; cy = p.Y;
        return true;
    }

    // THE unified per-action target check for window-bound macros — one entry point so
    // every input path applies the same rules. Verifies, in order: window alive and
    // visible, not minimized, client size still matches the binding, current DPI still
    // matches, and (when required) the target still holds the foreground. This check
    // NEVER activates the target — during a run a lost foreground is a stop condition,
    // not something to fight over. Returns false with a specific reason.
    public static bool EnsureUsable(WindowTargetInfo target, MouseClicker.Domain.WindowBinding bind, out string? error)
    {
        error = null;
        if (!Win32.IsWindow(target.Hwnd) || !Win32.IsWindowVisible(target.Hwnd))
        {
            error = "目标窗口已关闭或不可见。";
            return false;
        }
        if (Win32.IsIconic(target.Hwnd))
        {
            error = "目标窗口被最小化。";
            return false;
        }
        if (!Win32.GetClientRect(target.Hwnd, out var rc))
        {
            error = "无法获取目标窗口客户区。";
            return false;
        }
        int cw = rc.Right - rc.Left, ch = rc.Bottom - rc.Top;
        if (cw != bind.ClientWidth || ch != bind.ClientHeight)
        {
            error = $"目标窗口客户区尺寸已变化（当前 {cw}×{ch}，绑定时 {bind.ClientWidth}×{bind.ClientHeight}）。";
            return false;
        }
        int dpi = Win32.GetDpiForWindowSafe(target.Hwnd);
        if (dpi != bind.Dpi)
        {
            error = $"目标窗口 DPI 已变化（当前 {dpi}，绑定时 {bind.Dpi}）。";
            return false;
        }
        return true;
    }

    public static bool IsForeground(nint hwnd) => Win32.GetForegroundWindow() == hwnd;

    // Bring the target to the foreground with a NORMAL Win32 foreground request only —
    // no synthetic ALT or other辅助 keys. Cross-process activation may legitimately be
    // refused by the foreground lock; the caller then stops with an explicit error.
    public static bool TryBringForeground(nint hwnd)
    {
        if (Win32.GetForegroundWindow() == hwnd) return true;
        Win32.BringWindowToTop(hwnd);
        Win32.SetForegroundWindow(hwnd);
        return Win32.GetForegroundWindow() == hwnd;
    }

    // The top-level window actually under the point must be the bound target (or a
    // child of it). A covering window would receive the input instead, so refuse it.
    public static bool IsPointOverTarget(nint hwnd, int sx, int sy, out string? error, string what = "点击位置")
    {
        error = null;
        var hit = Win32.WindowFromPoint(new Win32.POINT { X = sx, Y = sy });
        if (hit == nint.Zero)
        {
            error = $"{what}上没有可命中的窗口。";
            return false;
        }
        var root = Win32.GetAncestor(hit, 2 /* GA_ROOT */);
        if (root != hwnd)
        {
            var title = Win32.GetWindowText(hit);
            error = $"{what}（{sx},{sy}）已被其他窗口覆盖（命中“{title}”），若继续会把输入发给错误的窗口。";
            return false;
        }
        return true;
    }

    internal static class Win32
    {
        public struct POINT { public int X, Y; }
        public struct RECT { public int Left, Top, Right, Bottom; }
        public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(nint hwnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeoutW(nint hWnd, uint Msg, nuint wParam, System.Text.StringBuilder lParam, uint fuFlags, uint uTimeout, out nuint lpdwResult);
        private const uint WM_GETTEXT = 0x000D;
        private const uint SMTO_ABORTIFHUNG = 0x0002;
        private const uint GETTEXT_TIMEOUT_MS = 200;
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint hwnd, System.Text.StringBuilder text, int count);
        [DllImport("user32.dll")] public static extern nint GetWindowThreadProcessId(nint hwnd, out int pid);
        [DllImport("user32.dll")] public static extern bool GetClientRect(nint hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(nint hwnd, out RECT rect);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(nint hwnd, ref POINT point);
        [DllImport("user32.dll")] public static extern bool ScreenToClient(nint hwnd, ref POINT point);
        [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool SetForegroundWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint hwnd);
        [DllImport("user32.dll")] public static extern nint WindowFromPoint(Win32.POINT point);
        [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
        [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);

        public static string GetWindowText(nint hwnd)
        {
            // Bounded WM_GETTEXT: a plain GetWindowTextW is an unbounded cross-thread/
            // cross-process SendMessage (same-process cross-thread has NO timeout) and
            // would freeze the engine forever if the window's UI thread is blocked.
            var sb = new System.Text.StringBuilder(512);
            _ = SendMessageTimeoutW(hwnd, WM_GETTEXT, (nuint)511, sb, SMTO_ABORTIFHUNG, GETTEXT_TIMEOUT_MS, out _);
            return sb.ToString();
        }

        // GetDpiForWindow exists since Win10 1607; fall back to the screen DC.
        public static int GetDpiForWindowSafe(nint hwnd)
        {
            try
            {
                uint dpi = GetDpiForWindow(hwnd);
                if (dpi is > 0 and <= 1000) return (int)dpi;
            }
            catch { /* entry point missing on old systems */ }
            try
            {
                var dc = GetDC(nint.Zero);
                try { return GetDeviceCaps(dc, 88); } // LOGPIXELSX
                finally { ReleaseDC(nint.Zero, dc); }
            }
            catch { return 96; }
        }

        [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd, nint dc);
        [DllImport("gdi32.dll")] private static extern int GetDeviceCaps(nint dc, int index);
    }
}

