using MultiEmulator.Spectrum.Tape;

namespace MultiEmulator.Spectrum.Tests;

public class TapeTransportTests
{
    private static TapeImage MiniTape()
    {
        // Tres bloques TAP mínimos
        var data = new byte[]
        {
            0x03, 0x00, 0x00, 0x11, 0x11,
            0x03, 0x00, 0xFF, 0x22, 0x22,
            0x03, 0x00, 0xFF, 0x33, 0x33
        };
        return TapeImage.LoadTap(data);
    }

    [Fact]
    public void FastForward_Cycles_Speeds_Then_Play()
    {
        var tape = MiniTape();
        var pulses = new TapePulsePlayer();
        pulses.Attach(tape);
        var t = new TapeTransport(pulses);
        t.Attach(tape);

        t.CycleFastForward();
        Assert.Equal(TapeTransportMode.FastForward, t.Mode);
        Assert.Equal(2, t.SpeedFactor);
        Assert.Equal(2, pulses.TurboSpeed);
        Assert.True(t.MuteAudio);

        t.CycleFastForward();
        Assert.Equal(4, t.SpeedFactor);

        t.CycleFastForward();
        Assert.Equal(8, t.SpeedFactor);

        t.CycleFastForward();
        Assert.Equal(TapeTransportMode.Play, t.Mode);
        Assert.Equal(1, t.SpeedFactor);
        Assert.False(t.MuteAudio);
    }

    [Fact]
    public void RewindOneBlock_Goes_Back()
    {
        var tape = MiniTape();
        tape.Position = 2;
        var pulses = new TapePulsePlayer();
        pulses.Attach(tape);
        var t = new TapeTransport(pulses);
        t.Attach(tape);
        t.Seek(2);

        t.RewindOneBlock();
        Assert.Equal(1, tape.Position);
        Assert.Equal(TapeTransportMode.Play, t.Mode);
    }

    [Fact]
    public void RewindToStart_Resets()
    {
        var tape = MiniTape();
        var pulses = new TapePulsePlayer();
        pulses.Attach(tape);
        var t = new TapeTransport(pulses);
        t.Attach(tape);
        t.Seek(2);
        t.RewindToStart();
        Assert.Equal(0, tape.Position);
        Assert.Equal(0, pulses.PlayPosition);
    }

    [Fact]
    public void RewindTick_Steps_Back_To_Start()
    {
        var tape = MiniTape();
        var pulses = new TapePulsePlayer();
        pulses.Attach(tape);
        var t = new TapeTransport(pulses);
        t.Attach(tape);
        t.Seek(2);
        t.CycleRewind(); // ×2
        Assert.Equal(TapeTransportMode.Rewind, t.Mode);

        for (int i = 0; i < 20; i++)
            t.Tick(70_000);

        Assert.Equal(0, tape.Position);
        Assert.Equal(TapeTransportMode.Rewind, t.Mode);
    }

    [Fact]
    public void FastForward_Works_Without_Tape_For_Deck_Mode()
    {
        var pulses = new TapePulsePlayer();
        var t = new TapeTransport(pulses);
        t.Attach(null);
        t.CycleFastForward();
        Assert.Equal(TapeTransportMode.FastForward, t.Mode);
        Assert.True(t.IsShuttle);
        Assert.True(t.MuteAudio);
    }

    [Fact]
    public void FastForward_Skips_Rom_Blocks_Aggressively()
    {
        var tape = MiniTape();
        var pulses = new TapePulsePlayer();
        pulses.Attach(tape);
        var t = new TapeTransport(pulses);
        t.Attach(tape);
        t.CycleFastForward(); // ×2
        int before = tape.Position;
        t.Tick(70_000, allowRomTrapSkip: true);
        Assert.True(tape.Position > before, $"expected skip; before={before} after={tape.Position}");
    }
}
