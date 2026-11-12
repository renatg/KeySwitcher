using System.Reflection;
using KeySwitcher.Layout;

namespace KeySwitcher.Detection;

/// <summary>Decides whether a word typed in one layout was actually meant for the other one.
/// Uses frequency dictionaries plus a letter trigram model trained on them.</summary>
internal sealed class LanguageDetector
{
    private readonly LanguageModel _en;
    private readonly LanguageModel _ru;

    public LanguageDetector(LanguageModel en, LanguageModel ru)
    {
        _en = en;
        _ru = ru;
    }

    private static LanguageDetector? _default;
    private static readonly object DefaultLock = new();

    public static LanguageDetector Default
    {
        get
        {
            lock (DefaultLock)
                return _default ??= new LanguageDetector(
                    LanguageModel.Load(LayoutKind.En, ReadResource("KeySwitcher.Dictionaries.en.txt")),
                    LanguageModel.Load(LayoutKind.Ru, ReadResource("KeySwitcher.Dictionaries.ru.txt")));
        }
    }

    private static IEnumerable<string> ReadResource(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Resource {name} not found");
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line) yield return line;
    }

    public LanguageModel Model(LayoutKind layout) => layout == LayoutKind.Ru ? _ru : _en;

    /// <summary>Should the text <paramref name="typed"/> (typed in <paramref name="typedLayout"/>)
    /// be replaced by <paramref name="alternative"/> — the same keys in the other layout?</summary>
    public bool ShouldConvert(string typed, LayoutKind typedLayout, string alternative, LayoutKind altLayout)
    {
        if (typedLayout == LayoutKind.Other || altLayout == LayoutKind.Other) return false;
        if (LooksTechnical(typed) || typed.Any(char.IsDigit)) return false;

        string typedCore = TextUtil.Core(typed);
        string altCore = TextUtil.Core(alternative);
        if (altCore.Length < 2) return false;
        if (TextUtil.IsCamelCase(typedCore)) return false;

        var altModel = Model(altLayout);
        var typedModel = Model(typedLayout);
        if (!altModel.IsCleanWord(altCore)) return false;

        string altLower = altCore.ToLowerInvariant();
        var alt = altModel.Score(altLower);

        if (!typedModel.IsCleanWord(typedCore))
        {
            // What was typed is not even a word shape in its own language (e.g. "j,tl" -> "обед").
            return alt.InDictionary
                ? (altLower.Length > 2 || alt.Rank <= 3000)
                : altLower.Length >= 4 && alt.Trigram > -3.2;
        }

        var cur = typedModel.Score(typedCore.ToLowerInvariant());

        // Both are real words: replace only a much rarer word, and never one whose letters would
        // turn into punctuation ("шею" -> "it.", "беру" -> ",the").
        if (cur.InDictionary && alt.InDictionary)
            return altCore.Length >= typedCore.Length && alt.LogFrequency - cur.LogFrequency > 5.0;

        if (cur.InDictionary)
            return false;

        if (alt.InDictionary)
        {
            return altLower.Length switch
            {
                2 => alt.Rank <= 3000 && alt.Trigram > cur.Trigram,
                3 => alt.Rank <= 20000 && alt.Trigram - cur.Trigram > -0.3,
                _ => alt.Trigram - cur.Trigram > -0.8,
            };
        }

        return altLower.Length >= 4 && alt.Trigram - cur.Trigram > 1.3;
    }

    /// <summary>Early check for an unfinished word: the typed letters cannot start any word of the
    /// current language, while the alternative is a confident beginning of a word in the other.</summary>
    public bool ShouldConvertEarly(string typed, LayoutKind typedLayout, string alternative, LayoutKind altLayout)
    {
        if (typedLayout == LayoutKind.Other || altLayout == LayoutKind.Other) return false;
        var typedModel = Model(typedLayout);
        var altModel = Model(altLayout);
        if (TextUtil.IsCamelCase(typed)) return false;
        if (!altModel.IsCleanWord(alternative)) return false;

        string altLower = alternative.ToLowerInvariant();
        if (!altModel.HasPrefix(altLower)) return false;

        if (typedModel.IsCleanWord(typed))
        {
            string typedLower = typed.ToLowerInvariant();
            if (typedModel.HasPrefix(typedLower)) return false;
            return altModel.Score(altLower, prefix: true).Trigram
                   - typedModel.Score(typedLower, prefix: true).Trigram > 1.5;
        }
        return true;
    }

    private static bool LooksTechnical(string text) =>
        text.Contains("://", StringComparison.Ordinal)
        || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
        || text.Contains('@')
        || text.Contains('\\');
}

internal static class TextUtil
{
    /// <summary>Strips leading and trailing non-letter characters.</summary>
    public static string Core(string text)
    {
        int start = 0, end = text.Length;
        while (start < end && !char.IsLetter(text[start])) start++;
        while (end > start && !char.IsLetter(text[end - 1])) end--;
        return text[start..end];
    }

    /// <summary>Mixed case such as "iPhone" or "McDonald". Capitalised ("Hello"), all caps ("NASA")
    /// and Caps Lock-inverted ("hELLO") words are not CamelCase.</summary>
    public static bool IsCamelCase(string word)
    {
        var letters = word.Where(char.IsLetter).ToArray();
        if (letters.Length < 2) return false;
        var rest = letters.AsSpan(1);
        bool restUpper = true, restLower = true;
        foreach (char c in rest)
        {
            if (char.IsUpper(c)) restLower = false;
            if (char.IsLower(c)) restUpper = false;
        }
        return !restUpper && !restLower;
    }
}
