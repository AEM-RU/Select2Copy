using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Select2Copy.Interop;
using Select2Copy.Models;
using Select2Copy.Services;

namespace Select2Copy.UI;

public partial class SettingsWindow : Window
{
    private readonly SettingsStore _store;
    private readonly HistoryStore _history;
    private readonly HotkeyService _hotkey;
    private bool _loading;
    private bool _saving;
    private bool _recording;

    internal SettingsWindow(SettingsStore store, HistoryStore history, HotkeyService hotkey)
    {
        _store = store;
        _history = history;
        _hotkey = hotkey;

        InitializeComponent();
        _store.Changed += OnStoreChanged;
        SourceInitialized += (_, _) =>
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            NativeMethods.RoundCorners(hwnd);
        };

        Load(_store.Current);
    }

    public void ShowAndActivate()
    {
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        UpdateLayout();
        Rect workArea = SystemParameters.WorkArea;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : 680;
        Left = Math.Max(workArea.Left + 10, workArea.Right - width - 10);
        Top = Math.Max(workArea.Top + 10, workArea.Bottom - height - 10);
        Activate();
        Focus();
    }

    private void Load(Settings settings)
    {
        _loading = true;
        try
        {
            AutostartCheck.IsChecked = settings.StartWithWindows;
            SoundCheck.IsChecked = settings.PlaySound;
            TipCheck.IsChecked = settings.ShowTooltip;
            DoubleClickCheck.IsChecked = settings.CopyOnDoubleClick;
            HistoryCheck.IsChecked = settings.HistoryEnabled;
            SequentialCheck.IsChecked = settings.HistoryEnabled && settings.SequentialPasteEnabled;
            SequentialCheck.IsEnabled = settings.HistoryEnabled;
            HotkeyButton.Content = settings.Hotkey.Display;
            ExcludedList.Items.Clear();
            foreach (string name in settings.ExcludedApps)
            {
                ExcludedList.Items.Add(name + ".exe");
            }

            if (!_recording)
            {
                HintText.Text = "";
            }
        }
        finally
        {
            _loading = false;
        }
    }

    private void Save()
    {
        if (_loading || _saving || _recording)
        {
            return;
        }

        try
        {
            Settings settings = _store.Current.Clone();
            settings.StartWithWindows = AutostartCheck.IsChecked == true;
            settings.PlaySound = SoundCheck.IsChecked == true;
            settings.ShowTooltip = TipCheck.IsChecked == true;
            settings.CopyOnDoubleClick = DoubleClickCheck.IsChecked == true;
            settings.HistoryEnabled = HistoryCheck.IsChecked == true;
            settings.SequentialPasteEnabled = settings.HistoryEnabled && SequentialCheck.IsChecked == true;
            settings.ExcludedApps = ExcludedList.Items
                .Cast<string>()
                .Select(ProcessService.Normalize)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!settings.HistoryEnabled && _store.Current.HistoryEnabled)
            {
                _history.Clear();
            }

            _saving = true;
            _store.Save(settings);
            _saving = false;
            Load(settings);
            HintText.Text = "";
        }
        catch (Exception ex)
        {
            Log.Write("Settings save: " + ex.Message);
            HintText.Text = "Не удалось сохранить настройки.";
            _saving = false;
        }
    }

    private void Setting_Click(object sender, RoutedEventArgs e)
    {
        Save();
    }

    private void History_Click(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        if (HistoryCheck.IsChecked != true)
        {
            SequentialCheck.IsChecked = false;
        }

        SequentialCheck.IsEnabled = HistoryCheck.IsChecked == true;
        Save();
    }

    private void HotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_recording)
        {
            return;
        }

        _recording = true;
        HotkeyButton.Content = "Нажмите комбинацию...";
        HintText.Text = "Esc — отмена";
        Focus();
        Keyboard.Focus(this);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Close();
            }

            return;
        }

        e.Handled = true;
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            CancelRecording();
            return;
        }

        if (HotkeyConfig.IsModifier(key))
        {
            return;
        }

        ModifierKeys modifiers = Keyboard.Modifiers;
        var config = new HotkeyConfig
        {
            Ctrl = (modifiers & ModifierKeys.Control) != 0,
            Shift = (modifiers & ModifierKeys.Shift) != 0,
            Alt = (modifiers & ModifierKeys.Alt) != 0,
            Win = (modifiers & ModifierKeys.Windows) != 0,
            KeyName = key.ToString()
        };

        if (!config.IsValid)
        {
            HintText.Text = "Комбинация должна содержать Ctrl, Shift, Alt или Win.";
            return;
        }

        if (!_hotkey.Register(config, out string? error))
        {
            HintText.Text = error ?? "Не удалось зарегистрировать комбинацию.";
            return;
        }

        _recording = false;
        HotkeyButton.Content = config.Display;
        HintText.Text = "";

        Settings settings = _store.Current.Clone();
        settings.Hotkey = config;
        _saving = true;
        _store.Save(settings);
        _saving = false;
    }

    private void CancelRecording()
    {
        _recording = false;
        HotkeyButton.Content = _store.Current.Hotkey.Display;
        HintText.Text = "";
    }

    private void AddExclusion_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new AddAppDialog
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.Result.Length == 0)
        {
            return;
        }

        string name = ProcessService.Normalize(dialog.Result);
        foreach (object item in ExcludedList.Items)
        {
            if (item is string text &&
                string.Equals(ProcessService.Normalize(text), name, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
        }

        ExcludedList.Items.Add(name + ".exe");
        Save();
    }

    private void RemoveExclusion_Click(object sender, RoutedEventArgs e)
    {
        if (ExcludedList.SelectedItem == null)
        {
            return;
        }

        ExcludedList.Items.Remove(ExcludedList.SelectedItem);
        Save();
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        _history.Clear();
        HintText.Text = "История очищена.";
    }

    private void OnStoreChanged(Settings settings)
    {
        if (_saving || _recording)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_saving && !_recording)
            {
                Load(settings);
            }
        }));
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        Close();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (_recording)
        {
            CancelRecording();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _store.Changed -= OnStoreChanged;
        base.OnClosed(e);
    }
}
