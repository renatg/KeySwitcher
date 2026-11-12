using KeySwitcher.Detection;
using KeySwitcher.Input;
using KeySwitcher.Layout;
using KeySwitcher.Native;
using KeySwitcher.Settings;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Core;

/// <summary>Immutable snapshot of the settings the hook thread needs.</summary>
internal sealed class EngineConfig
{
    public bool AutoSwitch { get; init; }
    public bool EarlyMode { get; init; }
    public int EarlyMinLetters { get; init; }
    public bool DoubleShift { get; init; }
    public int DoubleShiftMs { get; init; }
    public bool UndoOnBackspace { get; init; }
    public bool LearnFromUndo { get; init; }
    public bool FixCapsLock { get; init; }
    public Hotkey PauseHotkey { get; init; } = Hotkey.None;
    public Hotkey CaseHotkey { get; init; } = Hotkey.None;
    public HashSet<string> ExcludedWords { get; init; } = new();
    public HashSet<string> ExcludedApps { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    public static EngineConfig From(AppSettings s) => new()
    {
        AutoSwitch = s.AutoSwitchEnabled,
        EarlyMode = s.EarlyMode,
        EarlyMinLetters = Math.Clamp(s.EarlyModeMinLetters, 3, 8),
        DoubleShift = s.DoubleShiftEnabled,
        DoubleShiftMs = Math.Clamp(s.DoubleShiftIntervalMs, 150, 1000),
        UndoOnBackspace = s.UndoOnBackspace,
        LearnFromUndo = s.LearnFromUndo,
        FixCapsLock = s.FixCapsLockCase,
        PauseHotkey = s.PauseHotkey,
        CaseHotkey = s.CaseHotkey,
        ExcludedWords = new HashSet<string>(s.ExcludedWords.Select(w => w.Trim().ToLowerInvariant()).Where(w => w.Length > 0)),
        ExcludedApps = new HashSet<string>(
            s.ExcludedApps.Select(a => Path.GetFileNameWithoutExtension(a.Trim())).Where(a => a.Length > 0),
            StringComparer.OrdinalIgnoreCase),
    };
}

/// <summary>Callbacks from the hook thread. Implementations must marshal to the UI thread themselves.</summary>
internal interface IEngineHost
{
    void PauseToggled(bool paused);
    void CorrectionMade();
    void WordLearned(string word);
    void ConvertSelectionRequested();
    void ChangeCaseRequested();
}

/// <summary>The keyboard state machine. All methods except the public setters run on the hook thread.</summary>
internal sealed class SwitchEngine : IKeyboardHandler
{
    private sealed record Correction(
        List<KeyStroke> OriginalKeys, string OldText, string NewText, string Delimiter, LayoutKind OldLayout, long At);

    private const int ShiftTapMaxMs = 300;
    private const int UndoWindowMs = 7000;

    private readonly LayoutManager _layouts;
    private readonly IEngineHost _host;
    private volatile KeyTranslator _translator;
    private volatile LanguageDetector? _detector;
    private volatile EngineConfig _config;
    private volatile bool _paused;
    private volatile bool _resetLearned;

    // Word being typed and the previous word (followed only by spaces).
    private List<KeyStroke> _current = new();
    private List<KeyStroke>? _prev;
    private string _prevTrailing = string.Empty;
    private bool _noCorrect;
    private bool _earlyDone;
    private Correction? _last;
    private IntPtr _lastForeground;
    private readonly HashSet<string> _learned = new();
    private readonly HashSet<int> _swallowUp = new();

    // Double Shift detection.
    private bool _shiftHeld;
    private bool _shiftClean;
    private long _shiftDownAt;
    private long _lastShiftTapAt;

    public SwitchEngine(LayoutManager layouts, KeyTranslator translator, EngineConfig config, IEngineHost host)
    {
        _layouts = layouts;
        _translator = translator;
        _config = config;
        _host = host;
    }

