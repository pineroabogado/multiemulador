# Arquitectura — MULTIEMULADOR (ZX Spectrum)

## Capas

| Proyecto | Rol |
|----------|-----|
| `MultiEmulator.Spectrum` | Núcleo: Z80, ULA, memoria, cinta, disco Beta, audio, catálogo/carga |
| `MultiEmulator.App` | UI Avalonia + host (frames ~50 Hz, WaveOut PCM16, Xbox) |
| `MultiEmulator.Spectrum.Tests` | Regresión (Arkanoid, floating bus, cinta, catálogo…) |

## Bucle de host

1. `EmulatorHost` ejecuta **exactamente 1** `RunFrame()` por tick (~19,97 ms).
2. Presenta framebuffer y bombea **882** muestras PCM16 @ 44,1 kHz.
3. No hay warp ×N en partida (el warp solo aplica con Fast Load EAR activo).

## CPU / ULA

- Z80 con contención de memoria `$4000–$7FFF` (tabla Fuse `65432100`).
- Contención I/O Fuse/ZEsarUX: `ula_contend_port_early` / `late` en IN/OUT (crítica para beeper `OUT (FE)`).
- Floating bus (puertos no conectados): modelo Fuse, primer data byte **14338** (48K) / **14364** (128K), ciclo idle/idle/bitmap/attr/… .
- Muestreo del bus en el último T del ciclo I/O (`PartialTStates`), no en `+1` fijo.

## Carga de medios

- `LoadOrchestrator` + `load-solutions.json`: estrategias por título (ForceTap, FlashLoad, etc.).
- FlashLoad TAP→USR alineado con Spectaculator (IFF + un frame sin IRQ).
- Carpeta local `JUEGOS/` tiene prioridad sobre descargas del catálogo (no se versiona en git).

## Audio

- Beeper ULA 4 niveles (EAR+MIC), buffer interno 192 kHz → 44,1 kHz con LPF/DC-block.
- Host: NAudio `WaveOutEvent`, PCM **16-bit** mono (más fiable que IEEE float en algunos drivers).
