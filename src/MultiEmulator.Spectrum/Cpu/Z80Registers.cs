namespace MultiEmulator.Spectrum.Cpu;

public sealed class Z80Registers
{
    public byte A, F, B, C, D, E, H, L;
    public byte A2, F2, B2, C2, D2, E2, H2, L2;
    public byte I, R;
    public ushort IX, IY, SP, PC;
    public bool IFF1, IFF2, Halted;
    public byte IM;
    public bool EIDeferred;
    public int TStates;

    public ushort AF
    {
        get => (ushort)((A << 8) | F);
        set { A = (byte)(value >> 8); F = (byte)value; }
    }

    public ushort BC
    {
        get => (ushort)((B << 8) | C);
        set { B = (byte)(value >> 8); C = (byte)value; }
    }

    public ushort DE
    {
        get => (ushort)((D << 8) | E);
        set { D = (byte)(value >> 8); E = (byte)value; }
    }

    public ushort HL
    {
        get => (ushort)((H << 8) | L);
        set { H = (byte)(value >> 8); L = (byte)value; }
    }

    public void Reset()
    {
        A = F = B = C = D = E = H = L = 0;
        A2 = F2 = B2 = C2 = D2 = E2 = H2 = L2 = 0;
        I = R = 0;
        IX = IY = 0;
        SP = 0xFFFF;
        PC = 0;
        IFF1 = IFF2 = Halted = false;
        IM = 0;
        EIDeferred = false;
        TStates = 0;
    }

    public void ExchangeAF()
    {
        (A, A2) = (A2, A);
        (F, F2) = (F2, F);
    }

    public void ExchangeRegisterSets()
    {
        (B, B2) = (B2, B);
        (C, C2) = (C2, C);
        (D, D2) = (D2, D);
        (E, E2) = (E2, E);
        (H, H2) = (H2, H);
        (L, L2) = (L2, L);
    }
}

[Flags]
public enum Z80Flags : byte
{
    C = 0x01,
    N = 0x02,
    PV = 0x04,
    X3 = 0x08,
    H = 0x10,
    X5 = 0x20,
    Z = 0x40,
    S = 0x80
}
