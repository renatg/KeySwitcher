using KeySwitcher.Native;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Layout;

public enum LayoutKind
{
    Other = 0,
    En = 1,
    Ru = 2,
}

internal static class LayoutKindExtensions
{
    public static LayoutKind Opposite(this LayoutKind kind) => kind switch
    {
        LayoutKind.En => LayoutKind.Ru,
        LayoutKind.Ru => LayoutKind.En,
        _ => LayoutKind.Other,
    };
}

/// <summary>Finds the installed Russian and English layouts, reports the layout of the focused
/// window and switches it.</summary>
internal sealed class LayoutManager
{
    private const int LangRussian = 0x19;
    private const int LangEnglish = 0x09;

    private volatile IntPtr _ru;
    private volatile IntPtr _en;

    // After we request a switch the target window applies it asynchronously; until it does,
    // GetKeyboardLayout still reports the old layout.
    private volatile int _pendingLayout;
    private long _pendingUntilTicks;

    public LayoutManager() => Refresh();

    public IntPtr RuHkl => _ru;
    public IntPtr EnHkl => _en;
    public bool IsAvailable => _ru != IntPtr.Zero && _en != IntPtr.Zero;

    public void Refresh()
    {
        int count = GetKeyboardLayoutList(0, null);
        if (count <= 0) return;
        var list = new IntPtr[count];
        GetKeyboardLayoutList(count, list);
        IntPtr ru = IntPtr.Zero, en = IntPtr.Zero;
        foreach (var hkl in list)
        {
            var kind = Classify(hkl);
            if (kind == LayoutKind.Ru && ru == IntPtr.Zero) ru = hkl;
            // Prefer en-US, but accept any English layout.
            if (kind == LayoutKind.En && (en == IntPtr.Zero || LangId(hkl) == 0x0409)) en = hkl;
        }
        _ru = ru;
        _en = en;
    }

    public static int LangId(IntPtr hkl) => (int)((long)hkl & 0xFFFF);

    public static LayoutKind Classify(IntPtr hkl) => (LangId(hkl) & 0x3FF) switch
    {
        LangRussian => LayoutKind.Ru,
        LangEnglish => LayoutKind.En,
        _ => LayoutKind.Other,
    };

    public IntPtr HklOf(LayoutKind kind) => kind switch
    {
        LayoutKind.Ru => _ru,
        LayoutKind.En => _en,
        _ => IntPtr.Zero,
    };

    /// <summary>Returns the thread that owns keyboard focus in the foreground window.</summary>
    public static uint GetFocusThread(IntPtr foreground)
    {
        uint thread = GetWindowThreadProcessId(foreground, out _);
        var info = new GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<GUITHREADINFO>() };
        if (thread != 0 && GetGUIThreadInfo(thread, ref info) && info.hwndFocus != IntPtr.Zero)
        {
            uint focusThread = GetWindowThreadProcessId(info.hwndFocus, out _);
            if (focusThread != 0) return focusThread;
        }
        return thread;
    }

    public LayoutKind GetCurrentLayout()
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return LayoutKind.Other;
        var actual = Classify(GetKeyboardLayout(GetFocusThread(fg)));

        int pending = _pendingLayout;
        if (pending != 0)
        {
            if (actual == (LayoutKind)pending || Environment.TickCount64 > Interlocked.Read(ref _pendingUntilTicks))
                _pendingLayout = 0;
            else
                return (LayoutKind)pending;
        }
        return actual;
    }

    public void SwitchTo(LayoutKind kind)
    {
        if (kind == LayoutKind.Other) return;
        var hkl = HklOf(kind);
        if (hkl == IntPtr.Zero)
        {
            Refresh();
            hkl = HklOf(kind);
            if (hkl == IntPtr.Zero) return;
        }
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero) return;
        Interlocked.Exchange(ref _pendingUntilTicks, Environment.TickCount64 + 500);
        _pendingLayout = (int)kind;
        PostMessage(fg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, hkl);
    }
}
