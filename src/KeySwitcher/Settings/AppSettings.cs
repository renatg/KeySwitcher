using System.Text.Encodings.Web;
using System.Text.Json;

namespace KeySwitcher.Settings;

internal sealed class AppSettings
{
    public bool AutoSwitchEnabled { get; set; } = true;

    /// <summary>Correct the word while typing, after <see cref="EarlyModeMinLetters"/> letters,
    /// instead of waiting for the word to end.</summary>
    public bool EarlyMode { get; set; }

    public int EarlyModeMinLetters { get; set; } = 4;

    public bool DoubleShiftEnabled { get; set; } = true;

    public int DoubleShiftIntervalMs { get; set; } = 400;

    public bool UndoOnBackspace { get; set; } = true;

    public bool LearnFromUndo { get; set; } = true;

    public bool FixCapsLockCase { get; set; } = true;

    public bool PlaySound { get; set; } = true;

    public bool ShowLayoutInTray { get; set; } = true;

    public Hotkey PauseHotkey { get; set; } = Hotkey.Parse("Ctrl+Alt+P");

    public Hotkey CaseHotkey { get; set; } = Hotkey.Parse("Ctrl+Alt+U");

    /// <summary>Words (as typed) that are never converted. Lowercase.</summary>
    public List<string> ExcludedWords { get; set; } = new();

    /// <summary>Process names without ".exe" where KeySwitcher does nothing.</summary>
    public List<string> ExcludedApps { get; set; } = new()
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "mstsc", "KeePass", "KeePassXC", "1Password", "Bitwarden",
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeySwitcher");

    public static string FilePath => Path.Combine(Folder, "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static bool Exists => File.Exists(FilePath);

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Failed to read settings", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Failed to save settings", ex);
        }
    }

    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;
}
