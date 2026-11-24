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

**Estado: no resuelto.** En partida el usuario sigue sin oír los efectos de sonido de Arkanoid, mientras otros juegos sí tienen FX audibles.

### Qué sí está bien (medido / contrastado)

- Host de audio OK (PCM16; otros títulos suenan).
- El núcleo genera `OUT (FE)` / energía AC en el beeper en diagnósticos (`F070`…); `MuteBeeper` no aplica en partida.
- Pala y ritmo: FB Fuse 14338 + `PartialTStates`, contención I/O ULA, sync 1×/frame (FB lock tras wait + margen del doble `CALL 848F`).

### Intentos que no cerraron el hueco percibido

| Enfoque | Efecto colateral |
|---------|------------------|
| Latch FB “una vez/frame” agresivo | Ritmo OK, FX muy reducidos / silencio |
| Parche wait `848F` → trampolín `EBA4` | Rompe doble `CALL` `@8E8C`; silencea beeper |
| Skip-to-frame-end en re-wait | Mata CPU del FX del resto del frame |
| Throttle host / FB lock diferido | Mejora sync; el usuario sigue sin oír FX |
| Solo contención I/O Fuse | Energía en buffer; ritmo variable; FX no percibidos |

### Pista para retomar (sin implementar ahora)

Los cinco emuladores de referencia **no** parchean Arkanoid: FB + contención I/O bastan allí. La discrepancia puede estar en el **pipeline host** (bomba 882 muestras vs ráfagas del beeper), en el **orden wait/sonido** del bucle del juego bajo nuestro lock, o en un detalle fino de fase del FB distinto a Spectaculator. Contrastar de nuevo con Spectaculator lado a lado y trazar `EAR` edges solo en frames con impacto de bola.

**No seguir iterando parches por título hasta tener esa evidencia.**
