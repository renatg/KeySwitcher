using KeySwitcher.Detection;
using KeySwitcher.Layout;

namespace KeySwitcher.Tests;

public class DetectorTests
{
    private static readonly KeyTranslator Translator = KeyTranslator.CreateDefault();
    private static readonly LanguageDetector Detector = LanguageDetector.Default;

    private static bool Converts(string typed, LayoutKind layout)
    {
        string alt = Translator.ConvertText(typed, layout, layout.Opposite());
        return Detector.ShouldConvert(typed, layout, alt, layout.Opposite());
    }

    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("rfr", "как")]
    [InlineData("ytn", "нет")]
    [InlineData("yf", "на")]
    [InlineData("ds", "вы")]
    [InlineData("cgfcb,j", "спасибо")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("ghbdtn?", "привет,")]
    [InlineData("vjkjrj", "молоко")]
    [InlineData("hf,jnf", "работа")]
    [InlineData("ckjdj", "слово")]
    [InlineData("pfdnhf", "завтра")]
    [InlineData("gjxtve", "почему")]
    [InlineData("rjvgm.nth", "компьютер")]
    [InlineData("ltkf", "дела")]
    [InlineData("crfpfk", "сказал")]
    [InlineData("pltcm", "здесь")]
    [InlineData("rjulf", "когда")]
    public void EnglishLayoutTypoBecomesRussian(string typed, string expected)
    {
        Assert.Equal(expected, Translator.ConvertText(typed, LayoutKind.En, LayoutKind.Ru));
        Assert.True(Converts(typed, LayoutKind.En), typed);
    }

    [Theory]
    [InlineData("руддщ", "hello")]
    [InlineData("цщкдв", "world")]
    [InlineData("ерфтлы", "thanks")]
    [InlineData("щл", "ok")]
    [InlineData("ершы", "this")]
    [InlineData("пщщв", "good")]
    [InlineData("ьщктштп", "morning")]
    [InlineData("сщьзгеук", "computer")]
    [InlineData("ырщгдв", "should")]
    [InlineData("дщщл", "look")]
    public void RussianLayoutTypoBecomesEnglish(string typed, string expected)
    {
        Assert.Equal(expected, Translator.ConvertText(typed, LayoutKind.Ru, LayoutKind.En));
        Assert.True(Converts(typed, LayoutKind.Ru), typed);
    }

    [Theory]
    [InlineData("hello", LayoutKind.En)]
    [InlineData("ok", LayoutKind.En)]
    [InlineData("I", LayoutKind.En)]
    [InlineData("a", LayoutKind.En)]
    [InlineData("iPhone", LayoutKind.En)]
    [InlineData("C++", LayoutKind.En)]
    [InlineData("2023", LayoutKind.En)]
    [InlineData("user@mail.ru", LayoutKind.En)]
    [InlineData("https://ya.ru", LayoutKind.En)]
    [InlineData("the", LayoutKind.En)]
    [InlineData("is", LayoutKind.En)]
    [InlineData("to", LayoutKind.En)]
    [InlineData("it", LayoutKind.En)]
    [InlineData("on", LayoutKind.En)]
    [InlineData("we", LayoutKind.En)]
    [InlineData("github", LayoutKind.En)]
    [InlineData("kubernetes", LayoutKind.En)]
    [InlineData("json", LayoutKind.En)]
    [InlineData("hello,", LayoutKind.En)]
    [InlineData("don't", LayoutKind.En)]
    [InlineData("привет", LayoutKind.Ru)]
    [InlineData("в", LayoutKind.Ru)]
    [InlineData("и", LayoutKind.Ru)]
    [InlineData("т.е.", LayoutKind.Ru)]
    [InlineData("кто-то", LayoutKind.Ru)]
    [InlineData("не", LayoutKind.Ru)]
    [InlineData("он", LayoutKind.Ru)]
    [InlineData("мы", LayoutKind.Ru)]
    [InlineData("ты", LayoutKind.Ru)]
    [InlineData("до", LayoutKind.Ru)]
    [InlineData("привет.", LayoutKind.Ru)]
    [InlineData("смартфон", LayoutKind.Ru)]
    [InlineData("нейросеть", LayoutKind.Ru)]
    public void CorrectWordsStayUntouched(string typed, LayoutKind layout)
    {
        Assert.False(Converts(typed, layout), typed);
    }

    [Theory]
    [InlineData("ghbd", LayoutKind.En)]   // привет
    [InlineData("cgfc", LayoutKind.En)]   // спасибо
    [InlineData("руддщ", LayoutKind.Ru)]  // hello
    [InlineData("rjvg", LayoutKind.En)]   // комп
    public void EarlyModeCatchesObviousPrefixes(string typed, LayoutKind layout)
    {
        string alt = Translator.ConvertText(typed, layout, layout.Opposite());
        Assert.True(Detector.ShouldConvertEarly(typed, layout, alt, layout.Opposite()), typed);
    }

    [Theory]
    [InlineData("hell", LayoutKind.En)]
    [InlineData("comp", LayoutKind.En)]
    [InlineData("прив", LayoutKind.Ru)]
    [InlineData("e.g.", LayoutKind.En)]
    public void EarlyModeLeavesValidPrefixes(string typed, LayoutKind layout)
    {
        string alt = Translator.ConvertText(typed, layout, layout.Opposite());
        Assert.False(Detector.ShouldConvertEarly(typed, layout, alt, layout.Opposite()), typed);
    }
}
