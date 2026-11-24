using System.IO.Compression;
using MultiEmulator.Spectrum.Disk;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Tests;

public class TrdLoadTests
{
    private static readonly string SuperTetrisZip =
        @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\SuperTetris2.trd.zip";

    [Fact]
    public void TrdImage_ParsesSuperTetrisCatalog()
    {
        if (!File.Exists(SuperTetrisZip))
            return;

        var (name, data) = ArchiveMedia.ExtractPreferredMedia(SuperTetrisZip);
        Assert.EndsWith(".trd", name, StringComparison.OrdinalIgnoreCase);

        var trd = TrdImage.Load(data);
        Assert.Equal(2, trd.Files.Count);
        Assert.Contains(trd.Files, f => f.Name.Equals("boot", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(trd.Files, f => f.Name.StartsWith("S.TETR", StringComparison.OrdinalIgnoreCase));

        var entry = trd.ResolveAutoRunEntry();
        Assert.StartsWith("S.TETR", entry.Name, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TrdFileType.Basic, entry.Type);

        var raw = trd.ReadFile(entry);
        Assert.Equal(entry.SectorCount * 256, raw.Length);
        Assert.True(TrDosLoader.TryFindRandomizeUsr(raw.AsSpan(0, Math.Min(64, raw.Length)), out ushort usr));
        Assert.Equal(0x5D48, usr);
    }

    [Fact]
    public void ArchiveMedia_PrefersTrdInsideZip()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "me-trd-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var e = zip.CreateEntry("game.trd");
                using var s = e.Open();
                s.Write(new byte[9 * 256]);
            }

            var (name, _) = ArchiveMedia.ExtractPreferredMedia(zipPath);
            Assert.Equal("game.trd", name);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void MediaLoader_OpensSuperTetrisTrdZip_LoadsAutorunIntoMemory()
    {
        if (!File.Exists(SuperTetrisZip))
            return;

        if (!TestRomPaths.TryRead48(out var rom))
            return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        MediaLoader.OpenPath(m, SuperTetrisZip);

        Assert.NotNull(m.TrDos.Disk);
        Assert.Equal("S.TETR 2", m.TrDos.LastLoadedName);
        Assert.True(m.Beta.HasDisk);

        Assert.Equal(0x00, m.Memory.Read(0x5CCB));
        Assert.Equal(0x01, m.Memory.Read(0x5CCC));
        Assert.Equal(0xF9, m.Memory.Read(0x5CCF));
    }

    [Fact]
    public void BetaDisk_ReadsCatalogSector()
    {
        if (!File.Exists(SuperTetrisZip))
            return;

        var (_, data) = ArchiveMedia.ExtractPreferredMedia(SuperTetrisZip);
        var beta = new BetaDisk();
        beta.Enabled = true;
        beta.Insert(data);
        beta.ForcePaged(true);
        beta.WritePort(0xFF, 0x00);
        beta.WritePort(0x3F, 0);
        beta.WritePort(0x5F, 1);
        beta.WritePort(0x1F, 0x80);

        Assert.True(beta.SectorReadCount == 1, beta.LastIo);
        Assert.Equal((byte)'S', beta.ReadPort(0x7F));
    }
}
