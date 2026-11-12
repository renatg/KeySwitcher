using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using KeySwitcher.Input;
using KeySwitcher.Layout;
using KeySwitcher.Native;
using static KeySwitcher.Native.NativeMethods;
using LayoutKind = KeySwitcher.Layout.LayoutKind;
using WinFormsDataObject = System.Windows.Forms.DataObject;
using WinFormsIDataObject = System.Windows.Forms.IDataObject;

namespace KeySwitcher.Core;

/// <summary>Reads and replaces the selected text of the foreground application. Runs on the UI (STA) thread.</summary>
internal sealed class SelectionService
{
    private const int MaxTypedLength = 200;
    private const int MaxReselectLength = 300;

    private readonly LayoutManager _layouts;
    private readonly Func<KeyTranslator> _translator;

    public SelectionService(LayoutManager layouts, Func<KeyTranslator> translator)
    {
        _layouts = layouts;
        _translator = translator;
    }

    /// <summary>Retypes the selection in the other layout and switches the layout.</summary>
    public void ConvertLayout()
    {
        Transform(text =>
        {
            var from = KeyTranslator.GuessLayout(text);
            if (from == LayoutKind.Other) return null;
            return (_translator().ConvertText(text, from, from.Opposite()), from.Opposite());
        });
    }

    /// <summary>Cycles the case of the selection: lower → UPPER → Title.</summary>
    public void ChangeCase()
    {
        Transform(text => CaseFixer.CycleCase(text) is { } result ? (result, LayoutKind.Other) : null);
    }

    private void Transform(Func<string, (string Text, LayoutKind SwitchTo)?> transform)
    {
        try
        {
            if (!WaitForModifiersReleased()) return;
            var fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            ClipboardBackup? backup = null;
            string? text = GetSelectedText(fg, ref backup);
            try
            {
                if (string.IsNullOrEmpty(text)) return;
                var result = transform(text);
                if (result == null || result.Value.Text == text) return;
                Replace(result.Value.Text, ref backup);
                if (result.Value.SwitchTo != LayoutKind.Other) _layouts.SwitchTo(result.Value.SwitchTo);
            }
            finally
            {
                backup?.Restore();
            }
        }
        catch (Exception ex)
        {
            Log.Error("Selection transform failed", ex);
        }
    }

    private static bool WaitForModifiersReleased()
    {
        int[] keys = { VirtualKeys.Shift, VirtualKeys.Control, VirtualKeys.Menu, VirtualKeys.LWin, VirtualKeys.RWin };
        var until = Environment.TickCount64 + 1500;
        while (keys.Any(IsKeyDown))
        {
            if (Environment.TickCount64 > until) return false;
            Thread.Sleep(15);
        }
        return true;
    }

    // ---------------------------------------------------------------- reading

    private static string? GetSelectedText(IntPtr fg, ref ClipboardBackup? backup)
    {
        var (supported, uiaText) = TryGetSelectionViaUiAutomation();
        if (supported) return string.IsNullOrEmpty(uiaText) ? null : uiaText;
        return GetSelectionViaClipboard(fg, ref backup);
    }

    private static (bool Supported, string? Text) TryGetSelectionViaUiAutomation()
    {
        var task = Task.Run<(bool, string?)>(() =>
        {
            try
            {
                var element = AutomationElement.FocusedElement;
                if (element != null && element.TryGetCurrentPattern(TextPattern.Pattern, out object pattern))
                {
                    var sb = new StringBuilder();
                    foreach (var range in ((TextPattern)pattern).GetSelection())
                        sb.Append(range.GetText(-1));
                    return (true, sb.ToString());
                }
            }
            catch
            {
                // Element went away or does not implement the pattern properly.
            }
            return (false, null);
        });
        return task.Wait(300) ? task.Result : (false, null);
    }

