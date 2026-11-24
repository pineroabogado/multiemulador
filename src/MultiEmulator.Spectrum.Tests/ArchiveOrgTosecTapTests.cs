using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class ArchiveOrgTosecTapTests
{
    [Theory]
    [InlineData(
        "Manic Miner (1983)(Bug-Byte).zip",
        null,
        "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/Manic%20Miner%20%281983%29%28Bug-Byte%29.zip")]
    [InlineData(
        "Street Hawk (1986)(Ocean).zip",
        "TAP",
        "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/Street%20Hawk%20%281986%29%28Ocean%29.zip")]
    [InlineData(
        "Abadia del Crimen, La (1987)(Opera Soft)(ES).zip",
        null,
        "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/Abadia%20del%20Crimen%2C%20La%20%281987%29%28Opera%20Soft%29%28ES%29.zip")]
    [InlineData(
        "Games/[Z80]/Arkanoid (1987)(Imagine).zip",
        null,
        "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BZ80%5D/Arkanoid%20%281987%29%28Imagine%29.zip")]
    public void BuildFileUrl_EncodesLadyEklipsePath(string fileName, string? format, string expected)
    {
        string url = ArchiveOrgTosecTap.BuildFileUrl(fileName, format);
        Assert.Equal(expected, url);
    }

    [Fact]
    public void BuildFileUrl_EncodesTosecName_LegacyTapDefault()
    {
        string url = ArchiveOrgTosecTap.BuildFileUrl("Arkanoid (1987)(Imagine).zip");
        Assert.Equal(
            "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/Arkanoid%20%281987%29%28Imagine%29.zip",
            url);
    }

    [Fact]
    public void RankMatches_PrefersCleanDump_OverHackAndSequel()
    {
        string[] index =
        [
            "Arkanoid - Revenge of Doh (1988)(Imagine)(128K)[aka Arkanoid II].zip",
            "Arkanoid (1987)(Imagine)[h Msinc].zip",
            "Arkanoid (1987)(Imagine)[a].zip",
            "Arkanoid (1987)(Imagine).zip",
            "Arkanoid (1987)(Erbe)(ES)(en)[re-release].zip",
        ];

        var ranked = ArchiveOrgTosecTap.RankMatches(index, ["Arkanoid"]);
        Assert.Equal("Games/[TAP]/Arkanoid (1987)(Imagine).zip", ranked[0]);
        Assert.DoesNotContain(ranked.Take(2), n => n.Contains("[h ", StringComparison.Ordinal));
        Assert.DoesNotContain(ranked.Take(3), n => n.Contains("Arkanoid - ", StringComparison.Ordinal));
    }

    [Fact]
    public void RankMatches_PrefersTapOverZ80_SameTitle()
    {
        string[] index =
        [
            "Games/[Z80]/Street Hawk (1986)(Ocean).zip",
            "Games/[SNA]/Street Hawk (1986)(Ocean).zip",
            "Games/[TZX]/Street Hawk (1986)(Ocean).zip",
            "Games/[TAP]/Street Hawk (1986)(Ocean).zip",
        ];

        var ranked = ArchiveOrgTosecTap.RankMatches(index, ["Street Hawk"]);
        Assert.Equal(4, ranked.Count);
        Assert.Equal("Games/[TAP]/Street Hawk (1986)(Ocean).zip", ranked[0]);
        Assert.Equal("Games/[TZX]/Street Hawk (1986)(Ocean).zip", ranked[1]);
        Assert.Equal("Games/[Z80]/Street Hawk (1986)(Ocean).zip", ranked[2]);
        Assert.Equal("Games/[SNA]/Street Hawk (1986)(Ocean).zip", ranked[3]);
    }

    [Fact]
    public void MatchFileNames_InvertsSpanishArticle()
    {
        string[] index =
        [
            "Abadia del Crimen, La (1988)(Opera Soft)(128K)(ES).zip",
            "Abadia del Crimen, La (1988)(Opera Soft)(128K)(ES)[a].zip",
        ];

        var entry = new CatalogEntry
        {
            Title = "La Abadia del Crimen",
            ScFile = "LaAbadiadelCrimen",
            ScSlug = "La_Abadia_del_Crimen"
        };

        var matches = ArchiveOrgTosecTap.MatchFileNames(index, entry);
        Assert.Contains(matches, m => m.Contains("Abadia del Crimen, La (1988)", StringComparison.Ordinal));
        Assert.Equal("Games/[TAP]/Abadia del Crimen, La (1988)(Opera Soft)(128K)(ES).zip", matches[0]);
    }

    [Fact]
    public void ParseDirectoryListingHtml_ExtractsZipHrefs()
    {
        const string html = """
            <td><a href="Arkanoid%20%281987%29%28Imagine%29.zip">Arkanoid (1987)(Imagine).zip</a></td>
            <td><a href="Green%20Beret%20%281986%29%28Imagine%29.zip">Green Beret (1986)(Imagine).zip</a></td>
            <td><a href="subdir/ignore.zip">ignore</a></td>
            """;

        var names = ArchiveOrgTosecTap.ParseDirectoryListingHtml(html);
        Assert.Equal(2, names.Count);
        Assert.Contains("Arkanoid (1987)(Imagine).zip", names);
        Assert.Contains("Green Beret (1986)(Imagine).zip", names);
    }

    [Fact]
    public void ParseFilesXml_IndexesTapTzxZ80Sna_SkipsOther()
    {
        const string xml = """
            <files>
              <file name="Games/[TAP]/Arkanoid (1987)(Imagine).zip" source="original" />
              <file name="Games/[TZX]/Arkanoid (1987)(Imagine).zip" source="original" />
              <file name="Games/[Z80]/Arkanoid (1987)(Imagine).zip" source="original" />
              <file name="Games/[SNA]/Manic Miner (1983)(Bug-Byte).zip" source="original" />
              <file name="Games/[DSK]/Ignore Me (1985)(Test).zip" source="original" />
              <file name="Demos/[TAP]/Demo.zip" source="original" />
              <file name="Games/[TAP]/nested/no.zip" source="original" />
            </files>
            """;

        var names = ArchiveOrgTosecTap.ParseFilesXml(xml);
        Assert.Equal(4, names.Count);
        Assert.Contains("Games/[TAP]/Arkanoid (1987)(Imagine).zip", names);
        Assert.Contains("Games/[TZX]/Arkanoid (1987)(Imagine).zip", names);
        Assert.Contains("Games/[Z80]/Arkanoid (1987)(Imagine).zip", names);
        Assert.Contains("Games/[SNA]/Manic Miner (1983)(Bug-Byte).zip", names);
        Assert.DoesNotContain(names, n => n.Contains("[DSK]", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(names, n => n.StartsWith("Demos/", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TryRelativePathFromUrl_DecodesFormatFolder()
    {
        string url = ArchiveOrgTosecTap.BuildFileUrl("Street Hawk (1986)(Ocean).zip", "TZX");
        string? rel = ArchiveOrgTosecTap.TryRelativePathFromUrl(url);
        Assert.Equal("Games/[TZX]/Street Hawk (1986)(Ocean).zip", rel);
        Assert.True(ArchiveOrgTosecTap.IsTosecUrl(url));
    }

    [Fact]
    public void EnumerateTitleQueries_IncludesStemSplitAndArticleInvert()
    {
        var entry = SpectrumComputingUrls.CreateEntry(
            "Green Beret", scFile: "GreenBeret", scId: 1, scSlug: "Green_Beret");
        var q = ArchiveOrgTosecTap.EnumerateTitleQueries(entry);
        Assert.Contains(q, x => x.Equals("Green Beret", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task LiveDownload_KnownTosecTapZip_FromArchiveOrg()
    {
        AppPaths.EnsureCreated();
        string dest = Path.Combine(AppPaths.Downloads, "archiveorg-live-arkanoid-test.zip");
        if (File.Exists(dest)) File.Delete(dest);

        string url = ArchiveOrgTosecTap.BuildFileUrl("Arkanoid (1987)(Imagine).zip");
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test; +https://archive.org)");

        using var resp = await http.GetAsync(url);
        Assert.True(resp.IsSuccessStatusCode, $"HTTP {(int)resp.StatusCode} para {url}");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Length > 1000);
        await File.WriteAllBytesAsync(dest, bytes);
        Assert.True(File.Exists(dest));
    }

    [Fact]
    public async Task DownloadAsync_UsesOnlyTosec_WhenSourceIsTosec()
    {
        AppPaths.EnsureCreated();
        string cached = Path.Combine(AppPaths.Downloads, "archiveorg-tosec-source-test.zip");
        if (File.Exists(cached)) File.Delete(cached);

        ArchiveOrgTosecTap.SetIndexForTests(
        [
            "Arkanoid (1987)(Imagine).zip",
            "Arkanoid (1987)(Imagine)[a].zip",
        ]);
        // Software Library no debe usarse aunque hubiera candidatos.
        ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests(
        [
            "https://archive.org/download/zx_SHOULD_NOT/SHOULD_NOT.z80"
        ]);

        try
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test)");

            var cat = new CatalogService(http);
            var entry = SpectrumComputingUrls.CreateEntry(
                "Arkanoid", "archiveorg-tosec-source-test", "Arkanoid");

            string path = await cat.DownloadAsync(entry, RomDownloadSource.ArchiveOrgTosec);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 1000);
            Assert.Contains("archive.org/download/", entry.Url, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("%5BTAP%5D", entry.Url, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SHOULD_NOT", entry.Url, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArchiveOrgTosecTap.SetIndexForTests(null);
            ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests(null);
        }
    }

    [Fact]
    public void ShouldDeferLibrarySnapshotForTape_AutoDefersZ80()
    {
        Assert.True(CatalogService.ShouldDeferLibrarySnapshotForTape(
            RomMediaFormat.Auto,
            "https://archive.org/download/zx_1942_1986_Elite_Systems/1942_1986_Elite_Systems.z80"));
        Assert.True(CatalogService.ShouldDeferLibrarySnapshotForTape(
            RomMediaFormat.Tap,
            @"C:\cache\sc-9297.z80"));
        Assert.False(CatalogService.ShouldDeferLibrarySnapshotForTape(
            RomMediaFormat.Z80,
            "https://archive.org/download/zx_1942_1986_Elite_Systems/1942_1986_Elite_Systems.z80"));
        Assert.False(CatalogService.ShouldDeferLibrarySnapshotForTape(
            RomMediaFormat.Auto,
            "https://archive.org/download/ZXSpectrumTOSECSetV20171101LadyEklipse/Games/%5BTAP%5D/1942.zip"));
    }

    [Fact]
    public async Task DownloadAsync_Auto_DefersSoftLibraryZ80_For1942TosecTap()
    {
        AppPaths.EnsureCreated();
        string id = "sc-9297-1942-defer-test";
        foreach (string ext in new[] { ".tap.zip", ".tzx.zip", ".z80", ".z80.zip", ".zip" })
        {
            string stale = Path.Combine(AppPaths.Downloads, id + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        // Índice TOSEC local (sin tocar Soft Library override: evita carrera con otros tests).
        ArchiveOrgTosecTap.SetIndexForTests(
        [
            "Games/[TAP]/1942 (1986)(Elite Systems).zip",
            "Games/[Z80]/1942 (1986)(Elite Systems).zip",
        ]);

        try
        {
            var handler = new HostBlockHandler("spectrumcomputing.co.uk", "zxinfo.dk");
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test)");

            var cat = new CatalogService(http);
            var entry = SpectrumComputingUrls.CreateEntry(
                "1942", id, "1942", scId: 9297, scSlug: "1942");
            // Soft Library real (solo .z80) → diferir → TOSEC TAP.
            entry.Notes = null;

            string path = await cat.DownloadAsync(entry, RomDownloadSource.SpectrumComputing, RomMediaFormat.Auto);
            Assert.True(File.Exists(path));
            Assert.Contains("%5BTAP%5D", entry.Url ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.Contains("1942", entry.Url ?? "", StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Elite_Systems.z80", entry.Url ?? "", StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArchiveOrgTosecTap.SetIndexForTests(null);
        }
    }

    [Fact]
    public async Task DownloadAsync_PreferredSc_CascadesToArchiveWhenScBlocked()
    {
        AppPaths.EnsureCreated();
        string id = "sc-cascade-fallback-test";
        foreach (string ext in new[] { ".tap.zip", ".tzx.zip", ".z80", ".zip" })
        {
            string stale = Path.Combine(AppPaths.Downloads, id + ext);
            if (File.Exists(stale)) File.Delete(stale);
        }

        ArchiveOrgTosecTap.SetIndexForTests(["Games/[TAP]/Arkanoid (1987)(Imagine).zip"]);
        ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests(
        [
            ArchiveOrgSoftwareLibrary.BuildDownloadUrl(
                "zx_Arkanoid_1987_Imagine",
                "Arkanoid_1987_Imagine.z80")
        ]);

        try
        {
            // Bloquear SC + ZXInfo: la cascada debe seguir a Library/TOSEC.
            var handler = new HostBlockHandler("spectrumcomputing.co.uk", "zxinfo.dk");
            var http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MULTIEMULADOR/1.0 (test)");

            var cat = new CatalogService(http);
            var entry = SpectrumComputingUrls.CreateEntry(
                "Arkanoid", id, "Arkanoid");

            string path = await cat.DownloadAsync(entry, RomDownloadSource.SpectrumComputing);
            Assert.True(File.Exists(path));
            Assert.Contains("archive.org", entry.Url, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            ArchiveOrgTosecTap.SetIndexForTests(null);
            ArchiveOrgSoftwareLibrary.SetCandidateUrlsForTests(null);
        }
    }

    private sealed class HostBlockHandler : HttpClientHandler
    {
        private readonly string[] _blockedHosts;

        public HostBlockHandler(params string[] blockedHosts) => _blockedHosts = blockedHosts;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string host = request.RequestUri?.Host ?? "";
            if (_blockedHosts.Any(b => host.Contains(b, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
                {
                    RequestMessage = request,
                    ReasonPhrase = "blocked-for-cascade-test"
                });
            }

            return base.SendAsync(request, cancellationToken);
        }
    }
}
