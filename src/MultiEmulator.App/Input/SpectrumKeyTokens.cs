using MultiEmulator.Spectrum.Input;

namespace MultiEmulator.App.Input;

public sealed record SpectrumKeyEntry(string Id, string Label, (int row, int bit)[] Bits);

/// <summary>
/// Tokens de tecla Spectrum asignables al mando (simples o combos con nombre).
/// </summary>
public static class SpectrumKeyTokens
{
    public static IReadOnlyList<SpectrumKeyEntry> Catalog { get; } =
    [
        new("CAPS", "Caps Shift", [(0, 0)]),
        new("SYMBOL", "Symbol Shift", [(7, 1)]),
        new("SPACE", "Space", [(7, 0)]),
        new("ENTER", "Enter", [(6, 0)]),
        new("1", "1", [(3, 0)]),
        new("2", "2", [(3, 1)]),
        new("3", "3", [(3, 2)]),
        new("4", "4", [(3, 3)]),
        new("5", "5", [(3, 4)]),
        new("6", "6", [(4, 4)]),
        new("7", "7", [(4, 3)]),
        new("8", "8", [(4, 2)]),
        new("9", "9", [(4, 1)]),
        new("0", "0", [(4, 0)]),
        new("Q", "Q", [(2, 0)]),
        new("W", "W", [(2, 1)]),
        new("E", "E", [(2, 2)]),
        new("R", "R", [(2, 3)]),
        new("T", "T", [(2, 4)]),
        new("Y", "Y", [(5, 4)]),
        new("U", "U", [(5, 3)]),
        new("I", "I", [(5, 2)]),
        new("O", "O", [(5, 1)]),
        new("P", "P", [(5, 0)]),
        new("A", "A", [(1, 0)]),
        new("S", "S", [(1, 1)]),
        new("D", "D", [(1, 2)]),
        new("F", "F", [(1, 3)]),
        new("G", "G", [(1, 4)]),
        new("H", "H", [(6, 4)]),
        new("J", "J", [(6, 3)]),
        new("K", "K", [(6, 2)]),
        new("L", "L", [(6, 1)]),
        new("Z", "Z", [(0, 1)]),
        new("X", "X", [(0, 2)]),
        new("C", "C", [(0, 3)]),
        new("V", "V", [(0, 4)]),
        new("B", "B", [(7, 4)]),
        new("N", "N", [(7, 3)]),
        new("M", "M", [(7, 2)]),
        // Combos con nombre (atajos de UI)
        new("CURSOR_LEFT", "Cursor ← (Caps+5)", [(0, 0), (3, 4)]),
        new("CURSOR_RIGHT", "Cursor → (Caps+8)", [(0, 0), (4, 2)]),
        new("CURSOR_UP", "Cursor ↑ (Caps+7)", [(0, 0), (4, 3)]),
        new("CURSOR_DOWN", "Cursor ↓ (Caps+6)", [(0, 0), (4, 4)]),
        new("BREAK", "BREAK (Caps+Space)", [(0, 0), (7, 0)]),
        new("DELETE", "Delete (Caps+0)", [(0, 0), (4, 0)]),
    ];

    private static readonly Dictionary<string, SpectrumKeyEntry> ById =
        Catalog.ToDictionary(e => e.Id, StringComparer.OrdinalIgnoreCase);

    public static string LabelOf(string id) =>
        ById.TryGetValue(id, out var e) ? e.Label : id;

    public static bool IsKnown(string id) => ById.ContainsKey(id);

    public static void Apply(KeyboardMatrix kb, string id)
    {
        if (!ById.TryGetValue(id, out var e)) return;
        foreach (var (row, bit) in e.Bits)
            kb.SetKey(row, bit, true);
    }

    public static void ApplyAll(KeyboardMatrix kb, IEnumerable<string> ids)
    {
        foreach (var id in ids)
            Apply(kb, id);
    }
}
