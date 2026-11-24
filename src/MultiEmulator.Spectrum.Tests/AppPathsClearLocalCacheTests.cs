using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

[Collection("AppPathsSerial")]
public class AppPathsClearLocalCacheTests
{
    [Fact]
    public void ClearLocalCache_DeletesDownloadsAndMedia_ButKeepsCatalogAndArchiveIndexes()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-AppPaths-test-" + Guid.NewGuid().ToString("N"));
        AppPaths.SetRootForTests(tempRoot);
        try
        {
            AppPaths.EnsureCreated();

            string catalog = Path.Combine(AppPaths.Root, "catalog-cache.json");
            string tosecTapIndex = Path.Combine(AppPaths.Root, "archiveorg-tosec-tap-index.json");
            string tosecGamesIndex = Path.Combine(AppPaths.Root, "archiveorg-tosec-games-index.json");
            string otherIndex = Path.Combine(AppPaths.Root, "archiveorg-software-library-index.json");
            string recents = Path.Combine(AppPaths.Root, "recent-items.json");
            string download = Path.Combine(AppPaths.Downloads, "ephemeral.zip");
            string media = Path.Combine(AppPaths.Media, "cover.png");
            string rom = Path.Combine(AppPaths.Roms, "48.rom");
            string state = Path.Combine(AppPaths.States, "slot0.sna");

            File.WriteAllText(catalog, "{}");
            File.WriteAllText(tosecTapIndex, "[]");
            File.WriteAllText(tosecGamesIndex, "[]");
            File.WriteAllText(otherIndex, "[]");
            File.WriteAllText(recents, "[]");
            File.WriteAllText(download, "dl");
            File.WriteAllText(media, "img");
            File.WriteAllText(rom, "rom");
            File.WriteAllText(state, "st");

            AppPaths.ClearLocalCache();

            Assert.False(File.Exists(download));
            Assert.False(File.Exists(media));
            Assert.True(File.Exists(catalog));
            Assert.True(File.Exists(tosecTapIndex));
            Assert.True(File.Exists(tosecGamesIndex));
            Assert.True(File.Exists(otherIndex));
            Assert.True(File.Exists(recents));
            Assert.True(File.Exists(rom));
            Assert.True(File.Exists(state));
            Assert.True(Directory.Exists(AppPaths.Downloads));
            Assert.True(Directory.Exists(AppPaths.Media));
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignore cleanup failures
            }
        }
    }
}
