using System.Reflection;
using KeySwitcher.Detection;
using KeySwitcher.Layout;
using Xunit.Abstractions;

namespace KeySwitcher.Tests;

/// <summary>Measures the detector on the most frequent words of each language.</summary>
public class DetectorQualityTests(ITestOutputHelper output)
{
    private static readonly KeyTranslator Translator = KeyTranslator.CreateDefault();
    private static readonly LanguageDetector Detector = LanguageDetector.Default;

    private static List<string> TopWords(LayoutKind layout, int count)
    {
        string name = layout == LayoutKind.Ru ? "KeySwitcher.Dictionaries.ru.txt" : "KeySwitcher.Dictionaries.en.txt";
        using var stream = typeof(LanguageDetector).Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var words = new List<string>();
        while (words.Count < count && reader.ReadLine() is { } line) words.Add(line[..line.IndexOf(' ')]);
        return words;
    }

    private bool Converts(string typed, LayoutKind layout)
    {
        string alt = Translator.ConvertText(typed, layout, layout.Opposite());
        return Detector.ShouldConvert(typed, layout, alt, layout.Opposite());
    }

    [Theory]
    [InlineData(LayoutKind.En)]
    [InlineData(LayoutKind.Ru)]
    public void CorrectlyTypedWordsAreRarelyConverted(LayoutKind layout)
    {
        var words = TopWords(layout, 5000);
        var wrong = words.Where(w => Converts(w, layout)).ToList();
        output.WriteLine($"{layout}: {wrong.Count}/{words.Count} false positives: {string.Join(", ", wrong.Take(60))}");
        Assert.True(wrong.Count <= words.Count / 200, $"too many false positives: {wrong.Count}");
    }

    [Theory]
    [InlineData(LayoutKind.En)]
    [InlineData(LayoutKind.Ru)]
    public void WordsTypedInWrongLayoutAreMostlyConverted(LayoutKind intended)
    {
        var typedLayout = intended.Opposite();
        var words = TopWords(intended, 5000).Where(w => w.Length >= 2).ToList();
        var missed = words
            .Select(w => Translator.ConvertText(w, intended, typedLayout))
            .Where(typed => !Converts(typed, typedLayout))
            .ToList();
        double rate = 1 - (double)missed.Count / words.Count;
        output.WriteLine($"{intended} typed as {typedLayout}: {rate:P1} converted; missed: {string.Join(", ", missed.Take(60))}");
        Assert.True(rate >= 0.95, $"conversion rate {rate:P1}");
    }
}