    public bool Paused
    {
        get => _paused;
        set => _paused = value;
    }

    public void SetDetector(LanguageDetector detector) => _detector = detector;

    public void SetTranslator(KeyTranslator translator) => _translator = translator;

    public void UpdateConfig(EngineConfig config)
    {
        _config = config;
        _resetLearned = true;
    }

    // ---------------------------------------------------------------- keyboard events

    public bool OnKeyDown(int vk)
    {
        if (_resetLearned)
        {
            _resetLearned = false;
            _learned.Clear();
        }

        var cfg = _config;
        long now = Environment.TickCount64;

        if (cfg.PauseHotkey.Matches(vk))
        {
            _paused = !_paused;
            ResetAll();
            _swallowUp.Add(vk);
            _host.PauseToggled(_paused);
            return true;
        }

        if (!_paused && cfg.CaseHotkey.Matches(vk) && !IsExcludedApp(cfg))
        {
            ResetAll();
            _swallowUp.Add(vk);
            _host.ChangeCaseRequested();
            return true;
        }

        if (VirtualKeys.IsShift(vk))
        {
            if (!_shiftHeld)
            {
                _shiftHeld = true;
                _shiftDownAt = now;
                _shiftClean = !IsKeyDown(VirtualKeys.Control) && !IsKeyDown(VirtualKeys.Menu)
                              && !IsKeyDown(VirtualKeys.LWin) && !IsKeyDown(VirtualKeys.RWin);
            }
            return false;
        }

        // Any other key breaks a Shift tap sequence.
        _shiftClean = false;
        _lastShiftTapAt = 0;

        if (VirtualKeys.IsModifier(vk) || vk is VirtualKeys.Capital or VirtualKeys.NumLock or VirtualKeys.Scroll)
            return false;

        if (_paused)
        {
            ResetAll();
            return false;
        }

        var fg = GetForegroundWindow();
        if (fg != _lastForeground)
        {
            ResetAll();
            _lastForeground = fg;
        }

        if (IsKeyDown(VirtualKeys.Control) || IsKeyDown(VirtualKeys.Menu)
            || IsKeyDown(VirtualKeys.LWin) || IsKeyDown(VirtualKeys.RWin))
        {
            ResetAll();
            return false;
        }

        switch (vk)
        {
            case VirtualKeys.Back: return OnBackspace(cfg, now);
            case VirtualKeys.Space: return OnDelimiter(vk, " ", cfg, now);
            case VirtualKeys.Return: return OnDelimiter(vk, "\n", cfg, now);
            case VirtualKeys.Tab: return OnDelimiter(vk, "\t", cfg, now);
        }

        if (!_translator.IsCharKey(vk))
        {
            ResetAll();
            return false;
        }

        var layout = _layouts.GetCurrentLayout();
        if (layout == LayoutKind.Other)
        {
            ResetAll();
            return false;
        }

        bool shift = IsKeyDown(VirtualKeys.Shift);
        bool caps = (GetKeyState(VirtualKeys.Capital) & 1) != 0;
        _last = null;
        _prev = null;
        _prevTrailing = string.Empty;
        _current.Add(new KeyStroke(vk, shift, caps, layout));

        if (cfg.EarlyMode && cfg.AutoSwitch && !_noCorrect && !_earlyDone)
            return TryEarly(vk, cfg, now);
        return false;
    }

    public bool OnKeyUp(int vk)
    {
        if (_swallowUp.Remove(vk)) return true;
        if (!VirtualKeys.IsShift(vk)) return false;

        long now = Environment.TickCount64;
        bool tap = _shiftHeld && _shiftClean && now - _shiftDownAt <= ShiftTapMaxMs;
        _shiftHeld = false;
        if (!tap)
        {
            _lastShiftTapAt = 0;
            return false;
        }

        var cfg = _config;
        if (!cfg.DoubleShift || _paused) return false;

        if (_lastShiftTapAt != 0 && now - _lastShiftTapAt <= cfg.DoubleShiftMs)
        {
            _lastShiftTapAt = 0;
            OnDoubleShift(cfg);
        }
        else
        {
            _lastShiftTapAt = now;
        }
        return false;
    }

