using System.Windows.Forms;

namespace Translation.App;

public sealed record HotkeyDefinition(int Modifiers, int VirtualKey)
{
    public static HotkeyDefinition Default { get; } = new(
        NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT,
        (int)Keys.F9);

    public static bool TryParse(
        string? text,
        out HotkeyDefinition definition,
        out string? error)
    {
        definition = Default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键不能为空";
            return false;
        }

        var modifiers = 0;
        var virtualKey = 0;
        foreach (var rawPart in text.Split(
                     '+',
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.ToLowerInvariant();
            switch (part)
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    continue;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    continue;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    continue;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    continue;
            }

            if (virtualKey != 0)
            {
                error = "热键只能包含一个主键";
                return false;
            }

            if (rawPart.Length == 1 && char.IsDigit(rawPart[0]))
            {
                virtualKey = (int)Keys.D0 + (rawPart[0] - '0');
            }
            else if (rawPart.Length == 1 && char.IsLetter(rawPart[0]))
            {
                virtualKey = (int)Keys.A + (char.ToUpperInvariant(rawPart[0]) - 'A');
            }
            else if (rawPart.Length >= 2
                     && (rawPart[0] == 'F' || rawPart[0] == 'f')
                     && int.TryParse(rawPart[1..], out var functionKey)
                     && functionKey is >= 1 and <= 24)
            {
                virtualKey = (int)Keys.F1 + (functionKey - 1);
            }
            else if (Enum.TryParse<Keys>(rawPart, true, out var namedKey)
                     && namedKey != Keys.None)
            {
                virtualKey = (int)namedKey;
            }
            else
            {
                error = $"无法识别按键: {rawPart}";
                return false;
            }
        }

        if (modifiers == 0)
        {
            error = "热键至少需要一个修饰键（Ctrl/Alt/Shift/Win）";
            return false;
        }

        if (virtualKey == 0)
        {
            error = "热键缺少主键";
            return false;
        }

        if (IsModifierKey((Keys)virtualKey))
        {
            error = "主键不能是修饰键";
            return false;
        }

        definition = new HotkeyDefinition(modifiers, virtualKey);
        return true;
    }

    private static bool IsModifierKey(Keys key)
    {
        return key is Keys.ControlKey or Keys.LControlKey or Keys.RControlKey
            or Keys.Menu or Keys.LMenu or Keys.RMenu
            or Keys.ShiftKey or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LWin or Keys.RWin;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if ((Modifiers & NativeMethods.MOD_CONTROL) != 0)
        {
            parts.Add("Ctrl");
        }

        if ((Modifiers & NativeMethods.MOD_ALT) != 0)
        {
            parts.Add("Alt");
        }

        if ((Modifiers & NativeMethods.MOD_SHIFT) != 0)
        {
            parts.Add("Shift");
        }

        if ((Modifiers & NativeMethods.MOD_WIN) != 0)
        {
            parts.Add("Win");
        }

        var key = (Keys)VirtualKey;
        if (key is >= Keys.D0 and <= Keys.D9)
        {
            parts.Add(((char)('0' + (key - Keys.D0))).ToString());
        }
        else if (key is >= Keys.A and <= Keys.Z)
        {
            parts.Add(((char)('A' + (key - Keys.A))).ToString());
        }
        else
        {
            parts.Add(key.ToString());
        }

        return string.Join("+", parts);
    }
}
