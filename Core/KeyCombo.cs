using System;
using System.Text;

namespace SDX2_SystemMap;

/// <summary>
/// A key binding as the settings file stores it: "Ctrl+Shift+M", "Alt+F8", "Ctrl+Alt+OemQuestion", or "None" (unbound).
/// The key part is a game key name (MyKeys); the modifiers must match exactly when it fires. Game-free so it can be tested.
/// </summary>
public readonly struct KeyCombo : IEquatable<KeyCombo>
{
    public readonly string Key;
    public readonly bool Ctrl, Shift, Alt;

    public KeyCombo(string key, bool ctrl, bool shift, bool alt)
    {
        Key = string.IsNullOrWhiteSpace(key) || key.Trim().Equals("None", StringComparison.OrdinalIgnoreCase) ? null : key.Trim();
        Ctrl = Key != null && ctrl; Shift = Key != null && shift; Alt = Key != null && alt;
    }

    public static readonly KeyCombo None = default;
    public bool IsNone => Key == null;

    /// <summary>Reads "Ctrl+Shift+M" (any order, any case; "Control" works too). Anything without a key is None.</summary>
    public static KeyCombo Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return None;
        bool ctrl = false, shift = false, alt = false; string key = null;
        foreach (var raw in text.Split('+'))
        {
            var p = raw.Trim();
            if (p.Length == 0) continue;
            if (p.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || p.Equals("Control", StringComparison.OrdinalIgnoreCase)) ctrl = true;
            else if (p.Equals("Shift", StringComparison.OrdinalIgnoreCase)) shift = true;
            else if (p.Equals("Alt", StringComparison.OrdinalIgnoreCase)) alt = true;
            else key = FromDisplay(p);
        }
        return new KeyCombo(key, ctrl, shift, alt);
    }

    /// <summary>The settings-file form: "Ctrl+Shift+M", or "None".</summary>
    public override string ToString() => IsNone ? "None" : Join(Key);

    /// <summary>For the screen: "Ctrl+Alt+/", "Shift+M", or "unbound".</summary>
    public string Display => IsNone ? "unbound" : Join(KeyName(Key));

    private string Join(string key)
    {
        var sb = new StringBuilder();
        if (Ctrl) sb.Append("Ctrl+");
        if (Shift) sb.Append("Shift+");
        if (Alt) sb.Append("Alt+");
        return sb.Append(key).ToString();
    }

    private static readonly string[,] Names =
    {
        { "OemQuestion", "/" }, { "OemPipe", "\\" }, { "OemBackslash", "\\ (ISO)" }, { "OemTilde", "`" }, { "OemPlus", "=" },
        { "OemMinus", "-" }, { "OemComma", "," }, { "OemPeriod", "." }, { "OemSemicolon", ";" }, { "OemQuotes", "'" },
        { "OemOpenBrackets", "[" }, { "OemCloseBrackets", "]" },
    };

    /// <summary>A game key name as players know it: OemQuestion → "/", D5 → "5".</summary>
    public static string KeyName(string key)
    {
        if (key == null) return "";
        for (int i = 0; i < Names.GetLength(0); i++) if (Names[i, 0] == key) return Names[i, 1];
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) return key.Substring(1);
        return key;
    }

    private static string FromDisplay(string s)
    {
        for (int i = 0; i < Names.GetLength(0); i++) if (Names[i, 1] == s) return Names[i, 0];
        if (s.Length == 1 && char.IsDigit(s[0])) return "D" + s;
        return s;
    }

    public bool Equals(KeyCombo o) => string.Equals(Key, o.Key, StringComparison.OrdinalIgnoreCase) && Ctrl == o.Ctrl && Shift == o.Shift && Alt == o.Alt;
    public override bool Equals(object obj) => obj is KeyCombo k && Equals(k);
    public override int GetHashCode() => (Key?.ToUpperInvariant().GetHashCode() ?? 0) ^ (Ctrl ? 1 : 0) ^ (Shift ? 2 : 0) ^ (Alt ? 4 : 0);
}
