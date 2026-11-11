using System.Runtime.InteropServices;
using Select2Copy.Interop;
using Select2Copy.Services;

namespace Select2Copy.Hooks;

internal static class InputSender
{
    public static void SendCtrlC()
    {
        bool leftShift = NativeMethods.KeyDown(NativeMethods.VK_LSHIFT);
        bool rightShift = NativeMethods.KeyDown(NativeMethods.VK_RSHIFT);
        bool leftCtrl = NativeMethods.KeyDown(NativeMethods.VK_LCONTROL);
        bool rightCtrl = NativeMethods.KeyDown(NativeMethods.VK_RCONTROL);

        bool shiftDown = leftShift || rightShift || NativeMethods.KeyDown(NativeMethods.VK_SHIFT);
        bool ctrlDown = leftCtrl || rightCtrl || NativeMethods.KeyDown(NativeMethods.VK_CONTROL);

        var inputs = new List<NativeMethods.INPUT>(10);

        if (shiftDown)
        {
            if (leftShift) Add(inputs, NativeMethods.VK_LSHIFT, true);
            if (rightShift) Add(inputs, NativeMethods.VK_RSHIFT, true);
            if (!leftShift && !rightShift) Add(inputs, NativeMethods.VK_SHIFT, true);
        }

        if (!ctrlDown)
        {
            Add(inputs, NativeMethods.VK_CONTROL, false);
        }

        Add(inputs, NativeMethods.VK_C, false);
        Add(inputs, NativeMethods.VK_C, true);

        if (!ctrlDown)
        {
            Add(inputs, NativeMethods.VK_CONTROL, true);
        }

        if (shiftDown)
        {
            if (!leftShift && !rightShift) Add(inputs, NativeMethods.VK_SHIFT, false);
            if (rightShift) Add(inputs, NativeMethods.VK_RSHIFT, false);
            if (leftShift) Add(inputs, NativeMethods.VK_LSHIFT, false);
        }

        if (inputs.Count == 0)
        {
            return;
        }

        if (NativeMethods.SendInput(
                (uint)inputs.Count,
                inputs.ToArray(),
                Marshal.SizeOf<NativeMethods.INPUT>()) == 0)
        {
            Log.Write("SendInput failed: " + Marshal.GetLastWin32Error());
        }
    }

    private static void Add(List<NativeMethods.INPUT> inputs, int virtualKey, bool up)
    {
        inputs.Add(new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_KEYBOARD,
            u = new NativeMethods.INPUTUNION
            {
                ki = new NativeMethods.KEYBDINPUT
                {
                    wVk = (ushort)virtualKey,
                    dwFlags = up ? NativeMethods.KEYEVENTF_KEYUP : 0
                }
            }
        });
    }
}
