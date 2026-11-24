using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class RomDownloadSourceTests
{
    [Theory]
    [InlineData("https://spectrumcomputing.co.uk/pub/sinclair/games/a/Arkanoid.tap.zip", RomDownloadSource.SpectrumComputing)]
    [InlineData("https://zxdb.spectrumcomputing.co.uk/something.zip", RomDownloadSource.SpectrumComputing)]
    [InlineData("https://archive.org/download/zx_Arkanoid_1987_Imagine/Arkanoid_1987_Imagine.z80", RomDownloadSource.ArchiveOrgSoftwareLibrary)]
    [InlineData("https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/Arkanoid%20(1987)(Imagine).zip", RomDownloadSource.ArchiveOrgTosec)]
    public void TryDetectFromUrl_ClassifiesKnownHosts(string url, RomDownloadSource expected)
    {
        Assert.Equal(expected, RomDownloadSourceInfo.TryDetectFromUrl(url));
        Assert.True(RomDownloadSourceInfo.UrlMatches(expected, url));
    }

    [Fact]
    public void CatalogSettingsStore_PersistsRomDownloadSource()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-rom-src-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            CatalogSettingsStore.SaveRomDownloadSource(RomDownloadSource.ArchiveOrgTosec);
            var loaded = CatalogSettingsStore.Load();
            Assert.Equal(RomDownloadSource.ArchiveOrgTosec, loaded.RomDownloadSource);
            Assert.True(File.Exists(CatalogSettingsStore.FilePath));
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ClearLocalCache_PreservesCatalogSettings()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-rom-src-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            AppPaths.EnsureCreated();
            CatalogSettingsStore.SaveRomDownloadSource(RomDownloadSource.ArchiveOrgSoftwareLibrary);
            File.WriteAllText(Path.Combine(AppPaths.Downloads, "dummy.zip"), "x");

            AppPaths.ClearLocalCache();

            Assert.Equal(
                RomDownloadSource.ArchiveOrgSoftwareLibrary,
                CatalogSettingsStore.Load().RomDownloadSource);
            Assert.False(File.Exists(Path.Combine(AppPaths.Downloads, "dummy.zip")));
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }
}
