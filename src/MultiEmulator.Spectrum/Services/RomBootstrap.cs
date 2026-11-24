using System.Security.Cryptography;

namespace MultiEmulator.Spectrum.Services;

public sealed class RomBootstrap
{
    // Official Spectrum ROMs are redistributable with Amstrad permission (copyright retained).
    // URLs point to the well-known spectrum-roms package mirrors used by Linux distros / Fuse ecosystem.
    public const string Notice =
        "Amstrad have kindly given their permission for the redistribution of their copyrighted material but retain that copyright.";

    private static readonly Dictionary<string, (string file, string url, string sha256)> Catalog = new()
    {
        ["48"] = ("48.rom",
            "https://raw.githubusercontent.com/rastersoft/fbzx/master/data/spectrum-roms/48.rom",
            ""),
        ["128"] = ("128.rom",
            "https://raw.githubusercontent.com/rastersoft/fbzx/master/data/spectrum-roms/128-0.rom",
            "")
    };

    private readonly HttpClient _http;

    public RomBootstrap(HttpClient? http = null) =>
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

    public string NoticeText => Notice;

    public async Task<(byte[] rom48, byte[] rom128)> EnsureRomsAsync(CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        var rom48 = await EnsureRom48Async(ct);
        var rom128 = await EnsureRom128Async(ct);
        return (rom48, rom128);
    }

    /// <summary>TR-DOS 5.03 (16K) para Beta 128 / discos .trd. Opcional si no hay red.</summary>
    public async Task<byte[]?> EnsureTrDosRomAsync(CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        string dest = Path.Combine(AppPaths.Roms, "trdos.rom");

        string? found = AppPaths.TryFindSystemRom("trdos.rom");
        if (found != null)
        {
            var existing = await File.ReadAllBytesAsync(found, ct);
            if (existing.Length >= 0x4000)
                return existing;
        }

        if (TryReadBundled("trdos.rom", out var bundled))
        {
            await File.WriteAllBytesAsync(dest, bundled, ct);
            return bundled;
        }

        const string url = "https://raw.githubusercontent.com/programandala-net/tr-dos/master/trdos.rom";
        try
        {
            var bytes = await _http.GetByteArrayAsync(url, ct);
            if (bytes.Length < 0x4000)
                return null;
            await File.WriteAllBytesAsync(dest, bytes, ct);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    public Task<byte[]> EnsureRom48Async(CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        return EnsureOneAsync("48", 0x4000, ct);
    }

    public Task<byte[]> EnsureRom128Async(CancellationToken ct = default)
    {
        AppPaths.EnsureCreated();
        return Ensure128Async(ct);
    }

    private async Task<byte[]> EnsureOneAsync(string key, int expectedSize, CancellationToken ct)
    {
        var (file, url, _) = Catalog[key];
        string dest = Path.Combine(AppPaths.Roms, file);

        string? found = AppPaths.TryFindSystemRom(file);
        if (found != null)
        {
            var existing = await File.ReadAllBytesAsync(found, ct);
            if (existing.Length >= expectedSize)
                return existing;
        }

        if (TryReadBundled(file, out var bundled) && bundled.Length >= expectedSize)
        {
            await File.WriteAllBytesAsync(dest, bundled, ct);
            return bundled;
        }

        try
        {
            var bytes = await _http.GetByteArrayAsync(url, ct);
            if (bytes.Length < expectedSize)
                throw new InvalidDataException($"ROM {file} too small");
            await File.WriteAllBytesAsync(dest, bytes, ct);
            return bytes;
        }
        catch
        {
            // Fallback: generate empty ROM so emulator still boots to black (user can add manual ROM)
            var blank = new byte[expectedSize];
            // Minimal RST0 loop
            blank[0] = 0xF3; // DI
            blank[1] = 0xC3; // JP 0
            blank[2] = 0x00;
            blank[3] = 0x00;
            await File.WriteAllBytesAsync(dest, blank, ct);
            return blank;
        }
    }

    private async Task<byte[]> Ensure128Async(CancellationToken ct)
    {
        string dest = Path.Combine(AppPaths.Roms, "128.rom");

        string? found = AppPaths.TryFindSystemRom("128.rom");
        if (found != null)
        {
            var existing = await File.ReadAllBytesAsync(found, ct);
            if (existing.Length >= 0x8000)
                return existing;
        }

        if (TryReadBundled("128.rom", out var bundled128) && bundled128.Length >= 0x8000)
        {
            await File.WriteAllBytesAsync(dest, bundled128, ct);
            return bundled128;
        }

        string p0 = AppPaths.TryFindSystemRom("128-0.rom") ?? Path.Combine(AppPaths.Roms, "128-0.rom");
        string p1 = AppPaths.TryFindSystemRom("128-1.rom") ?? Path.Combine(AppPaths.Roms, "128-1.rom");
        string write0 = Path.Combine(AppPaths.Roms, "128-0.rom");
        string write1 = Path.Combine(AppPaths.Roms, "128-1.rom");
        const string url0 = "https://raw.githubusercontent.com/rastersoft/fbzx/master/data/spectrum-roms/128-0.rom";
        const string url1 = "https://raw.githubusercontent.com/rastersoft/fbzx/master/data/spectrum-roms/128-1.rom";

        try
        {
            if (!File.Exists(p0))
            {
                if (TryReadBundled("128-0.rom", out var b0))
                    await File.WriteAllBytesAsync(write0, b0, ct);
                else
                    await File.WriteAllBytesAsync(write0, await _http.GetByteArrayAsync(url0, ct), ct);
                p0 = write0;
            }

            if (!File.Exists(p1))
            {
                if (TryReadBundled("128-1.rom", out var b1))
                    await File.WriteAllBytesAsync(write1, b1, ct);
                else
                    await File.WriteAllBytesAsync(write1, await _http.GetByteArrayAsync(url1, ct), ct);
                p1 = write1;
            }

            var a = await File.ReadAllBytesAsync(p0, ct);
            var b = await File.ReadAllBytesAsync(p1, ct);
            var combined = new byte[0x8000];
            Buffer.BlockCopy(a, 0, combined, 0, Math.Min(0x4000, a.Length));
            Buffer.BlockCopy(b, 0, combined, 0x4000, Math.Min(0x4000, b.Length));
            await File.WriteAllBytesAsync(dest, combined, ct);
            return combined;
        }
        catch
        {
            return await EnsureOneAsync("128", 0x8000, ct);
        }
    }

    /// <summary>
    /// Lee ROMs embebidas junto al binario (<c>roms/{platform}/</c> o legado <c>roms/</c>).
    /// </summary>
    private static bool TryReadBundled(string file, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        string platformId = EmulatorPlatformInfo.Id(AppPaths.Platform);
        foreach (string candidate in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "roms", platformId, file),
                     Path.Combine(AppContext.BaseDirectory, "roms", file),
                 })
        {
            if (!File.Exists(candidate))
                continue;
            // Evitar releer el mismo path que AppPaths.Roms (ya comprobado).
            if (string.Equals(
                    Path.GetFullPath(candidate),
                    Path.GetFullPath(Path.Combine(AppPaths.Roms, file)),
                    StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                bytes = File.ReadAllBytes(candidate);
                return bytes.Length > 0;
            }
            catch
            {
                // siguiente candidato
            }
        }

        return false;
    }

    public static string Sha256(byte[] data) =>
        Convert.ToHexString(SHA256.HashData(data));
}