    public void OnMouseDown() => ResetAll();

    public void OnForeignInjected(int vk)
    {
        if (vk == VirtualKeys.Packet) ResetAll();
    }

    // ---------------------------------------------------------------- handlers

    private bool OnBackspace(EngineConfig cfg, long now)
    {
        var last = _last;
        _last = null;
        if (last != null && cfg.UndoOnBackspace && now - last.At <= UndoWindowMs && !IsExcludedApp(cfg))
        {
            Undo(last, cfg);
            _swallowUp.Add(VirtualKeys.Back);
            return true;
        }

        if (_current.Count > 0)
        {
            _current.RemoveAt(_current.Count - 1);
            if (_current.Count == 0)
            {
                _noCorrect = false;
                _earlyDone = false;
            }
            return false;
        }

        if (_prev != null)
        {
            if (_prevTrailing.Length > 0) _prevTrailing = _prevTrailing[..^1];
            if (_prevTrailing.Length == 0)
            {
                _current = _prev;
                _prev = null;
            }
        }
        return false;
    }

    private bool OnDelimiter(int vk, string delimiter, EngineConfig cfg, long now)
    {
        _last = null;
        if (_current.Count == 0)
        {
            if (vk == VirtualKeys.Space && _prev != null) _prevTrailing += " ";
            else
            {
                _prev = null;
                _prevTrailing = string.Empty;
            }
            return false;
        }

        var token = _current;
        bool handled = false;
        if (!_noCorrect && !_earlyDone && !IsExcludedApp(cfg))
            handled = TryCorrect(ref token, vk, delimiter, cfg, now);

        _current = new List<KeyStroke>();
        _noCorrect = false;
        _earlyDone = false;
        if (vk == VirtualKeys.Space)
        {
            _prev = token;
            _prevTrailing = " ";
        }
        else
        {
            _prev = null;
            _prevTrailing = string.Empty;
        }
        return handled;
    }

    /// <summary>Corrects the finished word (layout and/or Caps Lock case). On success the key
    /// strokes are replaced by what is now on screen and the delimiter is re-sent by us.</summary>
    private bool TryCorrect(ref List<KeyStroke> token, int vk, string delimiter, EngineConfig cfg, long now)
    {
        var translator = _translator;
        string typed = KeyStrokes.Render(token, translator);
        if (typed.Length == 0) return false;
        var layout = KeyStrokes.CommonLayout(token);

        bool convert = false;
        string result = typed;
        var keys = token;

        var detector = _detector;
        if (cfg.AutoSwitch && detector != null && layout != LayoutKind.Other
            && !IsExcludedWord(cfg, TextUtil.Core(typed).ToLowerInvariant()))
        {
            string alt = KeyStrokes.Render(token, translator, flip: true);
            if (detector.ShouldConvert(typed, layout, alt, layout.Opposite()))
            {
                convert = true;
                result = alt;
                keys = KeyStrokes.Flip(token);
            }
        }

        bool caseFix = false;
        if (cfg.FixCapsLock && token.All(k => k.Caps) && CaseFixer.IsInvertedCaps(TextUtil.Core(result)))
        {
            result = CaseFixer.SwapCase(result);
            keys = keys.Select(k => k with { Caps = false }).ToList();
            caseFix = true;
        }

        if (!convert && !caseFix) return false;

        var input = InputSender.Create().Backspaces(typed.Length).Text(result);
        if (caseFix) input.Press(VirtualKeys.Capital);
        input.Press(vk);
        input.Send();
        _swallowUp.Add(vk);

        if (convert)
        {
            _layouts.SwitchTo(layout.Opposite());
            _host.CorrectionMade();
            if (vk == VirtualKeys.Space)
                _last = new Correction(token, typed, result, delimiter, layout, now);
        }
        token = keys;
        return true;
    }

