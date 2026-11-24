using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

[CollectionDefinition("AppPathsSerial", DisableParallelization = true)]
public class AppPathsSerialCollection;

[Collection("AppPathsSerial")]
public class CatalogGameMetadataStoreTests
{
    [Fact]
    public void SaveAndPrefer_PersistsWorkingUrl_OutsideLocalAppData()
    {
        string tempCatalog = Path.Combine(
            Path.GetTempPath(),
            "MULTIEMULADOR-meta-test-" + Guid.NewGuid().ToString("N"),
            "assets",
            "catalog");
        Directory.CreateDirectory(tempCatalog);
        ProjectCatalogPaths.SetCatalogDirForTests(tempCatalog);

        string tempRoot = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-meta-app-" + Guid.NewGuid().ToString("N"));
        AppPaths.SetRootForTests(tempRoot);

        try
        {
            CatalogGameMetadataStore.Reload();

            var entry = new CatalogEntry
            {
                Id = "sc-255",
                Title = "Arkanoid",
                ScId = 255,
                ScSlug = "Arkanoid",
                ScFile = "Arkanoid",
                EntryUrl = "https://spectrumcomputing.co.uk/entry/255/Arkanoid",
                Url = "https://archive.org/download/zx_Arkanoid/Arkanoid.z80",
                Format = ".z80"
            };

            CatalogGameMetadataStore.SaveFromSuccessfulPlay(entry, new CatalogGameMediaUrls
            {
                LoadScreenUrl = "https://example.com/load.scr",
                CoverUrl = "https://example.com/cover.jpg",
                AdditionalMediaUrls = ["https://example.com/extra.gif"]
            });

            string metaPath = Path.Combine(tempCatalog, ProjectCatalogPaths.GamesMetadataFileName);
            Assert.True(File.Exists(metaPath));
            Assert.DoesNotContain(AppPaths.Root, metaPath, StringComparison.OrdinalIgnoreCase);

            // ClearLocalCache no debe tocar el JSON del proyecto.
            AppPaths.EnsureCreated();
            File.WriteAllText(Path.Combine(AppPaths.Downloads, "ephemeral.zip"), "x");
            AppPaths.ClearLocalCache();
            Assert.True(File.Exists(metaPath));

            CatalogGameMetadataStore.Reload();
            var loaded = CatalogGameMetadataStore.TryGet(entry);
            Assert.NotNull(loaded);
            Assert.Equal("https://archive.org/download/zx_Arkanoid/Arkanoid.z80", loaded!.WorkingDownloadUrl);
            Assert.Equal(".z80", loaded.Format);
            Assert.Equal(255, loaded.ScId);
            Assert.Equal("https://example.com/load.scr", loaded.LoadScreenUrl);
            Assert.Equal("https://example.com/cover.jpg", loaded.CoverUrl);
            Assert.Contains("https://example.com/extra.gif", loaded.AdditionalMediaUrls);

            var fresh = new CatalogEntry { Id = "sc-255", Title = "Arkanoid", ScId = 255 };
            var preferred = CatalogGameMetadataStore.GetPreferredDownloadUrls(fresh);
            Assert.Contains(preferred, u => u.Contains("zx_Arkanoid", StringComparison.OrdinalIgnoreCase));

            // TOSEC LadyEklipse: persiste ruta relativa Games/[TAP]/…
            var tosecEntry = new CatalogEntry
            {
                Id = "sc-9999",
                Title = "Manic Miner",
                ScId = 9999,
                Url = ArchiveOrgTosecTap.BuildFileUrl("Manic Miner (1983)(Bug-Byte).zip"),
                Format = ".zip"
            };
            CatalogGameMetadataStore.SaveFromSuccessfulPlay(tosecEntry);
            CatalogGameMetadataStore.Reload();
            var tosecMeta = CatalogGameMetadataStore.TryGet(tosecEntry);
            Assert.NotNull(tosecMeta);
            Assert.Equal("tosec", tosecMeta!.DownloadSource);
            Assert.Equal("Games/[TAP]/Manic Miner (1983)(Bug-Byte).zip", tosecMeta.TosecFileName);
        }
        finally
        {
            CatalogGameMetadataStore.Reload();
            ProjectCatalogPaths.SetCatalogDirForTests(null);
            AppPaths.SetRootForTests(null);
            try
            {
                string root = Path.GetDirectoryName(Path.GetDirectoryName(tempCatalog))!;
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch { /* ignore */ }
            try
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, recursive: true);
            }
            catch { /* ignore */ }
        }
    }
}
