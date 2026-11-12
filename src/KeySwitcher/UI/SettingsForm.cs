using System.Diagnostics;
using KeySwitcher.Settings;

namespace KeySwitcher.UI;

internal sealed class SettingsForm : Form
{
    private readonly AppSettings _settings;

    private readonly CheckBox _autoSwitch = Check("Автоматически исправлять раскладку");
    private readonly CheckBox _earlyMode = Check("Исправлять во время набора, не дожидаясь конца слова");
    private readonly NumericUpDown _earlyLetters = Number(3, 8);
    private readonly CheckBox _doubleShift = Check("Двойной Shift — сменить раскладку последнего слова или выделения");
    private readonly NumericUpDown _doubleShiftMs = Number(150, 1000, 50);
    private readonly CheckBox _undo = Check("Backspace сразу после исправления отменяет его");
    private readonly CheckBox _learn = Check("Запоминать отменённые слова как исключения");
    private readonly CheckBox _caps = Check("Исправлять «пРИВЕТ» → «Привет» при включённом Caps Lock");
    private readonly CheckBox _sound = Check("Звук при автоматическом исправлении");
    private readonly CheckBox _showLayout = Check("Показывать текущую раскладку в трее");
    private readonly CheckBox _autoStart = Check("Запускать при старте Windows");

    private readonly ListBox _words = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
    private readonly TextBox _wordInput = new() { Dock = DockStyle.Fill, PlaceholderText = "слово" };

