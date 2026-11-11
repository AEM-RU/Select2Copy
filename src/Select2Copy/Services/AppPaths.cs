using System.IO;

namespace Select2Copy.Services;

internal static class AppPaths
{
    public static string ExecutablePath { get; }
    public static string Root { get; }
    public static string SettingsFile => Path.Combine(Root, "Select2Copy.json");
    public static string HistoryFile => Path.Combine(Root, "Select2Copy.history.json");
    public static string LogFile => Path.Combine(Root, "Select2Copy.log");

    static AppPaths()
    {
        ExecutablePath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Select2Copy.exe");
        string? exeDir = Path.GetDirectoryName(ExecutablePath);
        if (string.IsNullOrWhiteSpace(exeDir))
        {
            exeDir = AppContext.BaseDirectory;
        }

        Root = IsWritable(exeDir)
            ? exeDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Select2Copy");

        try
        {
            Directory.CreateDirectory(Root);
        }
        catch
        {
        }
    }

    private static bool IsWritable(string directory)
    {
        try
        {
            string probe = Path.Combine(directory, ".select2copy_write_test_" + Guid.NewGuid().ToString("N"));
            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}
