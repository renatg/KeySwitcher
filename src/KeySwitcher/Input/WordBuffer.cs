using System.Text;
using KeySwitcher.Layout;

namespace KeySwitcher.Input;

/// <summary>One physical key press: which key, the modifier state and the layout it was typed in.
/// Keeping keys rather than characters lets us re-render the word in the other layout.</summary>
internal readonly record struct KeyStroke(int Vk, bool Shift, bool Caps, LayoutKind Layout)
{
    public KeyStroke Flipped() => this with { Layout = Layout.Opposite() };
}

internal static class KeyStrokes
{
    public static string Render(IReadOnlyList<KeyStroke> keys, KeyTranslator translator, bool flip = false)
    {
        var sb = new StringBuilder(keys.Count);
        foreach (var k in keys)
        {
            var layout = flip ? k.Layout.Opposite() : k.Layout;
            char c = translator.GetChar(layout, k.Vk, k.Shift, k.Caps);
            if (c != '\0') sb.Append(c);
        }
        return sb.ToString();
    }

    public static List<KeyStroke> Flip(IEnumerable<KeyStroke> keys) => keys.Select(k => k.Flipped()).ToList();

    /// <summary>The layout of the whole word, or Other if it was typed in more than one layout.</summary>
    public static LayoutKind CommonLayout(IReadOnlyList<KeyStroke> keys)
    {
        if (keys.Count == 0) return LayoutKind.Other;
        var first = keys[0].Layout;
        for (int i = 1; i < keys.Count; i++)
            if (keys[i].Layout != first) return LayoutKind.Other;
        return first;
    }
}
