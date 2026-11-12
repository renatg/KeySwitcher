using System.Globalization;
using System.Text;

namespace KeySwitcher.Core;

internal static class CaseFixer
{
    /// <summary>"пРИВЕТ" — typed with Caps Lock on while holding Shift for the first letter.</summary>
    public static bool IsInvertedCaps(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length < 3) return false;
        if (!char.IsLower(letters[0])) return false;
        for (int i = 1; i < letters.Length; i++)
            if (!char.IsUpper(letters[i])) return false;
        return true;
    }

    /// <summary>Swaps the case of every letter: "пРИВЕТ" → "Привет".</summary>
    public static string SwapCase(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : char.IsLower(c) ? char.ToUpperInvariant(c) : c);
        return sb.ToString();
    }

    /// <summary>Next case for selected text: lower → UPPER → Title → lower.
    /// Text that looks Caps Lock-inverted has its case swapped. Returns null if there are no letters.</summary>
    public static string? CycleCase(string text)
    {
        var letters = text.Where(char.IsLetter).ToArray();
        if (letters.Length == 0) return null;

        if (LooksInverted(text)) return SwapCase(text);

        bool allLower = letters.All(char.IsLower);
        bool allUpper = letters.All(char.IsUpper);
        if (allLower) return text.ToUpperInvariant();
        if (allUpper) return ToTitle(text);
        return text.ToLowerInvariant();
    }

    public static string ToTitle(string text)
    {
        var sb = new StringBuilder(text.Length);
        bool startOfWord = true;
        foreach (char c in text)
        {
            if (char.IsLetter(c))
            {
                sb.Append(startOfWord ? char.ToUpper(c, CultureInfo.InvariantCulture) : char.ToLower(c, CultureInfo.InvariantCulture));
                startOfWord = false;
            }
            else
            {
                sb.Append(c);
                startOfWord = c != '\'' && c != '’';
            }
        }
        return sb.ToString();
    }

    private static bool LooksInverted(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Count(char.IsLetter) >= 3)
            .ToArray();
        return words.Length > 0 && words.All(IsInvertedCaps);
    }
}
