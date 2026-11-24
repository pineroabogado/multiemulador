using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class SpectrumComputingListScraperTests
{
    [Fact]
    public void ParseListHtml_ExtractsSpectrumEntries()
    {
        const string html = """
            <div class="row zxdb_stripes">
              <a  href="/entry/255/ZX-Spectrum/Arkanoid" class="">Arkanoid</a>
              <a href="/entry/47/ZX-Spectrum/La_Abadia_del_Crimen" class="">La Abadia del Crimen</a>
              <a href="/entry/999/ZX81/Something" class="">Something</a>
              <a href="/entry/255/ZX-Spectrum/Arkanoid" class="">Arkanoid</a>
            </div>
            """;

        var games = SpectrumComputingListScraper.ParseListHtml(html).ToList();
        Assert.Equal(2, games.Count);
        Assert.Contains(games, g => g.Id == 255 && g.Title == "Arkanoid" && g.Slug == "Arkanoid");
        Assert.Contains(games, g => g.Id == 47 && g.Slug == "La_Abadia_del_Crimen");
    }

    [Fact]
    public void StemFromSlugOrTitle_StripsUnderscores()
    {
        Assert.Equal("AticAtac", SpectrumComputingListScraper.StemFromSlugOrTitle("Atic_Atac", "Atic Atac"));
        Assert.Equal("GreenBeret", SpectrumComputingListScraper.StemFromSlugOrTitle("GreenBeret", "Green Beret"));
    }

    [Fact]
    public async Task FetchPrefixA_ReturnsManyGames()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR-test/1.0");
        var index = await SpectrumComputingListScraper.BuildIndexAsync(http, new[] { "a" });
        Assert.True(index.Entries.Count > 100, $"Expected many A games, got {index.Entries.Count}");
        Assert.Contains(index.Entries, e => e.Title.Contains("Arkanoid", StringComparison.OrdinalIgnoreCase));
    }
}
