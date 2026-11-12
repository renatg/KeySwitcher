using KeySwitcher.Core;
using KeySwitcher.Detection;
using KeySwitcher.Input;
using KeySwitcher.Layout;
using KeySwitcher.Native;
using KeySwitcher.Settings;
using KeySwitcher.UI;
using Timer = System.Windows.Forms.Timer;

namespace KeySwitcher;

internal sealed class TrayContext : ApplicationContext, IEngineHost
{
    private static readonly HashSet<string> IgnoredForegroundApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "KeySwitcher", "explorer", "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "",
    };

    private readonly SynchronizationContext _ui;
    private AppSettings _settings;
    private readonly LayoutManager _layouts = new();
    private KeyTranslator _translator;
    private readonly SwitchEngine _engine;
    private readonly KeyboardHook _hook;
    private readonly SelectionService _selection;
    private readonly NotifyIcon _tray;
    private readonly TrayIconRenderer _icons = new();
    private readonly ClickSound _sound = new();
    private readonly Timer _timer;
    private readonly ToolStripMenuItem _autoSwitchItem;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _excludeAppItem;
    private readonly ToolStripMenuItem _autoStartItem;
    private SettingsForm? _settingsForm;
    private string _lastActiveApp = string.Empty;
    private IntPtr _lastRu, _lastEn;
    private int _ticks;
    private bool _busy;

    public TrayContext(AppSettings settings, bool firstRun)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _settings = settings;
        _translator = KeyTranslator.Create(_layouts.EnHkl, _layouts.RuHkl);
        _lastRu = _layouts.RuHkl;
        _lastEn = _layouts.EnHkl;

        _engine = new SwitchEngine(_layouts, _translator, EngineConfig.From(settings), this);
        _selection = new SelectionService(_layouts, () => _translator);
        _hook = new KeyboardHook(_engine);

        _autoSwitchItem = new ToolStripMenuItem("Автопереключение", null, (_, _) => ToggleAutoSwitch());
        _pauseItem = new ToolStripMenuItem("Пауза", null, (_, _) => SetPaused(!_engine.Paused));
        _excludeAppItem = new ToolStripMenuItem("Исключить приложение", null, (_, _) => ExcludeLastApp());
        _autoStartItem = new ToolStripMenuItem("Запускать при старте Windows", null, (_, _) =>
        {
            AutoStart.Set(!AutoStart.IsEnabled);
            _autoStartItem!.Checked = AutoStart.IsEnabled;
        });

        var menu = new ContextMenuStrip();
        menu.Items.Add(_autoSwitchItem);
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_excludeAppItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Настройки…", null, (_, _) => ShowSettings()) { Font = new Font(menu.Font, FontStyle.Bold) });
        menu.Items.Add(_autoStartItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Выход", null, (_, _) => ExitThread()));
        menu.Opening += (_, _) => UpdateMenu();

        _tray = new NotifyIcon
        {
            Icon = _icons.Get(LayoutKind.Other, false, settings.ShowLayoutInTray),
            Text = "KeySwitcher",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _tray.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) ShowSettings();
        };

        _hook.Start();
        if (!_hook.IsInstalled)
            _tray.ShowBalloonTip(5000, "KeySwitcher", "Не удалось установить перехват клавиатуры.", ToolTipIcon.Error);

        // The dictionaries take a moment to load; typing works meanwhile, corrections start when ready.
        Task.Run(() =>
        {
            try { _engine.SetDetector(LanguageDetector.Default); }
            catch (Exception ex) { Log.Error("Failed to load dictionaries", ex); }
        });

        _timer = new Timer { Interval = 250 };
        _timer.Tick += (_, _) => OnTimer();
        _timer.Start();

        if (!_layouts.IsAvailable)
            _tray.ShowBalloonTip(8000, "KeySwitcher",
                "Нужны русская и английская раскладки. Добавьте их в Параметры → Время и язык → Язык и регион.",
                ToolTipIcon.Warning);
        else if (firstRun)
            _tray.ShowBalloonTip(5000, "KeySwitcher",
                "Работает в области уведомлений. Двойной Shift — сменить раскладку слова или выделения.",
                ToolTipIcon.Info);
    }

    // ---------------------------------------------------------------- IEngineHost (hook thread → UI)

    public void PauseToggled(bool paused) => _ui.Post(_ =>
    {
        UpdateIcon();
        _tray.ShowBalloonTip(1500, "KeySwitcher", paused ? "Пауза: KeySwitcher отключён" : "KeySwitcher снова работает",
            ToolTipIcon.None);
    }, null);

    public void CorrectionMade() => _ui.Post(_ =>
    {
        if (_settings.PlaySound) _sound.Play();
    }, null);

    public void WordLearned(string word) => _ui.Post(_ =>
    {
        if (_settings.ExcludedWords.Contains(word)) return;
        _settings.ExcludedWords.Add(word);
        _settings.Save();
        // The engine already ignores the word; no config update needed (it would drop other learned words).
    }, null);

    public void ConvertSelectionRequested() => _ui.Post(_ => RunExclusive(_selection.ConvertLayout), null);

    public void ChangeCaseRequested() => _ui.Post(_ => RunExclusive(_selection.ChangeCase), null);

    private void RunExclusive(Action action)
    {
        if (_busy) return;
        _busy = true;
        try { action(); }
        finally { _busy = false; }
    }

    // ---------------------------------------------------------------- UI

    private void OnTimer()
    {
        var fg = NativeMethods.GetForegroundWindow();
        string app = ForegroundApp.GetProcessName(fg);
        if (!IgnoredForegroundApps.Contains(app)) _lastActiveApp = app;

        UpdateIcon();

        if (++_ticks % 40 == 0) // every 10 s: layouts may have been added or removed
        {
            _layouts.Refresh();
            if (_layouts.RuHkl != _lastRu || _layouts.EnHkl != _lastEn)
            {
                _lastRu = _layouts.RuHkl;
                _lastEn = _layouts.EnHkl;
                _translator = KeyTranslator.Create(_layouts.EnHkl, _layouts.RuHkl);
                _engine.SetTranslator(_translator);
            }
        }
    }

    private void UpdateIcon()
    {
        var layout = _layouts.GetCurrentLayout();
        string app = ForegroundApp.CurrentProcessName;
        bool excluded = _settings.ExcludedApps.Contains(app, StringComparer.OrdinalIgnoreCase);
        bool disabled = _engine.Paused || excluded;
        var icon = _icons.Get(layout, disabled, _settings.ShowLayoutInTray);
        if (!ReferenceEquals(_tray.Icon, icon)) _tray.Icon = icon;

        string state = _engine.Paused ? "пауза"
            : excluded ? $"отключён в {app}"
            : !_settings.AutoSwitchEnabled ? "автопереключение выкл."
            : "работает";
        string text = $"KeySwitcher — {state}";
        if (text.Length > 127) text = text[..127];
        if (_tray.Text != text) _tray.Text = text;
    }

    private void UpdateMenu()
    {
        _autoSwitchItem.Checked = _settings.AutoSwitchEnabled;
        _pauseItem.Checked = _engine.Paused;
        _pauseItem.ShortcutKeyDisplayString = _settings.PauseHotkey.ToString();
        _autoStartItem.Checked = AutoStart.IsEnabled;

        bool canExclude = _lastActiveApp.Length > 0
                          && !_settings.ExcludedApps.Contains(_lastActiveApp, StringComparer.OrdinalIgnoreCase);
        _excludeAppItem.Visible = _lastActiveApp.Length > 0;
        _excludeAppItem.Enabled = canExclude;
        _excludeAppItem.Text = canExclude
            ? $"Отключить в «{_lastActiveApp}»"
            : $"Отключено в «{_lastActiveApp}»";
    }

    private void ToggleAutoSwitch()
    {
        _settings.AutoSwitchEnabled = !_settings.AutoSwitchEnabled;
        ApplySettings(_settings);
    }

    private void SetPaused(bool paused)
    {
        _engine.Paused = paused;
        UpdateIcon();
    }

    private void ExcludeLastApp()
    {
        if (_lastActiveApp.Length == 0) return;
        if (!_settings.ExcludedApps.Contains(_lastActiveApp, StringComparer.OrdinalIgnoreCase))
            _settings.ExcludedApps.Add(_lastActiveApp);
        ApplySettings(_settings);
    }

    private void ShowSettings()
    {
        if (_settingsForm != null)
        {
            _settingsForm.Activate();
            return;
        }

        _settingsForm = new SettingsForm(_settings, _lastActiveApp);
        _settingsForm.FormClosed += (_, _) =>
        {
            var form = _settingsForm;
            _settingsForm = null;
            if (form?.DialogResult == DialogResult.OK)
            {
                AutoStart.Set(form.AutoStartEnabled);
                ApplySettings(form.Result);
            }
            form?.Dispose();
        };
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void ApplySettings(AppSettings settings)
    {
        _settings = settings;
        _settings.Save();
        _engine.UpdateConfig(EngineConfig.From(_settings));
        UpdateIcon();
    }

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _hook.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _icons.Dispose();
        _sound.Dispose();
        _settingsForm?.Close();
        base.ExitThreadCore();
    }
}
