using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class SpectrumComputingCatalogTests
{
    [Theory]
    [InlineData("Arkanoid", "https://spectrumcomputing.co.uk/pub/sinclair/games/a/Arkanoid.tap.zip")]
    [InlineData("GreenBeret", "https://spectrumcomputing.co.uk/pub/sinclair/games/g/GreenBeret.tap.zip")]
    [InlineData("Green Beret", "https://spectrumcomputing.co.uk/pub/sinclair/games/g/GreenBeret.tap.zip")]
    public void BuildTapZipUrl_MatchesSpectrumComputingTemplate(string title, string expected)
    {
        string stem = SpectrumComputingUrls.ToFileStem(title);
        Assert.Equal(expected, SpectrumComputingUrls.BuildTapZipUrl(stem));
        Assert.Equal(expected, SpectrumComputingUrls.BuildTapZipUrlFromTitle(title));
    }

    [Fact]
    public void NumericStem_Uses123Folder_AndTzxCandidate()
    {
        Assert.Equal("123", SpectrumComputingUrls.FolderPrefix("07ZglosSie"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/123/07ZglosSie.tap.zip",
            SpectrumComputingUrls.BuildTapZipUrl("07ZglosSie"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/123/07ZglosSie.tzx.zip",
            SpectrumComputingUrls.BuildPubGameZipUrl("07ZglosSie", ".tzx.zip"));

        var urls = SpectrumComputingUrls.EnumerateGameZipUrls("07ZglosSie");
        Assert.Contains(urls, u => u.EndsWith("/123/07ZglosSie.tap.zip"));
        Assert.Contains(urls, u => u.EndsWith("/123/07ZglosSie.tzx.zip"));
        Assert.Contains(urls, u => u.EndsWith("/0-9/07ZglosSie.tzx.zip"));
        Assert.Equal(new[] { "123", "0-9" }, SpectrumComputingUrls.EnumerateFolderPrefixes("1942"));
    }

    [Theory]
    [InlineData("ManicMiner", "m", ".tzx.zip")]
    [InlineData("StreetHawk", "s", ".tzx.zip")]
    [InlineData("Saboteur", "s", ".tzx.zip")]
    [InlineData("AbadiaDelCrimenLa", "a", ".tzx.zip")]
    public void BuildPubGameZipUrl_LetterFolder_MatchesScDoc(string stem, string folder, string ext)
    {
        Assert.Equal(folder, SpectrumComputingUrls.FolderPrefix(stem));
        Assert.Equal(
            $"https://spectrumcomputing.co.uk/pub/sinclair/games/{folder}/{stem}{ext}",
            SpectrumComputingUrls.BuildPubGameZipUrl(stem, ext));
    }

    [Fact]
    public void BuildPubGameZipUrl_1942_Includes123And0_9()
    {
        Assert.Equal("123", SpectrumComputingUrls.FolderPrefix("1942"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/123/1942.tzx.zip",
            SpectrumComputingUrls.BuildPubGameZipUrl("1942", ".tzx.zip"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/0-9/1942.tzx.zip",
            SpectrumComputingUrls.BuildPubGameZipUrl("1942", ".tzx.zip", "0-9"));

        var urls = SpectrumComputingUrls.EnumerateGameZipUrls("1942");
        Assert.Contains(urls, u => u.EndsWith("/123/1942.tzx.zip"));
        Assert.Contains(urls, u => u.EndsWith("/0-9/1942.tzx.zip"));
    }

    [Fact]
    public void BuildPubSnapAndDiskUrls_MatchScDoc()
    {
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/snaps/games/m/ManicMiner.z80.zip",
            SpectrumComputingUrls.BuildPubSnapZipUrl("ManicMiner", ".z80.zip"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/snaps/games/s/Saboteur.sna.zip",
            SpectrumComputingUrls.BuildPubSnapZipUrl("Saboteur", ".sna.zip"));
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games-disk/a/AbadiaDelCrimenLa.dsk.zip",
            SpectrumComputingUrls.BuildPubDiskZipUrl("AbadiaDelCrimenLa"));
    }

    [Fact]
    public void EnumerateGameZipUrls_TapFormat_PrefersTap()
    {
        var urls = SpectrumComputingUrls.EnumerateGameZipUrls(
            "FlyingShark", 1813, RomMediaFormat.Tap);
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tap.zip",
            urls[0]);
    }

    [Fact]
    public void ZxdbTemplate_Matches1Line3DMaze()
    {
        Assert.Equal(
            "https://spectrumcomputing.co.uk/zxdb/sinclair/entries/0029742/1Line3DMaze.tzx.zip",
            SpectrumComputingUrls.BuildZxdbGameZipUrl(29742, "1Line3DMaze", ".tzx.zip"));

        var urls = SpectrumComputingUrls.EnumerateGameZipUrls("1Line3DMaze", 29742);
        Assert.Contains(urls, u => u.Contains("/zxdb/sinclair/entries/0029742/") && u.EndsWith(".tzx.zip"));
        Assert.Contains(urls, u => u.Contains("/pub/sinclair/games/123/"));
    }

    [Fact]
    public void MediaUrls_Ballblazer_MatchSpectrumComputingTemplates()
    {
        var load = SpectrumComputingUrls.EnumerateLoadScreenUrls("Ballblazer");
        Assert.Contains(load, u => u.EndsWith("/pub/sinclair/screens/load/b/scr/Ballblazer.scr"));
        Assert.Contains(load, u => u.EndsWith("/pub/sinclair/screens/load/b/gif/Ballblazer.gif"));
        Assert.Contains(load, u => u.Contains("SCRtoFace.php?SCR=pub/sinclair/screens/load/b/scr/Ballblazer.scr"));
        Assert.Equal(load[0], "https://spectrumcomputing.co.uk/pub/sinclair/screens/load/b/scr/Ballblazer.scr");

        var inGame = SpectrumComputingUrls.EnumerateInGameScreenUrls("Ballblazer");
        Assert.Contains(inGame, u => u.EndsWith("/pub/sinclair/screens/in-game/b/Ballblazer.gif"));

        var cover = SpectrumComputingUrls.EnumerateCoverUrls("Ballblazer", 388);
        Assert.Contains(cover, u => u.EndsWith("/zxdb/sinclair/entries/0000388/Ballblazer.jpg"));
        Assert.Contains(cover, u => u.EndsWith("/pub/sinclair/games-adverts/b/Ballblazer.jpg"));
    }

    [Fact]
    public void MediaCacheKey_PrefersScId7()
    {
        var entry = SpectrumComputingUrls.CreateEntry("Ballblazer", scFile: "Ballblazer", scId: 388, scSlug: "Ballblazer");
        Assert.Equal("0000388", SpectrumComputingMedia.CacheFolderKey(entry));
        Assert.Equal("Ballblazer", SpectrumComputingMedia.ResolveStem(entry));
    }

    [Fact]
    public async Task DownloadAsync_1Line3DMaze_UsesZxdb()
    {
        AppPaths.EnsureCreated();
        string cached = Path.Combine(AppPaths.Downloads, "sc-29742-test.zip");
        if (File.Exists(cached)) File.Delete(cached);

        var cat = new CatalogService();
        var entry = SpectrumComputingUrls.CreateEntry(
            "1 Line 3D Maze", "sc-29742-test", "1Line3DMaze", scId: 29742, scSlug: "1_Line_3D_Maze");
        // Forzar que el 1er candidato pub falle y llegue a zxdb
        entry.Url = SpectrumComputingUrls.BuildPubGameZipUrl("1Line3DMaze", ".tap.zip");

        string path = await cat.DownloadAsync(entry);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 100);
        Assert.True(
            entry.Url.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase)
            || entry.Url.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase)
            || entry.Url.EndsWith(".z80", StringComparison.OrdinalIgnoreCase)
            || entry.Url.EndsWith(".z80.zip", StringComparison.OrdinalIgnoreCase),
            $"formato inesperado: {entry.Url}");
    }

    [Fact]
    public void Search_UnknownTitle_SynthesizesSpectrumComputingEntry()
    {
        var cat = new CatalogService();
        var hits = cat.Search("green beret").ToList();
        Assert.Single(hits);
        Assert.Equal("https://spectrumcomputing.co.uk/pub/sinclair/games/g/GreenBeret.tzx.zip", hits[0].Url);
        Assert.Equal(".tzx.zip", hits[0].Format);
    }

    [Fact]
    public void ResolveSpectrumComputingCandidates_PrefersTzxWhenTapPersisted()
    {
        var cat = new CatalogService();
        var entry = new CatalogEntry
        {
            Title = "Flying Shark",
            ScFile = "FlyingShark",
            ScId = 1813,
            Url = "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tap.zip",
            Format = ".tap.zip",
        };
        var urls = cat.ResolveSpectrumComputingCandidates(entry);
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tzx.zip",
            urls[0]);
        Assert.Contains(
            urls,
            u => u.Equals(
                "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tap.zip",
                StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EnumerateGameZipUrls_PrefersTzxBeforeTap()
    {
        var urls = SpectrumComputingUrls.EnumerateGameZipUrls("FlyingShark", 1813);
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tzx.zip",
            urls[0]);
        Assert.Contains(urls, u => u.EndsWith("/f/FlyingShark.tap.zip"));
    }

    [Fact]
    public async Task DownloadAsync_FlyingShark_PrefersTzxOverBioToxinTap()
    {
        AppPaths.EnsureCreated();
        string id = "sc-1813-flyingshark-test";
        foreach (string ext in new[] { ".tap.zip", ".tzx.zip", ".zip" })
        {
            string stale = Path.Combine(AppPaths.Downloads, id + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        var cat = new CatalogService();
        // Simula metadatos viejos que apuntan al TAP BioToxin.
        var entry = SpectrumComputingUrls.CreateEntry(
            "Flying Shark", id, "FlyingShark", scId: 1813, scSlug: "Flying_Shark");
        entry.Url = "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tap.zip";
        entry.Format = ".tap.zip";

        string path = await cat.DownloadAsync(entry);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1000);
        Assert.EndsWith(".tzx.zip", entry.Url, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".tzx.zip", path, StringComparison.OrdinalIgnoreCase);
        Assert.False(
            MediaLoader.TapPathRequiresInterpreter(path),
            "TZX no debe marcarse como TAP que requiere intérprete");
    }

    [Fact]
    public async Task DownloadAsync_Arkanoid_SavesZip()
    {
        AppPaths.EnsureCreated();
        foreach (string ext in new[] { ".zip", ".tzx.zip", ".tap.zip", ".z80", ".z80.zip" })
        {
            string stale = Path.Combine(AppPaths.Downloads, "arkanoid-test" + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        var cat = new CatalogService();
        var entry = SpectrumComputingUrls.CreateEntry("Arkanoid", "arkanoid-test", "Arkanoid");
        string path = await cat.DownloadAsync(entry);
        Assert.True(File.Exists(path));
        // SC a veces sirve un zip mínimo/placeholder (~1 KiB); la cascada puede
        // devolver TOSEC/Library con tamaño real. Aceptar cualquier .zip no vacío.
        Assert.True(new FileInfo(path).Length > 100, $"descarga demasiado pequeña: {path}");
        Assert.Equal(".zip", Path.GetExtension(path), ignoreCase: true);
    }

    [Fact]
    public async Task DownloadAsync_NumericTzx_Uses123Folder()
    {
        AppPaths.EnsureCreated();
        string cached = Path.Combine(AppPaths.Downloads, "07zglossie-test.zip");
        if (File.Exists(cached)) File.Delete(cached);

        var cat = new CatalogService();
        var entry = SpectrumComputingUrls.CreateEntry("07 Zglos Sie", "07zglossie-test", "07ZglosSie");
        string path = await cat.DownloadAsync(entry);
        Assert.True(File.Exists(path));
        Assert.True(new FileInfo(path).Length > 1000);
        Assert.Contains("/123/07ZglosSie.", entry.Url, StringComparison.OrdinalIgnoreCase);
        Assert.True(
            entry.Url.EndsWith(".tzx.zip", StringComparison.OrdinalIgnoreCase) ||
            entry.Url.EndsWith(".tap.zip", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadAsync_FlyingShark_ForcedTap_DownloadsTap()
    {
        AppPaths.EnsureCreated();
        string id = "sc-1813-flyingshark-tap-fmt";
        foreach (string ext in new[] { ".tap.zip", ".tzx.zip", ".zip" })
        {
            string stale = Path.Combine(AppPaths.Downloads, id + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        var cat = new CatalogService();
        var entry = SpectrumComputingUrls.CreateEntry(
            "Flying Shark", id, "FlyingShark", scId: 1813, scSlug: "Flying_Shark");
        entry.Url = "https://spectrumcomputing.co.uk/pub/sinclair/games/f/FlyingShark.tzx.zip";
        entry.Format = ".tzx.zip";

        string path = await cat.DownloadAsync(entry, RomDownloadSource.SpectrumComputing, RomMediaFormat.Tap);
        Assert.True(File.Exists(path));
        Assert.EndsWith(".tap.zip", path, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".tap.zip", entry.Url, StringComparison.OrdinalIgnoreCase);
        Assert.True(MediaLoader.TapPathRequiresInterpreter(path));
    }

    [Fact]
    public async Task SpectrumComputingApi_FlyingShark_ReturnsTapAndTzx()
    {
        var api = new SpectrumComputingApi();
        var entry = SpectrumComputingUrls.CreateEntry(
            "Flying Shark", scFile: "FlyingShark", scId: 1813, scSlug: "Flying_Shark");
        var urls = await api.ResolveDownloadUrlsAsync(entry, RomMediaFormat.Auto);
        Assert.NotEmpty(urls);
        Assert.Contains(urls, u => u.EndsWith("/f/FlyingShark.tzx.zip", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(urls, u => u.EndsWith("/f/FlyingShark.tap.zip", StringComparison.OrdinalIgnoreCase));

        var tapOnly = await api.ResolveDownloadUrlsAsync(entry, RomMediaFormat.Tap);
        Assert.All(tapOnly, u => Assert.Contains(".tap", u, StringComparison.OrdinalIgnoreCase));
    }
}
