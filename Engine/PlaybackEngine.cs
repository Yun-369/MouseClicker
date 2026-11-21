using System.Diagnostics;
using MouseClicker.Actions;
using MouseClicker.Domain;
using MouseClicker.Platform;
using MouseClicker.Storage;

namespace MouseClicker.Engine;

// State carried to the UI. Index/Total are the action progress WITHIN one loop;
// Loop/LoopsTotal track whole repetitions separately (LoopsTotal == InfiniteLoops
// means repeat until stopped). Outcome is the terminal result and is retained
// until the next Start so the final state, progress and failure reason stay shown.
public sealed record PlaybackSnapshot(
    RunState State, int Index, int Total, string? Error, string? Outcome,
    int Loop, int LoopsTotal, double Speed)
{
    public double Progress => Total <= 0 ? 0 : (double)Index / Total;
    public bool Infinite => LoopsTotal == PlaybackEngine.InfiniteLoops;
}

// Speed-scaled, loopable playback of a validated RecordingDocument. The speed
// multiplier only rescales the waits (0.5x slower, 2x faster); the source file is
// never rewritten. Every wait is cancellable AND truly pausable (remaining time is
// frozen while paused), and every exit path (finish / manual stop / error) and
// every loop boundary releases any button / key this playback held.
public sealed class PlaybackEngine(IInputDriver input)
{
    public const int InfiniteLoops = -1;
    public static readonly double[] Speeds = [0.5, 1.0, 2.0];
    private const int WaitSliceMs = 15;

    public event Action<string>? Log;
    // Raised when the pause request/confirm state changes, so the UI can show
    // "pausing…" until the worker actually parks and "paused" only once confirmed.
    public event Action? PauseStateChanged;

    // Optional per-event input guard (window binding): invoked AFTER each event's wait
    // completes and BEFORE its input is sent. Returning an error text aborts playback
    // with that reason — the normal error path releases held keys/buttons and the macro
    // engine surfaces the error to the parent. Null (standalone playback, unbound
    // macros) keeps the original behaviour.
    public Func<string?>? InputGuard { get; set; }

    private ActionContext context = new(input);
    private readonly object stateGate = new();
    private CancellationTokenSource? cts;
    private PauseToken? pause;
    private Task? worker;
    private bool finished;
    private PlaybackSnapshot snapshot = new(RunState.Idle, 0, 0, null, null, 0, 1, 1.0);

    // Test seam: invoked on the worker thread immediately BEFORE the terminal state
    // is published. Lets a regression test call Stop() at this exact interleaving
    // (instead of relying on repeated runs to hit the race). No-op in production.
    internal Action? BeforeFinalizeSnapshot { get; set; }

    public bool IsActive => Volatile.Read(ref worker) is { IsCompleted: false };
    // Internal: lets a host (the macro engine) await an embedded recording's worker.
    internal Task? WorkerTask => Volatile.Read(ref worker);
    public PlaybackSnapshot Snapshot => snapshot;
    // Requested = user asked to pause; Confirmed = the worker has actually parked.
    public bool IsPauseRequested => pause?.Requested ?? false;
    public bool IsPauseConfirmed => pause?.Confirmed ?? false;

    // Original-speed, single loop.
    public void Start(RecordingDocument document) => Start(document, 1.0, 1);

