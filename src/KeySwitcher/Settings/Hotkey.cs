using System.Text.Json;
using System.Text.Json.Serialization;
using KeySwitcher.Native;

namespace KeySwitcher.Settings;

/// <summary>A global shortcut, stored as text such as "Ctrl+Alt+P".</summary>
[JsonConverter(typeof(HotkeyJsonConverter))]
internal sealed record Hotkey(Keys Key, bool Ctrl, bool Alt, bool Shift, bool Win)
{
    public static readonly Hotkey None = new(Keys.None, false, false, false, false);

    public bool IsEmpty => Key == Keys.None;

    public bool Matches(int vk)
    {
        if (IsEmpty || (int)Key != vk) return false;
        bool ctrl = NativeMethods.IsKeyDown(VirtualKeys.Control);
        bool alt = NativeMethods.IsKeyDown(VirtualKeys.Menu);
        bool shift = NativeMethods.IsKeyDown(VirtualKeys.Shift);
        bool win = NativeMethods.IsKeyDown(VirtualKeys.LWin) || NativeMethods.IsKeyDown(VirtualKeys.RWin);
        return ctrl == Ctrl && alt == Alt && shift == Shift && win == Win;
    }

    public override string ToString()
    {
        if (IsEmpty) return string.Empty;
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    private static string KeyName(Keys key) => key switch
    {
        >= Keys.D0 and <= Keys.D9 => ((char)('0' + (key - Keys.D0))).ToString(),
        Keys.Pause => "Pause",
        Keys.Scroll => "ScrollLock",
        _ => key.ToString(),
    };

    public static Hotkey Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        bool ctrl = false, alt = false, shift = false, win = false;
        Keys key = Keys.None;
        foreach (var raw in text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "alt": alt = true; break;
                case "shift": shift = true; break;
                case "win": win = true; break;
                case "scrolllock": key = Keys.Scroll; break;
                default:
                    if (raw.Length == 1 && char.IsDigit(raw[0])) key = Keys.D0 + (raw[0] - '0');
                    else if (Enum.TryParse(raw, true, out Keys k)) key = k;
                    break;
            }
        }
        return key == Keys.None ? None : new Hotkey(key, ctrl, alt, shift, win);
    }

    /// <summary>Builds a hotkey from a WinForms key event (used by the settings editor).</summary>
    public static Hotkey FromKeyData(Keys keyData, bool win)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.None)
            return None;
        return new Hotkey(key, keyData.HasFlag(Keys.Control), keyData.HasFlag(Keys.Alt), keyData.HasFlag(Keys.Shift), win);
    }
}

internal sealed class HotkeyJsonConverter : JsonConverter<Hotkey>
{
    public override Hotkey Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Hotkey.Parse(reader.GetString());

    public override void Write(Utf8JsonWriter writer, Hotkey value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
