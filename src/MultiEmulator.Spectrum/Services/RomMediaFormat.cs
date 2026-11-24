namespace MultiEmulator.Spectrum.Services;

/// <summary>
/// Preferencia de formato de medio al descargar / jugar desde el catálogo.
/// </summary>
public enum RomMediaFormat
{
    /// <summary>Preferencia inteligente (TZX cuando es mejor; TAP viable si es el único o se elige).</summary>
    Auto = 0,

    /// <summary>Preferir .tap / .tap.zip.</summary>
    Tap = 1,

    /// <summary>Preferir .tzx / .tzx.zip.</summary>
    Tzx = 2,

    /// <summary>Preferir snapshot .z80 / .z80.zip.</summary>
    Z80 = 3,

    /// <summary>Preferir snapshot .sna / .sna.zip.</summary>
    Sna = 4,

    /// <summary>Preferir disco +3 .dsk / .dsk.zip.</summary>
    Dsk = 5,
}

/// <summary>Etiquetas UI y ordenación de URLs por formato preferido.</summary>
public static class RomMediaFormatInfo
{
    public static string DisplayName(RomMediaFormat format) => format switch
    {
        RomMediaFormat.Auto => "Auto",
        RomMediaFormat.Tap => "TAP",
        RomMediaFormat.Tzx => "TZX",
        RomMediaFormat.Z80 => "Z80",
        RomMediaFormat.Sna => "SNA",
        RomMediaFormat.Dsk => "DSK",
        _ => format.ToString()
    };

    public static IReadOnlyList<RomMediaFormat> All { get; } =
    [
        RomMediaFormat.Auto,
        RomMediaFormat.Tap,
        RomMediaFormat.Tzx,
        RomMediaFormat.Z80,
        RomMediaFormat.Sna,
        RomMediaFormat.Dsk,
    ];

    /// <summary>
    /// Extensiones de zip Spectrum Computing (plantillas) según preferencia.
    /// Incluye fallbacks de cinta cuando el formato forzado no exista en pub.
    /// </summary>
    public static string[] SpectrumComputingZipExtensions(RomMediaFormat format) => format switch
    {
        RomMediaFormat.Tap => [".tap.zip", ".tzx.zip"],
        RomMediaFormat.Tzx => [".tzx.zip", ".tap.zip"],
        RomMediaFormat.Z80 => [".z80.zip", ".sna.zip", ".tzx.zip", ".tap.zip"],
        RomMediaFormat.Sna => [".sna.zip", ".z80.zip", ".tzx.zip", ".tap.zip"],
        RomMediaFormat.Dsk => [".dsk.zip", ".tzx.zip", ".tap.zip"],
        _ => [".tzx.zip", ".tap.zip", ".z80.zip", ".sna.zip"],
    };

    /// <summary>True si la URL/ruta encaja con el formato forzado (Auto acepta todo jugable).</summary>
    public static bool MatchesFormat(RomMediaFormat format, string? url)
    {
        if (format == RomMediaFormat.Auto) return true;
        if (string.IsNullOrWhiteSpace(url)) return false;
        string kind = Classify(url);
        return format switch
        {
            RomMediaFormat.Tap => kind == "tap",
            RomMediaFormat.Tzx => kind == "tzx",
            RomMediaFormat.Z80 => kind == "z80",
            RomMediaFormat.Sna => kind == "sna",
            RomMediaFormat.Dsk => kind == "dsk",
            _ => true
        };
    }

    /// <summary>Puntuación de una URL/ruta según preferencia (mayor = mejor).</summary>
    public static int UrlScore(RomMediaFormat format, string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return 0;
        string kind = Classify(url);
        return format switch
        {
            RomMediaFormat.Tap => kind switch
            {
                "tap" => 100,
                "tzx" => 40,
                "z80" or "sna" => 20,
                "dsk" => 10,
                _ => 5
            },
            RomMediaFormat.Tzx => kind switch
            {
                "tzx" => 100,
                "tap" => 40,
                "z80" or "sna" => 20,
                "dsk" => 10,
                _ => 5
            },
            RomMediaFormat.Z80 => kind switch
            {
                "z80" => 100,
                "sna" => 70,
                "tzx" => 40,
                "tap" => 30,
                "dsk" => 10,
                _ => 5
            },
            RomMediaFormat.Sna => kind switch
            {
                "sna" => 100,
                "z80" => 70,
                "tzx" => 40,
                "tap" => 30,
                "dsk" => 10,
                _ => 5
            },
            RomMediaFormat.Dsk => kind switch
            {
                "dsk" => 100,
                "tzx" => 40,
                "tap" => 30,
                "z80" or "sna" => 20,
                _ => 5
            },
            _ => kind switch // Auto: TZX turbo, TAP viable, snaps útiles
            {
                "tzx" => 100,
                "tap" => 70,
                "z80" => 55,
                "sna" => 50,
                "dsk" => 35,
                _ => 5
            }
        };
    }