    public void Start(RecordingDocument document, double speed, int loops, IInputDriver? inputOverride = null)
    {
        if (IsActive) return;
        if (Array.IndexOf(Speeds, speed) < 0)
            throw new ArgumentException("倍速仅支持 0.5×、1×、2×。");
        if (loops != InfiniteLoops && loops < 1)
            throw new ArgumentException("回放轮数至少为 1（或选择无限循环）。");

        // Schema/parameter validation, then an environment preflight: every mouse
        // coordinate must fit the CURRENT virtual desktop before any input is sent.
        RecordingStore.Validate(document);
        EnsureCoordinatesFit(document);

        context = new ActionContext(inputOverride ?? input);
        if(inputOverride is null && document.WindowBind is {} binding)
        {
            var target=MouseClicker.Platform.WindowTargetResolver.Resolve(binding,out var error);
            if(target is null)throw new InvalidDataException(error);
            if(!MouseClicker.Platform.WindowTargetResolver.TryBringForeground(target.Hwnd))throw new InvalidDataException("无法激活回放目标窗口，未发送输入。");
            context=new ActionContext(new MouseClicker.Platform.BoundInputDriver(input,target,binding));
        }

        var tokenCts = new CancellationTokenSource();
        cts = tokenCts;
        var gate = new PauseToken();
        gate.Changed += () => PauseStateChanged?.Invoke();
        pause = gate;
        var token = tokenCts.Token;
        int total = document.Events.Count;
        lock (stateGate)
        {
            finished = false;
            snapshot = new PlaybackSnapshot(RunState.Armed, 0, total, null, null, 0, loops, speed);
        }
        // Do NOT pass the token to Task.Run: if Stop() cancels before the worker is
        // scheduled, a pre-cancelled token would short-circuit the delegate so its
        // finally (release + terminal snapshot) never runs, leaving it stuck Stopping.
        // The body observes the token itself and always finalizes.
        worker = Task.Run(() => RunAsync(document, token, gate, speed, loops, tokenCts));
    }

    // Throws before playback if any mouse-bearing event targets a point outside
    // the current virtual desktop (e.g. recorded on a monitor that is gone now).
    public static void EnsureCoordinatesFit(RecordingDocument document)
    {
        if(document.WindowBind is {} binding)
        {
            foreach(var e in document.Events)
                if(RecordingEventType.IsMouse(e.Type) && (e.X<0||e.Y<0||e.X>=binding.ClientWidth||e.Y>=binding.ClientHeight))
                    throw new InvalidDataException("录制坐标超出绑定客户区，未发送输入。");
            return;
        }
        var bounds = SystemInformation.VirtualScreen;
        for (int i = 0; i < document.Events.Count; i++)
        {
            var e = document.Events[i];
            bool mouse = e.Type is RecordingEventType.MouseMove or RecordingEventType.MouseDown
                or RecordingEventType.MouseUp or RecordingEventType.MouseWheel;
            if (mouse && !bounds.Contains(e.X, e.Y))
                throw new InvalidDataException(
                    $"第 {i + 1} 个动作的坐标（{e.X},{e.Y}）不在当前虚拟桌面范围内"
                    + $"（{bounds.Left},{bounds.Top}，{bounds.Width}×{bounds.Height}）；已取消回放，未发送任何输入。");
        }
    }

    // Non-fatal environment mismatch: all coordinates may still fit, but the recording
    // was made on a different desktop layout / DPI. Returns a human-readable warning
    // (caller asks the user to confirm) or null when the environment matches.
    public static string? DescribeEnvironmentMismatch(RecordingDocument document, int currentDpi)
    {
        var diffs = EnvironmentDiffs(document, currentDpi);
        if (diffs.Count == 0) return null;
        return "录制环境与当前桌面不一致：" + string.Join("；", diffs)
            + "。坐标按录制时的物理像素回放，目标位置可能偏移。仍要继续吗？";
    }

    // Single source of truth for "recording env vs current desktop" diffs, reused by
    // standalone playback AND macro reference preflight so the rules never diverge.
    public static IReadOnlyList<string> EnvironmentDiffs(RecordingDocument document, int currentDpi)
    {
        if(document.WindowBind is not null)return Array.Empty<string>();
        var bounds = SystemInformation.VirtualScreen;
        var diffs = new List<string>();
        if (document.VirtualWidth > 0 && document.VirtualHeight > 0 &&
            (document.VirtualLeft != bounds.Left || document.VirtualTop != bounds.Top
             || document.VirtualWidth != bounds.Width || document.VirtualHeight != bounds.Height))
        {
            diffs.Add($"录制时虚拟屏（{document.VirtualLeft},{document.VirtualTop}，"
                + $"{document.VirtualWidth}×{document.VirtualHeight}）与当前（{bounds.Left},{bounds.Top}，"
                + $"{bounds.Width}×{bounds.Height}）不同");
        }
        if (document.Dpi > 0 && document.Dpi != currentDpi)
            diffs.Add($"录制时 DPI {document.Dpi} 与当前 {currentDpi} 不同");
        return diffs;
    }

