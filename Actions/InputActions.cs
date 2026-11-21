using System.Diagnostics;
using System.IO;
using MouseClicker.Domain;

namespace MouseClicker.Actions;

public interface IInputDriver
{
    Point? CursorPosition => null;
    void MoveTo(int x, int y);
    void ButtonDown(ClickButton button);
    void ButtonUp(ClickButton button);
    void Click(ClickButton button, Point? position, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (position is { } point) MoveTo(point.X, point.Y);
        cancellationToken.ThrowIfCancellationRequested();
        ButtonDown(button);
        ButtonUp(button);
    }
    // Win32 virtual-key code (1..254). extended marks an extended key (Right
    // Ctrl/Alt, arrows, numpad Enter…); scan is the hardware scan code (0 lets the
    // driver derive it from the VK). Both are needed to replay numpad Enter and the
    // right-side keys faithfully.
    void KeyDown(ushort vk, bool extended = false, ushort scan = 0);
    void KeyUp(ushort vk, bool extended = false, ushort scan = 0);
    // Send ONE UTF-16 code unit as a Unicode keystroke (KEYEVENTF_UNICODE), independent
    // of the active keyboard layout / IME. A surrogate pair is sent high-down, low-down,
    // low-up, high-up by the caller. Never tracked as a held key (always paired).
    void UnicodeKey(ushort codeUnit, bool down);
    // Signed wheel delta; one notch is typically +-120 (WHEEL_DELTA).
    void Wheel(int delta, int x, int y);
    // Release every mouse button / key this driver is still holding.
    void ReleaseAll();
}

public sealed record ActionContext(IInputDriver Input);

public interface IAutomationAction
{
    ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken);
}

public sealed record MoveAction(int X, int Y) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Input.MoveTo(X, Y);
        return ValueTask.CompletedTask;
    }
}

public sealed record MouseButtonAction(ClickButton Button, bool Down) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Down) context.Input.ButtonDown(Button);
        else context.Input.ButtonUp(Button);
        return ValueTask.CompletedTask;
    }
}

public sealed record KeyButtonAction(ushort Vk, bool Down, bool Extended = false, ushort Scan = 0) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Down) context.Input.KeyDown(Vk, Extended, Scan);
        else context.Input.KeyUp(Vk, Extended, Scan);
        return ValueTask.CompletedTask;
    }
}

public sealed record WheelAction(int Delta, int X, int Y) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.Input.Wheel(Delta, X, Y);
        return ValueTask.CompletedTask;
    }
}

// A whole key press / chord (no separate hold/release this round). Modifiers go down in
// a fixed order Ctrl -> Alt -> Shift, the primary key presses, then everything is
// released in the REVERSE order. Stop/error cleanup is the driver's ReleaseAll job.
public sealed record KeyChordAction(ushort Vk, bool Extended, bool Ctrl, bool Alt, bool Shift) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var input = context.Input;
        if (Ctrl) input.KeyDown(MouseClicker.Domain.MacroKeys.VkCtrl);
        if (Alt) input.KeyDown(MouseClicker.Domain.MacroKeys.VkAlt);
        if (Shift) input.KeyDown(MouseClicker.Domain.MacroKeys.VkShift);
        try
        {
            input.KeyDown(Vk, Extended);
            input.KeyUp(Vk, Extended);
        }
        finally
        {
            if (Shift) input.KeyUp(MouseClicker.Domain.MacroKeys.VkShift);
            if (Alt) input.KeyUp(MouseClicker.Domain.MacroKeys.VkAlt);
            if (Ctrl) input.KeyUp(MouseClicker.Domain.MacroKeys.VkCtrl);
        }
        return ValueTask.CompletedTask;
    }
}

// Literal Unicode text typed via KEYEVENTF_UNICODE (no IME / clipboard; {} + ^ stay
// literal). '\n' / '\r\n' become a real Enter, '\t' a real Tab; any OTHER control
// character is rejected (validation also catches it before the run). Long text is sent
// in small batches and cancellation is observed BETWEEN batches, so a stop during a long
// paste takes effect instead of after the whole string. `batchGuard` (window binding)
// runs between batches too: returning an error text aborts the remaining characters
// (target lost foreground / closed), returning null continues.
public sealed record TextInputAction(string Text, int BatchChars = 32, Func<string?>? BatchGuard = null) : IAutomationAction
{
    private const ushort VkReturn = 0x0D, VkTab = 0x09;

    public async ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        var input = context.Input;
        int sinceBatch = 0;
        for (int i = 0; i < Text.Length; i++)
        {
            if (sinceBatch >= BatchChars)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (BatchGuard is not null)
                {
                    string? guardError = BatchGuard();
                    if (guardError is not null)
                        throw new InvalidDataException(guardError);
                }
                await Task.Yield(); // let the stop token be observed between batches
                sinceBatch = 0;
            }
            cancellationToken.ThrowIfCancellationRequested();
            char c = Text[i];

            if (c == '\r')
            {
                // Collapse CRLF; a lone CR still acts as Enter.
                if (i + 1 < Text.Length && Text[i + 1] == '\n') continue;
                input.KeyDown(VkReturn); input.KeyUp(VkReturn); sinceBatch++;
                continue;
            }
            if (c == '\n') { input.KeyDown(VkReturn); input.KeyUp(VkReturn); sinceBatch++; continue; }
            if (c == '\t') { input.KeyDown(VkTab); input.KeyUp(VkTab); sinceBatch++; continue; }
            if (char.IsControl(c))
                throw new InvalidDataException($"文本包含不支持的控制字符（U+{(int)c:X4}），已停止；请删除后再执行。");

            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= Text.Length || !char.IsLowSurrogate(Text[i + 1]))
                    throw new InvalidDataException("文本包含不成对的代理项（残缺字符），已停止。");
                ushort hi = c, lo = Text[i + 1];
                input.UnicodeKey(hi, true);
                input.UnicodeKey(lo, true);
                input.UnicodeKey(lo, false);
                input.UnicodeKey(hi, false);
                i++;
                sinceBatch += 2;
                continue;
            }
            input.UnicodeKey((ushort)c, true);
            input.UnicodeKey((ushort)c, false);
            sinceBatch++;
        }
    }
}

// Hybrid wait: 1 ms coarse sleeps until the last ~2 ms, then spin — accurate to
// roughly ±1 ms once TimerResolution has raised the system tick to 1 ms.
public sealed record WaitAction(int Milliseconds) : IAutomationAction
{
    public ValueTask ExecuteAsync(ActionContext context, CancellationToken cancellationToken)
    {
        if (Milliseconds > 0)
            WaitUntil(Stopwatch.GetTimestamp() + ToStopwatchTicks(Milliseconds), cancellationToken);
        return ValueTask.CompletedTask;
    }

    internal static long ToStopwatchTicks(int milliseconds)
        => (long)(milliseconds * Stopwatch.Frequency / 1000d);

    internal static void WaitUntil(long target, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = target - Stopwatch.GetTimestamp();
            if (remaining <= 0) return;
            if (remaining * 1000d / Stopwatch.Frequency > 2) Thread.Sleep(1);
            else Thread.SpinWait(60);
        }
    }
}
