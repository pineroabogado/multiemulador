using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class LocalJuegosMatcherTests
{
    [Theory]
    [InlineData("FlyingShark.tzx.zip", "FlyingShark")]
    [InlineData("1942.tap.zip", "1942")]
    [InlineData("Arkanoid.tap", "Arkanoid")]
    [InlineData("ManicMiner.z80.zip", "ManicMiner")]
    [InlineData("SuperTetris2.trd.zip", "SuperTetris2")]
    [InlineData("JetSetWilly-(again).tzx.zip", "JetSetWilly-(again)")]
    public void StripKnownExtension_RemovesCompoundSuffixes(string fileName, string expected)
    {
        Assert.Equal(expected, LocalJuegosMatcher.StripKnownExtension(fileName));
    }

    [Theory]
    [InlineData("Flying Shark", "flyingshark")]
    [InlineData("FlyingShark", "flyingshark")]
    [InlineData("1942", "1942")]
    [InlineData("Jet-Set_Willy", "jetsetwilly")]
    public void NormalizeKey_KeepsAlphanumericOnly(string input, string expected)
    {
        Assert.Equal(expected, LocalJuegosMatcher.NormalizeKey(input));
    }

    [Fact]
    public void EnumerateMatchStems_IncludesTitleScFileAndSlug()
    {
        var entry = new CatalogEntry
        {
            Title = "Flying Shark",
            ScFile = "FlyingShark",
            ScSlug = "Flying_Shark",
        };

        var stems = LocalJuegosMatcher.EnumerateMatchStems(entry);
        Assert.Contains("flyingshark", stems);
    }

    [Theory]
    [InlineData("FlyingShark.tzx.zip", "flyingshark", 100)]
    [InlineData("1942.tap.zip", "1942", 100)]
    [InlineData("Arkanoid.tap.zip", "arkanoid", 100)]
    [InlineData("OtherGame.tap.zip", "flyingshark", 0)]
    public void ScoreFileNameAgainstStems_ExactAndMiss(string file, string stem, int expected)
    {
        Assert.Equal(expected, LocalJuegosMatcher.ScoreFileNameAgainstStems(file, [stem]));
    }

    [Fact]
    public void TryFindLocalPath_PrefersTzxOverTap_WhenAuto()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-juegos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "FlyingShark.tap.zip"), "tap");
            File.WriteAllText(Path.Combine(dir, "FlyingShark.tzx.zip"), "tzx");

            var entry = new CatalogEntry { Title = "Flying Shark", ScFile = "FlyingShark" };
            string? path = LocalJuegosMatcher.TryFindLocalPath(entry, dir, RomMediaFormat.Auto);
            Assert.NotNull(path);
            Assert.Equal("FlyingShark.tzx.zip", Path.GetFileName(path), ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryFindLocalPath_RespectsForcedTap()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-juegos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "FlyingShark.tap.zip"), "tap");
            File.WriteAllText(Path.Combine(dir, "FlyingShark.tzx.zip"), "tzx");

            var entry = new CatalogEntry { Title = "Flying Shark", ScFile = "FlyingShark" };
            string? path = LocalJuegosMatcher.TryFindLocalPath(entry, dir, RomMediaFormat.Tap);
            Assert.NotNull(path);
            Assert.Equal("FlyingShark.tap.zip", Path.GetFileName(path), ignoreCase: true);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryFindLocalPath_Matches1942CaseInsensitive()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-juegos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "1942.TAP.ZIP"), "x");

            var entry = new CatalogEntry { Title = "1942", ScFile = "1942" };
            string? path = LocalJuegosMatcher.TryFindLocalPath(entry, dir);
            Assert.NotNull(path);
            Assert.Contains("1942", Path.GetFileName(path!), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryFindLocalPath_ReturnsNullWhenNoMatch()
    {
        string dir = Path.Combine(Path.GetTempPath(), "MULTIEMULADOR-juegos-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Arkanoid.tap.zip"), "x");
            var entry = new CatalogEntry { Title = "Manic Miner", ScFile = "ManicMiner" };
            Assert.Null(LocalJuegosMatcher.TryFindLocalPath(entry, dir));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void TryFindJuegosDirectory_FindsProjectFolder()
    {
        string? dir = AppPaths.TryFindJuegosDirectory();
        // En el árbol del repo debe existir JUEGOS/ con algún medio.
        Assert.NotNull(dir);
        Assert.True(Directory.Exists(dir));
        Assert.Equal("JUEGOS", Path.GetFileName(dir!.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
            ignoreCase: true);
    }
}
