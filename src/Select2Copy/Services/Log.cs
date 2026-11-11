using System.IO;
using System.Text;

namespace Select2Copy.Services;

internal static class Log
{
    private static readonly object Gate = new();

    public static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                var info = new FileInfo(AppPaths.LogFile);
                if (info.Exists && info.Length > 262144)
                {
                    info.Delete();
                }

                File.AppendAllText(
                    AppPaths.LogFile,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + message + Environment.NewLine,
                    new UTF8Encoding(false));
            }
        }
        catch
        {
        }
    }
}
