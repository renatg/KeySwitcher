using KeySwitcher.Core;
using KeySwitcher.Input;
using KeySwitcher.Layout;
using KeySwitcher.Settings;

namespace KeySwitcher.Tests;

public class TranslatorTests
{
    private static readonly KeyTranslator Translator = KeyTranslator.CreateDefault();

    [Theory]
    [InlineData("Ghbdtn? vbh!", "Привет, мир!")]
    [InlineData("Z ,eleнастроение", "Я будунастроение")]
    [InlineData("`", "ё")]
    [InlineData("[]", "хъ")]
    public void ConvertsEnglishToRussian(string input, string expected) =>
        Assert.Equal(expected, Translator.ConvertText(input, LayoutKind.En, LayoutKind.Ru));

    [Theory]
    [InlineData("Руддщ, цщкдв.", "Hello? world/")]
    [InlineData("Ёё", "~`")]
    public void ConvertsRussianToEnglish(string input, string expected) =>
        Assert.Equal(expected, Translator.ConvertText(input, LayoutKind.Ru, LayoutKind.En));

    [Theory]
    [InlineData("hello world", LayoutKind.En)]
    [InlineData("привет", LayoutKind.Ru)]
    [InlineData("123 !!", LayoutKind.Other)]
    public void GuessesLayout(string text, LayoutKind expected) =>
        Assert.Equal(expected, KeyTranslator.GuessLayout(text));

    [Fact]
    public void RendersKeyStrokesInBothLayouts()
    {
        // g h b d t n typed in the English layout, first one with Shift.
        var keys = "GHBDTN".Select((c, i) => new KeyStroke(c, i == 0, false, LayoutKind.En)).ToList();
        Assert.Equal("Ghbdtn", KeyStrokes.Render(keys, Translator));
        Assert.Equal("Привет", KeyStrokes.Render(keys, Translator, flip: true));
    }

    [Fact]
    public void CapsLockInvertsShiftForLettersOnly()
    {
        // Caps Lock on: 'a' key → 'A' without Shift, 'a' with Shift; ';' is not a letter in English.
        Assert.Equal('A', Translator.GetChar(LayoutKind.En, 'A', shift: false, caps: true));
        Assert.Equal('a', Translator.GetChar(LayoutKind.En, 'A', shift: true, caps: true));
        Assert.Equal(';', Translator.GetChar(LayoutKind.En, 0xBA, shift: false, caps: true));
        // ...but it is a letter (ж) in Russian.
        Assert.Equal('Ж', Translator.GetChar(LayoutKind.Ru, 0xBA, shift: false, caps: true));
    }

    [Fact]
    public void CommonLayoutIsOtherForMixedWords()
    {
        var keys = new List<KeyStroke>
        {
            new('A', false, false, LayoutKind.En),
            new('B', false, false, LayoutKind.Ru),
        };
        Assert.Equal(LayoutKind.Other, KeyStrokes.CommonLayout(keys));
    }
}

public class CaseFixerTests
{
    [Theory]
    [InlineData("пРИВЕТ", true)]
    [InlineData("hELLO,", true)]
    [InlineData("пРи", false)]
    [InlineData("Привет", false)]
    [InlineData("NASA", false)]
    [InlineData("oK", false)]
    public void DetectsInvertedCaps(string word, bool expected) =>
        Assert.Equal(expected, CaseFixer.IsInvertedCaps(word));

    [Fact]
    public void SwapsCase() => Assert.Equal("Привет, World", CaseFixer.SwapCase("пРИВЕТ, wORLD"));

    [Theory]
    [InlineData("hello world", "HELLO WORLD")]
    [InlineData("HELLO WORLD", "Hello World")]
    [InlineData("Hello World", "hello world")]
    [InlineData("пРИВЕТ мИР", "Привет Мир")]
    [InlineData("it's ok", "IT'S OK")]
    [InlineData("IT'S OK", "It's Ok")]
    public void CyclesCase(string input, string expected) =>
        Assert.Equal(expected, CaseFixer.CycleCase(input));

    [Fact]
    public void CycleCaseIgnoresTextWithoutLetters() => Assert.Null(CaseFixer.CycleCase("123 !?"));
}

public class HotkeyTests
{
    [Theory]
    [InlineData("Ctrl+Alt+P")]
    [InlineData("Ctrl+Shift+F12")]
    [InlineData("Pause")]
    [InlineData("Alt+1")]
    public void RoundTrips(string text) => Assert.Equal(text, Hotkey.Parse(text).ToString());

    [Fact]
    public void EmptyIsNone() => Assert.True(Hotkey.Parse("").IsEmpty);
}
