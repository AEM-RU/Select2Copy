using System.Windows;
using System.Windows.Input;

namespace Select2Copy.UI;

public partial class NoticeWindow : Window
{
    public NoticeWindow()
    {
        InitializeComponent();
    }

    public static void Show(Window? owner, string text)
    {
        var window = new NoticeWindow();
        window.MessageText.Text = text;

        if (owner != null && owner.IsVisible)
        {
            window.Owner = owner;
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        window.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