    private bool TryEarly(int vk, EngineConfig cfg, long now)
    {
        var token = _current;
        var detector = _detector;
        if (detector == null || token.Count < cfg.EarlyMinLetters || IsExcludedApp(cfg)) return false;
        var layout = KeyStrokes.CommonLayout(token);
        if (layout == LayoutKind.Other) return false;

        var translator = _translator;
        string typed = KeyStrokes.Render(token, translator);
        if (IsExcludedWord(cfg, typed.ToLowerInvariant())) return false;
        string alt = KeyStrokes.Render(token, translator, flip: true);
        if (!detector.ShouldConvertEarly(typed, layout, alt, layout.Opposite())) return false;

        // The current key has not reached the application yet: swallow it and retype everything.
        int onScreen = KeyStrokes.Render(token.Take(token.Count - 1).ToList(), translator).Length;
        InputSender.Create().Backspaces(onScreen).Text(alt).Send();
        _swallowUp.Add(vk);
        _layouts.SwitchTo(layout.Opposite());

        _current = KeyStrokes.Flip(token);
        _earlyDone = true;
        _last = new Correction(token, typed, alt, string.Empty, layout, now);
        _host.CorrectionMade();
        return true;
    }

    private void Undo(Correction c, EngineConfig cfg)
    {
        InputSender.Create()
            .Backspaces(c.NewText.Length + c.Delimiter.Length)
            .Text(c.OldText)
            .Send();
        _layouts.SwitchTo(c.OldLayout);

        _current = new List<KeyStroke>(c.OriginalKeys);
        _prev = null;
        _prevTrailing = string.Empty;
        _noCorrect = true;
        _earlyDone = true;

        if (cfg.LearnFromUndo)
        {
            string word = TextUtil.Core(c.OldText).ToLowerInvariant();
            if (word.Length > 0 && _learned.Add(word)) _host.WordLearned(word);
        }
    }

    private void OnDoubleShift(EngineConfig cfg)
    {
        if (IsExcludedApp(cfg)) return;
        if (GetForegroundWindow() != _lastForeground) ResetAll();
        _last = null;

        if (_current.Count > 0)
        {
            if (ConvertKeys(_current, string.Empty))
            {
                _current = KeyStrokes.Flip(_current);
                _noCorrect = true;
            }
        }
        else if (_prev != null && _prevTrailing.Length > 0 && _prevTrailing.All(c => c == ' '))
        {
            if (ConvertKeys(_prev, _prevTrailing)) _prev = KeyStrokes.Flip(_prev);
        }
        else
        {
            ResetAll();
            _host.ConvertSelectionRequested();
        }
    }

    private bool ConvertKeys(List<KeyStroke> keys, string trailing)
    {
        var translator = _translator;
        string typed = KeyStrokes.Render(keys, translator);
        string alt = KeyStrokes.Render(keys, translator, flip: true);
        if (typed.Length == 0) return false;
        InputSender.Create().Backspaces(typed.Length + trailing.Length).Text(alt + trailing).Send();
        _layouts.SwitchTo(keys[^1].Layout.Opposite());
        return true;
    }

    // ---------------------------------------------------------------- helpers

    private void ResetAll()
    {
        _current = new List<KeyStroke>();
        _prev = null;
        _prevTrailing = string.Empty;
        _last = null;
        _noCorrect = false;
        _earlyDone = false;
    }

    private bool IsExcludedWord(EngineConfig cfg, string lowerWord) =>
        cfg.ExcludedWords.Contains(lowerWord) || _learned.Contains(lowerWord);

    private static bool IsExcludedApp(EngineConfig cfg) =>
        cfg.ExcludedApps.Count > 0 && cfg.ExcludedApps.Contains(ForegroundApp.CurrentProcessName);
}
