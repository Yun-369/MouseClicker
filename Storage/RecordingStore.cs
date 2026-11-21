using System.Text;
using System.Text.Json;
using MouseClicker.Domain;

namespace MouseClicker.Storage;

// Save/load recorded scripts as versioned JSON. Loading validates size, schema,
// event count and every event's parameters up front: a damaged or unsupported
// file is rejected before playback is ever enabled. Saving runs the SAME
// validation and measures the real UTF-8 byte size before touching the target
// file, so an over-limit recording never overwrites an existing file.
public sealed class RecordingStore
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    // Serialize + validate + measure without writing anything. The caller can use
    // the returned text/ size to decide; nothing on disk is changed on failure.
    public (string json, long byteCount) Prepare(RecordingDocument document)
    {
        Validate(document); // same gate as Load
        var json = JsonSerializer.Serialize(document, WriteOptions);
        long byteCount = Encoding.UTF8.GetByteCount(json);
        if (byteCount > RecordingSchema.MaxFileBytes)
            throw new InvalidDataException(
                $"录制序列化后为 {byteCount} 字节，超过上限 {RecordingSchema.MaxFileBytes} 字节；未写入文件，已有文件与内存中的录制均保留，请删减动作后另存。");
        return (json, byteCount);
    }

    public void Save(RecordingDocument document, string path)
    {
        // Validate + size first; only then touch the disk, so a failure can never
        // truncate/overwrite an existing file.
        var (json, _) = Prepare(document);
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

    public RecordingDocument Load(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("录制文件不存在。", path);
        if (info.Length > RecordingSchema.MaxFileBytes)
            throw new InvalidDataException($"录制文件过大（{info.Length} 字节，上限 {RecordingSchema.MaxFileBytes}）。");

        RecordingDocument? doc;
        try { doc = JsonSerializer.Deserialize<RecordingDocument>(File.ReadAllText(path), ReadOptions); }
        catch (JsonException ex) { throw new InvalidDataException($"录制文件不是有效的 JSON：{ex.Message}"); }
        if (doc is null) throw new InvalidDataException("录制文件为空。");

        Validate(doc);
        return doc;
    }

    public static void Validate(RecordingDocument doc)
    {
        ArgumentNullException.ThrowIfNull(doc);
        var binding = doc.WindowBind;
        if(binding is not null)MacroStore.ValidateWindowBind(binding);
        // Compatibility rule: the on-disk format stays at v1 and evolves only by
        // ADDING optional fields (e.g. per-event Scan/Extended). Older v1 files that
        // lack those fields deserialize with defaults and still load; an unknown /
        // newer major version is rejected outright so a format change can never be
        // silently misread.
        if (doc.SchemaVersion != RecordingSchema.CurrentVersion)
            throw new InvalidDataException($"不支持的录制版本（{doc.SchemaVersion}），当前仅支持 v{RecordingSchema.CurrentVersion}。");
        if (doc.CoordinateSpace != "virtualDesktopPhysicalPixels")
            throw new InvalidDataException("录制坐标空间无法识别，拒绝加载。");
        if (doc.Events is null || doc.Events.Count == 0)
            throw new InvalidDataException("录制不包含任何动作。");
        if (doc.Events.Count > RecordingSchema.MaxEvents)
            throw new InvalidDataException($"动作数量超限（{doc.Events.Count}，上限 {RecordingSchema.MaxEvents}）。");

        int previousTime = -1;
        int cumulative = 0;
        for (int i = 0; i < doc.Events.Count; i++)
        {
            var e = doc.Events[i] ?? throw new InvalidDataException($"第 {i + 1} 个动作为空。");
            string where = $"第 {i + 1} 个动作";
            if (!RecordingEventType.All.Contains(e.Type))
                throw new InvalidDataException($"{where}类型未知：{e.Type}。");
            if (e.DelayMs < 0 || e.TimeMs < 0)
                throw new InvalidDataException($"{where}时间戳为负。");
            if (e.DelayMs > RecordingSchema.MaxDelayMs)
                throw new InvalidDataException($"{where}相邻等待过长（{e.DelayMs} ms，上限 {RecordingSchema.MaxDelayMs}）。");
            // Execution timing uses DelayMs (gap from the previous event); TimeMs is
            // the cumulative timeline for editors. The running sum of DelayMs must
            // equal TimeMs, otherwise the file is contradictory and is rejected.
            try { cumulative = checked(cumulative + e.DelayMs); }
            catch (OverflowException ex) { throw new InvalidDataException("录制累计时间超出支持范围。", ex); }
            if (e.TimeMs != cumulative)
                throw new InvalidDataException(
                    $"{where}相对时间戳与相邻间隔不一致（TimeMs={e.TimeMs}，按 DelayMs 累计应为 {cumulative}）；回放以 DelayMs 为准，请修正文件。");
            if (e.TimeMs < previousTime)
                throw new InvalidDataException($"{where}相对时间戳倒退，文件可能已损坏。");
            previousTime = e.TimeMs;

            bool mouse = e.Type is RecordingEventType.MouseMove or RecordingEventType.MouseDown
                or RecordingEventType.MouseUp or RecordingEventType.MouseWheel;
            if (mouse && (e.X is < -100000 or > 100000 || e.Y is < -100000 or > 100000))
                throw new InvalidDataException($"{where}坐标越界。");
            if (mouse && binding is not null && (e.X < 0 || e.Y < 0 || e.X >= binding.ClientWidth || e.Y >= binding.ClientHeight))
                throw new InvalidDataException($"{where}坐标超出绑定窗口客户区（{binding.ClientWidth}×{binding.ClientHeight}）。");

            if (e.Type is RecordingEventType.MouseDown or RecordingEventType.MouseUp)
            {
                if (e.Button is not ("left" or "middle" or "right"))
                    throw new InvalidDataException($"{where}鼠标键无效。");
            }
            if (e.Type == RecordingEventType.MouseWheel && e.Delta is < short.MinValue or > short.MaxValue)
                throw new InvalidDataException($"{where}滚轮量越界。");
            if (e.Type is RecordingEventType.KeyDown or RecordingEventType.KeyUp)
            {
                if (e.Vk is < 1 or > 254)
                    throw new InvalidDataException($"{where}虚拟键码越界。");
                // Scan/Extended are additive v1 fields: a missing/zero scan is a legacy
                // event and is allowed (the driver infers extended keys from VK); a
                // present scan must be a valid PS/2 set-1 scan code (0..255).
                if (e.Scan > 0xFF)
                    throw new InvalidDataException($"{where}键盘扫描码越界。");
            }
        }
    }
}
