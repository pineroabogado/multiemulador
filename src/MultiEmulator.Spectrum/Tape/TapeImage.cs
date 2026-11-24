namespace MultiEmulator.Spectrum.Tape;

public sealed class TapeImage
{
    public List<TapeBlock> Blocks { get; } = new();
    public int Position { get; set; }
    public bool IsPlaying { get; set; }
    public string? SourcePath { get; set; }
    public string? Description { get; set; }

    public static TapeImage LoadTap(byte[] data)
    {
        var tape = new TapeImage();
        int i = 0;
        while (i + 2 <= data.Length)
        {
            int len = data[i] | (data[i + 1] << 8);
            i += 2;
            if (len <= 0 || i + len > data.Length)
                break;
            var block = new byte[len];
            Buffer.BlockCopy(data, i, block, 0, len);
            i += len;
            bool isHeader = block[0] == 0x00;
            tape.Blocks.Add(new TapeBlock
            {
                Kind = TapeBlockKind.Data,
                Flag = block[0],
                Data = block,
                TzxId = 0,
                PilotCount = isHeader ? 8063 : 3223,
                PauseMs = 1000
            });
        }
        return tape;
    }

    public static TapeImage LoadTzx(byte[] data)
    {
        var tape = new TapeImage();
        if (data.Length < 10 ||
            data[0] != (byte)'Z' || data[1] != (byte)'X' ||
            data[2] != (byte)'T' || data[3] != (byte)'a')
            throw new InvalidDataException("Not a TZX file");

        var raw = new List<TapeBlock>();
        var loopStack = new Stack<(int startIndex, int repsLeft)>();
        int i = 10;

        while (i < data.Length)
        {
            byte id = data[i++];
            switch (id)
            {
                case 0x10: // Standard speed data
                {
                    if (i + 4 > data.Length) return Finish(tape, raw);
                    int pause = ReadU16(data, ref i);
                    int len = ReadU16(data, ref i);
                    if (!TryReadPayload(data, ref i, len, out var payload))
                        return Finish(tape, raw);
                    bool header = payload[0] == 0x00;
                    raw.Add(new TapeBlock
                    {
                        Kind = TapeBlockKind.Data,
                        Flag = payload[0],
                        Data = payload,
                        TzxId = 0x10,
                        PauseMs = pause,
                        PilotCount = header ? 8063 : 3223
                    });
                    break;
                }
                case 0x11: // Turbo speed data
                {
                    if (i + 18 > data.Length) return Finish(tape, raw);
                    int pilotPulse = ReadU16(data, ref i);
                    int sync1 = ReadU16(data, ref i);
                    int sync2 = ReadU16(data, ref i);
                    int zero = ReadU16(data, ref i);
                    int one = ReadU16(data, ref i);
                    int pilotCount = ReadU16(data, ref i);
                    int usedBits = data[i++];
                    int pause = ReadU16(data, ref i);
                    int len = ReadU24(data, ref i);
                    if (!TryReadPayload(data, ref i, len, out var payload))
                        return Finish(tape, raw);
                    raw.Add(new TapeBlock
                    {
                        Kind = TapeBlockKind.Data,
                        Flag = payload[0],
                        Data = payload,
                        TzxId = 0x11,
                        PilotPulse = pilotPulse,
                        Sync1 = sync1,
                        Sync2 = sync2,
                        ZeroPulse = zero,
                        OnePulse = one,
                        PilotCount = pilotCount,
                        UsedBitsLast = usedBits == 0 ? 8 : usedBits,
                        PauseMs = pause
                    });
                    break;
                }
                case 0x12: // Pure tone — imprescindible para cargadores turbo
                {
                    if (i + 4 > data.Length) return Finish(tape, raw);
                    int tonePulse = ReadU16(data, ref i);
                    int toneCount = ReadU16(data, ref i);
                    raw.Add(new TapeBlock
                    {
                        Kind = TapeBlockKind.PureTone,
                        TzxId = 0x12,
                        TonePulse = tonePulse,
                        ToneCount = toneCount
                    });
                    break;
                }
                case 0x13: // Pulse sequence
                {
                    if (i >= data.Length) return Finish(tape, raw);
                    int n = data[i++];
                    if (i + n * 2 > data.Length) return Finish(tape, raw);
                    var pulses = new int[n];
                    for (int p = 0; p < n; p++)
                        pulses[p] = ReadU16(data, ref i);
                    raw.Add(new TapeBlock
                    {
                        Kind = TapeBlockKind.PulseSequence,
                        TzxId = 0x13,
                        Pulses = pulses
                    });
                    break;
                }
                case 0x14: // Pure data
                {
                    if (i + 10 > data.Length) return Finish(tape, raw);
                    int zero = ReadU16(data, ref i);
                    int one = ReadU16(data, ref i);
                    int usedBits = data[i++];
                    int pause = ReadU16(data, ref i);
                    int len = ReadU24(data, ref i);
                    if (!TryReadPayload(data, ref i, len, out var payload))
                        return Finish(tape, raw);
                    raw.Add(new TapeBlock
                    {
                        Kind = TapeBlockKind.PureData,
                        Flag = payload.Length > 0 ? payload[0] : (byte)0,
                        Data = payload,
                        TzxId = 0x14,
                        ZeroPulse = zero,
                        OnePulse = one,
                        UsedBitsLast = usedBits == 0 ? 8 : usedBits,
                        PauseMs = pause,
                        PilotCount = 0
                    });
                    break;
                }
                case 0x15:
                {
                    if (i + 8 > data.Length) return Finish(tape, raw);
                    i += 5;
                    int len = ReadU24(data, ref i);
                    i += len;
                    break;
                }
                case 0x18:
                case 0x19:
                {
                    if (i + 4 > data.Length) return Finish(tape, raw);
                    int len = ReadU32(data, ref i);
                    i += len;
                    break;
                }
                case 0x20:
                    i += 2;
                    break;
                case 0x21:
                {
                    if (i >= data.Length) return Finish(tape, raw);
                    int n = data[i++];
                    i += n;
                    break;
                }
                case 0x22:
                    break;
                case 0x23:
                    i += 2;
                    break;
                case 0x24:
                {
                    if (i + 2 > data.Length) return Finish(tape, raw);
                    int reps = ReadU16(data, ref i);
                    if (reps < 1) reps = 1;
                    loopStack.Push((raw.Count, reps));
                    break;
                }
                case 0x25:
                {
                    if (loopStack.Count == 0) break;
                    var (start, reps) = loopStack.Pop();
                    if (reps <= 1) break;
                    var slice = raw.Skip(start).ToList();
                    for (int r = 1; r < reps; r++)
                        raw.AddRange(slice);
                    break;
                }
                case 0x26:
                {
                    if (i + 2 > data.Length) return Finish(tape, raw);
                    int n = ReadU16(data, ref i);
                    i += n * 2;
                    break;
                }
                case 0x27:
                    break;
                case 0x28:
                {
                    if (i + 2 > data.Length) return Finish(tape, raw);
                    int len = ReadU16(data, ref i);
                    i += len;
                    break;
                }
                case 0x2A:
                    i += 4;
                    break;
                case 0x2B:
                    i += 5;
                    break;
                case 0x30:
                {
                    if (i >= data.Length) return Finish(tape, raw);
                    int n = data[i++];
                    if (i + n <= data.Length)
                    {
                        tape.Description = System.Text.Encoding.ASCII.GetString(data, i, n);
                        i += n;
                    }
                    break;
                }
                case 0x31:
                {
                    if (i + 2 > data.Length) return Finish(tape, raw);
                    i++;
                    int n = data[i++];
                    i += n;
                    break;
                }
                case 0x32:
                {
                    if (i + 2 > data.Length) return Finish(tape, raw);
                    int len = ReadU16(data, ref i);
                    i += len;
                    break;
                }
                case 0x33:
                {
                    if (i >= data.Length) return Finish(tape, raw);
                    int n = data[i++];
                    i += n * 3;
                    break;
                }
                case 0x34:
                    i += 8;
                    break;
                case 0x35:
                {
                    if (i + 20 > data.Length) return Finish(tape, raw);
                    i += 16;
                    int len = ReadU32(data, ref i);
                    i += len;
                    break;
                }
                case 0x5A:
                    i += 9;
                    break;
                default:
                    return Finish(tape, raw);
            }
        }

        return Finish(tape, raw);
    }

    private static TapeImage Finish(TapeImage tape, List<TapeBlock> raw)
    {
        tape.Blocks.AddRange(raw);
        return tape;
    }

    private static bool TryReadPayload(byte[] data, ref int i, int len, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (len <= 0 || i + len > data.Length)
            return false;
        payload = new byte[len];
        Buffer.BlockCopy(data, i, payload, 0, len);
        i += len;
        return true;
    }

    private static int ReadU16(byte[] data, ref int i)
    {
        int v = data[i] | (data[i + 1] << 8);
        i += 2;
        return v;
    }

    private static int ReadU24(byte[] data, ref int i)
    {
        int v = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16);
        i += 3;
        return v;
    }

    private static int ReadU32(byte[] data, ref int i)
    {
        int v = data[i] | (data[i + 1] << 8) | (data[i + 2] << 16) | (data[i + 3] << 24);
        i += 4;
        return v;
    }

    public TapeBlock? NextBlock()
    {
        if (Position >= Blocks.Count) return null;
        return Blocks[Position++];
    }

    public void Rewind() => Position = 0;
}
