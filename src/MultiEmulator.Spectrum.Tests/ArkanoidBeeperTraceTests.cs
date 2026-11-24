using MultiEmulator.Spectrum;
using MultiEmulator.Spectrum.Input;
using MultiEmulator.Spectrum.Media;
using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Tests;

/// <summary>
/// Evidencia del FX de Arkanoid: flancos EAR, energía del buffer y sitios OUT (FE)
/// durante la partida. No cambia el comportamiento; deja el informe en JUEGOS/.
/// </summary>
public class ArkanoidBeeperTraceTests
{
    private static readonly string TapZip =
        @"C:\Users\piner\Desktop\DESARROLLOS\MULTIEMULADOR\JUEGOS\Arkanoid.tap.zip";

    [Fact]
    public void InGame_RecordsBeeperEvidence()
    {
        if (!TestRomPaths.TryRead48(out var rom))
            return;
        if (!File.Exists(TapZip))
            return;

        var m = new SpectrumMachine();
        m.LoadSystemRoms(rom, null);
        m.SetMachine(MachineType.Spectrum48);
        m.Joystick.Type = JoystickType.Kempston;
        for (int i = 0; i < 120; i++)
            m.RunFrame();

        MediaLoader.OpenPath(m, TapZip, RomMediaFormat.Tap);
        LoadPolicies.ApplyFlashLoadUsrEntry(m);
        LoadPolicies.ApplyFloatingBusVsyncLatch(m);
        for (int i = 0; i < 500; i++)
            m.RunFrame();

        bool entered = false;
        for (int round = 0; round < 80 && !entered; round++)
        {
            TapKey(m, 3, 0, 3);
            TapKey(m, 7, 0, 3);
            TapKey(m, 4, 0, 3);
            TapKey(m, 6, 0, 3);
            m.Joystick.Fire = true;
            for (int i = 0; i < 5; i++) m.RunFrame();
            m.Joystick.Fire = false;
            for (int i = 0; i < 10; i++) m.RunFrame();
            if (CountBottomInk(m) > 40)
                entered = true;
        }

        Assert.True(entered, $"sin partida PC={m.Registers.PC:X4}");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"PC={m.Registers.PC:X4} throttle={m.ArkanoidVsyncThrottle} mute={m.Tape.MuteBeeper} fast={m.Tape.IsFastLoadActive}");
        sb.AppendLine("wait@848F " + HexDump(m, 0x848F, 24));
        sb.AppendLine("around@8E70 " + HexDump(m, 0x8E70, 48));
        sb.AppendLine("fx@F040 " + HexDump(m, 0xF040, 192));

        var outFe = new List<int>();
        for (int a = 0x4000; a < 0xFFFE; a++)
        {
            if (m.Memory.Read((ushort)a) == 0xD3 && m.Memory.Read((ushort)(a + 1)) == 0xFE)
                outFe.Add(a);
        }

        sb.AppendLine("OUT (FE),A @ " + string.Join(" ", outFe.Select(a => a.ToString("X4"))));

        m.BeeperTrace = true;
        m.ResetBeeperTrace();
        m.Joystick.Fire = true;

