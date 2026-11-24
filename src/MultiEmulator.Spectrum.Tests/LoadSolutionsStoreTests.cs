using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

[Collection("AppPathsSerial")]
public class LoadSolutionsStoreTests
{
    [Fact]
    public void SeededSolutions_ResolveByScId_AndPreferFormat()
    {
        // Usa el JSON del proyecto (assets/catalog/zx-spectrum/load-solutions.json).
        ProjectCatalogPaths.SetCatalogDirForTests(null);
        LoadSolutionsStore.Reload(EmulatorPlatform.ZxSpectrum);

        var elite = new CatalogEntry { Title = "1942", ScId = 9297, ScFile = "1942" };
        var hint1942 = LoadSolutionsStore.Resolve(elite);
        Assert.NotNull(hint1942.Solution);
        Assert.Equal("tap", hint1942.Solution!.PreferredFormat, ignoreCase: true);
        Assert.Contains(hint1942.Solution.Strategies, s => LoadSolutionTags.IsInstantFlashIff(s));
        Assert.Contains(hint1942.Solution.Strategies, s => LoadSolutionTags.IsDeferMidLoadZ80(s));
        Assert.Equal(
            RomMediaFormat.Tap,
            LoadSolutionsStore.ApplyPreferredFormat(RomMediaFormat.Auto, hint1942));

        var shark = new CatalogEntry { Title = "Flying Shark", ScId = 1813, ScFile = "FlyingShark" };
        var hintShark = LoadSolutionsStore.Resolve(shark);
        Assert.NotNull(hintShark.Solution);
        Assert.Equal("tzx", hintShark.Solution!.PreferredFormat, ignoreCase: true);
        Assert.Contains(hintShark.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.PreferTzx, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hintShark.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.FastLoadWarp, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            RomMediaFormat.Tzx,
            LoadSolutionsStore.ApplyPreferredFormat(RomMediaFormat.Auto, hintShark));

        var ark = new CatalogEntry { Title = "Arkanoid", ScId = 255, ScFile = "Arkanoid" };
        var hintArk = LoadSolutionsStore.Resolve(ark);
        Assert.NotNull(hintArk.Solution);
        Assert.Equal("tap", hintArk.Solution!.PreferredFormat, ignoreCase: true);
        Assert.Contains(hintArk.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.ForceTap, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hintArk.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.FloatingBus, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hintArk.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.FloatingBusVsyncLatch, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(hintArk.Solution.Strategies, s =>
            string.Equals(s, LoadSolutionTags.HealthRetrySibling, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            RomMediaFormat.Tap,
            LoadSolutionsStore.ApplyPreferredFormat(RomMediaFormat.Auto, hintArk));
    }

    [Fact]
    public void Resolve_ByPathStem_FindsFlyingShark()
    {
        ProjectCatalogPaths.SetCatalogDirForTests(null);
        LoadSolutionsStore.Reload(EmulatorPlatform.ZxSpectrum);

        string path = Path.Combine(
            Path.GetTempPath(),
            "FlyingShark.tzx.zip");
        var hint = LoadSolutionsStore.Resolve(path: path);
        Assert.NotNull(hint.Solution);
        Assert.Equal(1813, hint.Solution!.ScId);
    }

    [Fact]
    public void RecordSuccessfulPlay_UpsertsAndSurvivesReload()
    {
        string tempCatalog = Path.Combine(
            Path.GetTempPath(),
            "MULTIEMULADOR-sol-test-" + Guid.NewGuid().ToString("N"),
            "assets",
            "catalog");
        Directory.CreateDirectory(tempCatalog);
        // Semilla mínima
        File.WriteAllText(
            Path.Combine(tempCatalog, ProjectCatalogPaths.LoadSolutionsFileName),
            """{"version":1,"patterns":[],"solutions":{}}""");

        ProjectCatalogPaths.SetCatalogDirForTests(tempCatalog);
        string tempRoot = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-sol-app-" + Guid.NewGuid().ToString("N"));
        AppPaths.SetRootForTests(tempRoot);

        try
        {
            LoadSolutionsStore.Reload();

            var entry = new CatalogEntry
            {
                Id = "sc-999",
                Title = "Test Game",
                ScId = 999,
                ScFile = "TestGame"
            };

            string fakeTap = Path.Combine(tempCatalog, "TestGame.tap.zip");
            File.WriteAllText(fakeTap, "x");

            LoadSolutionsStore.RecordSuccessfulPlay(
                entry,
                fakeTap,
                LoadStrategyKind.ForceTap,
                preferredSource: "juegos");

            LoadSolutionsStore.Reload();
            var loaded = LoadSolutionsStore.TryGet(entry);
            Assert.NotNull(loaded);
            Assert.Equal("tap", loaded!.PreferredFormat, ignoreCase: true);
            Assert.Equal("TestGame.tap.zip", loaded.LocalFileHint);
            Assert.True(loaded.SuccessCount >= 1);
            Assert.Contains(loaded.Strategies, s =>
                string.Equals(s, LoadSolutionTags.ForceTap, StringComparison.OrdinalIgnoreCase));

            string solPath = Path.Combine(tempCatalog, ProjectCatalogPaths.LoadSolutionsFileName);
            Assert.True(File.Exists(solPath));
            AppPaths.EnsureCreated();
            File.WriteAllText(Path.Combine(AppPaths.Downloads, "ephemeral.zip"), "x");
            AppPaths.ClearLocalCache();
            Assert.True(File.Exists(solPath), "load-solutions debe sobrevivir ClearLocalCache");
        }
        finally
        {
            LoadSolutionsStore.Reload();
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

    [Fact]
    public void Ranker_AppliesSolutionLocalFileHint_First()
    {
        string dir = Path.Combine(Path.GetTempPath(), "me-sol-rank-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string tap = Path.Combine(dir, "Demo.tap.zip");
            string tzx = Path.Combine(dir, "Demo.tzx.zip");
            // Cabeceras mínimas inválidas: el ranker tolera inspect fallido.
            File.WriteAllBytes(tap, [0x13, 0x00, 0x00]);
            File.WriteAllBytes(tzx, [0x13, 0x00, 0x00]);

            var hint = new LoadSolutionHint
            {
                Solution = new LoadSolution
                {
                    Title = "Demo",
                    PreferredFormat = "tzx",
                    LocalFileHint = "Demo.tzx.zip",
                    Strategies = [LoadSolutionTags.PreferTzx]
                },
                ResolvedKey = "zx-spectrum:title:demo"
            };

            var attempts = LoadCandidateRanker.BuildAttempts(
                tap, RomMediaFormat.Tzx, extraPaths: [tzx], inspect: false, solutionHint: hint);
            Assert.NotEmpty(attempts);
            Assert.True(
                attempts[0].Path.EndsWith("Demo.tzx.zip", StringComparison.OrdinalIgnoreCase),
                $"top={attempts[0].Path} score={attempts[0].Score}");
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void GreenBeret_AndGalen_AreDocumentedInRegistry()
    {
        ProjectCatalogPaths.SetCatalogDirForTests(null);
        LoadSolutionsStore.Reload(EmulatorPlatform.ZxSpectrum);

        var gb = LoadSolutionsStore.Resolve(new CatalogEntry { Title = "Green Beret", ScFile = "GreenBeret" });
        Assert.NotNull(gb.Solution);
        Assert.Contains(gb.Solution!.Strategies, s =>
            string.Equals(s, LoadSolutionTags.UlaIrqContentionGeneral, StringComparison.OrdinalIgnoreCase));

        var galen = LoadSolutionsStore.Resolve(new CatalogEntry { Title = "Galen", ScFile = "Galen" });
        Assert.NotNull(galen.Solution);
        Assert.Contains(galen.Solution!.Strategies, s =>
            string.Equals(s, LoadSolutionTags.MediaOnly, StringComparison.OrdinalIgnoreCase));
    }
}
