namespace MultiEmulator.Spectrum.Ula;

public sealed class SpectrumUla
{
    public const int ScreenWidth = 256;
    public const int ScreenHeight = 192;
    public const int FrameTStates48 = 69888;
    public const int FrameTStates128 = 70908;

    /// <summary>
    /// T-states from IRQ to first floating-bus data byte (48K).
    /// Fuse (no Ramsoft 14347): 14338 — necesario para Arkanoid/Sidewize.
    /// </summary>
    public const int FloatingBusFirstPaper48 = 14338;
    /// <summary>T-states per scanline (48K).</summary>
    public const int TStatesPerLine48 = 224;
    /// <summary>T-states from IRQ to first floating-bus data byte (128K, Fuse).</summary>
    public const int FloatingBusFirstPaper128 = 14364;
    /// <summary>T-states per scanline (128K).</summary>
    public const int TStatesPerLine128 = 228;

    // AARRGGBB — Spectrum ink/paper: 0 black, 1 blue, 2 red, 3 magenta, 4 green, 5 cyan, 6 yellow, 7 white
    private static readonly uint[] Palette =
    {
        0xFF000000, 0xFF0000D7, 0xFFD70000, 0xFFD700D7,
        0xFF00D700, 0xFF00D7D7, 0xFFD7D700, 0xFFD7D7D7,
        0xFF000000, 0xFF0000FF, 0xFFFF0000, 0xFFFF00FF,
        0xFF00FF00, 0xFF00FFFF, 0xFFFFFF00, 0xFFFFFFFF
    };

    private readonly uint[] _framebuffer = new uint[ScreenWidth * ScreenHeight];
    private byte _border;
    private bool _earOut;
    private bool _micOut;
    private bool _tapeIn;
    private bool _flashPhase;
    private int _frameCounter;
    /// <summary>Obsoleto: el vsync de Arkanoid se corrige con parche en 848F (LoadPolicies).</summary>
    public bool LatchFloatingBusOncePerFrame { get; set; }

    private int _skipToFrameEndT;

    public byte Border
    {
        get => _border;
        set => _border = (byte)(value & 0x07);
    }

    /// <summary>OUT (FE) bit 4 — EAR hacia el altavoz.</summary>
    public bool EarBit
    {
        get => _earOut;
        set => _earOut = value;
    }

    /// <summary>OUT (FE) bit 3 — MIC hacia el altavoz (4º nivel ULA).</summary>
    public bool MicOut
    {
        get => _micOut;
        set => _micOut = value;
    }

    /// <summary>Entrada de cinta (IN FE bit 6). No confundir con MicOut.</summary>
    public bool TapeIn
    {
        get => _tapeIn;
        set => _tapeIn = value;
    }

    /// <summary>Compat: antes MicBit se usaba como entrada de cinta.</summary>
    public bool MicBit
    {
        get => _tapeIn;
        set => _tapeIn = value;
    }

    public ReadOnlySpan<uint> Framebuffer => _framebuffer;
    public uint BorderArgb => Palette[_border];

    public void Reset()
    {
        _border = 0;
        _earOut = false;
        _micOut = false;
        _tapeIn = false;
        _flashPhase = false;
        _frameCounter = 0;
        LatchFloatingBusOncePerFrame = false;
        _skipToFrameEndT = 0;
        Array.Clear(_framebuffer);
    }

    public void EndFrame()
    {
        _skipToFrameEndT = 0;
        _frameCounter++;
        if ((_frameCounter & 0x0F) == 0)
            _flashPhase = !_flashPhase;
    }

    public void RequestSkipToFrameEnd(int remainT)
    {
        if (remainT > _skipToFrameEndT)
            _skipToFrameEndT = remainT;
    }

    public int ConsumeSkipToFrameEnd()
    {
        int n = _skipToFrameEndT;
        _skipToFrameEndT = 0;
        return n;
    }

    public void Render(ReadOnlySpan<byte> screenBank)
    {
        for (int y = 0; y < ScreenHeight; y++)
        {
            int pixelAddr = ((y & 0xC0) << 5) | ((y & 0x07) << 8) | ((y & 0x38) << 2);
            int attrAddr = 0x1800 + ((y >> 3) * 32);
            for (int x = 0; x < 32; x++)
            {
                byte pixels = screenBank[pixelAddr + x];
                byte attr = screenBank[attrAddr + x];
                int ink = attr & 0x07;
                int paper = (attr >> 3) & 0x07;
                bool bright = (attr & 0x40) != 0;
                bool flash = (attr & 0x80) != 0;
                if (flash && _flashPhase)
                    (ink, paper) = (paper, ink);
                if (bright)
                {
                    ink |= 0x08;
                    paper |= 0x08;
                }

                uint inkC = Palette[ink];
                uint paperC = Palette[paper];
                int dest = y * ScreenWidth + x * 8;
                for (int b = 0; b < 8; b++)
                    _framebuffer[dest + b] = (pixels & (0x80 >> b)) != 0 ? inkC : paperC;
            }
        }
    }

