using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Select2Copy.Interop;
using Select2Copy.Services;

namespace Select2Copy.Hooks;

internal sealed class CopyResult
{
    public string Text { get; }
    public uint Sequence { get; }

    public CopyResult(string text, uint sequence)
    {
        Text = text;
        Sequence = sequence;
    }
}

internal sealed class MouseHook : IDisposable
{
    private readonly SettingsStore _store;
    private readonly BlockingCollection<CopyRequest> _queue;
    private readonly CancellationTokenSource _cts = new();
    private readonly NativeMethods.HookProc _proc;
    private readonly ManualResetEventSlim _ready = new(false);
    private readonly uint _selfPid = (uint)Process.GetCurrentProcess().Id;

    private Thread? _thread;
    private uint _threadId;
    private IntPtr _hook;
    private volatile bool _stop;
    private volatile bool _down;
    private bool _dragged;
    private NativeMethods.POINT _downPoint;
    private NativeMethods.POINT _lastClickPoint;
    private uint _lastClickTime;
    private int _clicks;
    private bool _disposed;

    public event Action<CopyResult>? TextCopied;

    public MouseHook(SettingsStore store)
    {
        _store = store;
        _proc = Callback;
        _queue = new BlockingCollection<CopyRequest>(new ConcurrentQueue<CopyRequest>(), 4);

        _ = Task.Run(WorkerLoop);
    }

