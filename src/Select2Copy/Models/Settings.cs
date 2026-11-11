namespace Select2Copy.Models;

public sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool PlaySound { get; set; } = true;
    public bool ShowTooltip { get; set; } = true;
    public bool CopyOnDoubleClick { get; set; } = true;
    public bool HistoryEnabled { get; set; } = true;
    public bool SequentialPasteEnabled { get; set; }
    public HotkeyConfig Hotkey { get; set; } = HotkeyConfig.Default;
    public List<string> ExcludedApps { get; set; } = new();
    public int SchemaVersion { get; set; } = 1;

    public Settings Clone()
    {
        var clone = (Settings)MemberwiseClone();
        clone.Hotkey = Hotkey.Clone();
        clone.ExcludedApps = new List<string>(ExcludedApps);
        return clone;
    }
}
