using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Select2Copy.Interop;

namespace Select2Copy.UI;

public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hideTimer;
    private readonly DispatcherTimer _moveTimer;
    private int _generation;
    private bool _tracking;

    public ToastWindow()
    {
        InitializeComponent();

        _hideTimer = new DispatcherTimer();
        _hideTimer.Tick += (_, _) => HideAnimated();

        _moveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _moveTimer.Tick += (_, _) => PositionNearCursor();

        SourceInitialized += (_, _) => MakeClickThrough();
        Loaded += (_, _) => PositionNearCursor();
    }

    public void ShowText(string text, int milliseconds)
    {
        _generation++;
        MessageText.Text = text;
        Opacity = 0;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        PositionNearCursor();

        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));

        _hideTimer.Stop();
        _hideTimer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        _hideTimer.Start();

        if (!_tracking)
        {
            _tracking = true;
            _moveTimer.Start();
        }
    }

    private void HideAnimated()
    {
        _hideTimer.Stop();
        int generation = ++_generation;
        var animation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(160));
        animation.Completed += (_, _) =>
        {
            if (generation == _generation)
            {
                try
                {
                    Hide();
                }
                catch
                {
                }

                _moveTimer.Stop();
                _tracking = false;
            }
        };

        BeginAnimation(OpacityProperty, animation);
    }

    private void MakeClickThrough()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        long style = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
        style |= NativeMethods.WS_EX_TRANSPARENT |
                 NativeMethods.WS_EX_NOACTIVATE |
                 NativeMethods.WS_EX_TOOLWINDOW;
        NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(style));
        NativeMethods.RoundCorners(hwnd);
    }

    private void PositionNearCursor()
    {
        if (!IsVisible || !NativeMethods.GetCursorPos(out NativeMethods.POINT point))
        {
            return;
        }

        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double cursorX = point.X / dpi.DpiScaleX;
        double cursorY = point.Y / dpi.DpiScaleY;
        double width = ActualWidth > 0 ? ActualWidth : 100;
        double height = ActualHeight > 0 ? ActualHeight : 30;

        double workLeft = SystemParameters.WorkArea.Left;
        double workTop = SystemParameters.WorkArea.Top;
        double workRight = SystemParameters.WorkArea.Right;
        double workBottom = SystemParameters.WorkArea.Bottom;

        IntPtr monitor = NativeMethods.MonitorFromPoint(point, 2);
        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };

            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                workLeft = info.rcWork.Left / dpi.DpiScaleX;
                workTop = info.rcWork.Top / dpi.DpiScaleY;
                workRight = info.rcWork.Right / dpi.DpiScaleX;
                workBottom = info.rcWork.Bottom / dpi.DpiScaleY;
            }
        }

        double left = cursorX + 12;
        double top = cursorY + 18;

        if (left + width > workRight)
        {
            left = cursorX - width - 12;
        }

        if (top + height > workBottom)
        {
            top = cursorY - height - 18;
        }

        if (left < workLeft + 4)
        {
            left = workLeft + 4;
        }

        if (top < workTop + 4)
        {
            top = workTop + 4;
        }

        Left = left;
        Top = top;
    }
}
