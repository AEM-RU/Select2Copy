using System.Runtime.InteropServices;
using Select2Copy.Interop;

namespace Select2Copy.Services;

internal static class ClipboardService
{
    private static readonly object Gate = new();

    public static string? ReadText()
    {
        lock (Gate)
        {
            for (int attempt = 0; attempt < 8; attempt++)
            {
                if (NativeMethods.OpenClipboard(IntPtr.Zero))
                {
                    try
                    {
                        bool unicode = true;
                        IntPtr data = NativeMethods.GetClipboardData(NativeMethods.CF_UNICODETEXT);
                        if (data == IntPtr.Zero)
                        {
                            unicode = false;
                            data = NativeMethods.GetClipboardData(NativeMethods.CF_TEXT);
                        }

                        if (data == IntPtr.Zero)
                        {
                            return null;
                        }

                        IntPtr pointer = NativeMethods.GlobalLock(data);
                        if (pointer == IntPtr.Zero)
                        {
                            return null;
                        }

                        try
                        {
                            int size = (int)Math.Min((ulong)NativeMethods.GlobalSize(data), 1073741823UL);
                            string text = unicode
                                ? Marshal.PtrToStringUni(pointer, size / 2)
                                : Marshal.PtrToStringAnsi(pointer, size);

                            int zero = text.IndexOf('\0');
                            return zero >= 0 ? text[..zero] : text;
                        }
                        finally
                        {
                            NativeMethods.GlobalUnlock(data);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Write("Clipboard read: " + ex.Message);
                    }
                    finally
                    {
                        NativeMethods.CloseClipboard();
                    }
                }

                Thread.Sleep(12);
            }

            return null;
        }
    }

    public static bool WriteText(string text, IntPtr owner)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        lock (Gate)
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                if (NativeMethods.OpenClipboard(owner))
                {
                    bool success = false;
                    try
                    {
                        NativeMethods.EmptyClipboard();
                        IntPtr memory = NativeMethods.GlobalAlloc(
                            NativeMethods.GMEM_MOVEABLE,
                            (UIntPtr)(uint)((text.Length + 1) * 2));

                        if (memory != IntPtr.Zero)
                        {
                            IntPtr pointer = NativeMethods.GlobalLock(memory);
                            if (pointer != IntPtr.Zero)
                            {
                                Marshal.Copy(text.ToCharArray(), 0, pointer, text.Length);
                                Marshal.WriteInt16(pointer, text.Length * 2, 0);
                                NativeMethods.GlobalUnlock(memory);
                                success = NativeMethods.SetClipboardData(NativeMethods.CF_UNICODETEXT, memory) != IntPtr.Zero;
                            }

                            if (!success)
                            {
                                NativeMethods.GlobalFree(memory);
                            }
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseClipboard();
                    }

                    if (success)
                    {
                        return true;
                    }
                }

                Thread.Sleep(15);
            }

            Log.Write("Clipboard write failed");
            return false;
        }
    }
}
