namespace MultiEmulator.Spectrum.Input;

public sealed class KeyboardMatrix
{
    // 8 rows corresponding to high address bits of port FE
    private readonly byte[] _rows = { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

    public void Reset() => ReleaseAll();

    public void ReleaseAll()
    {
        for (int i = 0; i < 8; i++)
            _rows[i] = 0xFF;
    }

    public void SetKey(int row, int bit, bool pressed)
    {
        if (row is < 0 or > 7 || bit is < 0 or > 4) return;
        if (pressed)
            _rows[row] = (byte)(_rows[row] & ~(1 << bit));
        else
            _rows[row] = (byte)(_rows[row] | (1 << bit));
    }

    public byte Read(byte highAddr)
    {
        byte result = 0x1F;
        for (int i = 0; i < 8; i++)
        {
            if ((highAddr & (1 << i)) == 0)
                result &= _rows[i];
        }
        return (byte)(result & 0x1F);
    }

    /// <summary>
    /// PC key name → Spectrum matrix (row, bit). Avalonia digits arrive as D0–D9.
    /// Cursor keys return the number key only; host must also hold Caps Shift.
    /// </summary>
    public static (int row, int bit)? MapPcKey(string key)
    {
        return key.ToUpperInvariant() switch
        {
            "1" or "D1" or "NUMPAD1" => (3, 0),
            "2" or "D2" or "NUMPAD2" => (3, 1),
            "3" or "D3" or "NUMPAD3" => (3, 2),
            "4" or "D4" or "NUMPAD4" => (3, 3),
            "5" or "D5" or "NUMPAD5" => (3, 4),
            "6" or "D6" or "NUMPAD6" => (4, 4),
            "7" or "D7" or "NUMPAD7" => (4, 3),
            "8" or "D8" or "NUMPAD8" => (4, 2),
            "9" or "D9" or "NUMPAD9" => (4, 1),
            "0" or "D0" or "NUMPAD0" => (4, 0),
            "Q" => (2, 0), "W" => (2, 1), "E" => (2, 2), "R" => (2, 3), "T" => (2, 4),
            "Y" => (5, 4), "U" => (5, 3), "I" => (5, 2), "O" => (5, 1), "P" => (5, 0),
            "A" => (1, 0), "S" => (1, 1), "D" => (1, 2), "F" => (1, 3), "G" => (1, 4),
            "H" => (6, 4), "J" => (6, 3), "K" => (6, 2), "L" => (6, 1),
            "RETURN" or "ENTER" => (6, 0),
            "LEFTSHIFT" or "SHIFT" => (0, 0), // Caps Shift (solo Shift físico)
            "Z" => (0, 1), "X" => (0, 2), "C" => (0, 3), "V" => (0, 4),
            "B" => (7, 4), "N" => (7, 3), "M" => (7, 2),
            "RIGHTSHIFT" => (7, 1), // Symbol Shift
            "SPACE" => (7, 0),
            // Cursor = Caps Shift + 5/6/7/8 (number key only here)
            "UP" => (4, 3),
            "DOWN" => (4, 4),
            "LEFT" => (3, 4),
            "RIGHT" => (4, 2),
            "BACK" or "BACKSPACE" or "DELETE" => (0, 0), // Caps Shift; host adds 0
            _ => null
        };
    }

    public static bool IsCursorKey(string key) =>
        key.Equals("UP", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("DOWN", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("LEFT", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("RIGHT", StringComparison.OrdinalIgnoreCase);

    public static bool IsBackspaceKey(string key) =>
        key.Equals("BACK", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("BACKSPACE", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("DELETE", StringComparison.OrdinalIgnoreCase);
}