    public void Start()
    {
        if (_thread != null)
        {
            return;
        }

        _thread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "Select2Copy.MouseHook"
        };
        _thread.Start();
        _ready.Wait(2000);
        if (_hook == IntPtr.Zero)
        {
            Log.Write("Mouse hook failed to install");
        }
    }

    private void HookThreadProc()
    {
        _threadId = NativeMethods.GetCurrentThreadId();
        _hook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _proc,
            NativeMethods.GetModuleHandle(null),
            0);

        _ready.Set();
        if (_hook == IntPtr.Zero)
        {
            return;
        }

        while (!_stop && NativeMethods.GetMessage(out NativeMethods.MSG message, IntPtr.Zero, 0, 0) > 0)
        {
            NativeMethods.TranslateMessage(ref message);
            NativeMethods.DispatchMessage(ref message);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }
    }

    private unsafe IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            NativeMethods.MSLLHOOKSTRUCT data = *(NativeMethods.MSLLHOOKSTRUCT*)lParam;
            if ((data.flags & NativeMethods.LLMHF_LOWER_IL_INJECTED) == 0)
            {
                switch ((int)wParam)
                {
                    case NativeMethods.WM_MOUSEMOVE:
                        if (_down && !_dragged)
                        {
                            int dragX = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDRAG));
                            int dragY = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDRAG));
                            if (Math.Abs(data.pt.X - _downPoint.X) >= dragX ||
                                Math.Abs(data.pt.Y - _downPoint.Y) >= dragY)
                            {
                                _dragged = true;
                            }
                        }

                        break;

                    case NativeMethods.WM_LBUTTONDOWN:
                        OnDown(ref data);
                        break;

                    case NativeMethods.WM_LBUTTONUP:
                        OnUp(ref data);
                        break;

                    case NativeMethods.WM_RBUTTONDOWN:
                    case NativeMethods.WM_MBUTTONDOWN:
                        Reset();
                        break;
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    private void OnDown(ref NativeMethods.MSLLHOOKSTRUCT data)
    {
        if (!_store.Current.Enabled)
        {
            Reset();
            return;
        }

        int doubleClickTime = NativeMethods.GetDoubleClickTime();
        if (doubleClickTime < 100 || doubleClickTime > 1000)
        {
            doubleClickTime = 250;
        }

        int doubleClickX = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CXDOUBLECLK));
        int doubleClickY = Math.Max(4, NativeMethods.GetSystemMetrics(NativeMethods.SM_CYDOUBLECLK));

        bool within = data.time - _lastClickTime <= (uint)doubleClickTime &&
                      Math.Abs(data.pt.X - _lastClickPoint.X) <= doubleClickX &&
                      Math.Abs(data.pt.Y - _lastClickPoint.Y) <= doubleClickY;

        _clicks = within ? _clicks + 1 : 1;
        _lastClickPoint = data.pt;
        _lastClickTime = data.time;
        _downPoint = data.pt;
        _dragged = false;
        _down = true;
    }

    private void OnUp(ref NativeMethods.MSLLHOOKSTRUCT data)
    {
        if (!_down)
        {
            return;
        }

        _down = false;
        var settings = _store.Current;
        if (!settings.Enabled)
        {
            Reset();
            return;
        }

        if (NativeMethods.KeyDown(NativeMethods.VK_MENU) ||
            NativeMethods.KeyDown(NativeMethods.VK_LWIN) ||
            NativeMethods.KeyDown(NativeMethods.VK_RWIN))
        {
            return;
        }

        bool shouldCopy = _dragged ||
                          (settings.CopyOnDoubleClick && _clicks >= 2) ||
                          NativeMethods.KeyDown(NativeMethods.VK_SHIFT);

        if (!shouldCopy)
        {
            return;
        }

        var request = new CopyRequest(data.pt);
        if (!_queue.TryAdd(request))
        {
            _queue.TryTake(out _);
            _queue.TryAdd(request);
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (CopyRequest request in _queue.GetConsumingEnumerable(_cts.Token))
            {
                try
                {
                    ProcessRequest(request);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    Log.Write("Mouse worker: " + ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ProcessRequest(CopyRequest request)
    {
        var settings = _store.Current;
        if (!settings.Enabled)
        {
            return;
        }

        IntPtr hwnd = NativeMethods.WindowFromPoint(request.Point);
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == _selfPid || ProcessService.IsExcluded(pid, settings.ExcludedApps))
        {
            return;
        }

        IntPtr hitResult;
        if (NativeMethods.SendMessageTimeout(
                hwnd,
                NativeMethods.WM_NCHITTEST,
                IntPtr.Zero,
                NativeMethods.LParam(request.Point.X, request.Point.Y),
                NativeMethods.SMTO_ABORTIFHUNG,
                40,
                out hitResult) != IntPtr.Zero)
        {
            int hit = (int)(long)hitResult;
            if (hit != NativeMethods.HTCLIENT && hit != NativeMethods.HTTRANSPARENT)
            {
                return;
            }
        }

        Thread.Sleep(60);
        if (_cts.IsCancellationRequested)
        {
            return;
        }

        settings = _store.Current;
        if (!settings.Enabled || _down || ProcessService.IsExcluded(pid, settings.ExcludedApps))
        {
            return;
        }

        uint sequence = NativeMethods.GetClipboardSequenceNumber();
        InputSender.SendCtrlC();

        bool changed = false;
        for (int attempt = 0; attempt < 25; attempt++)
        {
            Thread.Sleep(10);
            if (_cts.IsCancellationRequested)
            {
                return;
            }

            if (NativeMethods.GetClipboardSequenceNumber() != sequence)
            {
                changed = true;
                break;
            }
        }

        if (!changed)
        {
            return;
        }

        string? text = ClipboardService.ReadText();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        TextCopied?.Invoke(new CopyResult(text!, NativeMethods.GetClipboardSequenceNumber()));
    }

    private void Reset()
    {
        _down = false;
        _dragged = false;
        _clicks = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stop = true;
        _queue.CompleteAdding();
        _cts.Cancel();

        if (_threadId != 0)
        {
            NativeMethods.PostThreadMessage(_threadId, NativeMethods.WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        }

        if (_thread != null && _thread.IsAlive)
        {
            _thread.Join(1000);
        }

        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        _ready.Dispose();
        _cts.Dispose();
    }

    private readonly struct CopyRequest
    {
        public NativeMethods.POINT Point { get; }

        public CopyRequest(NativeMethods.POINT point)
        {
            Point = point;
        }
    }
}
