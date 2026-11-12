using System.Text;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Layout;

/// <summary>Maps virtual keys to characters for the English and Russian layouts and back.</summary>
internal sealed class KeyTranslator
{
    /// <summary>Virtual keys that produce characters on the main block of a keyboard.</summary>
    public static readonly int[] CharKeys = BuildCharKeys();

    // [layout][vk * 2 + (shift ? 1 : 0)]
    private readonly char[] _en = new char[512];
    private readonly char[] _ru = new char[512];
    private readonly Dictionary<char, (int Vk, bool Shift)> _enReverse = new();
    private readonly Dictionary<char, (int Vk, bool Shift)> _ruReverse = new();

    private KeyTranslator() { }

    private static int[] BuildCharKeys()
    {
        var keys = new List<int>();
        for (int vk = 0x30; vk <= 0x39; vk++) keys.Add(vk);
        for (int vk = 0x41; vk <= 0x5A; vk++) keys.Add(vk);
        for (int vk = 0xBA; vk <= 0xC0; vk++) keys.Add(vk);
        for (int vk = 0xDB; vk <= 0xDF; vk++) keys.Add(vk);
        keys.Add(0xE2);
        return keys.ToArray();
    }

    /// <summary>Standard US QWERTY and Russian ЙЦУКЕН tables, used when the system layouts are not available.</summary>
    public static KeyTranslator CreateDefault()
    {
        var t = new KeyTranslator();
        void Row(char[] table, int[] vks, string normal, string shifted)
        {
            for (int i = 0; i < vks.Length; i++)
            {
                table[vks[i] * 2] = normal[i];
                table[vks[i] * 2 + 1] = shifted[i];
            }
        }

        int[] digits = { 0xC0, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x30, 0xBD, 0xBB };
        int[] top = { 0x51, 0x57, 0x45, 0x52, 0x54, 0x59, 0x55, 0x49, 0x4F, 0x50, 0xDB, 0xDD, 0xDC };
        int[] home = { 0x41, 0x53, 0x44, 0x46, 0x47, 0x48, 0x4A, 0x4B, 0x4C, 0xBA, 0xDE };
        int[] bottom = { 0xE2, 0x5A, 0x58, 0x43, 0x56, 0x42, 0x4E, 0x4D, 0xBC, 0xBE, 0xBF };

        Row(t._en, digits, "`1234567890-=", "~!@#$%^&*()_+");
        Row(t._en, top, "qwertyuiop[]\\", "QWERTYUIOP{}|");
        Row(t._en, home, "asdfghjkl;'", "ASDFGHJKL:\"");
        Row(t._en, bottom, "\\zxcvbnm,./", "|ZXCVBNM<>?");

        Row(t._ru, digits, "ё1234567890-=", "Ё!\"№;%:?*()_+");
        Row(t._ru, top, "йцукенгшщзхъ\\", "ЙЦУКЕНГШЩЗХЪ/");
        Row(t._ru, home, "фывапролджэ", "ФЫВАПРОЛДЖЭ");
        Row(t._ru, bottom, "\\ячсмитьбю.", "/ЯЧСМИТЬБЮ,");

        t.BuildReverse();
        return t;
    }

    /// <summary>Reads the character tables from the installed layouts via ToUnicodeEx.
    /// Falls back to the standard tables for a layout that cannot be read.</summary>
    public static KeyTranslator Create(IntPtr enHkl, IntPtr ruHkl)
    {
        var fallback = CreateDefault();
        var t = new KeyTranslator();
        if (!Fill(t._en, enHkl, 'a', 'z')) Array.Copy(fallback._en, t._en, t._en.Length);
        if (!Fill(t._ru, ruHkl, 'а', 'я')) Array.Copy(fallback._ru, t._ru, t._ru.Length);
        t.BuildReverse();
        return t;
    }

    private static bool Fill(char[] table, IntPtr hkl, char firstLetter, char lastLetter)
    {
        if (hkl == IntPtr.Zero) return false;
        var state = new byte[256];
        var sb = new StringBuilder(8);
        const uint doNotChangeKeyboardState = 0x4;
        foreach (int vk in CharKeys)
        {
            for (int shift = 0; shift < 2; shift++)
            {
                state[0x10] = (byte)(shift == 1 ? 0x80 : 0);
                sb.Clear();
                uint scan = MapVirtualKeyEx((uint)vk, MAPVK_VK_TO_VSC, hkl);
                int rc = ToUnicodeEx((uint)vk, scan, state, sb, sb.Capacity, doNotChangeKeyboardState, hkl);
                if (rc == 1 && sb.Length > 0 && !char.IsControl(sb[0]))
                    table[vk * 2 + shift] = sb[0];
            }
        }
        // Sanity check: the layout must produce its whole lowercase alphabet (ignoring ё).
        var produced = new HashSet<char>(table);
        for (char c = firstLetter; c <= lastLetter; c++)
            if (!produced.Contains(c)) return false;
        return true;
    }

    private void BuildReverse()
    {
        foreach (int vk in CharKeys)
        {
            for (int shift = 0; shift < 2; shift++)
            {
                char e = _en[vk * 2 + shift], r = _ru[vk * 2 + shift];
                if (e != '\0') _enReverse.TryAdd(e, (vk, shift == 1));
                if (r != '\0') _ruReverse.TryAdd(r, (vk, shift == 1));
            }
        }
    }

    private char[] Table(LayoutKind layout) => layout == LayoutKind.Ru ? _ru : _en;

    public bool IsCharKey(int vk) => vk is >= 0 and < 256 && (_en[vk * 2] != '\0' || _ru[vk * 2] != '\0');

    /// <summary>Character produced by a key in the given layout, honouring Caps Lock for letters.</summary>
    public char GetChar(LayoutKind layout, int vk, bool shift, bool caps)
    {
        if (vk is < 0 or >= 256) return '\0';
        var table = Table(layout);
        char normal = table[vk * 2];
        if (caps && char.IsLetter(normal)) shift = !shift;
        return table[vk * 2 + (shift ? 1 : 0)];
    }

    public bool TryGetKey(LayoutKind layout, char c, out int vk, out bool shift)
    {
        var map = layout == LayoutKind.Ru ? _ruReverse : _enReverse;
        if (map.TryGetValue(c, out var key))
        {
            vk = key.Vk;
            shift = key.Shift;
            return true;
        }
        vk = 0;
        shift = false;
        return false;
    }

    /// <summary>Retypes text as if the same keys had been pressed in the other layout.</summary>
    public string ConvertText(string text, LayoutKind from, LayoutKind to)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (TryGetKey(from, c, out int vk, out bool shift))
            {
                char converted = Table(to)[vk * 2 + (shift ? 1 : 0)];
                sb.Append(converted != '\0' ? converted : c);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    /// <summary>Guesses which layout a piece of text was typed in, by counting letters.</summary>
    public static LayoutKind GuessLayout(string text)
    {
        int latin = 0, cyrillic = 0;
        foreach (char c in text)
        {
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z') latin++;
            else if (c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё') cyrillic++;
        }
        if (latin == 0 && cyrillic == 0) return LayoutKind.Other;
        return latin >= cyrillic ? LayoutKind.En : LayoutKind.Ru;
    }
}
