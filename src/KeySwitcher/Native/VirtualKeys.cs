namespace KeySwitcher.Native;

internal static class VirtualKeys
{
    public const int Back = 0x08;
    public const int Tab = 0x09;
    public const int Return = 0x0D;
    public const int Shift = 0x10;
    public const int Control = 0x11;
    public const int Menu = 0x12;
    public const int Capital = 0x14;
    public const int Space = 0x20;
    public const int Left = 0x25;
    public const int Insert = 0x2D;
    public const int LWin = 0x5B;
    public const int RWin = 0x5C;
    public const int NumLock = 0x90;
    public const int Scroll = 0x91;
    public const int LShift = 0xA0;
    public const int RShift = 0xA1;
    public const int LControl = 0xA2;
    public const int RControl = 0xA3;
    public const int LMenu = 0xA4;
    public const int RMenu = 0xA5;
    public const int Packet = 0xE7;
    public const int C = 0x43;
    public const int V = 0x56;

    public static bool IsShift(int vk) => vk is Shift or LShift or RShift;

    public static bool IsModifier(int vk) =>
        vk is Control or LControl or RControl or Menu or LMenu or RMenu or LWin or RWin;

    /// <summary>Keys that need KEYEVENTF_EXTENDEDKEY when injected.</summary>
    public static bool IsExtended(int vk) =>
        vk is >= 0x21 and <= 0x2E   // PgUp, PgDn, End, Home, arrows, Insert, Delete
        or RControl or RMenu or LWin or RWin or 0x6F /* Divide */ or NumLock;
}
