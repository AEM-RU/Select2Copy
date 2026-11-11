using System.IO;
using System.Text;
using System.Text.Json;
using Select2Copy.Models;

namespace Select2Copy.Services;

internal sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private readonly object _gate = new();
    private Settings _current;

    public Settings Current => _current;
    public event Action<Settings>? Changed;

    public SettingsStore()
    {
        bool existed = File.Exists(AppPaths.SettingsFile);
        _current = Load();
        bool autoStart = AutoStartService.IsEnabled();
        bool changed = false;
        if (_current.StartWithWindows != autoStart)
        {
            _current.StartWithWindows = autoStart;
            changed = true;
        }

        if (!existed || changed)
        {
            SaveInternal(_current);
        }
    }

    public void Update(Action<Settings> change)
    {
        Settings settings;
        lock (_gate)
        {
            settings = _current.Clone();
            change(settings);
            Normalize(settings);
            SaveInternal(settings);
            _current = settings;
        }

        Changed?.Invoke(settings);
    }

    public void Save(Settings settings)
    {
        lock (_gate)
        {
            Normalize(settings);
            SaveInternal(settings);
            _current = settings;
        }

        Changed?.Invoke(settings);
    }

    private Settings Load()
    {
        if (File.Exists(AppPaths.SettingsFile))
        {
            try
            {
                string json = File.ReadAllText(AppPaths.SettingsFile, Encoding.UTF8);
                Settings? settings = JsonSerializer.Deserialize<Settings>(json, JsonOptions);
                if (settings != null)
                {
                    Normalize(settings);
                    return settings;
                }
            }
            catch (Exception ex)
            {
                Log.Write("Settings load: " + ex.Message);
            }
        }

        var defaults = new Settings();
        Normalize(defaults);
        return defaults;
    }

    private void SaveInternal(Settings settings)
    {
        try
        {
            bool autoStartChanged = _current == null || _current.StartWithWindows != settings.StartWithWindows;
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            string path = AppPaths.SettingsFile;
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                try
                {
                    File.Replace(temp, path, null);
                }
                catch
                {
                    File.Copy(temp, path, true);
                    File.Delete(temp);
                }
            }
            else
            {
                File.Move(temp, path);
            }

            if (autoStartChanged)
            {
                AutoStartService.Set(settings.StartWithWindows);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Settings save: " + ex.Message);
        }
    }

    private static void Normalize(Settings settings)
    {
        settings.Hotkey ??= HotkeyConfig.Default;
        if (!settings.Hotkey.IsValid)
        {
            settings.Hotkey = HotkeyConfig.Default;
        }

        settings.ExcludedApps = (settings.ExcludedApps ?? new List<string>())
            .Select(ProcessService.Normalize)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!settings.HistoryEnabled)
        {
            settings.SequentialPasteEnabled = false;
        }
    }
}