    private static string? GetSelectionViaClipboard(IntPtr fg, ref ClipboardBackup? backup)
    {
        backup ??= ClipboardBackup.Capture();
        uint seq = GetClipboardSequenceNumber();
        int copyKey = ForegroundApp.IsTerminal(fg) ? VirtualKeys.Insert : VirtualKeys.C;
        InputSender.Create().Chord(VirtualKeys.Control, copyKey).Send();

        var until = Environment.TickCount64 + 500;
        while (GetClipboardSequenceNumber() == seq)
        {
            if (Environment.TickCount64 > until)
            {
                // Nothing was copied: nothing is selected, the clipboard is untouched.
                backup = null;
                return null;
            }
            Thread.Sleep(15);
        }
        Thread.Sleep(30); // let the owner finish rendering all formats

        var data = Retry(Clipboard.GetDataObject);
        if (data == null || IsEmptySelectionCopy(data)) return null;
        return Retry(() => Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null);
    }

    /// <summary>Editors such as VS Code and Visual Studio copy the whole line when nothing is selected.</summary>
    private static bool IsEmptySelectionCopy(WinFormsIDataObject data)
    {
        try
        {
            var formats = data.GetFormats(false);
            if (formats.Contains("VisualStudioEditorOperationsLineCutCopyClipboardTag")) return true;
            if (formats.Contains("vscode-editor-data"))
            {
                string? json = data.GetData("vscode-editor-data", false) switch
                {
                    string s => s,
                    MemoryStream ms => Encoding.UTF8.GetString(ms.ToArray()).TrimEnd('\0'),
                    _ => null,
                };
                if (json != null && json.Replace(" ", "").Contains("\"isFromEmptySelection\":true")) return true;
            }
        }
        catch
        {
            // Unknown format contents: assume a real selection.
        }
        return false;
    }

    // ---------------------------------------------------------------- writing

    private static void Replace(string text, ref ClipboardBackup? backup)
    {
        string normalized = text.Replace("\r\n", "\n");
        bool multiline = normalized.Contains('\n');

        if (multiline || normalized.Length > MaxTypedLength)
        {
            backup ??= ClipboardBackup.Capture();
            Retry(() =>
            {
                Clipboard.SetText(text, TextDataFormat.UnicodeText);
                return true;
            });
            InputSender.Create().Chord(VirtualKeys.Control, VirtualKeys.V).Send();
            Thread.Sleep(250); // the target reads the clipboard asynchronously
        }
        else
        {
            InputSender.Create().Text(normalized).Send();
        }

        if (!multiline && normalized.Length <= MaxReselectLength)
        {
            InputSender.Create()
                .Key(VirtualKeys.Shift, false)
                .Press(VirtualKeys.Left, normalized.Length)
                .Key(VirtualKeys.Shift, true)
                .Send();
        }
    }

    private static T? Retry<T>(Func<T?> action)
    {
        for (int i = 0; ; i++)
        {
            try
            {
                return action();
            }
            catch (ExternalException) when (i < 5)
            {
                Thread.Sleep(30);
            }
        }
    }

    /// <summary>Copy of the clipboard contents, restored after we used the clipboard ourselves.</summary>
    private sealed class ClipboardBackup
    {
        private readonly WinFormsDataObject? _data;

        private ClipboardBackup(WinFormsDataObject? data) => _data = data;

        public static ClipboardBackup Capture()
        {
            try
            {
                var source = Retry(Clipboard.GetDataObject);
                if (source == null) return new ClipboardBackup(null);
                var copy = new WinFormsDataObject();
                foreach (var format in source.GetFormats(false))
                {
                    try
                    {
                        var value = source.GetData(format, false);
                        if (value != null) copy.SetData(format, false, value);
                    }
                    catch
                    {
                        // Formats that cannot be read back (delay-rendered, serialized objects) are skipped.
                    }
                }
                return new ClipboardBackup(copy.GetFormats().Length > 0 ? copy : null);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard backup failed", ex);
                return new ClipboardBackup(null);
            }
        }

        public void Restore()
        {
            try
            {
                if (_data == null) Retry(() => { Clipboard.Clear(); return true; });
                else Clipboard.SetDataObject(_data, true, 5, 50);
            }
            catch (Exception ex)
            {
                Log.Error("Clipboard restore failed", ex);
            }
        }
    }
}
