using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

/// <summary>
/// Resuelve ROMs de sistema desde el árbol del proyecto / output de tests,
/// no desde LocalAppData (salvo legado como último recurso).
/// </summary>
internal static class TestRomPaths
{
    public static string? Find48() => Find("48.rom");

    public static string? Find(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        foreach (string candidate in EnumerateCandidates(fileName))
        {
            try
            {
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            catch
            {
                // siguiente
            }
        }

        return null;
    }

    public static bool TryRead48(out byte[] rom)
    {
        rom = Array.Empty<byte>();
        string? path = Find48();
        if (path == null)
            return false;
        rom = File.ReadAllBytes(path);
        return rom.Length >= 0x4000;
    }

    private static IEnumerable<string> EnumerateCandidates(string fileName)
    {
        string platformId = EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum);

        foreach (string root in EnumerateSearchRoots())
        {
            yield return Path.Combine(root, "roms", platformId, fileName);
            yield return Path.Combine(root, "roms", fileName);
        }

        // Último recurso: caché LocalAppData histórica.
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MULTIEMULADOR",
            EmulatorPlatformInfo.LocalAppFolder(EmulatorPlatform.ZxSpectrum),
            "roms",
            fileName);
    }

    private static IEnumerable<string> EnumerateSearchRoots()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string? start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
        {
            if (string.IsNullOrWhiteSpace(start))
                continue;

            string? dir;
            try { dir = Path.GetFullPath(start); }
            catch { continue; }

            for (int i = 0; i < 12 && !string.IsNullOrEmpty(dir); i++)
            {
                if (seen.Add(dir))
                    yield return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }
    }
}
