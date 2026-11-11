using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Select2Copy.Interop;
using Select2Copy.Models;

namespace Select2Copy.Services;

internal sealed class HotkeyService : IDisposable
{
    private const int HotkeyId = 0x5343;
    private const int ErrorHotkeyAlreadyRegistered = 1409;

    private readonly HwndSource _source;
    private readonly DispatcherTimer _releaseTimer;
    private HotkeyConfig? _current;
    private bool _registered;
    private bool _latched;
    private bool _disposed;

    public event Action? Pressed;
    public event Action? ClipboardChanged;

    public IntPtr Handle => _source.Handle;

    public HotkeyService()
    {
        var parameters = new HwndSourceParameters("Select2Copy.HiddenWindow")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
            ExtendedWindowStyle = NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE,
            Width = 0,
            Height = 0
        };
        _source = new HwndSource(parameters);

        _source.AddHook(WndProc);
        if (!NativeMethods.AddClipboardFormatListener(_source.Handle))
        {
            Log.Write("Clipboard listener failed");
        }

        _releaseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(25)
        };
        _releaseTimer.Tick += (_, _) =>
        {
            if (_current == null || !NativeMethods.KeyDown((int)_current.VirtualKey))
            {
                _latched = false;
                _releaseTimer.Stop();
            }
        };
    }

    public bool IsCurrent(HotkeyConfig hotkey)
    {
        return _current != null && _registered && _current.Display == hotkey.Display;
    }

    public bool Register(HotkeyConfig hotkey, out string? error)
    {
        error = null;
        if (hotkey == null || !hotkey.IsValid)
        {
            error = "Некорректная комбинация клавиш.";
            return false;
        }

        if (_current != null && _registered && _current.Display == hotkey.Display)
        {
            return true;
        }

        HotkeyConfig? previous = _current;
        Unregister();

        if (NativeMethods.RegisterHotKey(
                _source.Handle,
                HotkeyId,
                hotkey.Modifiers | NativeMethods.MOD_NOREPEAT,
                hotkey.VirtualKey))
        {
            _current = hotkey;
            _registered = true;
            return true;
        }

        int code = Marshal.GetLastWin32Error();
        if (previous != null &&
            NativeMethods.RegisterHotKey(
                _source.Handle,
                HotkeyId,
                previous.Modifiers | NativeMethods.MOD_NOREPEAT,
                previous.VirtualKey))
        {
            _current = previous;
            _registered = true;
        }
        else
        {
            _current = null;
            _registered = false;
        }

        error = code == ErrorHotkeyAlreadyRegistered
            ? "Эта комбинация уже используется другой программой."
            : "Не удалось зарегистрировать комбинацию клавиш.";
        return false;
    }

    private void Unregister()
    {
        _latched = false;
        _releaseTimer.Stop();
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _current = null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_CLIPBOARDUPDATE)
        {
            ClipboardChanged?.Invoke();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            if (!_latched)
            {
                _latched = true;
                _releaseTimer.Start();
                Pressed?.Invoke();
            }

            handled = true;
            return IntPtr.Zero;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Unregister();
        _releaseTimer.Stop();
        NativeMethods.RemoveClipboardFormatListener(_source.Handle);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
