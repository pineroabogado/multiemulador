using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

public class SpectrumComputingEntryScraperTests
{
    private const string SampleRowHtml = """
        Additional File Downloads</a></div><div id="collapse_additional_file_downloads" class="panel-collapse collapse in"><div class="panel-body">
        <div class="panel-default"><div class="row zxdb_stripes" style="" id="local_2698">
          <a download href="/pub/sinclair/screens/load/b/scr/Ballblazer.scr" target="_blank" >
          <div class="col-sm-4 align-center  center_mobile"><div title="Loading screen" style="margin-left: auto; margin-right: auto; height:180px; background:url(&#039;/SCRtoImage.php?SCR=pub/sinclair/screens/load/b/scr/Ballblazer.scr&border=0&#039;) no-repeat  top / contain "></div></div>
          <div class="col-sm-2 align-left center_mobile"><small>Loading screen</small></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"><b>Ballblazer.scr</b></div>
          </a>
          </div><div class="row zxdb_stripes" style="" id="local_2699">
          <a  href="/pub/sinclair/screens/in-game/b/Ballblazer.gif" target="_blank" >
          <div class="col-sm-4 align-center  center_mobile"><div title="Running screen" style="margin-left: auto; margin-right: auto; height:180px; background:url(&#039;/pub/sinclair/screens/in-game/b/Ballblazer.gif&#039;) no-repeat  top / contain "></div></div>
          <div class="col-sm-2 align-left center_mobile"><small>Running screen</small></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"><b>Ballblazer.gif</b></div>
          </a>
          </div><div class="row zxdb_stripes" style="" id="local_125225">
          <a  href="/zxdb/sinclair/entries/0000388/Ballblazer.jpg" target="_blank" >
          <div class="col-sm-4 align-center  center_mobile"><div title="Inlay - Front" style="margin-left: auto; margin-right: auto; height:180px; background:url(&#039;/zxdb/sinclair/entries/0000388/Ballblazer.jpg&#039;) no-repeat  top / contain "></div></div>
          <div class="col-sm-2 align-left center_mobile"><small>Inlay - Front</small></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word">Single jewel case</div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"><b>Ballblazer.jpg</b></div>
          </a>
          </div><div class="row zxdb_stripes" style="" id="local_132358">
          <a  href="/zxdb/sinclair/entries/0000388/Ballblazer128K_Back.jpg" target="_blank" >
          <div class="col-sm-4 align-center  center_mobile"><div title="Inlay - Back" style="margin-left: auto; margin-right: auto; height:180px; background:url(&#039;/zxdb/sinclair/entries/0000388/Ballblazer128K_Back.jpg&#039;) no-repeat  top / contain "></div></div>
          <div class="col-sm-2 align-left center_mobile"><small>Inlay - Back</small></div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word">Single jewel case - black variant</div>
          <div class="col-sm-3 align-left center_mobile" style="overflow-wrap: break-word"><b>Ballblazer128K_Back.jpg</b></div>
          </a>
          </div></div></div>
          <div class="panel-heading">Other stuff
        """;

    [Fact]
    public void ParseAdditionalFileDownloads_ExtractsTypeDescriptionFileAndAbsoluteUrl()
    {
        var items = SpectrumComputingEntryScraper.ParseAdditionalFileDownloads(SampleRowHtml);
        Assert.True(items.Count >= 4);

        var load = items.First(i => i.Type.Contains("Loading", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Ballblazer.scr", load.FileName);
        Assert.Equal(
            "https://spectrumcomputing.co.uk/pub/sinclair/screens/load/b/scr/Ballblazer.scr",
            load.AbsoluteUrl);
        Assert.Equal(CatalogMediaKind.LoadScreen, load.MapToMainSlot());

        var run = items.First(i => i.Type.Contains("Running", StringComparison.OrdinalIgnoreCase));
        Assert.EndsWith("/pub/sinclair/screens/in-game/b/Ballblazer.gif", run.AbsoluteUrl);
        Assert.Equal(CatalogMediaKind.InGame, run.MapToMainSlot());

        var front = items.First(i => i.Type.Contains("Front", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("Single jewel case", front.Description);
        Assert.Equal("Ballblazer.jpg", front.FileName);
        Assert.Contains("/zxdb/sinclair/entries/0000388/Ballblazer.jpg", front.AbsoluteUrl);
        Assert.Equal(CatalogMediaKind.Cover, front.MapToMainSlot());

        var back = items.First(i => i.Type.Contains("Back", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("black variant", back.Description);
        Assert.Equal(CatalogMediaKind.CoverBack, back.MapToMainSlot());
    }

    [Fact]
    public void ToAbsoluteUrl_ResolvesRelativePubAndZxdb()
    {
        Assert.Equal(
            "https://spectrumcomputing.co.uk/zxdb/sinclair/entries/0000388/Ballblazer.jpg",
            SpectrumComputingEntryScraper.ToAbsoluteUrl("/zxdb/sinclair/entries/0000388/Ballblazer.jpg"));
    }
}
