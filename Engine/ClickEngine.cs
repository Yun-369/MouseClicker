using System.Diagnostics;
using MouseClicker.Actions;
using MouseClicker.Domain;
using MouseClicker.Platform;

namespace MouseClicker.Engine;

public sealed record RunSnapshot(RunState State, long Clicks, double AverageIntervalMs, string? Error);

// Called by the UI thread; all input execution belongs to one worker.
public sealed class ClickEngine(IInputDriver input)
{
    private CancellationTokenSource? cancellation;
    private Task? worker;
    private RunSnapshot snapshot = new(RunState.Idle, 0, 0, null);
    public RunSnapshot Snapshot => Volatile.Read(ref snapshot);
    public bool IsActive => worker is { IsCompleted: false };
    public event Action<string>? Log;

    public void Start(ClickSettings settings, IInputDriver? inputOverride = null)
    {
        if (IsActive) return;
        settings.Validate();
        cancellation?.Dispose();
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token;
        snapshot = new(RunState.Armed, 0, 0, null);
        Log?.Invoke($"启动：{settings.Describe()}");
        worker = Task.Run(() => RunAsync(settings, token, inputOverride ?? input));
    }

    public void Stop()
    {
        if (!IsActive) return;
        snapshot = Snapshot with { State = RunState.Stopping };
        cancellation?.Cancel();
    }

    public async Task StopAsync()
    {
        Stop();
        if (worker is not null) await worker;
    }

    private async Task RunAsync(ClickSettings settings, CancellationToken token, IInputDriver runInput)
    {
        using var timerResolution = new TimerResolution();
        var context = new ActionContext(runInput);
        long clicks = 0;
        double firstClick = 0, lastClick = 0;
        string? error = null;
        var reason = "完成";
        long startedAt = Stopwatch.GetTimestamp();
        long stopAt = settings.Stop == StopCondition.Duration
            ? startedAt + WaitAction.ToStopwatchTicks(settings.DurationMs)
            : long.MaxValue;
        var random = new Random();
        int sequence = 0;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                if (settings.Stop == StopCondition.Count && clicks >= settings.Count) break;
                if (Stopwatch.GetTimestamp() >= stopAt) break;

                Point? position = null;
                if (settings.Action != ClickAction.Release && settings.Position != PositionMode.FollowCursor)
                {
                    var (targetX, targetY) = settings.Position == PositionMode.Fixed
                        ? (settings.X, settings.Y)
                        : PickPoint(settings, random, ref sequence);
                    (targetX, targetY) = Jitter(settings, random, targetX, targetY);
                    position = new Point(targetX, targetY);
                }

                switch (settings.Action)
                {
                    case ClickAction.Press:
                        if (position is { } point)
                            await new MoveAction(point.X, point.Y).ExecuteAsync(context, token);
                        await new MouseButtonAction(settings.Button, true).ExecuteAsync(context, token);
                        clicks = 1;
                        // Publish Running immediately: the button is already held,
                        // the UI must not stay on "准备中 / 0 次" for the whole hold.
                        snapshot = new(RunState.Running, clicks, 0, null);
                        Log?.Invoke("已按下并保持（运行中），停止或时长到点后由清理路径松开。");
                        if (settings.Stop == StopCondition.Duration)
                        {
                            var remaining = Stopwatch.GetElapsedTime(Stopwatch.GetTimestamp(), stopAt);
                            if (remaining > TimeSpan.Zero) await Task.Delay(remaining, token);
                        }
                        else
                            await Task.Delay(Timeout.Infinite, token);
                        goto done;
                    case ClickAction.Release:
                        // Runs exactly once then finishes; not cancellable between checks.
                        await new MouseButtonAction(settings.Button, false).ExecuteAsync(context, CancellationToken.None);
                        clicks = 1;
                        snapshot = new(RunState.Running, clicks, 0, null);
                        Log?.Invoke("已执行一次松开。");
                        goto done;
                }

                long clickStart = Stopwatch.GetTimestamp();
                runInput.Click(settings.Button, position, token);
                var clickedAt = Stopwatch.GetElapsedTime(startedAt, clickStart).TotalMilliseconds;
                if (clicks == 0) firstClick = clickedAt;
                lastClick = clickedAt;
                clicks++;
                snapshot = new(RunState.Running, clicks,
                    clicks > 1 ? (lastClick - firstClick) / (clicks - 1) : 0, null);
                if (clicks % 100 == 0)
                    Log?.Invoke($"进度：{clicks} 次，平均 {snapshot.AverageIntervalMs:F2} ms");

                if (settings.Stop == StopCondition.Count && clicks >= settings.Count) break;
                var delay = random.Next(settings.IntervalMinMs, settings.IntervalMaxMs + 1);
                // Start-to-start intervals include dispatch time and never replay missed clicks.
                WaitAction.WaitUntil(Math.Min(stopAt, clickStart + WaitAction.ToStopwatchTicks(delay)), token);
            }
            done: ;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { reason = "手动停止"; }
        catch (Exception ex) { error = ex.Message; reason = "错误"; }
        finally
        {
            try { runInput.ReleaseAll(); }
            catch (Exception ex) { error = error is null ? ex.Message : $"{error}；释放失败：{ex.Message}"; }
            snapshot = new(error is null ? RunState.Idle : RunState.Error, clicks,
                clicks > 1 ? (lastClick - firstClick) / (clicks - 1) : 0, error);
            Log?.Invoke($"停止（{reason}）：共 {clicks} 次，平均 {snapshot.AverageIntervalMs:F2} ms"
                        + (error is null ? "" : $"，错误：{error}"));
        }
    }

    private static (int, int) PickPoint(ClickSettings settings, Random random, ref int sequence)
    {
        var index = settings.PointOrder == MultiPointOrder.Random
            ? random.Next(settings.Points.Count)
            : sequence++ % settings.Points.Count;
        return (settings.Points[index].X, settings.Points[index].Y);
    }

    // Uniform over the disc, not the square, so corners are not over-sampled.
    private static (int, int) Jitter(ClickSettings settings, Random random, int x, int y)
    {
        if (settings.RandomOffsetRadius <= 0) return (x, y);
        var angle = random.NextDouble() * Math.PI * 2;
        var distance = Math.Sqrt(random.NextDouble()) * settings.RandomOffsetRadius;
        return ((int)Math.Round(x + Math.Cos(angle) * distance),
                (int)Math.Round(y + Math.Sin(angle) * distance));
    }
}
