using System.IO.Compression;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Tests;

public class ArchiveMediaTests
{
    [Fact]
    public void ExtractPreferredMedia_FromZip_PrefersTapeOverSnapshot()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "me-test-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var tap = zip.CreateEntry("game.tap");
                using (var s = tap.Open())
                    s.Write(new byte[] { 0x03, 0x00, 0x00, 0x11, 0x11 });

                var z80 = zip.CreateEntry("folder/game.z80");
                using (var s = z80.Open())
                    s.Write(new byte[] { 0x00, 0x01, 0x02, 0x03 });
            }

            var (name, data) = ArchiveMedia.ExtractPreferredMedia(zipPath);
            Assert.Equal("game.tap", name);
            Assert.Equal(5, data.Length);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }

    [Fact]
    public void ExtractPreferredMedia_FromZip_PrefersTzxOverTap()
    {
        string zipPath = Path.Combine(Path.GetTempPath(), "me-test-" + Guid.NewGuid().ToString("N") + ".zip");
        try
        {
            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var tap = zip.CreateEntry("game.tap");
                using (var s = tap.Open())
                    s.Write(new byte[] { 0x03, 0x00, 0x00, 0x11, 0x11 });

                var tzx = zip.CreateEntry("game.tzx");
                using (var s = tzx.Open())
                    s.Write([0x5A, 0x58, 0x54, 0x61, 0x70, 0x65, 0x21, 0x1A, 0x01, 0x00]);
            }

            var (name, _) = ArchiveMedia.ExtractPreferredMedia(zipPath);
            Assert.Equal("game.tzx", name);
        }
        finally
        {
            File.Delete(zipPath);
        }
    }
}
