using System.Diagnostics;
using System.IO;
using MouseClicker.Actions;
using MouseClicker.Domain;
using MouseClicker.Platform;
using MouseClicker.Storage;

namespace MouseClicker.Engine;
public sealed class MacroEngine
{
    private readonly IInputDriver input;
    private ActionContext context;
    private readonly object stateGate = new();
    private CancellationTokenSource? cts;
    private Task? worker;
    private bool finished;
    private PlaybackEngine? currentChild;
    private MacroSnapshot snapshot = new(RunState.Idle, 0, 0, null, 0, 1, null, null, null);

    private string? inputInProgress;
    private WindowTargetInfo? windowTarget;
    private WindowBinding? bind;
    public MacroEngine(IInputDriver input) { this.input = input; context = new ActionContext(input); }
    public bool IsActive => Volatile.Read(ref worker) is { IsCompleted: false };
    public MacroSnapshot Snapshot => snapshot;
    public Action<string>? Log;
    public event Action? StateChanged;

    public sealed record MacroSnapshot(RunState State,int StepIndex,int StepTotal,string? Current,int MacroLoop,int MacroLoops,string? Inner,string? Error,string? Outcome);
    public void Start(MacroDocument doc, IReadOnlyDictionary<string, RecordingDocument> refs, string? macroFilePath,
        WindowTargetInfo? windowTarget = null)
    {
        if (IsActive) throw new InvalidOperationException("宏正在运行，请先停止。");
        // A previous failed/cancelled text action may have left its attribution
        // marker set.  Each new run starts with a clean input-progress state.
        inputInProgress = null;
        MacroStore.Validate(doc);
        if (doc.WindowBind is null && windowTarget is not null)
            throw new InvalidOperationException("宏未绑定窗口，却传入了目标窗口；请重新加载宏。");
        if (doc.WindowBind is not null && windowTarget is null)
            throw new InvalidOperationException("宏绑定了窗口，但目标尚未解析成功；未发送任何输入。");
        if (doc.WindowBind is not null && windowTarget is not null)
        {
            foreach (var rec in refs.Values)
            {
                if (rec.WindowBind is not null)
                    throw new InvalidDataException(
                        "窗口绑定宏不能引用另一个已绑定窗口的录制：子回放可能激活不同目标。"
                        + "请取消该录制的窗口绑定，或把它作为独立录制回放。未发送任何输入。");
                bool hasMouse = rec.Events.Any(e => e.Type is RecordingEventType.MouseMove
                    or RecordingEventType.MouseDown or RecordingEventType.MouseUp or RecordingEventType.MouseWheel);
                if (hasMouse)
                    throw new InvalidDataException(
                        "窗口绑定宏不能引用含鼠标事件的录制：录制的鼠标坐标是屏幕绝对坐标，本版本无法当作窗口客户区坐标回放。"
                        + "请改用纯键盘录制，或对该录制取消窗口绑定后单独回放。未发送任何输入。");
            }
        }
        EnsureReferencesResolved(doc, refs, macroFilePath);
        EnsureCoordinatesFit(doc, refs);

        ActionContext runContext = new(input);
        if (doc.WindowBind is not null && windowTarget is not null)
            // Macro coordinates are converted to screen pixels by
            // MoveBoundOrScreen; the guarded driver must therefore not convert
            // them a second time.  It still checks foreground, window geometry,
            // actual cursor position and click hit testing at send time.
            runContext = new ActionContext(new BoundInputDriver(input, windowTarget, doc.WindowBind, screenCoordinates: true));

        var tokenCts = new CancellationTokenSource();
        var token = tokenCts.Token;
        lock (stateGate)
        {
            finished = false;
            cts = tokenCts;
            this.windowTarget = windowTarget;
            bind = doc.WindowBind;
            context = runContext;

            snapshot = new MacroSnapshot(RunState.Armed, 0, doc.Steps.Count, null, 0, doc.Loops, null, null, null);
        }
        worker = Task.Run(() => RunAsync(doc, refs, macroFilePath, token, tokenCts));
    }
    // Every ref step (top level AND inside finite loops) must already be present in the
    // caller-resolved dictionary and be a valid, executable recording - checked BEFORE
    // the worker starts, the target is activated or any input is sent. Without this
    // gate a direct engine call could send the earlier clicks/text and only discover a
    // missing reference when it reaches it. The lookup uses the exact same path rule as
    // RunReference (absolute stays absolute, relative resolves against the macro file),
    // so the keys produced by MacroStore.ResolveReferences always match; nothing is
    // re-read from disk here, so the dictionary the caller validated is what runs.
    public static void EnsureReferencesResolved(MacroDocument doc, IReadOnlyDictionary<string, RecordingDocument> refs,
        string? macroFilePath)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(refs);
        void Walk(IEnumerable<MacroStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type == MacroStepType.Loop && s.Children is not null) { Walk(s.Children); continue; }
                if (s.Type != MacroStepType.RefRecording) continue;
                string full = Path.IsPathRooted(s.RefPath!)
                    ? Path.GetFullPath(s.RefPath!)
                    : MacroStore.ResolvePath(s.RefPath!, macroFilePath);
                if (!refs.TryGetValue(full, out var rec))
                    throw new InvalidDataException(
                        $"宏引用的录制未在启动前解析：{s.RefPath}（解析为 {full}）。"
                        + "引擎要求开始前把宏引用的所有录制加载并通过校验；已取消，未发送任何输入。");
                RecordingStore.Validate(rec);
            }
        }
        Walk(doc.Steps);
    }

    public static void EnsureCoordinatesFit(MacroDocument doc, IReadOnlyDictionary<string, RecordingDocument>? refs = null)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var bind = doc.WindowBind;
        void CheckSteps(IEnumerable<MacroStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type is MacroStepType.Move or MacroStepType.Click)
                {
                    if (bind is not null)
                    {
                        if (s.X < 0 || s.Y < 0 || s.X >= bind.ClientWidth || s.Y >= bind.ClientHeight)
                            throw new InvalidDataException(
                                $"动作“{MacroStepType.Display(s.Type)}（{s.X},{s.Y}）”超出绑定窗口客户区范围"
                                + $"（0..{bind.ClientWidth - 1}，0..{bind.ClientHeight - 1}）；已取消，未发送任何输入。");
                    }
                    else if (!bounds.Contains(s.X, s.Y))
                        throw new InvalidDataException(
                            $"动作“{MacroStepType.Display(s.Type)}（{s.X},{s.Y}）”不在当前虚拟桌面范围内"
                            + $"（{bounds.Left},{bounds.Top}，{bounds.Width}×{bounds.Height}）；已取消，未发送任何输入。");
                }
                if(s.Type==MacroStepType.Loop) CheckSteps(s.Children);
            }
        }
        CheckSteps(doc.Steps);
        if (refs is not null)
            foreach (var rec in refs.Values) PlaybackEngine.EnsureCoordinatesFit(rec);
    }
    public static string? DescribeEnvironmentMismatch(MacroDocument doc,
        IReadOnlyDictionary<string, RecordingDocument> refs, int currentDpi)
    {
        var bounds = System.Windows.Forms.SystemInformation.VirtualScreen;
        var diffs = new List<string>();
        if (doc.WindowBind is not null)
        {
        }
        else if (!doc.HasEnvironment)
            diffs.Add("宏未记录创建时的虚拟屏/DPI（旧文件，按未知处理，不能假定为当前桌面）");
        else
        {
            if (doc.VirtualWidth > 0 && doc.VirtualHeight > 0 &&
                (doc.VirtualLeft != bounds.Left || doc.VirtualTop != bounds.Top
                 || doc.VirtualWidth != bounds.Width || doc.VirtualHeight != bounds.Height))
                diffs.Add($"宏录制时虚拟屏（{doc.VirtualLeft},{doc.VirtualTop}，{doc.VirtualWidth}×{doc.VirtualHeight}）与当前不同");
            if (doc.Dpi > 0 && doc.Dpi != currentDpi)
                diffs.Add($"宏录制时 DPI {doc.Dpi} 与当前 {currentDpi} 不同");
        }
        foreach (var kv in refs)
        {
            var recDiffs = PlaybackEngine.EnvironmentDiffs(kv.Value, currentDpi);
            foreach (var d in recDiffs)
                diffs.Add($"引用录制 {System.IO.Path.GetFileName(kv.Key)}：{d}");
        }
        if (diffs.Count == 0) return null;
        return "宏或其引用录制的环境与当前桌面不一致：" + string.Join("；", diffs)
            + "。坐标按录制时的物理像素回放，目标位置可能偏移。仍要继续吗？";
    }
    private async Task RunAsync(MacroDocument doc, IReadOnlyDictionary<string, RecordingDocument> refs,
        string? macroFilePath,
        CancellationToken token, CancellationTokenSource tokenCts)
    {
        using var timerResolution = new TimerResolution();
        string reason = "宏执行完成";
        string? error = null;
        try
        {
            if (windowTarget is not null && bind is not null)
            {
                Log?.Invoke($"窗口绑定：{windowTarget.ProcessName}“{windowTarget.Title}”，客户区 {bind.ClientWidth}×{bind.ClientHeight}；"
                    + "移动/单击使用客户区坐标（每次动作前重新换算），键盘/文本输入前校验前台。");
                string? fgErr = EnsureForegroundOnce();
                if (fgErr is not null)
                    throw new InvalidDataException($"{fgErr}已停止，未发送任何输入。");
            }
            Log?.Invoke($"开始执行宏：{doc.Steps.Count} 个顶层动作，整体 {doc.Loops} 轮。");
            for (int macroLoop = 1; macroLoop <= doc.Loops; macroLoop++)
            {
                token.ThrowIfCancellationRequested();
                Publish(snapshot with { State = RunState.Running, MacroLoop = macroLoop });
                await RunSteps(doc.Steps, refs, macroFilePath, token);
                input.ReleaseAll();
            }
        }
        catch (OperationCanceledException)
        {
            reason = "宏已手动停止" + Attribution();
        }
        catch (Exception ex)
        {
            reason = "宏执行错误" + Attribution();
            error = ex.Message; Log?.Invoke($"宏执行错误：{ex.Message}");
        }
        finally
        {
            try { input.ReleaseAll(); }
            catch (Exception rel) { error = error is null ? rel.Message : $"{error}；清理按键失败：{rel.Message}"; }
            lock (stateGate)
            {
                var last = snapshot;
                snapshot = new MacroSnapshot(error is null ? RunState.Idle : RunState.Error,
                    last.StepIndex, last.StepTotal, last.Current, last.MacroLoop, last.MacroLoops,
                    last.Inner, error, error is null ? reason : $"{reason}：{error}");
                finished = true;
                currentChild = null;
                inputInProgress = null;
            }
            StateChanged?.Invoke();
            tokenCts.Dispose();
        }
    }
    private async Task RunSteps(IReadOnlyList<MacroStep> steps, IReadOnlyDictionary<string, RecordingDocument> refs,
        string? macroFilePath, CancellationToken token)
    {
        for (int i = 0; i < steps.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var s = steps[i];
            string label = Describe(s);
            Publish(snapshot with { State = RunState.Running, StepIndex = i + 1, StepTotal = steps.Count, Current = label });

            switch (s.Type)
            {
                case MacroStepType.Move:
                    await MoveBoundOrScreen(s.X, s.Y, hitTest: false, token);
                    break;
                case MacroStepType.Click:
                    var btn = ParseButton(s.Button);
                    await MoveBoundOrScreen(s.X, s.Y, hitTest: true, token);
                    await new MouseButtonAction(btn, true).ExecuteAsync(context, token);
                    await new MouseButtonAction(btn, false).ExecuteAsync(context, token);
                    break;
                case MacroStepType.Wait:
                    await new WaitAction(s.WaitMs).ExecuteAsync(context, token);
                    break;
                case MacroStepType.Loop:
                    for (int k = 1; k <= s.Times; k++)
                    {
                        token.ThrowIfCancellationRequested();
                        Publish(snapshot with { Inner = $"循环 {k}/{s.Times}" });
                        await RunSteps(s.Children, refs, macroFilePath, token);
                    }
                    Publish(snapshot with { Inner = null });
                    break;
                case MacroStepType.RefRecording:
                    await RunReference(s, refs, macroFilePath, token);
                    break;
                case MacroStepType.Key:
                    if (!MacroKeys.TryParse(s.Key, out ushort kvk, out bool kext))
                        throw new InvalidDataException($"按键无法识别：{s.Key}");
                    VerifyTargetForInput();
                    await new KeyChordAction(kvk, kext, s.Ctrl, s.Alt, s.Shift).ExecuteAsync(context, token);
                    break;
                case MacroStepType.Text:
                    await RunTextInputAsync(s.Text ?? "", "文本输入", token);
                    Log?.Invoke($"文本输入完成：{s.Text?.Length ?? 0} 个字符。");
                    break;
                default:
                    throw new InvalidOperationException($"未知宏动作：{s.Type}");
            }
        }
    }
    public void Stop()
    {
        lock (stateGate)
        {
            var w = worker;
            if (finished || w is null) return;
            try { cts?.Cancel(); } catch (ObjectDisposedException) { }
            try { currentChild?.Stop(); } catch { /* child stop best-effort */ }
            snapshot = snapshot with { State = RunState.Stopping };
        }
    }
    public async Task StopAsync()
    {
        Stop();
        var t = worker;
        if (t is not null)
        {
            try { await t; } catch { /* surfaced in snapshot */ }
        }
    }
    private async Task RunTextInputAsync(string value, string descriptor, CancellationToken token)
    {
        VerifyTargetForInput();
        inputInProgress = descriptor;
        await new TextInputAction(value, 32, MakeInputGuard()).ExecuteAsync(context, token);
        inputInProgress = null;
    }
    private async Task MoveBoundOrScreen(int x, int y, bool hitTest, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (windowTarget is null || bind is null)
        {
            await new MoveAction(x, y).ExecuteAsync(context, token);
            return;
        }
        VerifyTargetForInput();
        if (context.Input is BoundInputDriver guarded && guarded.UsesScreenCoordinates)
        {
            // Keep the client point in the guarded driver so a window move after
            // this conversion is detected again at ButtonDown/ButtonUp.
            Point screen = guarded.MoveClient(x, y);
            if (hitTest && !WindowTargetResolver.IsPointOverTarget(windowTarget.Hwnd, screen.X, screen.Y, out string? guardHitErr))
                throw new InvalidDataException($"{guardHitErr}已停止，未发送点击。");
            return;
        }
        if (!WindowTargetResolver.ClientToScreenPoint(windowTarget.Hwnd, x, y, out int sx, out int sy))
            throw new InvalidDataException("无法获取目标窗口客户区位置，已停止，未发送后续输入。");
        if (hitTest && !WindowTargetResolver.IsPointOverTarget(windowTarget.Hwnd, sx, sy, out string? hitErr))
            throw new InvalidDataException($"{hitErr}已停止，未发送点击。");
        await new MoveAction(sx, sy).ExecuteAsync(context, token);
    }
    private void VerifyTargetForInput()
    {
        if (windowTarget is null || bind is null) return;
        if (!WindowTargetResolver.EnsureUsable(windowTarget, bind, out string? err))
            throw new InvalidDataException($"{err}已停止，未发送输入。");
        if (!WindowTargetResolver.IsForeground(windowTarget.Hwnd))
            throw new InvalidDataException("目标窗口不在前台（运行期间不会自动抢回焦点），已停止，未发送输入。");
    }
    private Func<string?>? MakeInputGuard(string tail = "已停止，未发送剩余文本。")
    {
        if (windowTarget is null || bind is null) return null;
        return () =>
        {
            if (!WindowTargetResolver.EnsureUsable(windowTarget, bind, out string? err))
                return $"{err}{tail}";
            if (!WindowTargetResolver.IsForeground(windowTarget.Hwnd))
                return $"目标窗口不在前台（运行期间不会自动抢回焦点），{tail}";
            return null;
        };
    }
    private void VerifyTargetUsable()
    {
        if (windowTarget is null || bind is null) return;
        if (!WindowTargetResolver.EnsureUsable(windowTarget, bind, out string? err))
            throw new InvalidDataException($"{err}已停止，未发送后续输入。");
    }
    private string? EnsureForegroundOnce()
    {
        var hwnd = windowTarget!.Hwnd;
        if (!WindowTargetResolver.EnsureUsable(windowTarget, bind!, out string? err))
            return err;
        if (WindowTargetResolver.IsForeground(hwnd)) return null;
        if (!WindowTargetResolver.TryBringForeground(hwnd))
            return "目标窗口不在前台且无法激活。";
        Thread.Sleep(60); // give the foreground switch a beat to settle
        return WindowTargetResolver.IsForeground(hwnd) ? null : "目标窗口不在前台（激活未生效，焦点仍在其他窗口）。";
    }
    private async Task RunReference(MacroStep s, IReadOnlyDictionary<string, RecordingDocument> refs,
        string? macroFilePath, CancellationToken token)
    {
        string full = Path.IsPathRooted(s.RefPath!) ? Path.GetFullPath(s.RefPath!)
            : MacroStore.ResolvePath(s.RefPath!, macroFilePath);
        if (!refs.TryGetValue(full, out var rec))
            throw new InvalidOperationException($"宏引用的录制未通过预检：{s.RefPath}");
        VerifyTargetForInput();
        var child = new PlaybackEngine(input);
        child.InputGuard = MakeInputGuard("已停止，未发送后续按键。");
        child.Log += msg => Log?.Invoke(msg);
        currentChild = child;
        child.Start(rec, 1.0, s.RefLoops);
        var task = child.WorkerTask ?? throw new InvalidOperationException("子回放未启动。");
        using (token.Register(() => { try { child.Stop(); } catch { } }))
        {
            await task;
        }
        currentChild = null;
        var cs = child.Snapshot;
        if (cs.State == RunState.Error)
            throw new InvalidOperationException($"引用录制回放失败（{s.RefPath}）：{cs.Error}");
        token.ThrowIfCancellationRequested();
    }
    private void Publish(MacroSnapshot next)
    {
        snapshot = next;
        StateChanged?.Invoke();
    }
    private string Attribution()=>inputInProgress is null ? "" : $"（{inputInProgress}可能已部分输入）";

    private static ClickButton ParseButton(string? b) => b switch
    {
        "middle" => ClickButton.Middle,
        "right" => ClickButton.Right,
        _ => ClickButton.Left
    };

    public static string Describe(MacroStep s) => s.Type switch
    {
        MacroStepType.Move => $"移动到（{s.X},{s.Y}）",
        MacroStepType.Click => $"{ButtonName(s.Button)}单击（{s.X},{s.Y}）",
        MacroStepType.Wait => $"等待 {s.WaitMs} ms",
        MacroStepType.Loop => $"循环 {s.Times} 次（{s.Children.Count} 个子动作）",
        MacroStepType.RefRecording => $"引用录制 {s.RefPath} ×{s.RefLoops}",
        MacroStepType.Key => $"按键 {MacroKeys.ChordLabel(s.Key, s.Ctrl, s.Alt, s.Shift)}",
        MacroStepType.Text => $"文本输入（{(s.Text ?? "").Length} 字）",
        _ => s.Type
    };

    private static string ButtonName(string? b) => b switch
    {
        "middle" => "中键", "right" => "右键", _ => "左键"
    };
}
