using Microsoft.Win32;

namespace Select2Copy.Services;

internal static class AutoStartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Select2Copy";

    private static string RunValue => "\"" + AppPaths.ExecutablePath + "\"";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
            {
                return false;
            }

            object? value = key.GetValue(ValueName);
            if (value == null)
            {
                return false;
            }

            if (!string.Equals(value as string, RunValue, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, RunValue, RegistryValueKind.String);
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Write("Autostart read: " + ex.Message);
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null)
            {
                return;
            }

            if (enabled)
            {
                key.SetValue(ValueName, RunValue, RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, false);
            }
        }
        catch (Exception ex)
        {
            Log.Write("Autostart write: " + ex.Message);
        }
    }
}
