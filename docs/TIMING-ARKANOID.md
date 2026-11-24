# Timing: Arkanoid (pala, ritmo, FX)

Caso de regresión frente a Spectaculator / Fuse / ZEsarUX / SpecEmu / RVM.

## Síntomas que hubo que resolver

1. **Pala (Vaus) desaparece** si el floating bus se muestrea mal (`+1` en lugar del último T del IN).
2. **Juego ~2× rápido** si el wait de vsync `@848F` (`IN 28FFh` mientras FF) sale varias veces por frame de vídeo.
3. **FX mudos** si se “arregla” el ritmo con parches que rompen el **doble `CALL 848F`** (p.ej. `@8E8C`) o con un busy-wait que se come la CPU del beeper `@F070`.

## Qué hacen los emuladores de referencia

Ninguno de los cinco de `EMULADORES/README.md` parchea Arkanoid por título:

| Emulador | Floating bus | Contención I/O | Parche juego |
|----------|--------------|----------------|--------------|
| Fuse | `spectrum_unattached_port`, primer byte **14338** | `ula_contend_port_*` | No |
| ZEsarUX | Igual (documenta Arkanoid) | Igual | No |
| SpecEmu | Floating Bus en núcleo | Preciso (asm) | No |
| Retro Virtual Machine | Floating + contention | Sí | No |
| Spectaculator | Floating + `loudspeaker.dll` | Sí | No |

## Solución en MULTIEMULADOR

1. **FB Fuse** + muestreo `PartialTStates` → pala estable.
2. **Contención I/O ULA** en IN/OUT → el beeper `OUT (FE)` consume T reales en paper (tono y duración).
3. **`FloatingBusVsyncLatch` / auto-detect `@848F = F3 01 FF 28`**:
   - Tras la primera salida del wait (`PC→8498`) se deja un margen (~120 T) para el doble CALL.
   - Después, lecturas de floating bus devuelven `0xFF` hasta fin de frame → el siguiente wait espira de verdad (1 sync/frame).
   - Se activa solo al detectar el wait en RAM (también si se abre el TAP sin pasar por el catálogo).

## Tags en `load-solutions.json` (Arkanoid)

- `ForceTap`, `FloatingBus`, `FloatingBusVsyncLatch`, `InstantFlashIffOn`, `HealthRetrySibling`

## Tests

- `ArkanoidPaddleTests`, `ArkanoidLoadPipelineTests`, `FloatingBusTests`

## Pendiente — FX / beeper (abierto)

**Estado: PENDIENTE (2026-11-24).** En partida el usuario **sigue sin oír** los efectos de Arkanoid
tras varios intentos. Pala y ritmo 1× sí están OK. Otros juegos sí tienen FX audibles.

### Evidencia ya medida (`ArkanoidBeeperTraceTests`)

- Núcleo: `MuteBeeper=false`, throttle ON, rutina `@F089`/`@F0AB` con muchos `OUT (FE)`.
- Portadora EAR ~**18 kHz** (~96 T/flanco); lo audible debería ser la envolvente PWM.
- Hay energía en el buffer / WAV de diagnóstico (`JUEGOS/arkanoid-beeper.wav`, no versionado);
  **eso no se traduce en FX percibidos en la app.**

### Intentos que no cerraron el hueco (no reabrir a ciegas)

| Enfoque | Resultado |
|---------|-----------|
| Latch FB agresivo / trampolín `EBA4` / skip-to-frame-end | Rompe ritmo o silencia más |
| Quitar `DiscardOnBufferOverflow` + tope cola 400 ms | Usuario: sigue sin FX |
| LPF/DC “abiertos” | No basta |
| Beeper Fuse-like (caja + ULA asimétrico + HPF 750 Hz) | Usuario: sigue sin FX |
| Flanco EAR en el T del `OUT (FE)` | Hecho; no resolvió la percepción |

### Pista para retomar

Contrastar **lado a lado con Spectaculator/Fuse** el mismo TAP en partida (golpe bola/ladrillo),
capturar PCM de ambos y comparar. Revisar si el vsync latch deja CPU al beeper en los frames
de impacto, o si el host WaveOut/dispositivo descarta distinto que el WAV de test.
Referencias: Fuse `speaker_filter`/`ula_filter`, bootcamp PWM 1-bit.

**No parchear Arkanoid por título** hasta tener esa comparación.
