using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Select2Copy.UI;

public partial class HistoryWindow : Window
{
    private bool _closing;

    public event Action<string>? Selected;
    public event EventHandler? UserClosed;

    public HistoryWindow()
    {
        InitializeComponent();

        Deactivated += (_, _) => CloseSmoothly();
        MouseRightButtonDown += (_, _) => CloseSmoothly();
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseSmoothly();
            }
        };
    }

    public void ShowAt(string[] history)
    {
        _closing = false;
        UpdateHistory(history);
        Opacity = 0;
        Show();
        UpdateLayout();

        Rect workArea = SystemParameters.WorkArea;
        double width = ActualWidth > 0 ? ActualWidth : Width;
        double height = ActualHeight > 0 ? ActualHeight : 100;
        Left = Math.Max(workArea.Left + 10, workArea.Right - width - 10);
        Top = Math.Max(workArea.Top + 10, workArea.Bottom - height - 10);

        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
    }

    public void UpdateHistory(string[] history)
    {
        Items.Children.Clear();
        Style? itemStyle = TryFindResource("HistoryItem") as Style;

        foreach (string text in history)
        {
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            string full = text;
            string preview = text.Replace("\r\n", "  ↵  ")
                .Replace("\r", "  ↵  ")
                .Replace("\n", "  ↵  ")
                .Replace("\t", "    ");

            if (preview.Length > 170)
            {
                preview = preview.Substring(0, 167) + "...";
            }

            var border = new Border();
            if (itemStyle != null)
            {
                border.Style = itemStyle;
            }

            border.ToolTip = new ToolTip
            {
                Content = full
            };

            border.Child = new TextBlock
            {
                Text = preview,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 12,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxHeight = 48,
                VerticalAlignment = VerticalAlignment.Center
            };

            ToolTipService.SetInitialShowDelay(border, 300);
            ToolTipService.SetShowDuration(border, 60000);
            border.MouseLeftButtonDown += (_, _) =>
            {
                Selected?.Invoke(full);
                CloseSmoothly();
            };

            Items.Children.Add(border);
        }

        Empty.Visibility = Items.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CloseText_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        CloseSmoothly();
    }

    private void CloseSmoothly()
    {
        if (_closing)
        {
            return;
        }

        _closing = true;
        var animation = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(120));
        animation.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnClosed(EventArgs e)
    {
        UserClosed?.Invoke(this, EventArgs.Empty);
        base.OnClosed(e);
    }
}
