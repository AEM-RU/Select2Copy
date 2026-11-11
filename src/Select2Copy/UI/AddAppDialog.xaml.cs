using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Select2Copy.Services;

namespace Select2Copy.UI;

public partial class AddAppDialog : Window
{
    public string Result { get; private set; } = "";

    public AddAppDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus();
        LoadRunningApps();
    }

    private void LoadRunningApps()
    {
        var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            try
            {
                if (process.MainWindowHandle != IntPtr.Zero && process.ProcessName.Length > 0)
                {
                    string name = ProcessService.Normalize(process.ProcessName);
                    if (name.Length > 0 && !ProcessService.IsShell(name))
                    {
                        names.Add(name + ".exe");
                    }
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
            }
        }

        AppsList.ItemsSource = names.ToList();
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Исполняемые файлы (*.exe)|*.exe|Все файлы (*.*)|*.*",
            Title = "Выберите приложение"
        };

        if (dialog.ShowDialog(this) == true)
        {
            NameBox.Text = ProcessService.Normalize(dialog.FileName);
        }
    }

    private void AppsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AppsList.SelectedItem is string value)
        {
            NameBox.Text = ProcessService.Normalize(value);
        }
    }

    private void AppsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (AppsList.SelectedItem != null)
        {
            Confirm();
        }
    }

    private void NameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            e.Handled = true;
            Confirm();
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        Confirm();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void CloseText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        DialogResult = false;
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Confirm()
    {
        string name = ProcessService.Normalize(NameBox.Text);
        if (name.Length == 0)
        {
            return;
        }

        Result = name;
        DialogResult = true;
    }
}
