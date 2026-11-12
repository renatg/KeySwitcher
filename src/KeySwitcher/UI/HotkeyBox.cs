using KeySwitcher.Native;
using KeySwitcher.Settings;

namespace KeySwitcher.UI;

/// <summary>Text box that records a shortcut. Backspace or Delete clears it.</summary>
internal sealed class HotkeyBox : TextBox
{
    private Hotkey _hotkey = Hotkey.None;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        ShortcutsEnabled = false;
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Hotkey Hotkey
    {
        get => _hotkey;
        set
        {
            _hotkey = value;
            Text = value.IsEmpty ? "(нет)" : value.ToString();
        }
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var key = keyData & Keys.KeyCode;
        if (key is Keys.Back or Keys.Delete && (keyData & Keys.Modifiers) == 0)
        {
            Hotkey = Hotkey.None;
            return true;
        }
        if (key == Keys.Tab && (keyData & Keys.Modifiers) == 0) return base.ProcessCmdKey(ref msg, keyData);

        bool win = NativeMethods.IsKeyDown(VirtualKeys.LWin) || NativeMethods.IsKeyDown(VirtualKeys.RWin);
        var hotkey = Hotkey.FromKeyData(keyData, win);
        if (!hotkey.IsEmpty)
        {
            // A bare letter or digit would fire while typing; require a modifier for those.
            bool hasModifier = hotkey.Ctrl || hotkey.Alt || hotkey.Win;
            bool isFunctionKey = key is >= Keys.F1 and <= Keys.F24 or Keys.Pause or Keys.Scroll;
            if (hasModifier || isFunctionKey) Hotkey = hotkey;
        }
        return true;
    }
}
