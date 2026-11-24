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