    private async Task RunAsync(RecordingDocument document, CancellationToken token,
        PauseToken gate, double speed, int loops, CancellationTokenSource tokenCts)
    {
        using var timerResolution = new TimerResolution();
        int total = document.Events.Count;
        string reason = "回放完成";
        string? error = null;
        int loopIndex = 0;
        try
        {
            Log?.Invoke($"开始回放：{total} 个动作，{speed:0.##}×，{(loops == InfiniteLoops ? "无限循环" : $"{loops} 轮")}。");
            for (loopIndex = 0; loops == InfiniteLoops || loopIndex < loops; loopIndex++)
            {
                for (int i = 0; i < total; i++)
                {
                    token.ThrowIfCancellationRequested();
                    // Park here if a pause was requested before this event starts.
                    if (gate.Requested) await gate.ParkAsync(token);

                    var e = document.Events[i];
                    // Speed rescales only the gap from the previous event; the source
                    // document's DelayMs/TimeMs are never modified. The delay is
                    // pausable: time spent while parked does NOT count down.
                    int wait = (int)Math.Max(0, Math.Round(e.DelayMs / speed));
                    await DelayPausableAsync(wait, token, gate);

                    // Re-coordinate pause/stop immediately before sending, so an event
                    // can never be injected while paused or after a stop request.
                    token.ThrowIfCancellationRequested();
                    if (gate.Requested) await gate.ParkAsync(token);

                    // Window-bound guard: the wait above just finished — re-check the
                    // target one final time before this event's input goes out.
                    if(context.Input is MouseClicker.Platform.BoundInputDriver bound)bound.Check();
                    if (InputGuard is not null)
                    {
                        string? guardError = InputGuard();
                        if (guardError is not null)
                            throw new InvalidDataException(guardError);
                    }

                    switch (e.Type)
                    {
                        case RecordingEventType.MouseMove:
                            await new MoveAction(e.X, e.Y).ExecuteAsync(context, token);
                            break;
                        case RecordingEventType.MouseDown:
                            // Locate even with no preceding mouse_move, so a recording
                            // whose first event is a press still lands right.
                            await new MoveAction(e.X, e.Y).ExecuteAsync(context, token);
                            await new MouseButtonAction(MapButton(e.Button), true).ExecuteAsync(context, token);
                            break;
                        case RecordingEventType.MouseUp:
                            // Move to the recorded release point too — the drag endpoint.
                            await new MoveAction(e.X, e.Y).ExecuteAsync(context, token);
                            await new MouseButtonAction(MapButton(e.Button), false).ExecuteAsync(context, token);
                            break;
                        case RecordingEventType.MouseWheel:
                            await new MoveAction(e.X, e.Y).ExecuteAsync(context, token);
                            await new WheelAction(e.Delta, e.X, e.Y).ExecuteAsync(context, token);
                            break;
                        case RecordingEventType.KeyDown:
                            await new KeyButtonAction(e.Vk, true, e.Extended, e.Scan).ExecuteAsync(context, token);
                            break;
                        case RecordingEventType.KeyUp:
                            await new KeyButtonAction(e.Vk, false, e.Extended, e.Scan).ExecuteAsync(context, token);
                            break;
                    }
                    snapshot = new PlaybackSnapshot(RunState.Running, i + 1, total, null, null,
                        loopIndex + 1, loops, speed);
                }

                // Release anything still held at the end of EACH loop before entering
                // the next, so an unpaired down can never carry across. A cleanup
                // failure terminates playback with an error instead of continuing in
                // an unknown key/button state.
                context.Input.ReleaseAll();
            }
            Log?.Invoke("回放完成。");
        }
        catch (OperationCanceledException)
        {
            reason = "回放已手动停止";
            Log?.Invoke("回放被手动停止。");
        }
        catch (Exception ex)
        {
            reason = "回放错误";
            error = ex.Message;
            Log?.Invoke($"回放错误：{ex.Message}");
        }
        finally
        {
            try { context.Input.ReleaseAll(); }
            catch (Exception releaseEx)
            {
                error = error is null ? releaseEx.Message : $"{error}；清理按键失败：{releaseEx.Message}";
            }
            // Controlled-interleave seam (tests only): a late Stop() issued here must
            // not win over the terminal state published below.
            BeforeFinalizeSnapshot?.Invoke();
            string outcome = error is null ? reason : $"{reason}：{error}";
            lock (stateGate)
            {
                var last = snapshot;
                snapshot = new PlaybackSnapshot(
                    error is null ? RunState.Idle : RunState.Error, last.Index, last.Total, error, outcome,
                    last.Loop, last.LoopsTotal, last.Speed);
                finished = true;
            }
            tokenCts.Dispose();
        }
    }

