using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Select2Copy.Interop;
using Select2Copy.Models;
using Select2Copy.Services;

namespace Select2Copy.UI;

internal sealed class TrayIcon : IDisposable
{
    private sealed class DarkColorTable : ProfessionalColorTable
    {
        private static readonly Color Bg = Color.FromArgb(20, 22, 25);
        private static readonly Color Hover = Color.FromArgb(42, 48, 60);
        private static readonly Color Accent = Color.FromArgb(79, 209, 197);
        private static readonly Color Separator = Color.FromArgb(50, 55, 65);

        public override Color ToolStripDropDownBackground => Bg;
        public override Color MenuItemSelected => Hover;
        public override Color MenuItemBorder => Accent;
        public override Color MenuBorder => Accent;
        public override Color ImageMarginGradientBegin => Bg;
        public override Color ImageMarginGradientMiddle => Bg;
        public override Color ImageMarginGradientEnd => Bg;
        public override Color SeparatorDark => Separator;
        public override Color SeparatorLight => Separator;
    }

    private readonly SettingsStore _store;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _historyItem;
    private readonly ToolStripMenuItem _excludeItem;
    private readonly System.Windows.Forms.Timer _clickTimer;
    private readonly int _doubleClickTime;

    private Icon _iconOn;
    private Icon _iconOff;
    private bool _disposed;
    private string _lastForegroundName = "";
    private DateTime _lastDoubleClick = DateTime.MinValue;

    public event Action? Toggle;
    public event Action? OpenSettings;
    public event Action? OpenHistory;
    public event Action? Exit;

    public TrayIcon(SettingsStore store)
    {
        _store = store;
        _doubleClickTime = Math.Clamp(SystemInformation.DoubleClickTime, 120, 800);
        _iconOn = CreateIcon(true);
        _iconOff = CreateIcon(false);

        _clickTimer = new System.Windows.Forms.Timer
        {
            Interval = _doubleClickTime
        };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            Toggle?.Invoke();
        };

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            ShowCheckMargin = true,
            BackColor = Color.FromArgb(20, 22, 25),
            ForeColor = Color.White,
            Renderer = new ToolStripProfessionalRenderer(new DarkColorTable())
        };

        _enabledItem = AddItem("Включено", () => Toggle?.Invoke());
        _historyItem = AddItem("История", () => OpenHistory?.Invoke());
        _excludeItem = AddItem("Исключить текущее приложение", ToggleExclusion);
        _menu.Items.Add(new ToolStripSeparator());
        AddItem("Настройки", () => OpenSettings?.Invoke());
        AddItem("Выход", () => Exit?.Invoke());

        _menu.Opening += (_, _) => UpdateExcludeMenu();

        _notifyIcon = new NotifyIcon
        {
            Text = "Активен",
            Icon = _iconOn,
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                string name = ProcessService.ForegroundName();
                if (name.Length > 0 && !ProcessService.IsShell(name))
                {
                    _lastForegroundName = name;
                }
            }
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Middle)
            {
                _clickTimer.Stop();
                OpenSettings?.Invoke();
            }
            else if (e.Button == MouseButtons.Left)
            {
                if ((DateTime.Now - _lastDoubleClick).TotalMilliseconds >= _doubleClickTime + 50)
                {
                    _clickTimer.Stop();
                    _clickTimer.Start();
                }
            }
        };

        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                _clickTimer.Stop();
                _lastDoubleClick = DateTime.Now;
                OpenHistory?.Invoke();
            }
        };

        _store.Changed += OnStoreChanged;
        Refresh(_store.Current);
    }

    private ToolStripMenuItem AddItem(string text, Action action)
    {
        var item = new ToolStripMenuItem(text)
        {
            ForeColor = Color.White
        };
        item.Click += (_, _) => action();
        _menu.Items.Add(item);
        return item;
    }

    public void Refresh(Settings settings)
    {
        _enabledItem.Checked = settings.Enabled;
        _historyItem.Enabled = settings.HistoryEnabled;
        _notifyIcon.Text = settings.Enabled ? "Активен" : "Отключен";
        _notifyIcon.Icon = settings.Enabled ? _iconOn : _iconOff;
    }

    private void OnStoreChanged(Settings settings)
    {
        Refresh(settings);
    }

    private void UpdateExcludeMenu()
    {
        string name = _lastForegroundName;
        if (name.Length == 0)
        {
            name = ProcessService.ForegroundName();
        }

        if (name.Length == 0 || ProcessService.IsShell(name))
        {
            _excludeItem.Visible = false;
            return;
        }

        bool excluded = _store.Current.ExcludedApps
            .Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));

        string display = name.Length > 28 ? name[..27] + "..." : name;
        _excludeItem.Text = (excluded ? "Включить: " : "Исключить: ") + display;
        _excludeItem.Checked = excluded;
        _excludeItem.Visible = true;
    }

    private void ToggleExclusion()
    {
        string name = _lastForegroundName;
        if (name.Length == 0 || ProcessService.IsShell(name))
        {
            return;
        }

        _store.Update(settings =>
        {
            if (settings.ExcludedApps.Any(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase)))
            {
                settings.ExcludedApps.RemoveAll(item => string.Equals(item, name, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                settings.ExcludedApps.Add(name);
            }
        });

        Refresh(_store.Current);
    }

    private static Icon CreateIcon(bool enabled)
    {
        using var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.Clear(Color.Transparent);

            Color accent = enabled
                ? Color.FromArgb(79, 209, 197)
                : Color.FromArgb(128, 128, 128);

            using var background = new SolidBrush(Color.FromArgb(20, 22, 25));
            using var border = new Pen(accent, 2f)
            {
                Alignment = PenAlignment.Inset
            };

            graphics.FillEllipse(background, 0, 0, 31, 31);
            graphics.DrawEllipse(border, 1, 1, 29, 29);

            using var font = new Font("Segoe UI", 13f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var textBrush = new SolidBrush(Color.White);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };

            graphics.DrawString("S2", font, textBrush, new RectangleF(0, 0, 32, 32), format);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using Icon temporary = Icon.FromHandle(handle);
            return (Icon)temporary.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.Changed -= OnStoreChanged;
        _clickTimer.Stop();
        _clickTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _iconOn.Dispose();
        _iconOff.Dispose();
    }
}
