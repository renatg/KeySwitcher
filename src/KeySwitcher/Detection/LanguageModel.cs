using KeySwitcher.Layout;

namespace KeySwitcher.Detection;

internal readonly record struct WordScore(bool InDictionary, int Rank, double LogFrequency, double Trigram);

/// <summary>Word frequencies and an interpolated letter trigram model for one language.</summary>
internal sealed class LanguageModel
{
    private const char Start = '^';
    private const char End = '$';
    private const double L3 = 0.6, L2 = 0.3, L1 = 0.1;

    private readonly Dictionary<string, (int Rank, double LogFreq)> _words = new(StringComparer.Ordinal);
    private readonly string[] _sorted;
    private readonly Dictionary<long, double> _tri = new();   // key: a,b,c
    private readonly Dictionary<long, double> _bi = new();    // key: a,b
    private readonly Dictionary<long, double> _uni = new();   // key: a
    private readonly Dictionary<long, double> _ctx2 = new();  // count of (a,b,*)
    private readonly Dictionary<long, double> _ctx1 = new();  // count of (a,*)
    private double _uniTotal;
    private readonly int _alphabet;
    private readonly Func<char, bool> _isLetter;

    public LayoutKind Layout { get; }

    private LanguageModel(LayoutKind layout, IEnumerable<(string Word, long Count)> words)
    {
        Layout = layout;
        _isLetter = layout == LayoutKind.Ru
            ? c => c is >= 'а' and <= 'я' or 'ё'
            : c => c is >= 'a' and <= 'z';
        _alphabet = (layout == LayoutKind.Ru ? 33 : 26) + 3;

        var list = words.ToList();
        double total = list.Sum(w => (double)w.Count);
        int rank = 0;
        foreach (var (word, count) in list)
        {
            rank++;
            _words.TryAdd(word, (rank, Math.Log(count / total)));
            Train(word, 1.0 + Math.Log10(Math.Max(1, count)));
        }
        _sorted = _words.Keys.ToArray();
        Array.Sort(_sorted, StringComparer.Ordinal);
    }

    /// <summary>Loads "word count" lines, most frequent first.</summary>
    public static LanguageModel Load(LayoutKind layout, IEnumerable<string> lines)
    {
        var words = new List<(string, long)>();
        foreach (var line in lines)
        {
            int sp = line.IndexOf(' ');
            if (sp <= 0) continue;
            if (!long.TryParse(line.AsSpan(sp + 1), out long count)) continue;
            words.Add((Normalize(line[..sp]), count));
        }
        return new LanguageModel(layout, words);
    }

    private static string Normalize(string word) => word.ToLowerInvariant().Replace('ё', 'е');

    private static long Key(char a, char b = '\0', char c = '\0') => ((long)a << 32) | ((long)b << 16) | c;

    private static void Add(Dictionary<long, double> d, long key, double w) =>
        d[key] = d.TryGetValue(key, out var v) ? v + w : w;

    private void Train(string word, double weight)
    {
        char a = Start, b = Start;
        foreach (char c in word.Append(End))
        {
            Add(_tri, Key(a, b, c), weight);
            Add(_ctx2, Key(a, b), weight);
            Add(_bi, Key(b, c), weight);
            Add(_ctx1, Key(b), weight);
            Add(_uni, Key(c), weight);
            _uniTotal += weight;
            a = b;
            b = c;
        }
    }

    private double Prob(char a, char b, char c)
    {
        double p1 = (_uni.GetValueOrDefault(Key(c)) + 0.5) / (_uniTotal + 0.5 * _alphabet);
        double ctx1 = _ctx1.GetValueOrDefault(Key(b));
        double p2 = ctx1 > 0 ? _bi.GetValueOrDefault(Key(b, c)) / ctx1 : p1;
        double ctx2 = _ctx2.GetValueOrDefault(Key(a, b));
        double p3 = ctx2 > 0 ? _tri.GetValueOrDefault(Key(a, b, c)) / ctx2 : p2;
        return L3 * p3 + L2 * p2 + L1 * p1;
    }

    /// <summary>Average log-probability per character (higher is more plausible).</summary>
    public double TrigramScore(string lowerWord, bool prefix = false)
    {
        string w = Normalize(lowerWord);
        double sum = 0;
        int n = 0;
        char a = Start, b = Start;
        IEnumerable<char> chars = prefix ? w : w.Append(End);
        foreach (char c in chars)
        {
            sum += Math.Log(Prob(a, b, c));
            n++;
            a = b;
            b = c;
        }
        return n == 0 ? double.NegativeInfinity : sum / n;
    }

    public WordScore Score(string lowerWord, bool prefix = false)
    {
        string w = Normalize(lowerWord);
        double tri = TrigramScore(w, prefix);
        // The source lists drop apostrophes: "don't" is stored as "dont".
        return _words.TryGetValue(w, out var info) || (w.Contains('\'') && _words.TryGetValue(w.Replace("'", ""), out info))
            ? new WordScore(true, info.Rank, info.LogFreq, tri)
            : new WordScore(false, int.MaxValue, double.NegativeInfinity, tri);
    }

    public bool Contains(string lowerWord) => _words.ContainsKey(Normalize(lowerWord));

    /// <summary>Is there a dictionary word that starts with (or equals) the given text?</summary>
    public bool HasPrefix(string lowerPrefix)
    {
        string p = Normalize(lowerPrefix);
        int i = Array.BinarySearch(_sorted, p, StringComparer.Ordinal);
        if (i >= 0) return true;
        i = ~i;
        return i < _sorted.Length && _sorted[i].StartsWith(p, StringComparison.Ordinal);
    }

    /// <summary>Only letters of this language, optionally with inner hyphens (and apostrophes for English).</summary>
    public bool IsCleanWord(string word)
    {
        if (word.Length == 0) return false;
        for (int i = 0; i < word.Length; i++)
        {
            char c = char.ToLowerInvariant(word[i]);
            if (_isLetter(c)) continue;
            bool inner = i > 0 && i < word.Length - 1;
            if (inner && c == '-') continue;
            if (inner && c == '\'' && Layout == LayoutKind.En) continue;
            return false;
        }
        return true;
    }
}