    // Wait totalMs while honouring pause: only wall-clock time spent NOT parked is
    // consumed. On resume the remaining portion is waited out.
    private static async Task DelayPausableAsync(int totalMs, CancellationToken token, PauseToken gate)
    {
        if (totalMs <= 0) return;
        var stopwatch = Stopwatch.StartNew();
        long remaining = totalMs;
        while (remaining > 0)
        {
            token.ThrowIfCancellationRequested();
            if (gate.Requested) await gate.ParkAsync(token);
            stopwatch.Restart();
            int slice = (int)Math.Min(WaitSliceMs, remaining);
            await Task.Delay(slice, token); // OperationCanceledException propagates on stop
            remaining -= stopwatch.ElapsedMilliseconds;
        }
    }

    private static ClickButton MapButton(string? value) => value switch
    {
        "left" => ClickButton.Left,
        "middle" => ClickButton.Middle,
        "right" => ClickButton.Right,
        _ => throw new InvalidDataException($"未知鼠标键：{value}")
    };

    // Idempotent stop. The decision and the Stopping publication share stateGate with
    // the worker's terminal-state publication, so a Stop that arrives just as the
    // worker finishes can NEVER overwrite the retained final state (Idle/Error). When
    // nothing is running (never started / finished / error) the state is left as-is;
    // repeated stops while running simply cancel again. Resuming the pause gate lets a
    // parked worker finish stopping.
    public void Stop()
    {
        lock (stateGate)
        {
            var w = worker;
            if (finished || w is null) return;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            pause?.RequestResume();
            snapshot = snapshot with { State = RunState.Stopping };
        }
    }

    public async Task StopAsync()
    {
        Stop();
        var task = worker;
        if (task is not null)
        {
            try { await task; } catch { /* terminal state is in the snapshot */ }
        }
    }

    public void Pause() => pause?.RequestPause();
    public void Resume() => pause?.RequestResume();
    // Button toggle follows the request; the UI must gate the "已暂停" wording on
    // IsPauseConfirmed, not on this.
    public bool IsPaused => pause?.Requested ?? false;
}

// Pause gate with two distinct flags: Requested is set immediately by Pause(),
// Confirmed flips on only when the worker actually reaches a park point. Resume
// releases a parked waiter (and stop cancels it).
internal sealed class PauseToken
{
    private readonly object gate = new();
    private TaskCompletionSource<bool>? parked;

    public bool Requested { get; private set; }
    public bool Confirmed { get; private set; }
    public event Action? Changed;

    public void RequestPause()
    {
        lock (gate)
        {
            if (Requested) return;
            Requested = true;
        }
        Changed?.Invoke();
    }

    public void RequestResume()
    {
        TaskCompletionSource<bool>? toRelease;
        lock (gate)
        {
            Requested = false;
            Confirmed = false;
            toRelease = parked;
            parked = null;
        }
        toRelease?.TrySetResult(true);
        Changed?.Invoke();
    }

    // Called by the worker: mark confirmed (so the UI may show "已暂停"), then wait
    // until Resume or cancellation.
    public async Task ParkAsync(CancellationToken token)
    {
        Task waiting;
        lock (gate)
        {
            if (!Requested) return;
            Confirmed = true;
            parked ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting = parked.Task;
        }
        Changed?.Invoke();
        using var reg = token.Register(() =>
        {
            lock (gate) parked?.TrySetCanceled(token);
        });
        await waiting;
        lock (gate) Confirmed = false;
        Changed?.Invoke();
    }
}
