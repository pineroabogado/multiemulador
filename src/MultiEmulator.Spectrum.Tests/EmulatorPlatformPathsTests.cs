using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

[Collection("AppPathsSerial")]
public class EmulatorPlatformPathsTests
{
    [Fact]
    public void PlatformIds_AreStable()
    {
        Assert.Equal("zx-spectrum", EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum));
        Assert.Equal("amstrad-cpc", EmulatorPlatformInfo.Id(EmulatorPlatform.AmstradCpc));
        Assert.Equal("msx", EmulatorPlatformInfo.Id(EmulatorPlatform.Msx));
        Assert.True(EmulatorPlatformInfo.IsCatalogActive(EmulatorPlatform.ZxSpectrum));
        Assert.False(EmulatorPlatformInfo.IsCatalogActive(EmulatorPlatform.AmstradCpc));
        Assert.False(EmulatorPlatformInfo.IsCatalogActive(EmulatorPlatform.Msx));
        Assert.Equal("Spectrum", EmulatorPlatformInfo.LocalAppFolder(EmulatorPlatform.ZxSpectrum));
    }

    [Fact]
    public void TryGetCatalogDir_PrefersPlatformSubfolder()
    {
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            "MULTIEMULADOR-plat-test-" + Guid.NewGuid().ToString("N"));
        string catalogRoot = Path.Combine(tempRoot, "assets", "catalog");
        string zx = Path.Combine(catalogRoot, "zx-spectrum");
        Directory.CreateDirectory(zx);
        File.WriteAllText(Path.Combine(zx, "index.json"), """{"version":1,"platform":"zx-spectrum","entries":[]}""");
        File.WriteAllText(Path.Combine(catalogRoot, "platforms.json"), "{}");

        // Forzar búsqueda desde un cwd bajo el árbol temporal: override absoluto.
        ProjectCatalogPaths.SetCatalogDirForTests(zx);
        try
        {
            string? dir = ProjectCatalogPaths.TryGetCatalogDir(EmulatorPlatform.ZxSpectrum);
            Assert.Equal(Path.GetFullPath(zx), Path.GetFullPath(dir!));
            Assert.Equal(
                Path.Combine(zx, ProjectCatalogPaths.GamesMetadataFileName),
                ProjectCatalogPaths.GamesMetadataPath(EmulatorPlatform.ZxSpectrum));
        }
        finally
        {
            ProjectCatalogPaths.SetCatalogDirForTests(null);
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch { /* ignore */ }
        }
    }

    [Fact]
    public void CatalogEntry_KeyFor_IncludesPlatformPrefix()
    {
        var entry = new CatalogEntry
        {
            Id = "sc-255",
            Title = "Arkanoid",
            ScId = 255,
            Platform = "zx-spectrum"
        };
        Assert.Equal("zx-spectrum:sc:255", CatalogGameMetadataStore.KeyFor(entry));
        Assert.Equal("zx-spectrum:sc:255", CatalogFavoritesStore.KeyFor(entry));
    }

    [Fact]
    public async Task CatalogService_InactivePlatform_ClearsEntries()
    {
        var cat = new CatalogService(platform: EmulatorPlatform.AmstradCpc);
        Assert.Empty(cat.Entries);
        // LoadAsync no debe lanzar ni rellenar.
        await cat.LoadAsync(null, "no-such-file.json");
        Assert.Empty(cat.Entries);
    }

    [Fact]
    public void TryFindSystemRom_ResolvesProjectZxSpectrumRoms()
    {
        AppPaths.SetRootForTests(null);
        AppPaths.SetRomsForTests(null);
        AppPaths.Platform = EmulatorPlatform.ZxSpectrum;

        string? rom48 = AppPaths.TryFindSystemRom("48.rom");
        Assert.NotNull(rom48);
        Assert.True(File.Exists(rom48));

        string full = Path.GetFullPath(rom48);
        // Preferido: …/roms/zx-spectrum/48.rom (proyecto o publish).
        // Legado plano …/roms/48.rom también es válido hasta republish.
        bool underPlatform = full.Contains(
            Path.Combine("roms", "zx-spectrum"),
            StringComparison.OrdinalIgnoreCase);
        bool underFlat = Path.GetFileName(Path.GetDirectoryName(full)!)
            .Equals("roms", StringComparison.OrdinalIgnoreCase);
        Assert.True(underPlatform || underFlat, $"Unexpected ROM path: {full}");

        string romsDir = Path.GetFullPath(AppPaths.Roms);
        Assert.True(
            File.Exists(Path.Combine(romsDir, "48.rom"))
            || underPlatform,
            $"AppPaths.Roms={romsDir} should resolve system ROMs");
    }
}
