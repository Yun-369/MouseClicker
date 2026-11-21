using System.Text;
using System.Text.Json;
using MouseClicker.Domain;

namespace MouseClicker.Storage;
public sealed class MacroStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    public (string json, long byteCount) Prepare(MacroDocument doc)
    {
        Validate(doc);
        var json = JsonSerializer.Serialize(doc, WriteOptions);
        long byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > MacroSchema.MaxFileBytes)
            throw new InvalidDataException(
                $"宏序列化后为 {byteCount} 字节，超过上限 {MacroSchema.MaxFileBytes} 字节；未写入文件，已有文件与内存中的宏均保留，请精简后另存。");
        return (json, byteCount);
    }

    public void Save(MacroDocument doc, string path)
    {
        var (json, _) = Prepare(doc);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            File.Move(temp, path, true);
        }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch { }
        }
    }

    public MacroDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("宏文件不存在。", path);
        if (info.Length > MacroSchema.MaxFileBytes)
            throw new InvalidDataException($"宏文件过大（{info.Length} 字节，上限 {MacroSchema.MaxFileBytes}）。");
        MacroDocument? doc;
        try { doc = JsonSerializer.Deserialize<MacroDocument>(File.ReadAllText(path), ReadOptions); }
        catch (JsonException ex) { throw new InvalidDataException($"宏文件不是有效的 JSON：{ex.Message}"); }
        if (doc is null) throw new InvalidDataException("宏文件为空。");
        if (doc.SchemaKind != MacroSchema.Kind)
            throw new InvalidDataException("该文件不是宏文档（缺少宏标识，可能是录制文件），已拒绝。");
        Validate(doc);
        return doc;
    }

    public static void Validate(MacroDocument doc)
    {
        if(doc.SchemaKind != MacroSchema.Kind || doc.SchemaVersion != MacroSchema.CurrentVersion) throw new InvalidDataException("不是支持的基础宏版本。");
        if(doc.CoordinateSpace != "virtualDesktopPhysicalPixels") throw new InvalidDataException("不支持的坐标空间。");
        if(doc.Loops<1 || doc.Loops>MacroSchema.MaxLoopTimes) throw new InvalidDataException("宏轮数无效。");
        if(doc.WindowBind is not null) ValidateWindowBind(doc.WindowBind);
        if(doc.Steps is null || doc.Steps.Count==0) throw new InvalidDataException("宏不包含动作。");
        if(ValidateSteps(doc.Steps,false)>MacroSchema.MaxSteps) throw new InvalidDataException("宏动作数量超限。");
    }
    private static int ValidateSteps(IEnumerable<MacroStep> steps,bool child)
    {
        int count=0;
        foreach(var s in steps)
        {
            if(s is null || !MacroStepType.All.Contains(s.Type)) throw new InvalidDataException($"此宏含已移除或未知动作：{s?.Type}。文件未更改，请使用仅包含七种基础动作的宏。");
            count++;
            if(s.Children is null) throw new InvalidDataException("子动作列表无效。");
            if(s.Type!=MacroStepType.Loop && s.Children.Count>0) throw new InvalidDataException("只有有限循环可以包含子动作。");
            switch(s.Type)
            {
                case MacroStepType.Move: case MacroStepType.Click:
                    CheckCoord(s,"动作");
                    if(s.Type==MacroStepType.Click && s.Button is not ("left" or "middle" or "right")) throw new InvalidDataException("鼠标按钮无效。");
                    break;
                case MacroStepType.Wait:
                    if(s.WaitMs<0 || s.WaitMs>MacroSchema.MaxWaitMs) throw new InvalidDataException("等待时间无效。"); break;
                case MacroStepType.Loop:
                    if(child || s.Times<1 || s.Times>MacroSchema.MaxLoopTimes || s.Children.Count==0) throw new InvalidDataException("循环次数、子动作无效，或存在嵌套循环。");
                    count+=ValidateSteps(s.Children,true); break;
                case MacroStepType.RefRecording:
                    if(string.IsNullOrWhiteSpace(s.RefPath) || s.RefLoops<1 || s.RefLoops>MacroSchema.MaxRefLoops) throw new InvalidDataException("引用路径或轮数无效。"); break;
                case MacroStepType.Key:
                    if(MacroKeys.IsModifierToken(s.Key) || !MacroKeys.TryParse(s.Key,out _,out _)) throw new InvalidDataException("按键无法识别或主键仅为修饰键。"); break;
                case MacroStepType.Text: ValidateText(s.Text,"动作"); break;
            }
        }
        return count;
    }

    public static void ValidateWindowBind(WindowBinding b)
    {
        if (string.IsNullOrWhiteSpace(b.ProcessName))
            throw new InvalidDataException("窗口绑定的进程名为空。");
        if (b.ProcessName.Length > 128)
            throw new InvalidDataException("窗口绑定的进程名过长。");
        if (!WindowTitleRule.All.Contains(b.TitleRule))
            throw new InvalidDataException($"窗口绑定的标题匹配规则无法识别：{b.TitleRule}。");
        if (string.IsNullOrEmpty(b.TitleText))
            throw new InvalidDataException("窗口绑定的标题文本为空。");
        if (b.TitleText.Length > 512)
            throw new InvalidDataException("窗口绑定的标题文本过长。");
        if (b.ClassName.Length > 256)
            throw new InvalidDataException("窗口绑定的类名过长。");
        if (b.ClientWidth <= 0 || b.ClientHeight <= 0)
            throw new InvalidDataException($"窗口绑定的客户区尺寸无效（{b.ClientWidth}×{b.ClientHeight}）。");
        if (b.Dpi <= 0)
            throw new InvalidDataException($"窗口绑定的 DPI 无效（{b.Dpi}）。");
    }
    public static void ValidateText(string? text, string where)
    {
        if (string.IsNullOrEmpty(text))
            throw new InvalidDataException($"{where}的文本内容为空。");
        if (text.Length > MacroSchema.MaxTextLength)
            throw new InvalidDataException($"{where}的文本超长（{text.Length} 字，上限 {MacroSchema.MaxTextLength} 字）。");
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r' || c == '\n' || c == '\t') continue;
            if (char.IsControl(c))
                throw new InvalidDataException($"{where}的文本包含不支持的控制字符（U+{(int)c:X4}）。");
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    throw new InvalidDataException($"{where}的文本包含不成对的代理项（残缺字符）。");
                i++;
            }
            else if (char.IsLowSurrogate(c))
                throw new InvalidDataException($"{where}的文本包含不成对的代理项（残缺字符）。");
        }
    }
    private static void CheckCoord(MacroStep s, string where)
    {
        if (s.X is < -100000 or > 100000 || s.Y is < -100000 or > 100000)
            throw new InvalidDataException($"{where}坐标越界（{s.X},{s.Y}）。");
    }
    public Dictionary<string, RecordingDocument> ResolveReferences(MacroDocument doc, string? macroFilePath,
        Func<string, RecordingDocument> loadRecording)
    {
        string? baseDir = macroFilePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(macroFilePath));
        var map = new Dictionary<string, RecordingDocument>(StringComparer.OrdinalIgnoreCase);
        void Walk(IEnumerable<MacroStep> steps)
        {
            foreach (var s in steps)
            {
                if (s.Type == MacroStepType.RefRecording)
                {
                    string full = Path.IsPathRooted(s.RefPath!)
                        ? Path.GetFullPath(s.RefPath!)
                        : Path.GetFullPath(Path.Combine(baseDir ?? AppContext.BaseDirectory, s.RefPath!));
                    if (!File.Exists(full))
                        throw new FileNotFoundException($"宏引用的录制不存在：{s.RefPath}（解析为 {full}），已在发送输入前拒绝。", full);
                    if (!map.ContainsKey(full))
                    {
                        RecordingDocument rec;
                        try { rec = loadRecording(full); }
                        catch (Exception ex) when (ex is not FileNotFoundException)
                        { throw new InvalidDataException($"宏引用的录制无法加载或已损坏（{s.RefPath}）：{ex.Message}"); }
                        RecordingStore.Validate(rec);
                        map[full] = rec;
                    }
                }
                else if (s.Type == MacroStepType.Loop && s.Children is not null) Walk(s.Children);
            }
        }
        Walk(doc.Steps);
        return map;
    }
    public static string ResolvePath(string refPath, string? macroFilePath)
    {
        string? baseDir = macroFilePath is null ? null : Path.GetDirectoryName(Path.GetFullPath(macroFilePath));
        return Path.IsPathRooted(refPath)
            ? Path.GetFullPath(refPath)
            : Path.GetFullPath(Path.Combine(baseDir ?? AppContext.BaseDirectory, refPath));
    }
    public static string RelativizeToMacro(string picked, string? macroFilePath)
    {
        try
        {
            string fullPick = Path.GetFullPath(picked);
            if (macroFilePath is null) return fullPick;
            string baseDir = Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(macroFilePath))!);
            if (string.Equals(Path.GetPathRoot(fullPick), Path.GetPathRoot(baseDir), StringComparison.OrdinalIgnoreCase))
                return Path.GetRelativePath(baseDir, fullPick);
        }
        catch { /* fall back to absolute */ }
        return picked;
    }
    public static MacroDocument RewriteReferencesForNewDir(MacroDocument source,string? oldPath,string targetPath)
    {
        var copy=source.Clone();
        void Walk(IEnumerable<MacroStep> steps) { foreach(var s in steps) { if(s.Type==MacroStepType.RefRecording) s.RefPath=RelativizeToMacro(ResolvePath(s.RefPath!,oldPath),targetPath); if(s.Type==MacroStepType.Loop) Walk(s.Children); } }
        Walk(copy.Steps); return copy;
    }
    public static bool SameDirectory(string a, string b)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(a))!),
                Path.GetFullPath(Path.GetDirectoryName(Path.GetFullPath(b))!),
                StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
