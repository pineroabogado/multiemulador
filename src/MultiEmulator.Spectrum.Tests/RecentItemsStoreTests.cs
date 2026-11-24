using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

[Collection("AppPathsSerial")]
public class RecentItemsStoreTests
{
    [Fact]
    public void AddLocal_PersistsNewestFirst_AndDedups()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-recents-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            AppPaths.EnsureCreated();

            string a = Path.Combine(temp, "a.tap");
            string b = Path.Combine(temp, "b.tap");
            File.WriteAllText(a, "a");
            File.WriteAllText(b, "b");

            var store = new RecentItemsStore();
            store.AddLocal(a);
            store.AddLocal(b);
            store.AddLocal(a);

            var snap = store.Snapshot();
            Assert.Equal(2, snap.Count);
            Assert.Equal(RecentKind.Local, snap[0].Kind);
            Assert.Equal(Path.GetFullPath(a), Path.GetFullPath(snap[0].LocalPath!));
            Assert.Equal(Path.GetFullPath(b), Path.GetFullPath(snap[1].LocalPath!));
            Assert.True(File.Exists(store.FilePath));

            var reload = new RecentItemsStore();
            reload.Load();
            Assert.Equal(2, reload.Count);
            Assert.Equal(snap[0].DedupKey(), reload.Snapshot()[0].DedupKey());
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void AddCatalog_StoresSourceAndUrl_EnoughToReload()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-recents-cat-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            var store = new RecentItemsStore();
            var entry = new CatalogEntry
            {
                Id = "arkanoid",
                Title = "Arkanoid",
                Platform = EmulatorPlatformInfo.Id(EmulatorPlatform.ZxSpectrum),
                ScId = 255,
                Url = "https://spectrumcomputing.co.uk/pub/sinclair/games/a/Arkanoid.tap.zip",
                Format = ".tap.zip",
                ScFile = "Arkanoid",
                ScSlug = "Arkanoid",
            };

            store.AddCatalog(entry, RomDownloadSource.SpectrumComputing);
            var item = store.Snapshot().Single();
            Assert.Equal(RecentKind.Catalog, item.Kind);
            Assert.Equal(255, item.ScId);
            Assert.Equal(RomDownloadSource.SpectrumComputing, item.RomDownloadSource);
            Assert.Equal(entry.Url, item.Url);

            var rebuilt = item.ToCatalogEntry();
            Assert.Equal("arkanoid", rebuilt.Id);
            Assert.Equal("Arkanoid", rebuilt.Title);
            Assert.Equal(255, rebuilt.ScId);
            Assert.Equal(entry.Url, rebuilt.Url);
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void ClearLocalCache_PreservesRecentItemsFile()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-recents-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            AppPaths.EnsureCreated();

            var store = new RecentItemsStore();
            store.AddLocal(Path.Combine(temp, "game.tap"));
            File.WriteAllText(Path.Combine(AppPaths.Downloads, "ephemeral.zip"), "x");

            AppPaths.ClearLocalCache();

            Assert.True(File.Exists(store.FilePath));
            Assert.False(File.Exists(Path.Combine(AppPaths.Downloads, "ephemeral.zip")));

            var reload = new RecentItemsStore();
            reload.Load();
            Assert.Equal(1, reload.Count);
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void MaxItems_TrimsOldest()
    {
        string temp = Path.Combine(Path.GetTempPath(), "me-recents-max-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            AppPaths.SetRootForTests(temp);
            var store = new RecentItemsStore();
            for (int i = 0; i < RecentItemsStore.MaxItems + 5; i++)
                store.AddLocal(Path.Combine(temp, $"g{i}.tap"));

            Assert.Equal(RecentItemsStore.MaxItems, store.Count);
            Assert.Contains("g" + (RecentItemsStore.MaxItems + 4), store.Snapshot()[0].DisplayTitle);
        }
        finally
        {
            AppPaths.SetRootForTests(null);
            try { Directory.Delete(temp, recursive: true); } catch { /* ignore */ }
        }
    }
}
