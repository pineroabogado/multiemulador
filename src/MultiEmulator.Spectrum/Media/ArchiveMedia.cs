using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MultiEmulator.Spectrum.Media;

public static class ArchiveMedia
{
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tap", ".tzx", ".trd", ".z80", ".sna", ".dsk", ".rom", ".pok"
    };

    private static readonly HashSet<string> ArchiveExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".rar", ".7z", ".gz", ".tgz", ".tar", ".bz2", ".xz", ".lz"
    };

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path));

    public static bool IsMedia(string path) =>
        MediaExtensions.Contains(Path.GetExtension(path));

    /// <summary>
    /// Returns (virtualName, bytes) for the best Spectrum media entry inside an archive.
    /// Preference: .z80/.sna (instant) then .tap/.tzx/.trd then .rom.
    /// </summary>
    public static (string name, byte[] data) ExtractPreferredMedia(string archivePath)
    {
        var candidates = new List<(string name, byte[] data, int score)>();

        // Prefer built-in ZIP for .zip (faster / no third-party quirks)
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = ZipFile.OpenRead(archivePath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name) || entry.FullName.EndsWith('/'))
                    continue;
                string name = Path.GetFileName(entry.FullName);
                if (!IsMedia(name)) continue;
                using var s = entry.Open();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                candidates.Add((name, ms.ToArray(), Score(name)));
            }
        }
        else
        {
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                string name = Path.GetFileName(entry.Key ?? "");
                if (string.IsNullOrEmpty(name) || !IsMedia(name))
                    continue;
                using var s = entry.OpenEntryStream();
                using var ms = new MemoryStream();
                s.CopyTo(ms);
                candidates.Add((name, ms.ToArray(), Score(name)));
            }
        }

        if (candidates.Count == 0)
            throw new InvalidDataException($"No hay ficheros Spectrum (.tap/.tzx/.trd/.z80/.sna/.rom) dentro de {Path.GetFileName(archivePath)}");

        var best = candidates.OrderByDescending(c => c.score).ThenBy(c => c.name, StringComparer.OrdinalIgnoreCase).First();
        return (best.name, best.data);
    }

    /// <summary>Also opens nested zip-inside-zip one level deep (common in Spectrum archives).</summary>
    public static (string name, byte[] data) ExtractPreferredMediaDeep(string archivePath)
    {
        try
        {
            return ExtractPreferredMedia(archivePath);
        }
        catch (InvalidDataException)
        {
            // try nested zip/rar entries
            using var archive = ArchiveFactory.OpenArchive(archivePath);
            foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
            {
                string name = Path.GetFileName(entry.Key ?? "");
                if (!IsArchive(name)) continue;
                string temp = Path.Combine(Path.GetTempPath(), "me-" + Guid.NewGuid().ToString("N") + name);
                try
                {
                    using (var s = entry.OpenEntryStream())
                    using (var fs = File.Create(temp))
                        s.CopyTo(fs);
                    return ExtractPreferredMedia(temp);
                }
                finally
                {
                    try { File.Delete(temp); } catch { /* ignore */ }
                }
            }
            throw;
        }
    }

    private static int Score(string name)
    {
        // Preferir cintas sanas sobre snapshots (Soft Library mid-load es frecuente).
        string ext = Path.GetExtension(name).ToLowerInvariant();
        return ext switch
        {
            ".tzx" => 100,
            ".tap" => 90,
            ".trd" => 70,
            ".dsk" => 65,
            ".z80" => 50,
            ".sna" => 45,
            ".rom" => 40,
            ".pok" => 10,
            _ => 0
        };
    }
}
