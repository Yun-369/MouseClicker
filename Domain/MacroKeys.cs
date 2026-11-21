namespace MouseClicker.Domain;

// Canonical key map for the macro "key" action. The file and the editor only ever use
// human tokens (TAB / ENTER / A / F1 / COMMA …), never raw Win32 numbers. One place
// owns token<->VK, the friendly Chinese label and the extended-key flag so the engine,
// validator and editor cannot disagree.
public static class MacroKeys
{
    public const ushort VkCtrl = 0x11, VkAlt = 0x12, VkShift = 0x10;

    // token -> (vk, extended, friendly label). Letters/digits are derived separately.
    private static readonly (string token, ushort vk, bool ext, string label)[] Named =
    [
        ("TAB", 0x09, false, "Tab"),
        ("ENTER", 0x0D, false, "Enter（回车）"),
        ("ESC", 0x1B, false, "Esc"),
        ("SPACE", 0x20, false, "空格"),
        ("BACK", 0x08, false, "退格 Backspace"),
        ("CAPS", 0x14, false, "CapsLock"),
        ("PAGEUP", 0x21, true, "PageUp"),
        ("PAGEDOWN", 0x22, true, "PageDown"),
        ("END", 0x23, true, "End"),
        ("HOME", 0x24, true, "Home"),
        ("LEFT", 0x25, true, "方向键 ←"),
        ("UP", 0x26, true, "方向键 ↑"),
        ("RIGHT", 0x27, true, "方向键 →"),
        ("DOWN", 0x28, true, "方向键 ↓"),
        ("INSERT", 0x2D, true, "Insert"),
        ("DELETE", 0x2E, true, "Delete"),
        ("NUMPAD0", 0x60, false, "小键盘 0"), ("NUMPAD1", 0x61, false, "小键盘 1"),
        ("NUMPAD2", 0x62, false, "小键盘 2"), ("NUMPAD3", 0x63, false, "小键盘 3"),
        ("NUMPAD4", 0x64, false, "小键盘 4"), ("NUMPAD5", 0x65, false, "小键盘 5"),
        ("NUMPAD6", 0x66, false, "小键盘 6"), ("NUMPAD7", 0x67, false, "小键盘 7"),
        ("NUMPAD8", 0x68, false, "小键盘 8"), ("NUMPAD9", 0x69, false, "小键盘 9"),
        ("MULTIPLY", 0x6A, false, "小键盘 *"), ("ADD", 0x6B, false, "小键盘 +"),
        ("SUBTRACT", 0x6D, false, "小键盘 -"), ("DECIMAL", 0x6E, false, "小键盘 ."),
        ("DIVIDE", 0x6F, true, "小键盘 /"),
        ("SEMICOLON", 0xBA, false, "; :"), ("EQUALS", 0xBB, false, "= +"),
        ("COMMA", 0xBC, false, ", <"), ("MINUS", 0xBD, false, "- _"),
        ("PERIOD", 0xBE, false, ". >"), ("SLASH", 0xBF, false, "/ ?"),
        ("BACKQUOTE", 0xC0, false, "` ~"), ("LBRACKET", 0xDB, false, "[ {"),
        ("BACKSLASH", 0xDC, false, "\\ |"), ("RBRACKET", 0xDD, false, "] }"),
        ("QUOTE", 0xDE, false, "' \""),
    ];

    private static readonly Dictionary<string, (ushort vk, bool ext)> ByToken =
        Named.ToDictionary(n => n.token, n => (n.vk, n.ext), StringComparer.OrdinalIgnoreCase);

    // Editor dropdown in a stable, grouped order.
    public static IReadOnlyList<(string Token, string Label)> PrimaryKeys { get; } = BuildList();

    private static IReadOnlyList<(string, string)> BuildList()
    {
        var list = new List<(string, string)>();
        foreach (var n in Named) list.Add((n.token, n.label));
        for (char c = 'A'; c <= 'Z'; c++) list.Add((c.ToString(), c.ToString()));
        for (char d = '0'; d <= '9'; d++) list.Add((d.ToString(), d.ToString()));
        for (int f = 1; f <= 12; f++) list.Add(($"F{f}", $"F{f}"));
        return list;
    }

    public static bool IsModifierToken(string? token) =>
        token is "CTRL" or "ALT" or "SHIFT" or "LCONTROL" or "RCONTROL" or "LMENU" or "RMENU"
            or "LSHIFT" or "RSHIFT";

    // Resolve a stored token to its VK + extended flag. Letters/digits/F-keys accepted too.
    public static bool TryParse(string? token, out ushort vk, out bool extended)
    {
        vk = 0; extended = false;
        if (string.IsNullOrWhiteSpace(token)) return false;
        string t = token.Trim().ToUpperInvariant();
        if (ByToken.TryGetValue(t, out var hit)) { vk = hit.vk; extended = hit.ext; return true; }
        if (t.Length == 1)
        {
            char c = t[0];
            if (c is >= 'A' and <= 'Z') { vk = (ushort)c; return true; }
            if (c is >= '0' and <= '9') { vk = (ushort)c; return true; }
        }
        if (t.Length is >= 2 and <= 3 && t[0] == 'F'
            && int.TryParse(t.AsSpan(1), out int fn) && fn is >= 1 and <= 12)
        { vk = (ushort)(0x70 + fn - 1); return true; }
        return false;
    }

    public static string Label(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return "";
        string t = token.Trim().ToUpperInvariant();
        var named = Named.FirstOrDefault(n => n.token == t);
        return named.token is not null ? named.label : t;
    }

    public static string ChordLabel(string? token, bool ctrl, bool alt, bool shift)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        parts.Add(Label(token));
        return string.Join("+", parts);
    }
}
