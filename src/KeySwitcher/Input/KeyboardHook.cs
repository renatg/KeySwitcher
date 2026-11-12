using System.Runtime.InteropServices;
using KeySwitcher.Native;
using static KeySwitcher.Native.NativeMethods;

namespace KeySwitcher.Input;

internal interface IKeyboardHandler
{
    /// <summary>Returns true to swallow the key.</summary>
    bool OnKeyDown(int vk);

    /// <summary>Returns true to swallow the key.</summary>
    bool OnKeyUp(int vk);

    void OnMouseDown();

    /// <summary>Input injected by some other program (on-screen keyboard, remote tools...).</summary>
    void OnForeignInjected(int vk);
}

/// <summary>Low-level keyboard and mouse hooks running on a dedicated thread with its own
/// message loop, so a busy UI thread never delays typing.</summary>
internal sealed class KeyboardHook : IDisposable
{
    private readonly IKeyboardHandler _handler;
    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private readonly ManualResetEventSlim _started = new();
    private Thread? _thread;
    private uint _threadId;
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;

    public KeyboardHook(IKeyboardHandler handler)
    {
        _handler = handler;
        _keyboardProc = KeyboardProc;
        _mouseProc = MouseProc;
    }

    public bool IsInstalled => _keyboardHook != IntPtr.Zero;

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "KeySwitcher hook" };
        _thread.Start();
        _started.Wait(TimeSpan.FromSeconds(5));
    }

    private void Run()
    {
        _threadId = GetCurrentThreadId();
        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero)
            Log.Error($"SetWindowsHookEx(keyboard) failed: {Marshal.GetLastWin32Error()}");
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
        _started.Set();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
    }

    private IntPtr KeyboardProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = (int)wParam;
                bool up = msg is WM_KEYUP or WM_SYSKEYUP;
                bool injected = (data.flags & LLKHF_INJECTED) != 0;
                int vk = (int)data.vkCode;

                if (injected && data.dwExtraInfo == InputSender.Marker)
                    return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);

                if (injected && !up)
                {
                    _handler.OnForeignInjected(vk);
                    if (vk == VirtualKeys.Packet)
                        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
                }

                bool swallow = up ? _handler.OnKeyUp(vk) : _handler.OnKeyDown(vk);
                if (swallow) return 1;
            }
            catch (Exception ex)
            {
                Log.Error("Keyboard hook failure", ex);
            }
        }
        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = (int)wParam;
            if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN or WM_XBUTTONDOWN)
            {
                try { _handler.OnMouseDown(); }
                catch (Exception ex) { Log.Error("Mouse hook failure", ex); }
            }
        }
        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    public void Dispose()
    {
        if (_thread == null) return;
        PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }
}
