using System.Runtime.Serialization;
using System.Text;
using System.Windows.Input;
using Select2Copy.Interop;

namespace Select2Copy.Models;

[DataContract]
public sealed class HotkeyConfig
{
    [DataMember(Name = "ctrl", Order = 0)]
    public bool Ctrl { get; set; }

    [DataMember(Name = "shift", Order = 1)]
    public bool Shift { get; set; }

    [DataMember(Name = "alt", Order = 2)]
    public bool Alt { get; set; }

    [DataMember(Name = "win", Order = 3)]
    public bool Win { get; set; }

    [DataMember(Name = "keyName", Order = 4)]
    public string KeyName { get; set; } = nameof(Key.F2);

    public static HotkeyConfig Default => new() { Ctrl = true, KeyName = nameof(Key.F2) };

    public bool IsValid
    {
        get
        {
            if (!TryGetKey(out Key key) || key == Key.None || IsModifier(key))
            {
                return false;
            }

            if (key < Key.F1 || key > Key.F24)
            {
                return Ctrl || Shift || Alt || Win;
            }

            return true;
        }
    }

    public uint Modifiers
    {
        get
        {
            uint value = 0;
            if (Alt) value |= NativeMethods.MOD_ALT;
            if (Ctrl) value |= NativeMethods.MOD_CONTROL;
            if (Shift) value |= NativeMethods.MOD_SHIFT;
            if (Win) value |= NativeMethods.MOD_WIN;
            return value;
        }
    }

    public uint VirtualKey => TryGetKey(out Key key) ? (uint)KeyInterop.VirtualKeyFromKey(key) : 0;

    public string Display
    {
        get
        {
            var builder = new StringBuilder();
            if (Ctrl) builder.Append("Ctrl + ");
            if (Shift) builder.Append("Shift + ");
            if (Alt) builder.Append("Alt + ");
            if (Win) builder.Append("Win + ");

            if (TryGetKey(out Key key))
            {
                string text = key.ToString();
                if (key >= Key.D0 && key <= Key.D9)
                {
                    text = text.Substring(1);
                }

                builder.Append(text);
            }

            return builder.ToString();
        }
    }

    public static bool TryParse(string? text, out HotkeyConfig? result)
    {
        result = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var config = new HotkeyConfig();
        bool hasKey = false;
        string[] parts = text!.Split(new[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (string part in parts)
        {
            string token = part.Trim();
            if (token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Control", StringComparison.OrdinalIgnoreCase))
            {
                config.Ctrl = true;
                continue;
            }

            if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase))
            {
                config.Shift = true;
                continue;
            }

            if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase))
            {
                config.Alt = true;
                continue;
            }

            if (token.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("Windows", StringComparison.OrdinalIgnoreCase))
            {
                config.Win = true;
                continue;
            }

            if (token.Length == 1 && char.IsDigit(token[0]))
            {
                token = "D" + token;
            }

            if (!Enum.TryParse(token, true, out Key key) || !Enum.IsDefined(typeof(Key), key))
            {
                return false;
            }

            config.KeyName = key.ToString();
            hasKey = true;
        }

        if (!hasKey || !config.IsValid)
        {
            return false;
        }

        result = config;
        return true;
    }

    public static bool IsModifier(Key key)
    {
        return key is Key.LeftCtrl or Key.RightCtrl
            or Key.LeftShift or Key.RightShift
            or Key.LeftAlt or Key.RightAlt
            or Key.LWin or Key.RWin
            or Key.System;
    }

    public HotkeyConfig Clone()
    {
        return (HotkeyConfig)MemberwiseClone();
    }

    public bool TryGetKey(out Key key)
    {
        return Enum.TryParse(KeyName, true, out key) && Enum.IsDefined(typeof(Key), key);
    }

    public override string ToString()
    {
        return Display;
    }
}
