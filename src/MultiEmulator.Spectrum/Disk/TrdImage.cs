namespace MultiEmulator.Spectrum.Disk;

/// <summary>Imagen TR-DOS (.trd): catálogo + lectura de ficheros por track/sector.</summary>
public sealed class TrdImage
{
    public const int SectorSize = 256;
    public const int SectorsPerTrack = 16;
    public const int MaxCatalogEntries = 128;

    private readonly byte[] _data;

    public TrdImage(byte[] data)
    {
        _data = data ?? throw new ArgumentNullException(nameof(data));
        if (_data.Length < SectorSize * 9)
            throw new InvalidDataException("TRD demasiado pequeño (falta catálogo)");
        Files = ParseCatalog();
        DiskType = _data[SectorOffset(0, 8) + 0xE3];
        FileCount = _data[SectorOffset(0, 8) + 0xE4];
    }

    public IReadOnlyList<TrdFileEntry> Files { get; }
    public byte DiskType { get; }
    public byte FileCount { get; }
    public int DataLength => _data.Length;

    public static TrdImage Load(byte[] data) => new(data);

    /// <summary>Lee todos los sectores del fichero (TR-DOS carga sectores enteros).</summary>
    public byte[] ReadFile(TrdFileEntry entry)
    {
        int total = entry.SectorCount * SectorSize;
        var buf = new byte[total];
        int track = entry.StartTrack;
        int sector = entry.StartSector;
        int offset = 0;
        for (int i = 0; i < entry.SectorCount; i++)
        {
            int src = SectorOffset(track, sector);
            int n = Math.Min(SectorSize, _data.Length - src);
            if (n > 0)
                Buffer.BlockCopy(_data, src, buf, offset, n);
            offset += SectorSize;
            sector++;
            if (sector >= SectorsPerTrack)
            {
                sector = 0;
                track++;
            }
        }

        return buf;
    }

    /// <summary>
    /// Resuelve el fichero de arranque: <c>boot</c> → RUN/LOAD "nombre", o primer BASIC/CODE.
    /// </summary>
    public TrdFileEntry ResolveAutoRunEntry()
    {
        var boot = Files.FirstOrDefault(f =>
            f.Name.Equals("boot", StringComparison.OrdinalIgnoreCase));
        if (boot != null)
        {
            var raw = ReadFile(boot);
            if (TryParseRunOrLoadName(raw, out string target))
            {
                var hit = FindFile(target);
                if (hit != null)
                    return hit;
            }

            if (boot.Type == TrdFileType.Basic)
                return boot;
        }

        var basicAuto = Files.FirstOrDefault(f => f.Type == TrdFileType.Basic && f.AutoStartLine > 0);
        if (basicAuto != null)
            return basicAuto;

        var basic = Files.FirstOrDefault(f => f.Type == TrdFileType.Basic);
        if (basic != null)
            return basic;

        var code = Files.FirstOrDefault(f => f.Type == TrdFileType.Code);
        if (code != null)
            return code;

        throw new InvalidDataException("El TRD no tiene ficheros BASIC/CODE arrancables");
    }

    public TrdFileEntry? FindFile(string name)
    {
        string key = NormalizeName(name);
        return Files.FirstOrDefault(f => NormalizeName(f.Name) == key);
    }

    private List<TrdFileEntry> ParseCatalog()
    {
        var list = new List<TrdFileEntry>();
        for (int i = 0; i < MaxCatalogEntries; i++)
        {
            int sectorIndex = i / 16;
            int indexInSector = i % 16;
            int off = SectorOffset(0, sectorIndex) + indexInSector * 16;
            if (off + 16 > _data.Length)
                break;

            byte first = _data[off];
            if (first == 0x00)
                break; // fin de catálogo
            if (first == 0x01)
                continue; // borrado

            char typeChar = (char)_data[off + 8];
            var type = typeChar switch
            {
                'B' => TrdFileType.Basic,
                'C' => TrdFileType.Code,
                'D' => TrdFileType.Data,
                '#' => TrdFileType.Print,
                _ => TrdFileType.Unknown
            };

            string name = System.Text.Encoding.ASCII.GetString(_data, off, 8).TrimEnd(' ', '\0');
            ushort param1 = (ushort)(_data[off + 9] | (_data[off + 10] << 8));
            ushort param2 = (ushort)(_data[off + 11] | (_data[off + 12] << 8));
            byte sectors = _data[off + 13];
            byte startSec = _data[off + 14];
            byte startTrk = _data[off + 15];

            list.Add(new TrdFileEntry(
                name,
                type,
                typeChar,
                param1,
                param2,
                sectors,
                startSec,
                startTrk));
        }

        return list;
    }

    private int SectorOffset(int track, int sector)
    {
        if ((uint)sector >= SectorsPerTrack)
            throw new ArgumentOutOfRangeException(nameof(sector));
        return (track * SectorsPerTrack + sector) * SectorSize;
    }

    private static string NormalizeName(string name) =>
        name.Trim().TrimEnd(' ', '\0').ToUpperInvariant();

    /// <summary>Busca tokens RUN/LOAD "nombre" en un BASIC TR-DOS (p.ej. boot).</summary>
    public static bool TryParseRunOrLoadName(ReadOnlySpan<byte> basic, out string name)
    {
        name = "";
        for (int i = 0; i < basic.Length - 3; i++)
        {
            byte t = basic[i];
            // RUN 0xF7 o LOAD 0xEF seguidos de comillas
            if ((t == 0xF7 || t == 0xEF) && basic[i + 1] == (byte)'"')
            {
                int start = i + 2;
                int end = start;
                while (end < basic.Length && basic[end] != (byte)'"' && basic[end] != 0x0D)
                    end++;
                if (end > start)
                {
                    name = System.Text.Encoding.ASCII.GetString(basic[start..end]).Trim();
                    if (name.Length > 0)
                        return true;
                }
            }
        }

        return false;
    }
}

public enum TrdFileType
{
    Unknown,
    Basic,
    Code,
    Data,
    Print
}

public sealed record TrdFileEntry(
    string Name,
    TrdFileType Type,
    char TypeChar,
    ushort Param1,
    ushort Param2,
    byte SectorCount,
    byte StartSector,
    byte StartTrack)
{
    /// <summary>Línea de autostart (BASIC) o dirección de carga (CODE).</summary>
    public ushort AutoStartLine => Type == TrdFileType.Basic ? Param1 : (ushort)0;

    public ushort LoadAddress => Type == TrdFileType.Code ? Param1 : (ushort)0;

    public ushort LengthBytes => Param2;
}
