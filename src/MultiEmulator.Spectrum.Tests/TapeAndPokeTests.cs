using MultiEmulator.Spectrum.Poke;
using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Tests;

public class TapeAndPokeTests
{
    [Fact]
    public void LoadTap_ParsesBlocks()
    {
        // two blocks: flag+1 byte payload+checksum each roughly
        var data = new byte[]
        {
            0x03, 0x00, 0x00, 0x11, 0x11,
            0x03, 0x00, 0xFF, 0x22, 0x22
        };
        var tape = TapeImage.LoadTap(data);
        Assert.Equal(2, tape.Blocks.Count);
        Assert.Equal(0x00, tape.Blocks[0].Flag);
        Assert.Equal(0xFF, tape.Blocks[1].Flag);
    }

    [Fact]
    public void ParsePok_ReadsTrainer()
    {
        const string pok = """
NInfinite lives
Z 0 23675 0 1
Y
""";
        var trainers = PokeService.ParsePok(pok);
        Assert.Single(trainers);
        Assert.Equal("Infinite lives", trainers[0].Name);
        Assert.Single(trainers[0].Entries);
        Assert.Equal(23675, trainers[0].Entries[0].Address);
        Assert.Equal(0, trainers[0].Entries[0].Value);
    }
}
