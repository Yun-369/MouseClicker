namespace MouseClicker.Domain;
using System.Text.Json.Serialization;
public static class MacroStepType
{
    public const string Move="move", Click="click", Wait="wait", Loop="loop", RefRecording="ref", Key="key", Text="text";
    public static readonly string[] All=[Move,Click,Wait,Loop,RefRecording,Key,Text];
    public static readonly string[] AllowedInLoop=[Move,Click,Wait,RefRecording,Key,Text];
    public static string Display(string type)=>type switch { Move=>"鼠标移动",Click=>"鼠标单击",Wait=>"等待",Loop=>"有限循环",RefRecording=>"引用录制",Key=>"键盘按键",Text=>"文本输入",_=>type };
}
public sealed class MacroStep
{
    public string Type { get; set; } = MacroStepType.Wait;
    public int X { get; set; }
    public int Y { get; set; }
    public string Button { get; set; } = "left";
    public int WaitMs { get; set; }
    public int Times { get; set; } = 1;
    public List<MacroStep> Children { get; set; } = [];
    public string? RefPath { get; set; }
    public int RefLoops { get; set; } = 1;
    public string? Key { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public string? Text { get; set; }

    public MacroStep Clone() { var copy = (MacroStep)MemberwiseClone(); copy.Children = Children.Select(c => c.Clone()).ToList(); return copy; }
}
public sealed class MacroDocument
{
    public string SchemaKind { get; set; } = MacroSchema.Kind;
    public int SchemaVersion { get; set; } = MacroSchema.CurrentVersion;
    public string Name { get; set; } = "新建宏";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public int Loops { get; set; } = 1;
    public string CoordinateSpace { get; set; } = "virtualDesktopPhysicalPixels";
    public bool HasEnvironment { get; set; }
    public int Dpi { get; set; }
    public int VirtualLeft { get; set; }
    public int VirtualTop { get; set; }
    public int VirtualWidth { get; set; }
    public int VirtualHeight { get; set; }
    public List<MacroStep> Steps { get; set; } = [];
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WindowBinding? WindowBind { get; set; }
    public static MacroDocument CreateNew()
    {
        var vs = System.Windows.Forms.SystemInformation.VirtualScreen;
        int dpi;
        try
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            dpi = (int)Math.Round(g.DpiX);
        }
        catch { dpi = 0; }
        return new MacroDocument
        {
            Name = "新建宏",
            CreatedAt = DateTimeOffset.Now,
            Loops = 1,
            HasEnvironment = true,
            Dpi = dpi,
            VirtualLeft = vs.Left, VirtualTop = vs.Top,
            VirtualWidth = vs.Width, VirtualHeight = vs.Height
        };
    }

    public MacroDocument Clone()
    {
        var copy = (MacroDocument)MemberwiseClone();
        copy.Steps = Steps.Select(s => s.Clone()).ToList();
        if (WindowBind is not null)
            copy.WindowBind = WindowBind.Clone();
        return copy;
    }
    public void ClearAllMoveClickCoordinates()
    {
        void Walk(List<MacroStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type is MacroStepType.Move or MacroStepType.Click) { s.X = 0; s.Y = 0; }



                if (s.Children is not null) Walk(s.Children);


            }
        }
        Walk(Steps);
    }
}
public sealed class WindowBinding
{
    public string ProcessName { get; set; } = "";
    public string TitleRule { get; set; } = WindowTitleRule.EqualsText;
    public string TitleText { get; set; } = "";
    public string ClassName { get; set; } = "";
    public bool MatchClass { get; set; }
    public int ClientWidth { get; set; }
    public int ClientHeight { get; set; }
    public int Dpi { get; set; }

    public WindowBinding Clone() => (WindowBinding)MemberwiseClone();

    public string Describe() =>
        $"进程 {ProcessName}，标题{WindowTitleRule.Label(TitleRule)}“{TitleText}”"
        + (string.IsNullOrEmpty(ClassName) ? "" : (MatchClass ? $"，类名 {ClassName}（校验）" : $"，类名 {ClassName}（仅存档）"))
        + $"，绑定客户区 {ClientWidth}×{ClientHeight} @ DPI {Dpi}";
}

public static class WindowTitleRule
{
    public const string EqualsText = "equals";
    public const string Contains = "contains";
    public const string StartsWith = "starts";
    public const string EndsWith = "ends";
    public static readonly string[] All = [EqualsText, Contains, StartsWith, EndsWith];

    public static bool Matches(string rule, string titleText, string title) => rule switch
    {
        EqualsText => string.Equals(title, titleText, StringComparison.OrdinalIgnoreCase),
        Contains => title.Contains(titleText, StringComparison.OrdinalIgnoreCase),
        StartsWith => title.StartsWith(titleText, StringComparison.OrdinalIgnoreCase),
        EndsWith => title.EndsWith(titleText, StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    public static string Label(string rule) => rule switch
    {
        EqualsText => "完全等于",
        Contains => "包含",
        StartsWith => "以…开头",
        EndsWith => "以…结尾",
        _ => rule
    };
}

public static class MacroSchema
{
    public const string Kind = "mouseclicker-macro";
    public const int CurrentVersion = 1;
    public const long MaxFileBytes = 4 * 1024 * 1024;
    public const int MaxSteps = 10000;
    public const int MaxLoopTimes = 1_000_000;
    public const int MaxWaitMs = 3_600_000;
    public const int MaxRefLoops = 1_000_000;
    public const int MaxTextLength = 8000;
}
