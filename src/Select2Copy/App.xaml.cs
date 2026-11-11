using System.Windows;
using Select2Copy.Hooks;
using Select2Copy.Interop;
using Select2Copy.Models;
using Select2Copy.Services;
using Select2Copy.UI;

namespace Select2Copy;

public partial class App : Application
{
    private const string MutexName = "Select2Copy_SingleInstance_7C4F2A19";

    private Mutex? _mutex;
    private bool _owner;
    private bool _exiting;
    private SettingsStore _store = null!;
    private HistoryStore _history = null!;
    private HotkeyService _hotkey = null!;
    private MouseHook _mouse = null!;
    private SequentialPasteService _sequential = null!;
    private TrayIcon _tray = null!;
    private ToastWindow? _toast;
    private HistoryWindow? _historyWindow;
    private SettingsWindow? _settingsWindow;

    private bool _lastHistoryEnabled;
    private bool _lastSequentialEnabled;
    private uint _lastSequence;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("UI: " + args.Exception);
            args.Handled = true;
        };

        SessionEnding += (_, _) => Cleanup();

        try
        {
            _mutex = new Mutex(true, MutexName, out _owner);
        }
        catch (Exception ex)
        {
            NoticeWindow.Show(null, "Ошибка проверки запущенной копии: " + ex.Message);
            Shutdown();
            return;
        }

        if (!_owner)
        {
            NoticeWindow.Show(null, "Select2Copy уже запущен.");
            Shutdown();
            return;
        }

        try
        {
            Init();
            HandleStartupArgs(e.Args);
        }
        catch (Exception ex)
        {
            Log.Write("Init: " + ex);
            NoticeWindow.Show(null, "Не удалось запустить Select2Copy:\n\n" + ex.Message);
            ExitApp();
        }
    }

    private void Init()
    {
        _store = new SettingsStore();
        _history = new HistoryStore();
        _hotkey = new HotkeyService();
        _mouse = new MouseHook(_store);
        _sequential = new SequentialPasteService(text => ClipboardService.WriteText(text, _hotkey.Handle));
        _tray = new TrayIcon(_store);
        _toast = new ToastWindow();

        _mouse.TextCopied += OnTextCopied;
        _hotkey.Pressed += OnHotkeyPressed;
        _hotkey.ClipboardChanged += OnClipboardChanged;
        _store.Changed += OnSettingsChanged;
        _history.Changed += OnHistoryChanged;

        _tray.Toggle += () => ToggleEnabled(showToast: false, playSound: false);
        _tray.OpenSettings += ShowSettings;
        _tray.OpenHistory += ShowHistory;
        _tray.Exit += ExitApp;

        _mouse.Start();

        if (!_hotkey.Register(_store.Current.Hotkey, out string? error))
        {
            Log.Write("Hotkey: " + error);
            Dispatcher.BeginInvoke(() =>
            {
                ShowToast("Горячая клавиша занята. Смените её в настройках.", 2600);
            });
        }

        _lastHistoryEnabled = !_store.Current.HistoryEnabled;
        _lastSequentialEnabled = !_store.Current.SequentialPasteEnabled;
        ApplyHistoryState(putFirst: false);

        if (_store.Current.HistoryEnabled && _store.Current.SequentialPasteEnabled)
        {
            SyncSequential(ClipboardService.ReadText());
        }
    }

    private void HandleStartupArgs(string[] args)
    {
        if (args.Any(arg => arg.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
        {
            ShowSettings();
        }
        else if (args.Any(arg => arg.Equals("--history", StringComparison.OrdinalIgnoreCase)))
        {
            ShowHistory();
        }
    }

    private void ToggleEnabled(bool showToast, bool playSound)
    {
        if (_exiting)
        {
            return;
        }

        bool enabled = !_store.Current.Enabled;
        _store.Update(settings => settings.Enabled = enabled);

        if (playSound && _store.Current.PlaySound)
        {
            SoundService.Play();
        }

        if (showToast)
        {
            ShowToast(enabled ? "Select2Copy активен" : "Select2Copy отключен", 1200);
        }
    }

    private void OnTextCopied(CopyResult result)
    {
        if (_exiting)
        {
            return;
        }

        _lastSequence = result.Sequence;
        Settings settings = _store.Current;

        if (settings.PlaySound)
        {
            SoundService.Play();
        }

        if (settings.HistoryEnabled)
        {
            _history.Add(result.Text);
            _sequential.OnAutoCopy(result.Text, _history.Snapshot);
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (_exiting)
            {
                return;
            }

            if (settings.ShowTooltip)
            {
                ShowToast("Скопировано!", 800);
            }

            if (_historyWindow?.IsVisible == true)
            {
                _historyWindow.UpdateHistory(_history.Snapshot);
            }
        });
    }

    private void OnHotkeyPressed()
    {
        ToggleEnabled(showToast: true, playSound: true);
    }

    private void OnClipboardChanged()
    {
        if (_exiting)
        {
            return;
        }

        Settings settings = _store.Current;
        if (!_sequential.IsEnabled || !settings.HistoryEnabled)
        {
            return;
        }

        uint sequence = NativeMethods.GetClipboardSequenceNumber();
        if (sequence == 0 || sequence != _lastSequence)
        {
            SyncSequential(ClipboardService.ReadText());
        }
    }

    private void SyncSequential(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            _sequential.OnNonText();
        }
        else
        {
            _sequential.OnExternal(text!, _history.Snapshot);
        }
    }

    private void OnSettingsChanged(Settings settings)
    {
        if (_exiting)
        {
            return;
        }

        _tray.Refresh(settings);
        if (!_hotkey.IsCurrent(settings.Hotkey))
        {
            _hotkey.Register(settings.Hotkey, out _);
        }

        ApplyHistoryState(putFirst: true);
    }

    private void ApplyHistoryState(bool putFirst)
    {
        Settings settings = _store.Current;
        if (settings.HistoryEnabled == _lastHistoryEnabled &&
            settings.SequentialPasteEnabled == _lastSequentialEnabled)
        {
            if (!settings.HistoryEnabled && _historyWindow != null)
            {
                _historyWindow.Close();
                _historyWindow = null;
            }

            return;
        }

        _lastHistoryEnabled = settings.HistoryEnabled;
        _lastSequentialEnabled = settings.SequentialPasteEnabled;

        if (!settings.HistoryEnabled)
        {
            _sequential.Enable(false, null, false);
            if (_historyWindow != null)
            {
                _historyWindow.Close();
                _historyWindow = null;
            }

            return;
        }

        if (settings.SequentialPasteEnabled)
        {
            if (!_sequential.Enable(true, _history.Snapshot, putFirst))
            {
                _store.Update(s => s.SequentialPasteEnabled = false);
            }
        }
        else
        {
            _sequential.Enable(false, null, false);
        }
    }

    private void ShowSettings()
    {
        if (_exiting)
        {
            return;
        }

        if (_settingsWindow == null)
        {
            _settingsWindow = new SettingsWindow(_store, _history, _hotkey);
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.ShowAndActivate();
    }

    private void ShowHistory()
    {
        if (_exiting || !_store.Current.HistoryEnabled)
        {
            return;
        }

        if (_historyWindow == null)
        {
            _historyWindow = new HistoryWindow();
            _historyWindow.Selected += OnHistorySelected;
            _historyWindow.UserClosed += (_, _) => _historyWindow = null;
        }

        _historyWindow.ShowAt(_history.Snapshot);
    }

    private void OnHistorySelected(string text)
    {
        if (_exiting || string.IsNullOrEmpty(text))
        {
            return;
        }

        if (!ClipboardService.WriteText(text, _hotkey.Handle))
        {
            return;
        }

        _lastSequence = NativeMethods.GetClipboardSequenceNumber();
        _sequential.OnHistorySelected(text, _history.Snapshot);

        Settings settings = _store.Current;
        if (settings.PlaySound)
        {
            SoundService.Play();
        }

        if (settings.ShowTooltip)
        {
            ShowToast("Скопировано!", 800);
        }
    }

    private void OnHistoryChanged(string[] history)
    {
        if (_exiting)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (!_exiting && _historyWindow?.IsVisible == true)
            {
                _historyWindow.UpdateHistory(history);
            }
        });
    }

    private void ShowToast(string text, int milliseconds)
    {
        if (_exiting)
        {
            return;
        }

        _toast ??= new ToastWindow();
        _toast.ShowText(text, milliseconds);
    }

    private void ExitApp()
    {
        Cleanup();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cleanup();
        base.OnExit(e);
    }

    private void Cleanup()
    {
        if (_exiting)
        {
            return;
        }

        _exiting = true;

        try
        {
            if (_store != null)
            {
                _store.Changed -= OnSettingsChanged;
            }

            if (_history != null)
            {
                _history.Changed -= OnHistoryChanged;
            }

            _mouse?.Dispose();
            _sequential?.Dispose();
            _hotkey?.Dispose();
            _tray?.Dispose();
            _toast?.Close();
            _historyWindow?.Close();
            _settingsWindow?.Close();
            _history?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Write("Cleanup: " + ex.Message);
        }

        try
        {
            if (_owner && _mutex != null)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
        }

        _mutex?.Dispose();
        _mutex = null;
    }
}
