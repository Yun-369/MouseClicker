namespace MouseClicker.Domain;

public enum RunState { Idle, Armed, Running, Stopping, Error }
public enum ClickButton { Left, Middle, Right }
public enum PositionMode { FollowCursor, Fixed, MultiPoints }
public enum ClickAction { Click, Press, Release }
public enum StopCondition { Unlimited, Count, Duration }
public enum MultiPointOrder { Sequential, Random }
public enum TriggerMode { HotkeyToggle, HoldKey }

public sealed record PointSpec(int X, int Y);

public sealed record ClickSettings
{
    public ClickButton Button { get; init; } = ClickButton.Left;
    public ClickAction Action { get; init; } = ClickAction.Click;
    public PositionMode Position { get; init; } = PositionMode.FollowCursor;
    public int X { get; init; }
    public int Y { get; init; }
    public List<PointSpec> Points { get; init; } = [];
    public MultiPointOrder PointOrder { get; init; } = MultiPointOrder.Sequential;
    // 0 disables random offset; otherwise clicks land within this radius (px) of the target.
    public int RandomOffsetRadius { get; init; }
    public int IntervalMinMs { get; init; } = 100;
    public int IntervalMaxMs { get; init; } = 100;
    public StopCondition Stop { get; init; } = StopCondition.Count;
    public int Count { get; init; } = 10;
    public int DurationMs { get; init; } = 10000;
    public TriggerMode Trigger { get; init; } = TriggerMode.HotkeyToggle;
    // Hold-to-run trigger key as a Win32 virtual-key code (F1..F12 = 0x70..0x7B).
    public int HoldKeyVk { get; init; } = 0x77;
    public bool HoldCtrl { get; init; }
    public bool HoldAlt { get; init; }
    public bool HoldShift { get; init; }

    public void Validate()
    {
        ValidateForStorage();
        if (Action == ClickAction.Click && IntervalMinMs > IntervalMaxMs)
            throw new ArgumentException("最小间隔不能大于最大间隔。");
        if (Action != ClickAction.Release && Position == PositionMode.MultiPoints && Points.Count == 0)
            throw new ArgumentException("多点模式至少需要一个坐标点。");
    }

    // An unfinished configuration can still be saved and edited after restart.
    internal void ValidateForStorage()
    {
        if (!Enum.IsDefined(Button) || !Enum.IsDefined(Position) || !Enum.IsDefined(Action)
            || !Enum.IsDefined(Stop) || !Enum.IsDefined(PointOrder) || !Enum.IsDefined(Trigger))
            throw new ArgumentException("不支持的选项值。");

        // Interval drives Click only; Press/Release run a single time.
        if (IntervalMinMs is < 1 or > 10000 || IntervalMaxMs is < 1 or > 10000)
            throw new ArgumentException("间隔需为 1–10000 ms。");

        if (Count is < 0 or > 1000000)
            throw new ArgumentException("次数需为 0–1000000。");
        if (DurationMs is < 1000 or > 3600000)
            throw new ArgumentException("时长需为 1000–3600000 ms。");
        if (RandomOffsetRadius is < 0 or > 500)
            throw new ArgumentException("随机偏移半径需为 0–500 px。");
        if (!IsAllowedHotkey(HoldKeyVk, HoldCtrl, HoldAlt))
            throw new ArgumentException("启停热键支持鼠标侧键、F1–F12，或带 Ctrl/Alt 的字母、数字和空格。");

        if (Points is null || Points.Any(point => point is null))
            throw new ArgumentException("多点列表无效。");
        if (Points.Count > 100)
            throw new ArgumentException("多点列表最多 100 个点。");
    }

    public const int MouseSide1 = 0x05, MouseSide2 = 0x06;
    public static bool IsMouseSideButton(int vk) => vk is MouseSide1 or MouseSide2;

    public static bool IsAllowedHotkey(int vk, bool ctrl, bool alt)
        => IsMouseSideButton(vk) || vk is >= 0x70 and <= 0x7B
            || ((ctrl || alt) && (vk is >= 0x30 and <= 0x39 or >= 0x41 and <= 0x5A or 0x20));