    private readonly ListBox _apps = new() { Dock = DockStyle.Fill, IntegralHeight = false, SelectionMode = SelectionMode.MultiExtended };
    private readonly ComboBox _appInput = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDown };

    private readonly HotkeyBox _pauseHotkey = new() { Width = 220 };
    private readonly HotkeyBox _caseHotkey = new() { Width = 220 };

    public SettingsForm(AppSettings settings, string? lastActiveApp)
    {
        _settings = settings.Clone();

        Text = "KeySwitcher — настройки";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(560, 460);
        Size = new Size(620, 520);
        ShowInTaskbar = true;
        Icon = SystemIcons.Application;

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(GeneralPage());
        tabs.TabPages.Add(WordsPage());
        tabs.TabPages.Add(AppsPage(lastActiveApp));
        tabs.TabPages.Add(HotkeysPage());

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true, MinimumSize = new Size(90, 0) };
        var cancel = new Button { Text = "Отмена", DialogResult = DialogResult.Cancel, AutoSize = true, MinimumSize = new Size(90, 0) };
        AcceptButton = ok;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);

        Controls.Add(tabs);
        Controls.Add(buttons);

        LoadValues();
        // The form is shown modeless, so DialogResult alone does not close it.
        ok.Click += (_, _) =>
        {
            SaveValues();
            DialogResult = DialogResult.OK;
            Close();
        };
        cancel.Click += (_, _) =>
        {
            DialogResult = DialogResult.Cancel;
            Close();
        };
    }

    /// <summary>The edited settings (valid after the dialog returns OK).</summary>
    public AppSettings Result => _settings;

    public bool AutoStartEnabled => _autoStart.Checked;

    // ---------------------------------------------------------------- pages

    private TabPage GeneralPage()
    {
        var page = new TabPage("Общие") { Padding = new Padding(12) };
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
        };

        panel.Controls.Add(_autoSwitch);
        panel.Controls.Add(Indented(_earlyMode));
        panel.Controls.Add(Indented(Row("Начинать проверку после букв:", _earlyLetters), 40));
        panel.Controls.Add(_undo);
        panel.Controls.Add(Indented(_learn));
        panel.Controls.Add(_doubleShift);
        panel.Controls.Add(Indented(Row("Интервал между нажатиями, мс:", _doubleShiftMs)));
        panel.Controls.Add(_caps);
        panel.Controls.Add(_sound);
        panel.Controls.Add(_showLayout);
        panel.Controls.Add(_autoStart);

        var openFolder = new LinkLabel { Text = "Открыть папку с настройками", AutoSize = true, Margin = new Padding(3, 14, 3, 3) };
        openFolder.LinkClicked += (_, _) =>
        {
            Directory.CreateDirectory(AppSettings.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppSettings.Folder}\"") { UseShellExecute = true });
        };
        panel.Controls.Add(openFolder);

        _autoSwitch.CheckedChanged += (_, _) => UpdateEnabled();
        _earlyMode.CheckedChanged += (_, _) => UpdateEnabled();
        _undo.CheckedChanged += (_, _) => UpdateEnabled();
        _doubleShift.CheckedChanged += (_, _) => UpdateEnabled();

        page.Controls.Add(panel);
        return page;
    }

    private TabPage WordsPage()
    {
        var page = new TabPage("Слова-исключения") { Padding = new Padding(12) };
        var hint = new Label
        {
            Text = "Слова, набранные так, никогда не исправляются автоматически (например, «ghbdtn» или «ок»).",
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(560, 0),
            Padding = new Padding(0, 0, 0, 8),
        };
        page.Controls.Add(ListEditor(_words, _wordInput, () => _wordInput.Text.Trim().ToLowerInvariant()));
        page.Controls.Add(hint);
        return page;
    }

    private TabPage AppsPage(string? lastActiveApp)
    {
        var page = new TabPage("Приложения-исключения") { Padding = new Padding(12) };
        var hint = new Label
        {
            Text = "В этих программах KeySwitcher ничего не делает: ни автоисправления, ни двойного Shift. "
                   + "Укажите имя процесса без .exe — например, игры, терминалы, менеджеры паролей.",
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(560, 0),
            Padding = new Padding(0, 0, 0, 8),
        };

        var running = Process.GetProcesses()
            .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
            .Select(p => p.ProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _appInput.Items.AddRange(running);
        if (!string.IsNullOrEmpty(lastActiveApp)) _appInput.Text = lastActiveApp;

        page.Controls.Add(ListEditor(_apps, _appInput,
            () => Path.GetFileNameWithoutExtension(_appInput.Text.Trim())));
        page.Controls.Add(hint);
        return page;
    }

    private TabPage HotkeysPage()
    {
        var page = new TabPage("Горячие клавиши") { Padding = new Padding(12) };
        var table = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        table.Controls.Add(new Label { Text = "Пауза (вкл/выкл всё):", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        table.Controls.Add(_pauseHotkey, 1, 0);
        table.Controls.Add(new Label { Text = "Регистр выделенного текста:", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 1);
        table.Controls.Add(_caseHotkey, 1, 1);

        var hint = new Label
        {
            Text = "Щёлкните поле и нажмите сочетание. Backspace — очистить.\n\n"
                   + "Двойной Shift — сменить раскладку последнего слова, а если слова нет — выделенного текста.\n"
                   + "Регистр выделения меняется по кругу: строчные → ПРОПИСНЫЕ → Как В Заголовке.",
            AutoSize = true,
            Dock = DockStyle.Top,
            MaximumSize = new Size(560, 0),
            Padding = new Padding(0, 12, 0, 0),
        };

        page.Controls.Add(hint);
        page.Controls.Add(table);
        return page;
    }

    private static Control ListEditor(ListBox list, Control input, Func<string> readInput)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var add = new Button { Text = "Добавить", AutoSize = true, Dock = DockStyle.Top };
        var remove = new Button { Text = "Удалить", AutoSize = true, Dock = DockStyle.Top };

        void Add()
        {
            string value = readInput();
            if (value.Length == 0) return;
            if (!list.Items.Cast<string>().Contains(value, StringComparer.OrdinalIgnoreCase)) list.Items.Add(value);
            input.Text = string.Empty;
        }

        add.Click += (_, _) => Add();
        input.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            Add();
            e.Handled = e.SuppressKeyPress = true;
        };
        remove.Click += (_, _) =>
        {
            foreach (var item in list.SelectedItems.Cast<object>().ToArray()) list.Items.Remove(item);
        };
        list.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            foreach (var item in list.SelectedItems.Cast<object>().ToArray()) list.Items.Remove(item);
        };

        var side = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        side.Controls.Add(remove);

        layout.Controls.Add(input, 0, 0);
        layout.Controls.Add(add, 1, 0);
        layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(side, 1, 1);
        return layout;
    }

    // ---------------------------------------------------------------- values

    private void LoadValues()
    {
        var s = _settings;
        _autoSwitch.Checked = s.AutoSwitchEnabled;
        _earlyMode.Checked = s.EarlyMode;
        _earlyLetters.Value = Math.Clamp(s.EarlyModeMinLetters, 3, 8);
        _doubleShift.Checked = s.DoubleShiftEnabled;
        _doubleShiftMs.Value = Math.Clamp(s.DoubleShiftIntervalMs, 150, 1000);
        _undo.Checked = s.UndoOnBackspace;
        _learn.Checked = s.LearnFromUndo;
        _caps.Checked = s.FixCapsLockCase;
        _sound.Checked = s.PlaySound;
        _showLayout.Checked = s.ShowLayoutInTray;
        _autoStart.Checked = AutoStart.IsEnabled;
        _words.Items.AddRange(s.ExcludedWords.Cast<object>().ToArray());
        _apps.Items.AddRange(s.ExcludedApps.Cast<object>().ToArray());
        _pauseHotkey.Hotkey = s.PauseHotkey;
        _caseHotkey.Hotkey = s.CaseHotkey;
        UpdateEnabled();
    }

    private void SaveValues()
    {
        var s = _settings;
        s.AutoSwitchEnabled = _autoSwitch.Checked;
        s.EarlyMode = _earlyMode.Checked;
        s.EarlyModeMinLetters = (int)_earlyLetters.Value;
        s.DoubleShiftEnabled = _doubleShift.Checked;
        s.DoubleShiftIntervalMs = (int)_doubleShiftMs.Value;
        s.UndoOnBackspace = _undo.Checked;
        s.LearnFromUndo = _learn.Checked;
        s.FixCapsLockCase = _caps.Checked;
        s.PlaySound = _sound.Checked;
        s.ShowLayoutInTray = _showLayout.Checked;
        s.ExcludedWords = _words.Items.Cast<string>().ToList();
        s.ExcludedApps = _apps.Items.Cast<string>().ToList();
        s.PauseHotkey = _pauseHotkey.Hotkey;
        s.CaseHotkey = _caseHotkey.Hotkey;
    }

    private void UpdateEnabled()
    {
        _earlyMode.Enabled = _autoSwitch.Checked;
        _earlyLetters.Enabled = _autoSwitch.Checked && _earlyMode.Checked;
        _learn.Enabled = _undo.Checked;
        _doubleShiftMs.Enabled = _doubleShift.Checked;
    }

    // ---------------------------------------------------------------- helpers

    private static CheckBox Check(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(3, 4, 3, 4) };

    private static NumericUpDown Number(int min, int max, int step = 1) =>
        new() { Minimum = min, Maximum = max, Increment = step, Width = 70 };

    private static Control Row(string label, Control control)
    {
        var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = Padding.Empty };
        row.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) });
        row.Controls.Add(control);
        return row;
    }

    private static Control Indented(Control control, int left = 22)
    {
        control.Margin = new Padding(left, control.Margin.Top, control.Margin.Right, control.Margin.Bottom);
        return control;
    }
}
