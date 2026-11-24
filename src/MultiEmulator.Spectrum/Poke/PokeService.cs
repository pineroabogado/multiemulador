using MultiEmulator.Spectrum.Memory;

namespace MultiEmulator.Spectrum.Poke;

public readonly record struct PokeEntry(ushort Address, byte Value, byte? Original = null);

public sealed class PokeTrainer
{
    public string Name { get; init; } = "";
    public List<PokeEntry> Entries { get; } = new();
}

public static class PokeService
{
    public static void Apply(SpectrumMemory mem, ushort address, byte value) =>
        mem.Write(address, value);

    public static void Apply(SpectrumMemory mem, IEnumerable<PokeEntry> entries)
    {
        foreach (var e in entries)
            mem.Write(e.Address, e.Value);
    }

    /// <summary>Parse classic .pok (Trainer) format.</summary>
    public static List<PokeTrainer> ParsePok(string text)
    {
        var list = new List<PokeTrainer>();
        PokeTrainer? current = null;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            char tag = line[0];
            string rest = line.Length > 1 ? line[1..] : "";
            switch (tag)
            {
                case 'N':
                    current = new PokeTrainer { Name = rest.Trim() };
                    list.Add(current);
                    break;
                case 'M':
                case 'Z':
                    if (current == null)
                    {
                        current = new PokeTrainer { Name = "Trainer" };
                        list.Add(current);
                    }
                    // format: bank address value original (space separated decimals)
                    var parts = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 3 &&
                        ushort.TryParse(parts[1], out var addr) &&
                        byte.TryParse(parts[2], out var val))
                    {
                        byte? orig = parts.Length >= 4 && byte.TryParse(parts[3], out var o) ? o : null;
                        current.Entries.Add(new PokeEntry(addr, val, orig));
                    }
                    break;
                case 'Y':
                    current = null;
                    break;
            }
        }
        return list;
    }
}