    public string Describe()
    {
        var interval = IntervalMinMs == IntervalMaxMs ? $"{IntervalMinMs} ms" : $"{IntervalMinMs}–{IntervalMaxMs} ms";
        var position = Position switch
        {
            PositionMode.FollowCursor => "跟随光标",
            PositionMode.Fixed => $"固定({X},{Y})",
            PositionMode.MultiPoints => $"多点×{Points.Count}({(PointOrder == MultiPointOrder.Random ? "随机" : "顺序")})",
            _ => "?"
        };
        var stop = Stop switch
        {
            StopCondition.Unlimited => "无限",
            StopCondition.Count => $"{Count} 次",
            StopCondition.Duration => $"{DurationMs} ms",
            _ => "?"
        };
        var action = Action switch
        {
            ClickAction.Click => "单击",
            ClickAction.Press => "按下保持",
            ClickAction.Release => "松开",
            _ => "?"
        };
        return $"{Button} {action} @ {position}，间隔 {interval}，停止 {stop}"
               + (RandomOffsetRadius > 0 ? $"，偏移±{RandomOffsetRadius}px" : "");
    }
}

// Shared document envelope for future macros. Execution support is added per type.
public sealed record ScriptDocument
{
    public int SchemaVersion { get; init; } = 1;
    public string Name { get; init; } = "新脚本";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CoordinateSpace { get; init; } = "virtualDesktopPhysicalPixels";
    public int Dpi { get; init; } = 96;
    public List<ScriptStep> Actions { get; init; } = [];
}

public sealed record ScriptStep(string Type, int DelayMs = 0, int X = 0, int Y = 0,
    ClickButton Button = ClickButton.Left);

// ---- P1 recording -------------------------------------------------------

public static class RecordingEventType
{
    public static bool IsMouse(string type)=>type is MouseMove or MouseDown or MouseUp or MouseWheel;
    public const string MouseMove = "mouse_move";
    public const string MouseDown = "mouse_down";
    public const string MouseUp = "mouse_up";
    public const string MouseWheel = "mouse_wheel";
    public const string KeyDown = "key_down";
    public const string KeyUp = "key_up";

    public static readonly IReadOnlySet<string> All =
        new HashSet<string> { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp };
}

// One captured input event. DelayMs is the gap from the previous event (used for
// original-speed playback); TimeMs is the cumulative offset from record start.
public sealed record RecordedEvent
{
    public string Type { get; init; } = "";
    public int DelayMs { get; init; }
    public int TimeMs { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    // "left" | "middle" | "right" for mouse down/up.
    public string? Button { get; init; }
    // Signed wheel delta (one notch usually 120).
    public int Delta { get; init; }
    // Win32 virtual-key code for key down/up.
    public ushort Vk { get; init; }
    // Hardware scan code captured by the low-level hook (0 for legacy v1 files /
    // non-key events). Extended marks the right-side / cluster / numpad keys that
    // Windows sends with KEYEVENTF_EXTENDEDKEY (Right Ctrl/Alt, arrows, Insert/
    // Delete/Home/End/PageUp/Down, numpad Enter/Slash, NumLock…). Numpad Enter and
    // the main Enter share VK 0x0D and can ONLY be told apart by this flag.
    // Both are additive optional v1 fields: old files missing them still load and
    // fall back to VK-based extended-key inference at injection time.
    public ushort Scan { get; init; }
    public bool Extended { get; init; }
}

// Versioned recording file. Coordinates are virtual-desktop physical pixels;
// DPI / virtual-screen bounds are recorded so a later stage can detect mismatch.
public sealed record RecordingDocument
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public WindowBinding? WindowBind { get; init; }
    public int SchemaVersion { get; init; } = RecordingSchema.CurrentVersion;
    public string Name { get; init; } = "录制";
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string CoordinateSpace { get; init; } = "virtualDesktopPhysicalPixels";
    public int Dpi { get; init; } = 96;
    public int VirtualLeft { get; init; }
    public int VirtualTop { get; init; }
    public int VirtualWidth { get; init; }
    public int VirtualHeight { get; init; }
    public List<RecordedEvent> Events { get; init; } = [];
}

public static class RecordingSchema
{
    public const int CurrentVersion = 1;
    public const long MaxFileBytes = 8 * 1024 * 1024;
    public const int MaxEvents = 100_000;
    public const int MaxDelayMs = 3_600_000; // single gap capped at 1 hour
}
