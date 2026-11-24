using MultiEmulator.Spectrum.Input;

namespace MultiEmulator.Spectrum.Tests;

public class KeyboardMatrixTests
{
    [Theory]
    [InlineData("A", 1, 0)]
    [InlineData("D1", 3, 0)]
    [InlineData("1", 3, 0)]
    [InlineData("ENTER", 6, 0)]
    [InlineData("SPACE", 7, 0)]
    public void MapPcKey_CommonKeys(string key, int row, int bit)
    {
        var m = KeyboardMatrix.MapPcKey(key);
        Assert.NotNull(m);
        Assert.Equal((row, bit), m!.Value);
    }

    [Fact]
    public void SetKey_MakesRowActiveLow()
    {
        var kb = new KeyboardMatrix();
        kb.SetKey(2, 0, true); // Q
        // Row Q–T is bit0 of high addr = 0xFB (bit 2 clear)
        Assert.Equal(0x1E, kb.Read(0xFB)); // bit0 cleared
        kb.SetKey(2, 0, false);
        Assert.Equal(0x1F, kb.Read(0xFB));
    }
}