    public byte ReadFe(byte keyboardRowMask) =>
        // bits 0-4 teclado, bit5=1, bit6=EAR cinta (Fuse/libspectrum), bit7=1
        (byte)(0xA0 | (keyboardRowMask & 0x1F) | (_tapeIn ? 0x40 : 0));

    public void WriteFe(byte value)
    {
        Border = (byte)(value & 0x07);
        _micOut = (value & 0x08) != 0; // MIC
        _earOut = (value & 0x10) != 0; // EAR
    }

    /// <summary>
    /// Contención ULA tipo Fuse <c>spectrum_contend_delay_65432100</c>
    /// (fase indexada como el floating bus Fuse: idle,idle,data…).
    /// </summary>
    private static readonly int[] ContentionDelays = { 5, 4, 3, 2, 1, 0, 0, 6 };

    /// <summary>
    /// Contención de memoria en $4000–$7FFF (48K) durante el paper.
    /// Alineada con el ciclo floating-bus Fuse (offset 1 respecto al primer data byte).
    /// </summary>
    public static int MemoryContentionDelay(int tStatesInFrame, bool is128K)
    {
        // Primer T contendido = 1 antes del primer data byte del floating bus.
        int first = (is128K ? FloatingBusFirstPaper128 : FloatingBusFirstPaper48) - 1;
        int lineTs = is128K ? TStatesPerLine128 : TStatesPerLine48;
        int d = tStatesInFrame - first;
        if (d < 0)
            return 0;
        int line = d / lineTs;
        int phase = d - line * lineTs;
        if ((uint)line >= ScreenHeight || phase >= 128)
            return 0;
        return ContentionDelays[phase & 7];
    }

    /// <summary>
    /// Contención I/O Fuse/ZEsarUX <c>ula_contend_port_early</c> (48K/128K).
    /// Devuelve T a sumar (incluye el +1 base del early).
    /// </summary>
    public static int PortContentionEarly(ushort port, int tStatesInFrame, bool is128K)
    {
        int delay = 0;
        // Fuse/ZEsarUX: si el high del puerto cae en $4000–$7FFF.
        if ((port & 0xC000) == 0x4000)
            delay += MemoryContentionDelay(tStatesInFrame, is128K);
        return delay + 1;
    }

    /// <summary>
    /// Contención I/O Fuse/ZEsarUX <c>ula_contend_port_late</c>.
    /// Puertos pares = ULA (OUT/IN FE del beeper): contend + 2 en paper.
    /// </summary>
    public static int PortContentionLate(ushort port, int tStatesInFrame, bool is128K)
    {
        // port_from_ula_48k: todos los puertos pares.
        if ((port & 1) == 0)
            return MemoryContentionDelay(tStatesInFrame, is128K) + 2;

        if ((port & 0xC000) == 0x4000)
        {
            // Fuse: contend; t++; contend; t++; contend
            int t = tStatesInFrame;
            int c1 = MemoryContentionDelay(t, is128K);
            t += c1 + 1;
            int c2 = MemoryContentionDelay(t, is128K);
            t += c2 + 1;
            int c3 = MemoryContentionDelay(t, is128K);
            return c1 + 1 + c2 + 1 + c3;
        }

        return 2;
    }

    /// <summary>
    /// Floating bus (puertos no conectados). Modelo Fuse <c>spectrum_unattached_port</c>:
    /// primer data byte a <see cref="FloatingBusFirstPaper48"/> / 128;
    /// en el ciclo de 8 T: idle, idle, bitmap, attr, bitmap+1, attr+1, idle, idle.
    /// Ramsoft 14347 + bitmap-first rompía el ritmo de Arkanoid al combinarlo con
    /// muestreo en el último T del IN.
    /// </summary>
    public byte ReadFloatingBus(ReadOnlySpan<byte> screenBank, int tStatesInFrame, bool is128K)
    {
        int firstData = is128K ? FloatingBusFirstPaper128 : FloatingBusFirstPaper48;
        int lineTs = is128K ? TStatesPerLine128 : TStatesPerLine48;
        // Ciclo de 8 T alineado para que firstData caiga en fase 2 (bitmap).
        int cycleStart = firstData - 2;
        int d = tStatesInFrame - cycleStart;
        if (d < 0)
            return 0xFF;

        int line = d / lineTs;
        int phase = d - line * lineTs;
        if ((uint)line >= ScreenHeight || phase >= 128)
            return 0xFF;

        int sub = phase & 7;
        int x = (phase >> 3) << 1; // columna de carácter (par)

        switch (sub)
        {
            case 2: // bitmap
                break;
            case 3: // attr
                break;
            case 4: // bitmap+1
                x++;
                break;
            case 5: // attr+1
                x++;
                break;
            default:
                return 0xFF;
        }

        if ((sub & 1) == 0)
        {
            int pixelAddr = ((line & 0xC0) << 5) | ((line & 0x07) << 8) | ((line & 0x38) << 2) | x;
            return screenBank[pixelAddr];
        }

        int attrAddr = 0x1800 + ((line >> 3) * 32) + x;
        return screenBank[attrAddr];
    }
}
