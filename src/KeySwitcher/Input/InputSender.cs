using System.Runtime.InteropServices;
using KeySwitcher.Native;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Input;

/// <summary>Builds and sends synthetic keyboard input. Every event carries <see cref="Marker"/>
/// so that our own hook can recognise and ignore it.</summary>
internal sealed class InputSender
{
    public static readonly IntPtr Marker = new(0x4B535754); // "KSWT"

    private readonly List<INPUT> _inputs = new();

    public static InputSender Create() => new();

    public InputSender Key(int vk, bool up)
    {
        uint flags = up ? KEYEVENTF_KEYUP : 0;
        if (VirtualKeys.IsExtended(vk)) flags |= KEYEVENTF_EXTENDEDKEY;
        _inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = (ushort)vk,
                    wScan = (ushort)MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC, IntPtr.Zero),
                    dwFlags = flags,
                    dwExtraInfo = Marker,
                },
            },
        });
        return this;
    }

    public InputSender Press(int vk, int times = 1)
    {
        for (int i = 0; i < times; i++) Key(vk, false).Key(vk, true);
        return this;
    }

    public InputSender Backspaces(int count) => Press(VirtualKeys.Back, count);

    public InputSender Text(string text)
    {
        foreach (char c in text)
        {
            if (c == '\r') continue;
            if (c == '\n') { Press(VirtualKeys.Return); continue; }
            if (c == '\t') { Press(VirtualKeys.Tab); continue; }
            Unicode(c, false).Unicode(c, true);
        }
        return this;
    }

    public InputSender Chord(int modifier, int vk) =>
        Key(modifier, false).Key(vk, false).Key(vk, true).Key(modifier, true);

    private InputSender Unicode(char c, bool up)
    {
        _inputs.Add(new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = c,
                    dwFlags = KEYEVENTF_UNICODE | (up ? KEYEVENTF_KEYUP : 0),
                    dwExtraInfo = Marker,
                },
            },
        });
        return this;
    }

    public bool Send()
    {
        if (_inputs.Count == 0) return true;
        var arr = _inputs.ToArray();
        _inputs.Clear();
        return SendInput((uint)arr.Length, arr, Marshal.SizeOf<INPUT>()) == arr.Length;
    }
}
