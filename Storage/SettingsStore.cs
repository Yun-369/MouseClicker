using System.Text.Json;
using MouseClicker.Domain;

namespace MouseClicker.Storage;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private readonly string path;

    public SettingsStore() : this(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MouseClicker", "settings.json")) { }

    internal SettingsStore(string path) => this.path = path;

    public ClickSettings Load()
    {
        if (!File.Exists(path)) return new();
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var value = doc.RootElement.TryGetProperty("IntervalMs", out _)
            ? MigrateLegacy(doc.RootElement)
            : JsonSerializer.Deserialize<ClickSettings>(json, Options)
              ?? throw new InvalidDataException("设置文件为空。");
        value.ValidateForStorage();
        return value;
    }

    // Pre-P0 files stored a single IntervalMs and used Count=0 as "unlimited".
    private static ClickSettings MigrateLegacy(JsonElement root)
    {
        int Interval(string name, int fallback)
            => root.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : fallback;
        bool Flag(string name) => root.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True;
        T Enum<T>(string name, T fallback) where T : struct, Enum
        {
            if (!root.TryGetProperty(name, out var v) || !v.TryGetInt32(out var i) || !System.Enum.IsDefined(typeof(T), i))
                return fallback;
            return (T)(object)i;
        }
        var count = Interval("Count", 10);
        var interval = Math.Clamp(Interval("IntervalMs", 100), 1, 10000);
        return new ClickSettings
        {
            Button = Enum<ClickButton>("Button", ClickButton.Left),
            Position = Enum<PositionMode>("Position", PositionMode.FollowCursor),
            X = Interval("X", 0), Y = Interval("Y", 0),
            IntervalMinMs = interval, IntervalMaxMs = interval,
            Stop = count == 0 ? StopCondition.Unlimited : StopCondition.Count,
            Count = Math.Clamp(count, 0, 1000000),
            Trigger = Enum<TriggerMode>("Trigger", TriggerMode.HotkeyToggle),
            HoldKeyVk = Interval("HoldKeyVk", 0x77),
            HoldCtrl = Flag("HoldCtrl"),
            HoldAlt = Flag("HoldAlt"),
            HoldShift = Flag("HoldShift")
        };
    }

    public void Save(ClickSettings settings)
    {
        settings.ValidateForStorage();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(path + ".tmp", path, true);
    }
}
