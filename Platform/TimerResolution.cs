using System.Runtime.InteropServices;

namespace MouseClicker.Platform;

// Match each successful request with a release when the task finishes.
internal sealed class TimerResolution : IDisposable
{
    private bool enabled;

    public TimerResolution()
    {
        try { enabled = timeBeginPeriod(1) == 0; } catch { }
    }

    public void Dispose()
    {
        if (!enabled) return;
        enabled = false;
        try { timeEndPeriod(1); } catch { }
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);
    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}
