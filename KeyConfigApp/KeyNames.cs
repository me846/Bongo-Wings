using System.Windows.Input;

namespace ArcadeLeverKeyConfig;

public static class KeyNames
{
    public static string? ToCode(Key sourceKey)
    {
        var key = sourceKey;

        if (key is >= Key.A and <= Key.Z)
        {
            return $"Key{key}";
        }
        if (key is >= Key.D0 and <= Key.D9)
        {
            return $"Digit{(int)key - (int)Key.D0}";
        }
        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            return $"Numpad{(int)key - (int)Key.NumPad0}";
        }
        if (key is >= Key.F1 and <= Key.F24)
        {
            return $"F{(int)key - (int)Key.F1 + 1}";
        }

        return key switch
        {
            Key.Up => "ArrowUp",
            Key.Down => "ArrowDown",
            Key.Left => "ArrowLeft",
            Key.Right => "ArrowRight",
            Key.Space => "Space",
            Key.Enter => "Enter",
            Key.Tab => "Tab",
            Key.Back => "Backspace",
            Key.Delete => "Delete",
            Key.Insert => "Insert",
            Key.Home => "Home",
            Key.End => "End",
            Key.PageUp => "PageUp",
            Key.PageDown => "PageDown",
            Key.Pause => "Pause",
            Key.CapsLock => "CapsLock",
            Key.Scroll => "ScrollLock",
            Key.PrintScreen => "PrintScreen",
            Key.LeftShift => "ShiftLeft",
            Key.RightShift => "ShiftRight",
            Key.LeftCtrl => "ControlLeft",
            Key.RightCtrl => "ControlRight",
            Key.LeftAlt => "AltLeft",
            Key.RightAlt => "AltRight",
            Key.LWin => "MetaLeft",
            Key.RWin => "MetaRight",
            Key.Multiply => "NumpadMultiply",
            Key.Add => "NumpadAdd",
            Key.Subtract => "NumpadSubtract",
            Key.Decimal => "NumpadDecimal",
            Key.Divide => "NumpadDivide",
            Key.NumLock => "NumLock",
            Key.OemSemicolon => "Semicolon",
            Key.OemPlus => "Equal",
            Key.OemComma => "Comma",
            Key.OemMinus => "Minus",
            Key.OemPeriod => "Period",
            Key.OemQuestion => "Slash",
            Key.OemTilde => "Backquote",
            Key.OemOpenBrackets => "BracketLeft",
            Key.OemPipe => "Backslash",
            Key.OemCloseBrackets => "BracketRight",
            Key.OemQuotes => "Quote",
            _ => null,
        };
    }

    public static string Display(string code) => code switch
    {
        "ArrowUp" => "↑",
        "ArrowDown" => "↓",
        "ArrowLeft" => "←",
        "ArrowRight" => "→",
        "Escape" => "Esc",
        "Semicolon" => ";",
        "Quote" => "'",
        "Comma" => ",",
        "Period" => ".",
        "Slash" => "/",
        "Backslash" => "\\",
        "BracketLeft" => "[",
        "BracketRight" => "]",
        "Minus" => "-",
        "Equal" => "=",
        _ when code.StartsWith("Key", StringComparison.Ordinal) => code[3..],
        _ when code.StartsWith("Digit", StringComparison.Ordinal) => code[5..],
        _ when code.StartsWith("Numpad", StringComparison.Ordinal) => $"Num {code[6..]}",
        _ => code,
    };
}
