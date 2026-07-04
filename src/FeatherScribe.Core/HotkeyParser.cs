namespace FeatherScribe.Core;

/// <summary>
/// "Ctrl+Alt+Space" 形式の文字列を Win32 RegisterHotKey 用の値へ変換する。
/// </summary>
public static class HotkeyParser
{
    [Flags]
    public enum Modifiers : uint
    {
        None = 0x0,
        Alt = 0x1,
        Control = 0x2,
        Shift = 0x4,
        Win = 0x8,
    }

    private static readonly Dictionary<string, uint> KeyNameToVirtualKey =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Space"] = 0x20,
            ["Enter"] = 0x0D,
            ["Return"] = 0x0D,
            ["Tab"] = 0x09,
            ["Escape"] = 0x1B,
            ["Esc"] = 0x1B,
            ["Backspace"] = 0x08,
            ["Insert"] = 0x2D,
            ["Delete"] = 0x2E,
            ["Home"] = 0x24,
            ["End"] = 0x23,
            ["PageUp"] = 0x21,
            ["PageDown"] = 0x22,
            ["Up"] = 0x26,
            ["Down"] = 0x28,
            ["Left"] = 0x25,
            ["Right"] = 0x27,
        };

    public static bool TryParse(string? text, out HotkeyDefinition definition)
    {
        definition = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var modifiers = Modifiers.None;
        uint virtualKey = 0;

        foreach (var rawPart in text.Split('+'))
        {
            var part = rawPart.Trim();
            if (part.Length == 0)
            {
                return false;
            }

            switch (part.ToUpperInvariant())
            {
                case "CTRL" or "CONTROL":
                    modifiers |= Modifiers.Control;
                    continue;
                case "ALT":
                    modifiers |= Modifiers.Alt;
                    continue;
                case "SHIFT":
                    modifiers |= Modifiers.Shift;
                    continue;
                case "WIN" or "WINDOWS":
                    modifiers |= Modifiers.Win;
                    continue;
            }

            if (virtualKey != 0)
            {
                return false; // 非修飾キーが2つ以上ある
            }

            virtualKey = ResolveVirtualKey(part);
            if (virtualKey == 0)
            {
                return false;
            }
        }

        if (virtualKey == 0)
        {
            return false; // 非修飾キーがない
        }

        definition = new HotkeyDefinition(modifiers, virtualKey);
        return true;
    }

    private static uint ResolveVirtualKey(string part)
    {
        if (KeyNameToVirtualKey.TryGetValue(part, out var known))
        {
            return known;
        }

        if (part.Length == 1)
        {
            var ch = char.ToUpperInvariant(part[0]);
            if (ch is >= 'A' and <= 'Z' or >= '0' and <= '9')
            {
                return ch;
            }
        }

        // F1 - F24
        if ((part[0] is 'F' or 'f') && int.TryParse(part[1..], out var fn) && fn is >= 1 and <= 24)
        {
            return (uint)(0x70 + fn - 1);
        }

        return 0;
    }
}

public readonly record struct HotkeyDefinition(
    HotkeyParser.Modifiers Modifiers,
    uint VirtualKey);
