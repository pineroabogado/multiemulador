using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class ArchiveOrgSoftwareLibraryTests
{
    [Fact]
    public void BuildSearchUrl_TargetsCollection()
    {
        string url = ArchiveOrgSoftwareLibrary.BuildSearchUrl("Green Beret");
        Assert.Contains("advancedsearch.php", url, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Uri.EscapeDataString("collection:zx_spectrum_library_games"), url, StringComparison.Ordinal);
        Assert.Contains("Green", url, StringComparison.Ordinal);
        Assert.Contains("output=json", url, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildDownloadUrl_EncodesIdentifierAndFile()
    {
        string url = ArchiveOrgSoftwareLibrary.BuildDownloadUrl(
            "zx_Green_Beret_1986_Imagine_Software",
            "Green_Beret_1986_Imagine_Software.z80");
        Assert.Equal(
            "https://archive.org/download/zx_Green_Beret_1986_Imagine_Software/Green_Beret_1986_Imagine_Software.z80",
            url);
    }

    [Fact]
    public void BuildIdentifierFromTosecName_MatchesDocumentedExamples()
    {
        Assert.Equal(
            "zx_Street_Hawk_1986_Ocean",
            ArchiveOrgSoftwareLibrary.BuildIdentifierFromTosecName("Street Hawk (1986)(Ocean)"));
        Assert.Equal(
            "zx_Manic_Miner_1983_Bug-Byte",
            ArchiveOrgSoftwareLibrary.BuildIdentifierFromTosecName("Manic Miner (1983)(Bug-Byte)"));
        Assert.Equal(
            "zx_Saboteur_1985_Durell_Software",
            ArchiveOrgSoftwareLibrary.BuildIdentifierFromTosecName("Saboteur! (1985)(Durell Software)"));
        Assert.Equal(
            "zx_Abadia_del_Crimen_La_1987_Opera_Soft",
            ArchiveOrgSoftwareLibrary.BuildIdentifierFromTosecName("La Abadia del Crimen (1987)(Opera Soft)"));
    }

    [Fact]
    public void BuildConventionalDownloadUrls_UsesStemWithoutZxPrefix()
    {
        var urls = ArchiveOrgSoftwareLibrary.BuildConventionalDownloadUrls("zx_Street_Hawk_1986_Ocean");
        Assert.Equal(
            "https://archive.org/download/zx_Street_Hawk_1986_Ocean/Street_Hawk_1986_Ocean.z80",
            urls[0]);
        Assert.Contains(urls, u => u.EndsWith(".tzx", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(urls, u => u.EndsWith(".tap", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(
            "https://archive.org/details/zx_Street_Hawk_1986_Ocean",
            ArchiveOrgSoftwareLibrary.BuildDetailsUrl("zx_Street_Hawk_1986_Ocean"));
    }

    [Fact]
    public void FileStemFromIdentifier_StripsZxPrefix()
    {
        Assert.Equal(
            "Street_Hawk_1986_Ocean",
            ArchiveOrgSoftwareLibrary.FileStemFromIdentifier("zx_Street_Hawk_1986_Ocean"));
    }

    [Fact]
    public void RankHits_PrefersCleanExactTitle_OverHackAndSequel()
    {
        var hits = new[]
        {
            new ArchiveOrgSoftwareLibrary.SoftwareLibraryHit(
                "zx_Arkanoid_II_Revenge_of_Doh_1988_Imagine_Software",
                "Arkanoid II - Revenge of Doh"),
            new ArchiveOrgSoftwareLibrary.SoftwareLibraryHit(
                "zx_Arkanoid_1987_Imagine_Software_h",
                "Arkanoid [h]"),
            new ArchiveOrgSoftwareLibrary.SoftwareLibraryHit(
                "zx_Arkanoid_1987_Imagine_Software_a",
                "Arkanoid [a]"),
            new ArchiveOrgSoftwareLibrary.SoftwareLibraryHit(
                "zx_Arkanoid_1987_Imagine_Software",
                "Arkanoid"),
        };

        var ranked = ArchiveOrgSoftwareLibrary.RankHits(hits, ["Arkanoid"]);
        Assert.Equal("zx_Arkanoid_1987_Imagine_Software", ranked[0].Identifier);
        Assert.DoesNotContain(ranked.Take(2), h => h.Title.Contains("[h]", StringComparison.Ordinal));
        Assert.DoesNotContain(ranked.Take(3), h => h.Title.Contains("II", StringComparison.Ordinal));
    }

    [Fact]
    public void ParseSearchJson_ReadsDocs()
    {
        const string json = """
            {"response":{"numFound":2,"docs":[
              {"identifier":"zx_1942_1986_Elite_Systems","title":"1942"},
              {"identifier":"zx_1942_1986_Elite_Systems_a","title":"1942 [a]"}
            ]}}
            """;
        var hits = ArchiveOrgSoftwareLibrary.ParseSearchJson(json);
        Assert.Equal(2, hits.Count);
        Assert.Equal("zx_1942_1986_Elite_Systems", hits[0].Identifier);
    }

    [Fact]
    public void PickBestMediaFileName_PrefersZ80()
    {
        string? best = ArchiveOrgSoftwareLibrary.PickBestMediaFileName(
        [
            "screenshot_00.png",
            "Game.tap",
            "Game_1987_Pub.z80",
            "__ia_thumb.jpg",
        ]);
        Assert.Equal("Game_1987_Pub.z80", best);
    }

    [Fact]
    public void ParseMetadataFileNames_ExtractsNames()
    {
        const string json = """
            {"files":[
              {"name":"Green_Beret_1986_Imagine_Software.z80","size":"44371"},
              {"name":"screenshot_00.png","size":"100"}
            ]}
            """;
        var names = ArchiveOrgSoftwareLibrary.ParseMetadataFileNames(json);
        Assert.Contains("Green_Beret_1986_Imagine_Software.z80", names);
        Assert.Contains("screenshot_00.png", names);
    }

    [Fact]
    public async Task LiveSearchAndResolve_GreenBeret()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test; +https://archive.org)");

        var entry = SpectrumComputingUrls.CreateEntry(
            "Green Beret", scFile: "GreenBeret", scId: 1, scSlug: "Green_Beret");

        var urls = await ArchiveOrgSoftwareLibrary.ResolveCandidateUrlsAsync(http, entry);
        Assert.NotEmpty(urls);
        Assert.Contains(urls, u =>
            u.Contains("archive.org/download/zx_Green_Beret", StringComparison.OrdinalIgnoreCase) &&
            u.EndsWith(".z80", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DownloadAsync_UsesOnlySoftwareLibrary_WhenSourceSelected()
    {
        AppPaths.EnsureCreated();
        foreach (string ext in new[] { ".zip", ".z80", ".tzx.zip", ".tap.zip", ".z80.zip", ".sna" })
        {
            string stale = Path.Combine(AppPaths.Downloads, "ia-lib-source-test" + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        // Fuerza Software Library con URL real; TOSEC no debe usarse.
        string forced = ArchiveOrgSoftwareLibrary.BuildDownloadUrl(
            "zx_Green_Beret_1986_Imagine_Software",
            "Green_Beret_1986_Imagine_Software.z80");
        ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests([forced]);
        ArchiveOrgTosecTap.SetIndexForTests(["SHOULD_NOT_BE_USED (1986)(Imagine).zip"]);

        try
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test)");

            var cat = new CatalogService(http);
            var entry = SpectrumComputingUrls.CreateEntry(
                "Green Beret", "ia-lib-source-test", "GreenBeret");

            string path = await cat.DownloadAsync(
                entry, RomDownloadSource.ArchiveOrgSoftwareLibrary);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.EndsWith(".z80", path, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("zx_Green_Beret_1986_Imagine_Software", entry.Url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("%5BTAP%5D", entry.Url, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests(null);
            ArchiveOrgTosecTap.SetIndexForTests(null);
        }
    }

}