        const int frames = 400;
        int loud = 0;
        double energy = 0;
        double peak = 0;
        int audibleFrames = 0;
        int edgesAtBucket = 0;
        long prevEdges = 0;
        long prevRunT = 0;
        var edgeGapsT = new List<double>(256);
        var wav = new List<short>(frames * 882);
        Span<float> pcm = stackalloc float[882];
        for (int f = 0; f < frames; f++)
        {
            if ((f % 50) == 0)
            {
                sb.AppendLine($"  t={f} pc={m.Registers.PC:X4} bucketEdges={m.TraceEarEdges - edgesAtBucket} energy={energy:F1}");
                edgesAtBucket = m.TraceEarEdges;
            }

            m.Joystick.Left = (f / 30) % 2 == 0;
            m.Joystick.Right = !m.Joystick.Left;
            m.RunFrame();

            long dEdges = m.TraceEarEdges - prevEdges;
            long dT = m.TraceRunT - prevRunT;
            prevEdges = m.TraceEarEdges;
            prevRunT = m.TraceRunT;
            if (dEdges >= 8 && dT > 0)
            {
                // Periodo medio entre flancos → tono aproximado.
                double tPerEdge = (double)dT / dEdges;
                edgeGapsT.Add(tPerEdge);
            }

            m.ReadAudioSamples(pcm);
            double e = 0;
            int crossings = 0;
            float prev = 0;
            for (int i = 0; i < pcm.Length; i++)
            {
                float s = pcm[i];
                e += s * s;
                float a = Math.Abs(s);
                if (a > peak) peak = a;
                if ((prev <= 0 && s > 0) || (prev >= 0 && s < 0))
                    crossings++;
                prev = s;
                wav.Add((short)(Math.Clamp(s, -1f, 1f) * 28000f));
            }

            energy += e;
            if (e > 0.05) audibleFrames++;
            if (e > 1.0)
            {
                loud++;
                if (loud <= 3 || f >= frames - 40)
                {
                    double hz = dEdges > 0 && dT > 0
                        ? (3_500_000.0 / (2.0 * dT / dEdges))
                        : 0;
                    sb.AppendLine($"  loud f={f} e={e:F2} xing={crossings} ~{hz:F0}Hz pc={m.Registers.PC:X4}");
                }
            }
        }

        if (edgeGapsT.Count > 0)
        {
            edgeGapsT.Sort();
            double mid = edgeGapsT[edgeGapsT.Count / 2];
            double hz = 3_500_000.0 / (2.0 * mid);
            sb.AppendLine($"edgePeriodT median={mid:F1} (~{hz:F0} Hz square) samples={edgeGapsT.Count}");
        }

        sb.AppendLine(
            $"frames={frames} energy={energy:F3} peak={peak:F4} audible={audibleFrames} loud={loud}");
        sb.AppendLine(
            $"earEdges={m.TraceEarEdges} feWrites={m.TraceFeWrites} waitT={m.TraceWaitT} runT={m.TraceRunT} waitFrac={(m.TraceRunT == 0 ? 0 : (double)m.TraceWaitT / m.TraceRunT):F3}");
        foreach (var kv in m.TraceFeSites.OrderByDescending(kv => kv.Value).Take(12))
        {
            sb.AppendLine($"  site {kv.Key:X4} x{kv.Value}  {HexDump(m, kv.Key, 16)}");
        }

        string dir = Path.GetDirectoryName(TapZip)!;
        Directory.CreateDirectory(dir);
        string reportPath = Path.Combine(dir, "arkanoid-beeper-trace.txt");
        File.WriteAllText(reportPath, sb.ToString());
        WriteWav(Path.Combine(dir, "arkanoid-beeper.wav"), wav);

        // La partida tiene que generar flancos EAR. Si no, el FX no está en el buffer.
        Assert.True(m.TraceEarEdges > 0 || energy > 0.05, sb.ToString());
    }

    private static void WriteWav(string path, List<short> samples)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        int dataBytes = samples.Count * 2;
        bw.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataBytes);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(System.Text.Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16);
        bw.Write((short)1);
        bw.Write((short)1);
        bw.Write(44100);
        bw.Write(44100 * 2);
        bw.Write((short)2);
        bw.Write((short)16);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("data"));
        bw.Write(dataBytes);
        foreach (short s in samples)
            bw.Write(s);
    }

    private static string HexDump(SpectrumMachine m, int addr, int len)
    {
        var bytes = new byte[len];
        for (int i = 0; i < len; i++)
            bytes[i] = m.Memory.Read((ushort)(addr + i));
        return Convert.ToHexString(bytes);
    }

    private static void TapKey(SpectrumMachine m, int row, int bit, int frames)
    {
        m.Keyboard.SetKey(row, bit, true);
        for (int i = 0; i < frames; i++) m.RunFrame();
        m.Keyboard.SetKey(row, bit, false);
    }

    private static int CountBottomInk(SpectrumMachine m)
    {
        int n = 0;
        var scr = m.Memory.ScreenBank;
        for (int y = 184; y <= 191; y++)
        {
            int pa = ((y & 0xC0) << 5) | ((y & 0x07) << 8) | ((y & 0x38) << 2);
            for (int x = 0; x < 32; x++)
                n += System.Numerics.BitOperations.PopCount(scr[pa + x]);
        }

        return n;
    }
}