    /// <summary>Reordena candidatos in-place (estable respecto al orden previo entre empates).</summary>
    public static void SortUrls(List<string> urls, RomMediaFormat format)
    {
        if (urls.Count < 2) return;
        var ranked = urls
            .Select((u, i) => (Url: u, Score: UrlScore(format, u), Index: i))
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Url)
            .ToList();
        urls.Clear();
        urls.AddRange(ranked);
    }

    /// <summary>
    /// Extensiones de caché local a probar, en orden de preferencia.
    /// </summary>
    public static string[] CacheExtensions(RomMediaFormat format) => format switch
    {
        RomMediaFormat.Tap =>
        [
            ".tap.zip", ".tap",
            ".tzx.zip", ".tzx",
            ".z80.zip", ".z80", ".sna.zip", ".sna",
            ".dsk.zip", ".dsk", ".trd", ".zip"
        ],
        RomMediaFormat.Tzx =>
        [
            ".tzx.zip", ".tzx",
            ".tap.zip", ".tap",
            ".z80.zip", ".z80", ".sna.zip", ".sna",
            ".dsk.zip", ".dsk", ".trd", ".zip"
        ],
        RomMediaFormat.Z80 =>
        [
            ".z80.zip", ".z80",
            ".sna.zip", ".sna",
            ".tzx.zip", ".tzx", ".tap.zip", ".tap",
            ".dsk.zip", ".dsk", ".trd", ".zip"
        ],
        RomMediaFormat.Sna =>
        [
            ".sna.zip", ".sna",
            ".z80.zip", ".z80",
            ".tzx.zip", ".tzx", ".tap.zip", ".tap",
            ".dsk.zip", ".dsk", ".trd", ".zip"
        ],
        RomMediaFormat.Dsk =>
        [
            ".dsk.zip", ".dsk",
            ".tzx.zip", ".tzx", ".tap.zip", ".tap",
            ".z80.zip", ".z80", ".sna.zip", ".sna",
            ".trd", ".zip"
        ],
        _ =>
        [
            ".tzx.zip", ".tzx", ".tap.zip", ".tap",
            ".z80.zip", ".z80", ".sna.zip", ".sna",
            ".dsk.zip", ".dsk", ".trd", ".zip"
        ]
    };

    /// <summary>
    /// True si, con esta preferencia, conviene anteponer la hermana .tzx.zip
    /// a un .tap.zip (Auto / TZX). Con TAP forzado no.
    /// </summary>
    public static bool PreferTzxSibling(RomMediaFormat format) =>
        format is RomMediaFormat.Auto or RomMediaFormat.Tzx;

    /// <summary>
    /// True si conviene diferir un TAP «difícil» (POKE/DATA) mientras queden TZX.
    /// Con TAP forzado se acepta al momento.
    /// </summary>
    public static bool DeferHardTapForTzx(RomMediaFormat format) =>
        format is RomMediaFormat.Auto or RomMediaFormat.Tzx;

    public static string Classify(string url)
    {
        string path = url.Trim();
        int q = path.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0) path = path[..q];

        // TOSEC Games/[FMT]/…
        if (path.Contains("%5BTAP%5D", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/[TAP]/", StringComparison.OrdinalIgnoreCase))
            return "tap";
        if (path.Contains("%5BTZX%5D", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/[TZX]/", StringComparison.OrdinalIgnoreCase))
            return "tzx";
        if (path.Contains("%5BZ80%5D", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/[Z80]/", StringComparison.OrdinalIgnoreCase))
            return "z80";
        if (path.Contains("%5BSNA%5D", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/[SNA]/", StringComparison.OrdinalIgnoreCase))
            return "sna";
        if (path.Contains("%5BDSK%5D", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/[DSK]/", StringComparison.OrdinalIgnoreCase))
            return "dsk";

        if (path.Contains("/snaps/", StringComparison.OrdinalIgnoreCase))
        {
            if (path.Contains(".sna", StringComparison.OrdinalIgnoreCase)) return "sna";
            return "z80";
        }
        if (path.Contains("/games-disk/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".dsk.zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".dsk", StringComparison.OrdinalIgnoreCase))
            return "dsk";

        if (path.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tap", StringComparison.OrdinalIgnoreCase))
            return "tap";
        if (path.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase))
            return "tzx";
        if (path.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".z80", StringComparison.OrdinalIgnoreCase))
            return "z80";
        if (path.EndsWith(".sna.zip", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".sna", StringComparison.OrdinalIgnoreCase))
            return "sna";
        return "other";
    }
}
