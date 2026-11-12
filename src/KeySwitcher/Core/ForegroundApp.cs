using System.Collections.Concurrent;
using System.Diagnostics;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Core;

/// <summary>Resolves the process name of the foreground window (cached per process id).</summary>
internal static class ForegroundApp
{
    private static readonly ConcurrentDictionary<uint, string> Names = new();

    private static readonly HashSet<string> Terminals = new(StringComparer.OrdinalIgnoreCase)
    {
        "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "mintty", "alacritty", "wezterm-gui", "putty", "kitty", "Tabby", "Hyper",
    };

    public static string GetProcessName(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return string.Empty;
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return string.Empty;
        if (Names.TryGetValue(pid, out var name)) return name;
        try
        {
            using var p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch
        {
            name = string.Empty;
        }
        if (Names.Count > 256) Names.Clear();
        Names[pid] = name;
        return name;
    }

    public static string CurrentProcessName => GetProcessName(GetForegroundWindow());

    /// <summary>Terminals treat Ctrl+C as "interrupt", so copying must use Ctrl+Insert there.</summary>
    public static bool IsTerminal(IntPtr hwnd) =>
        Terminals.Contains(GetProcessName(hwnd))
        || GetWindowClass(hwnd) is "ConsoleWindowClass" or "CASCADIA_HOSTING_WINDOW_CLASS";
}
