# MULTIEMULADOR — ZX Spectrum

Emulador de ZX Spectrum **48K / 128K** para Windows (.NET 8 + Avalonia), orientado a partida jugable (cinta, catálogo, mando Xbox) con timings ULA alineados a Fuse / Spectaculator.

## Características

- Emulación 48K y 128K (Z80, ULA, teclado, beeper, AY, paging `7FFD`)
- Contención de memoria e **I/O** estilo Fuse (beeper y floating bus)
- Floating bus (Arkanoid, Sidewize, Cobra…): primer data byte **14338** (48K)
- Cinta `.tap` / `.tzx` con trampas ROM, FlashLoad y registro `load-solutions.json`
- Snapshots `.z80` / `.sna`, guardado de estado, POKEs / `.pok`
- Mando Xbox (XInput) → Kempston / Sinclair
- Abrir medios (menú o drag & drop), incluidos ZIP / RAR / 7z
- Catálogo online + caché; prioridad opcional a carpeta local `JUEGOS/` (no versionada)
- ROMs de sistema en `roms/zx-spectrum/` (aviso Amstrad incluido)

## Requisitos

- Windows 10+
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar

## Compilar y ejecutar

```bash
dotnet build
dotnet run --project src/MultiEmulator.App
```

O `MULTIEMULADOR.bat`. Publicar:

```bash
dotnet publish src/MultiEmulator.App -c Release -r win-x64 --self-contained false -o publish/win-x64
```

## Tests

```bash
dotnet test
```

Incluyen regresión de **Arkanoid** (pala / pipeline FlashLoad), floating bus y cinta.

## Controles

| Acción | Tecla / mando |
|--------|----------------|
| Teclado Spectrum | Teclas PC → matriz ULA (ventana activa) |
| Cursores | Flechas = Caps Shift + 5/6/7/8 |
| Pausa | F5 |
| Guardar estado | F8 |
| Joystick | Kempston (defecto) o Sinclair 1; Xbox D-pad/stick + A |

## Documentación

| Documento | Contenido |
|-----------|-----------|
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Capas, host, ULA, carga, audio |
| [docs/TIMING-ARKANOID.md](docs/TIMING-ARKANOID.md) | Pala, ritmo 1×, FX y sync vsync |
| [docs/REFERENCE-EMULATORS.md](docs/REFERENCE-EMULATORS.md) | Lista de emuladores usados como referencia |

### Pendiente conocido

- **Arkanoid — FX / beeper:** el sonido de efectos en partida sigue sin oírse de forma fiable (otros juegos sí). Detalle y intentos fallidos en [docs/TIMING-ARKANOID.md](docs/TIMING-ARKANOID.md#pendiente--fx--beeper-abierto).

## Legal — ROMs de sistema

Amstrad have kindly given their permission for the redistribution of their copyrighted material but retain that copyright.

No distribuyas cintas comerciales con el repo. El catálogo debe apuntar a URLs con licencia explícita (homebrew, dominio público, etc.).

## Estructura

```
src/MultiEmulator.Spectrum      # núcleo
src/MultiEmulator.App           # UI Avalonia + host
src/MultiEmulator.Spectrum.Tests
roms/zx-spectrum/               # ROMs 48/128/TR-DOS + aviso Amstrad
assets/catalog/zx-spectrum/     # índice, metadata, load-solutions.json
assets/legal/
docs/                           # arquitectura y timing
EMULADORES/                     # solo local (gitignore): Fuse, ZEsarUX, SpecEmu, RVM, Spectaculator
JUEGOS/                         # solo local (gitignore): TAPs de prueba
```

## .gitignore relevante

- `EMULADORES/` — binarios de referencia (no subir)
- `JUEGOS/` — medios con copyright
- `publish/`, `**/bin/`, `**/obj/`
