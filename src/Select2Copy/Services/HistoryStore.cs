using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using Select2Copy.Interop;

namespace Select2Copy.Services;

internal sealed class HistoryStore : IDisposable
{
    private const int MaxItems = 5;
    private const int MaxChars = 100_000;

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Select2Copy.History.v1");

    private readonly object _gate = new();
    private readonly object _saveLock = new();
    private readonly System.Threading.Timer _saveTimer;
    private readonly List<string> _items = new();
    private bool _dirty;
    private bool _disposed;

    public event Action<string[]>? Changed;

    public HistoryStore()
    {
        _saveTimer = new System.Threading.Timer(_ => SaveNow(), null, Timeout.Infinite, Timeout.Infinite);
        Load();
    }

    public string[] Snapshot
    {
        get
        {
            lock (_gate)
            {
                return _items.ToArray();
            }
        }
    }

    public void Add(string text)
    {
        if (string.IsNullOrEmpty(text) || text.Length > MaxChars)
        {
            return;
        }

        lock (_gate)
        {
            _items.RemoveAll(item => item == text);
            _items.Insert(0, text);
            if (_items.Count > MaxItems)
            {
                _items.RemoveRange(MaxItems, _items.Count - MaxItems);
            }

            _dirty = true;
        }

        _saveTimer.Change(400, Timeout.Infinite);
        Changed?.Invoke(Snapshot);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
            _dirty = true;
        }

        SaveNow();
        Changed?.Invoke(Array.Empty<string>());
    }

    public void Flush()
    {
        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveNow();
    }

    private void Load()
    {
        if (!File.Exists(AppPaths.HistoryFile))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(AppPaths.HistoryFile, Encoding.UTF8);
            HistoryFile? file = JsonFile.Deserialize<HistoryFile>(json);
            if (file?.Items == null)
            {
                return;
            }

            var loaded = new List<string>();
            foreach (string encoded in file.Items)
            {
                string? text = Unprotect(encoded);
                if (!string.IsNullOrEmpty(text) && !loaded.Contains(text!))
                {
                    loaded.Add(text!);
                    if (loaded.Count >= MaxItems)
                    {
                        break;
                    }
                }
            }

            lock (_gate)
            {
                _items.Clear();
                _items.AddRange(loaded);
            }
        }
        catch (Exception ex)
        {
            Log.Write("History load: " + ex.Message);
        }
    }

    private void SaveNow()
    {
        lock (_saveLock)
        {
            string[] snapshot;
            lock (_gate)
            {
                if (_disposed || !_dirty)
                {
                    return;
                }

                _dirty = false;
                snapshot = _items.ToArray();
            }

            try
            {
                var file = new HistoryFile();
                foreach (string text in snapshot)
                {
                    if (text.Length > MaxChars)
                    {
                        continue;
                    }

                    try
                    {
                        file.Items.Add(Protect(text));
                    }
                    catch (Exception ex)
                    {
                        Log.Write("History encryption failed: " + ex.Message);
                    }
                }

                string json = JsonFile.Serialize(file);
                string path = AppPaths.HistoryFile;
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
            }
            catch (Exception ex)
            {
                lock (_gate)
                {
                    _dirty = true;
                }

                Log.Write("History save: " + ex.Message);
            }
        }
    }

    private static string Protect(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        try
        {
            byte[] encrypted = DpapiProtect(bytes);
            return Convert.ToBase64String(encrypted);
        }
        finally
        {
            Array.Clear(bytes, 0, bytes.Length);
        }
    }

    private static string? Unprotect(string encoded)
    {
        try
        {
            byte[] encrypted = Convert.FromBase64String(encoded);
            byte[] bytes = DpapiUnprotect(encrypted);
            try
            {
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                Array.Clear(bytes, 0, bytes.Length);
            }
        }
        catch (Exception ex)
        {
            Log.Write("History entry skipped: " + ex.Message);
            return null;
        }
    }

    private static byte[] DpapiProtect(byte[] data)
    {
        var input = new NativeMethods.DATA_BLOB
        {
            cbData = data.Length,
            pbData = Marshal.AllocHGlobal(data.Length)
        };
        var entropy = new NativeMethods.DATA_BLOB
        {
            cbData = Entropy.Length,
            pbData = Marshal.AllocHGlobal(Entropy.Length)
        };

        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            Marshal.Copy(Entropy, 0, entropy.pbData, Entropy.Length);

            if (!NativeMethods.CryptProtectData(
                    ref input,
                    "Select2Copy",
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.CRYPTPROTECT_UI_FORBIDDEN,
                    out NativeMethods.DATA_BLOB output))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                NativeMethods.LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
            Marshal.FreeHGlobal(entropy.pbData);
        }
    }

    private static byte[] DpapiUnprotect(byte[] data)
    {
        var input = new NativeMethods.DATA_BLOB
        {
            cbData = data.Length,
            pbData = Marshal.AllocHGlobal(data.Length)
        };
        var entropy = new NativeMethods.DATA_BLOB
        {
            cbData = Entropy.Length,
            pbData = Marshal.AllocHGlobal(Entropy.Length)
        };

        try
        {
            Marshal.Copy(data, 0, input.pbData, data.Length);
            Marshal.Copy(Entropy, 0, entropy.pbData, Entropy.Length);

            if (!NativeMethods.CryptUnprotectData(
                    ref input,
                    IntPtr.Zero,
                    ref entropy,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    NativeMethods.CRYPTPROTECT_UI_FORBIDDEN,
                    out NativeMethods.DATA_BLOB output))
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                var result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            finally
            {
                NativeMethods.LocalFree(output.pbData);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(input.pbData);
            Marshal.FreeHGlobal(entropy.pbData);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _saveTimer.Change(Timeout.Infinite, Timeout.Infinite);
        SaveNow();
        _disposed = true;
        _saveTimer.Dispose();
    }
}

[DataContract]
internal sealed class HistoryFile
{
    [DataMember(Name = "version", Order = 0)]
    public int Version { get; set; } = 1;

    [DataMember(Name = "items", Order = 1)]
    public List<string> Items { get; set; } = new();
}
