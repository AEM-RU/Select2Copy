using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using Select2Copy.Interop;

namespace Select2Copy.Services;

internal static class ProcessService
{
    private static readonly HashSet<string> Shell = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer",
        "dwm",
        "taskmgr",
        "searchhost",
        "searchui",
        "shellexperiencehost",
        "startmenuexperiencehost",
        "textinputhost",
        "applicationframehost",
        "select2copy"
    };

    private static readonly ConcurrentDictionary<uint, CacheEntry> Cache = new();

    private readonly struct CacheEntry
    {
        public string Name { get; }
        public int Tick { get; }

        public CacheEntry(string name, int tick)
        {
            Name = name;
            Tick = tick;
        }
    }

    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }

        name = name!.Trim();
        if (name.Length >= 2 && name[0] == '"' && name[name.Length - 1] == '"')
        {
            name = name.Substring(1, name.Length - 2).Trim();
        }

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            name = name.Substring(0, name.Length - 4);
        }

        return name.Trim();
    }

    public static string PathOf(uint pid)
    {
        if (pid == 0)
        {
            return "";
        }

        IntPtr process = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process == IntPtr.Zero)
        {
            return "";
        }

        try
        {
            var builder = new StringBuilder(1024);
            int size = builder.Capacity;
            return NativeMethods.QueryFullProcessImageName(process, 0, builder, ref size)
                ? builder.ToString()
                : "";
        }
        finally
        {
            NativeMethods.CloseHandle(process);
        }
    }

    public static string NameOf(uint pid)
    {
        if (pid == 0)
        {
            return "";
        }

        int tick = Environment.TickCount;
        if (Cache.TryGetValue(pid, out CacheEntry cached) && tick - cached.Tick < 2000)
        {
            return cached.Name;
        }

        string name = "";
        string path = PathOf(pid);
        if (path.Length > 0)
        {
            name = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            try
            {
                using Process process = Process.GetProcessById((int)pid);
                name = process.ProcessName;
            }
            catch
            {
            }
        }

        if (name.Length > 0)
        {
            if (Cache.Count > 128)
            {
                Cache.Clear();
            }

            Cache[pid] = new CacheEntry(name, tick);
        }

        return name;
    }

    public static string NameOfWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
        {
            return "";
        }

        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        return NameOf(pid);
    }

    public static bool IsShell(string name)
    {
        return name.Length > 0 && Shell.Contains(name);
    }

    public static bool IsExcluded(uint pid, IEnumerable<string> excluded)
    {
        string name = NameOf(pid);
        if (name.Length == 0)
        {
            return false;
        }

        if (Shell.Contains(name))
        {
            return true;
        }

        foreach (string item in excluded)
        {
            if (string.Equals(item, name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static string ForegroundName()
    {
        IntPtr hwnd = NativeMethods.GetForegroundWindow();
        return NameOfWindow(hwnd);
    }
}
