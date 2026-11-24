using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class SpectrumComputingPrefixShardTests
{
    [Fact]
    public void TryLoadPrefixShards_ReadsLetterA_Export()
    {
        string? dir = ProjectCatalogPaths.TryGetCatalogDir(EmulatorPlatform.ZxSpectrum);
        if (dir == null || !File.Exists(Path.Combine(dir, "sc-games-prefix-a.json")))
            return;

        var index = SpectrumComputingListScraper.TryLoadPrefixShards(dir);
        Assert.NotNull(index);
        Assert.True(index!.Entries.Count >= 900, $"expected ~1000 from prefix a, got {index.Entries.Count}");
        Assert.Contains(index.Entries, e => e.ScId == 255 || e.Title.Contains("Arkanoid", StringComparison.OrdinalIgnoreCase));
    }
}
