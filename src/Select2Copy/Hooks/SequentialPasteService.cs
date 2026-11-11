using System.Runtime.InteropServices;
using Select2Copy.Interop;
using Select2Copy.Services;

namespace Select2Copy.Hooks;

internal sealed class SequentialPasteService : IDisposable
{
    private static readonly string[] None = Array.Empty<string>();

    private readonly Func<string, bool> _write;
    private readonly NativeMethods.HookProc _proc;
    private readonly uint _selfPid = (uint)Environment.ProcessId;
    private readonly object _gate = new();
    private readonly System.Threading.Timer _timer;
    private readonly ManualResetEventSlim _ready = new(false);

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _stop;
    private string[] _queue = None;
    private int _index = -1;
    private bool _enabled;
    private bool _armed;
    private bool _pending;
    private bool _vDown;
    private bool _disposed;

    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
    }

    public SequentialPasteService(Func<string, bool> write)
    {
        _write = write;
        _proc = Callback;
        _timer = new System.Threading.Timer(_ => OnTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool Enable(bool on, string[]? history, bool putFirstInClipboard)
    {
        if (!on)
        {
            Disable();
            return true;
        }

        bool startHook;
        lock (_gate)
        {
            startHook = !_enabled;
        }

        if (startHook && !StartHook())
        {
            return false;
        }

        string[] queue = history ?? None;
        if (putFirstInClipboard && queue.Length > 0 && !_write(queue[0]))
        {
            Disable();
            return false;
        }

        lock (_gate)
        {
            _enabled = true;
            _pending = false;
            _vDown = false;
            _queue = queue;
            _index = queue.Length > 0 ? 0 : -1;
            _armed = queue.Length > 0;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }

        return true;
    }

    public void OnAutoCopy(string text, string[] history)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            _pending = false;
            _vDown = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _queue = history ?? None;
            int index = Array.IndexOf(_queue, text);
            _index = index >= 0 ? index : (_queue.Length > 0 ? 0 : -1);
            _armed = _queue.Length > 0;
        }
    }

    public void OnExternal(string text, string[] history)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            _pending = false;
            _vDown = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _queue = history ?? None;
            _index = Array.IndexOf(_queue, text);
            _armed = _queue.Length > 0;
        }
    }

    public void OnNonText()
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            _pending = false;
            _vDown = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _index = -2;
            _armed = false;
        }
    }

    public void OnHistorySelected(string text, string[] history)
    {
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            _pending = false;
            _vDown = false;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            _queue = history ?? None;
            int index = Array.IndexOf(_queue, text);
            _index = index;
            _armed = index >= 0;
        }
    }

    private bool StartHook()
    {
        _stop = false;
        _ready.Reset();
        var thread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "Select2Copy.KeyboardHook"
        };

        lock (_gate)
        {
            _thread = thread;
        }

        thread.Start();
        _ready.Wait(1500);

        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
            {
                return true;
            }

            _thread = null;
        }

        thread.Join(500);
        Log.Write("Keyboard hook failed to install: " + Marshal.GetLastWin32Error());
        return false;
    }

    private void HookThreadProc()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        IntPtr hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_KEYBOARD_LL,
            _proc,
            NativeMethods.GetModuleHandle(null),
            0);

        lock (_gate)
        {
            _hook = hook;
        }

        _ready.Set();
        if (hook == IntPtr.Zero)
        {
            return;
        }

        while (!_stop && NativeMethods.GetMessage(out NativeMethods.MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        NativeMethods.UnhookWindowsHookEx(hook);
        lock (_gate)
        {
            if (_hook == hook)
            {
                _hook = IntPtr.Zero;
            }
        }
    }

    private unsafe IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            NativeMethods.KBDLLHOOKSTRUCT data = *(NativeMethods.KBDLLHOOKSTRUCT*)lParam;
            if (data.vkCode == NativeMethods.VK_V)
            {
                int message = (int)wParam;
                if (message is NativeMethods.WM_KEYUP or NativeMethods.WM_SYSKEYUP)
                {
                    lock (_gate)
                    {
                        _vDown = false;
                    }
                }
                else if (message is NativeMethods.WM_KEYDOWN or NativeMethods.WM_SYSKEYDOWN)
                {
                    bool ctrl = NativeMethods.KeyDown(NativeMethods.VK_CONTROL) ||
                                NativeMethods.KeyDown(NativeMethods.VK_LCONTROL) ||
                                NativeMethods.KeyDown(NativeMethods.VK_RCONTROL);
                    bool shift = NativeMethods.KeyDown(NativeMethods.VK_SHIFT) ||
                                 NativeMethods.KeyDown(NativeMethods.VK_LSHIFT) ||
                                 NativeMethods.KeyDown(NativeMethods.VK_RSHIFT);
                    bool alt = NativeMethods.KeyDown(NativeMethods.VK_MENU) ||
                               NativeMethods.KeyDown(NativeMethods.VK_LMENU) ||
                               NativeMethods.KeyDown(NativeMethods.VK_RMENU);
                    bool win = NativeMethods.KeyDown(NativeMethods.VK_LWIN) ||
                               NativeMethods.KeyDown(NativeMethods.VK_RWIN);

                    if (ctrl && !shift && !alt && !win &&
                        !SelfForeground())
                    {
                        bool suppress = false;
                        lock (_gate)
                        {
                            if (_enabled && _armed)
                            {
                                if (_vDown || _pending)
                                {
                                    suppress = true;
                                }
                                else
                                {
                                    _vDown = true;
                                    _pending = true;
                                    _timer.Change(150, Timeout.Infinite);
                                }
                            }
                        }

                        if (suppress)
                        {
                            return new IntPtr(1);
                        }
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private bool SelfForeground()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return pid == _selfPid;
    }

    private void OnTimer()
    {
        int nextIndex;
        string? next;
        lock (_gate)
        {
            _pending = false;
            if (!_enabled || !_armed)
            {
                return;
            }

            nextIndex = _index + 1;
            if (nextIndex < 0 || nextIndex >= _queue.Length)
            {
                _armed = false;
                return;
            }

            next = _queue[nextIndex];
        }

        bool written = next != null && _write(next);
        lock (_gate)
        {
            if (!_enabled)
            {
                return;
            }

            if (written)
            {
                _index = nextIndex;
            }
            else
            {
                _armed = false;
                Log.Write("Sequential paste: clipboard advance failed");
            }
        }
    }

    private void Disable()
    {
        Thread? thread;
        uint threadId;
        lock (_gate)
        {
            _enabled = false;
            _armed = false;
            _pending = false;
            _vDown = false;
            _index = -1;
            _queue = None;
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            thread = _thread;
            threadId = _threadId;
            _thread = null;
            _threadId = 0;
        }

        _stop = true;
        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (thread != null && thread.IsAlive)
        {
            thread.Join(1000);
        }

        lock (_gate)
        {
            if (_hook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disable();
        _timer.Dispose();
        _ready.Dispose();
    }
}
