using System.Runtime.Serialization;

namespace Select2Copy.Models;

[DataContract]
public sealed class Settings
{
    [DataMember(Name = "enabled", Order = 0)]
    public bool Enabled { get; set; } = true;

    [DataMember(Name = "startWithWindows", Order = 1)]
    public bool StartWithWindows { get; set; }

    [DataMember(Name = "playSound", Order = 2)]
    public bool PlaySound { get; set; } = true;

    [DataMember(Name = "showTooltip", Order = 3)]
    public bool ShowTooltip { get; set; } = true;

    [DataMember(Name = "copyOnDoubleClick", Order = 4)]
    public bool CopyOnDoubleClick { get; set; } = true;

    [DataMember(Name = "historyEnabled", Order = 5)]
    public bool HistoryEnabled { get; set; } = true;

    [DataMember(Name = "sequentialPasteEnabled", Order = 6)]
    public bool SequentialPasteEnabled { get; set; }

    [DataMember(Name = "hotkey", Order = 7)]
    public HotkeyConfig Hotkey { get; set; } = HotkeyConfig.Default;

    [DataMember(Name = "excludedApps", Order = 8)]
    public List<string> ExcludedApps { get; set; } = new();

    [DataMember(Name = "schemaVersion", Order = 9)]
    public int SchemaVersion { get; set; } = 1;

    public Settings Clone()
    {
        var clone = (Settings)MemberwiseClone();
        clone.Hotkey = Hotkey.Clone();
        clone.ExcludedApps = new List<string>(ExcludedApps);
        return clone;
    }
}
