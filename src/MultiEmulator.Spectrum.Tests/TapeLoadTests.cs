using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Media;

namespace MultiEmulator.Spectrum.Tests;

public class TapeLoadTests
{
    [Fact]
    public void TrapLoad_WritesPayload_WhenCarrySet()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        for (int i = 0; i < 80; i++) m.RunFrame();

        // Minimal TAP: header-like flag 0 block + data flag 0xFF
        // We'll call trap by setting regs and PC directly.
        byte[] payload = new byte[10];
        for (int i = 0; i < 10; i++) payload[i] = (byte)(0xA0 + i);
        var tap = BuildTapBlock(0xFF, payload);
        m.InsertTape(MultiEmulator.Spectrum.Tape.TapeImage.LoadTap(tap));
        m.Tape.AutoLoad = false;

        m.Registers.A = 0xFF;
        m.Registers.F = 0x01; // CF=1 load
        m.Registers.IX = 0x4000;
        m.Registers.DE = 10;
        m.Registers.PC = 0x0556;

        Assert.True(m.Tape.TryTrapLoad());
        for (int i = 0; i < 10; i++)
            Assert.Equal((byte)(0xA0 + i), m.Memory.Read((ushort)(0x4000 + i)));
        Assert.Equal(0x05E2, m.Registers.PC);
        Assert.Equal(0x01, m.Registers.F & 0x01);
    }

    [Fact]
    public void ArkanoidTap_InstantLoad_JumpsToUsrWithoutLastK()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string zip = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "JUEGOS", "Arkanoid.tap.zip"));
        if (!File.Exists(zip))
        {
            zip = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tap.zip";
            if (!File.Exists(zip)) return;
        }

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        MediaLoader.OpenPath(m, zip);

        Assert.True(m.Tape.UsedInstantLoad, "expected instant flash+USR");
        Assert.True(m.Tape.Tape != null && m.Tape.Tape.Blocks.Count > 0, "tape blocks");
        Assert.Equal(0xFF37, m.Registers.PC); // RANDOMIZE USR 65335
        Assert.Equal(0, m.Memory.Read(23560)); // no LAST_K LOAD ""

        for (int i = 0; i < 120; i++)
            m.RunFrame();

        Assert.True(m.Tape.Tape!.Position > 4, $"pos={m.Tape.Tape.Position}");
        Assert.True(
            CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800)) > 500,
            "expected loading screen / game pixels");
    }

    [Fact]
    public void FlyingSharkTzx_InstantLoad_ReachesGameCode()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string zip = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tzx.zip";
        if (!File.Exists(zip)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        MediaLoader.OpenPath(m, zip);
        Assert.True(m.Tape.UsedInstantLoad, "expected instant flash+USR");
        Assert.Equal(0xCD00, m.Registers.PC);
        Assert.True(m.Tape.FastLoad, "Fast Load on by default (Spectaculator parity)");
        Assert.True(m.Tape.IsFastLoadActive || m.Tape.HasPendingEarBlocks(),
            "turbo EAR remaining after flash should engage Fast Load");

        // Spectaculator Fast Loading: warp CPU+EAR (~64 frames/tick) → segundos, no minutos.
        // No usar PC>=0x8000 como until: el loader arranca en CD00.
        int hostTicks = m.Tape.RunFastLoadBurst(
            () => m.RunFrame(),
            maxHostTicks: 400,
            until: () => m.Tape.Tape!.Position >= 130
                         && !m.Tape.IsFastLoadActive
                         && !m.Registers.Halted
                         && m.Registers.PC is >= 0x8000 and < 0xCD00);
        Assert.True(hostTicks < 350, $"expected Fast Load finish quickly, hostTicks={hostTicks}");

        for (int i = 0; i < 200; i++)
            m.RunFrame();

        Assert.False(m.Registers.Halted, "HALT con IFF=0 tras turbo EAR");
        Assert.True(m.Registers.PC >= 0x8000, $"expected game RAM PC, got {m.Registers.PC:X4}");
        Assert.True(
            CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800)) > 200,
            "expected game / menu pixels after load");
    }

    [Fact]
    public void FlyingSharkTap_PrefersTzxSibling()
    {
        string tap = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tap.zip";
        string tzx = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tzx.zip";
        if (!File.Exists(tap) || !File.Exists(tzx)) return;

        Assert.True(MediaLoader.TryPreferTzxSibling(tap, out string preferred));
        Assert.Equal(tzx, preferred);
    }

    [Fact]
    public void FlyingSharkTap_ForcedTap_HlePrepJumpsToUsr()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string tap = @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\FlyingShark.tap.zip";
        if (!File.Exists(tap)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        // Forzar TAP aunque exista TZX hermano (HLE POKE/DATA + USR 52480).
        MediaLoader.OpenPath(m, tap, MultiEmulator.Spectrum.Services.RomMediaFormat.Tap);

        Assert.True(m.Tape.UsedInstantLoad, "HLE prep + USR");
        Assert.Equal(0xCD00, m.Registers.PC); // USR VAL "52480"
        Assert.True(m.Tape.Tape is { IsPlaying: true });
        Assert.True(m.Tape.Tape.Position >= 4, "CODE Shark1 flasheado; quedan bloques");
        Assert.Equal(195, m.Memory.Read(52612)); // POKE JP 32768
        Assert.Equal(0x00, m.Memory.Read(52613));
        Assert.Equal(0x80, m.Memory.Read(52614));
        Assert.Equal(205, m.Memory.Read(32768)); // DATA → CALL …

        int pos0 = m.Tape.Tape.Position;
        // El loader en 32768 llama LD-BYTES (trampas ROM) sobre el resto del TAP.
        for (int i = 0; i < 400; i++)
            m.RunFrame();
        Assert.True(
            m.Tape.Tape.Position > pos0 || m.Registers.PC >= 0x8000,
            $"TAP loader no avanza: pos={m.Tape.Tape.Position} PC={m.Registers.PC:X4}");
    }

    private static string? Find1942TapZip()
    {
        foreach (string zip in new[]
                 {
                     @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\1942.tap.zip",
                     @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\tmp-1942\tosec-tap.zip",
                 })
        {
            if (File.Exists(zip)) return zip;
        }
        return null;
    }

    [Fact]
    public void Elite1942Tap_BasicPokesAfterUsr_DoNotRequireInterpreter()
    {
        string? zip = Find1942TapZip();
        if (zip is null) return;

        var (name, data) = ArchiveMedia.ExtractPreferredMediaDeep(zip);
        Assert.EndsWith(".tap", name, StringComparison.OrdinalIgnoreCase);
        var tape = MultiEmulator.Spectrum.Tape.TapeImage.LoadTap(data);
        Assert.True(tape.Blocks.Count >= 2);
        // Trainer POKE tras USR 65001 no debe forzar HLE BioToxin.
        Assert.False(
            MultiEmulator.Spectrum.Tape.TapeLoader.BasicRequiresInterpreterBeforeUsr(tape.Blocks[1].Data));
    }

    [Fact]
    public void Elite1942Tap_InstantLoad_ReachesGameCode()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;
        string? zip = Find1942TapZip();
        if (zip is null) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);

        MediaLoader.OpenPath(m, zip);

        Assert.True(m.Tape.UsedInstantLoad, "expected instant flash+USR");
        Assert.Equal(65001, m.Registers.PC); // RANDOMIZE USR 65001
        Assert.Equal(0x31, m.Memory.Read(65001)); // stub LD SP,…
        // BASIC RANDOMIZE USR deja IFF activas; el stub @65001 no hace EI.
        Assert.True(m.Registers.IFF1, "IFF1 must stay on after flash→USR (menu needs IRQs)");
        Assert.True(m.Registers.IFF2, "IFF2 must stay on after flash→USR");
        Assert.True(
            CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800)) > 500,
            "loading screen should be flashed");

        for (int i = 0; i < 400; i++)
            m.RunFrame();

        Assert.True(m.Registers.PC >= 0x8000, $"expected game RAM PC, got {m.Registers.PC:X4}");
        Assert.NotEqual(0x6F97, m.Registers.PC); // Soft Library Z80 mid-load hang
        Assert.True(m.Registers.IFF1, "IFF1 must remain on in title loop");
        Assert.True(
            CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800)) > 200,
            "expected title/menu pixels after USR");

        // Menú Elite: tecla 4 (Cursor) debe alterar la pantalla (Spectaculator parity).
        int scrBefore = CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800));
        m.Keyboard.SetKey(3, 3, true); // '4'
        for (int i = 0; i < 8; i++) m.RunFrame();
        m.Keyboard.SetKey(3, 3, false);
        for (int i = 0; i < 60; i++) m.RunFrame();
        int scrAfter = CountNonZero(m.Memory.ScreenBank.AsSpan(0, 0x1800));
        Assert.NotEqual(scrBefore, scrAfter);
    }

    [Fact]
    public void Reset_CancelsPendingAutoType()
    {
        if (!TestRomPaths.TryRead48(out var rom)) return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        for (int i = 0; i < 80; i++) m.RunFrame();

        m.Tape.BeginAutoLoadCommand(1);
        m.Reset();
        m.Tape.ApplyAutoType(m.Keyboard);
        Assert.Equal(0, m.Memory.Read(23560));
    }

    private static int CountNonZero(ReadOnlySpan<byte> s)
    {
        int n = 0;
        foreach (var b in s)
            if (b != 0) n++;
        return n;
    }

    private static byte[] BuildTapBlock(byte flag, byte[] payload)
    {
        // len = 1 flag + payload + 1 checksum
        int body = 1 + payload.Length + 1;
        var tap = new byte[2 + body];
        tap[0] = (byte)(body & 0xFF);
        tap[1] = (byte)(body >> 8);
        tap[2] = flag;
        Buffer.BlockCopy(payload, 0, tap, 3, payload.Length);
        byte xor = flag;
        foreach (var b in payload) xor ^= b;
        tap[3 + payload.Length] = xor;
        return tap;
    }
}
