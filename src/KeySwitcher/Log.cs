using KeySwitcher.Settings;

namespace KeySwitcher;

/// <summary>Minimal error log in %AppData%\KeySwitcher\error.log (never logs typed text).</summary>
internal static class Log
{
    private static readonly object Sync = new();

    public static void Error(string message, Exception? ex = null)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(AppSettings.Folder);
                var path = Path.Combine(AppSettings.Folder, "error.log");
                if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{(ex != null ? ": " + ex : "")}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }
}
