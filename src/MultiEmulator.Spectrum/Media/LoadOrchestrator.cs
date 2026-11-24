using MultiEmulator.Spectrum.Services;

namespace MultiEmulator.Spectrum.Media;

public sealed class LoadAttemptResult
{
    public bool Success { get; init; }
    public string Path { get; init; } = "";
    public LoadStrategyKind Strategy { get; init; }
    public LoadHealthResult? Health { get; init; }
    public string StatusMessage { get; init; } = "";
    public int Attempts { get; init; }
    public LoadSolutionHint? AppliedHint { get; init; }
}

/// <summary>
/// Abre un medio con registro de soluciones (si hay), preflight, health-check
/// post-carga y reintento automático de estrategias/hermanos.
/// </summary>
public static class LoadOrchestrator
{
    public static LoadAttemptResult OpenWithRetry(
        SpectrumMachine machine,
        string path,
        RomMediaFormat format = RomMediaFormat.Auto,
        IEnumerable<string>? extraCandidates = null,
        Action<string>? status = null,
        bool resetBetweenAttempts = true,
        int healthFrames = LoadHealthCheck.DefaultSampleFrames,
        bool runHealthCheck = true,
        LoadSolutionHint? solutionHint = null)
    {
        // Registro primero: preferencias conocidas antes del ranking genérico Auto.
        solutionHint ??= LoadSolutionsStore.Resolve(path: path);
        format = LoadSolutionsStore.ApplyPreferredFormat(format, solutionHint);
        LoadPolicies.ApplySolutionMachineHint(machine, solutionHint);

        if (solutionHint.Solution != null)
            status?.Invoke($"Solución conocida: {solutionHint.Solution.Title}…");

        var attempts = LoadCandidateRanker.BuildAttempts(
            path, format, extraCandidates, inspect: true, solutionHint: solutionHint);
        if (attempts.Count == 0)
        {
            attempts = new List<LoadCandidate>
            {
                new()
                {
                    Path = path,
                    Strategy = LoadStrategyKind.Default,
                    Format = format,
                    Score = 0
                }
            };
        }

        bool hasTape = attempts.Any(a =>
        {
            string k = a.Signals?.Kind ?? RomMediaFormatInfo.Classify(a.Path);
            return k is "tap" or "tzx";
        });
        bool deferMidLoad = solutionHint.HasDeferMidLoadZ80
            || format is RomMediaFormat.Auto or RomMediaFormat.Tap or RomMediaFormat.Tzx;

        LoadAttemptResult? lastFail = null;
        int n = 0;
        bool first = true;

        foreach (var candidate in attempts)
        {
            string kind = candidate.Signals?.Kind ?? RomMediaFormatInfo.Classify(candidate.Path);
            if (hasTape
                && kind is "z80" or "sna"
                && deferMidLoad
                && (candidate.Signals?.LikelyMidLoadSnapshot == true
                    || candidate.Strategy == LoadStrategyKind.SkipSnapshot
                    || solutionHint.HasDeferMidLoadZ80))
            {
                continue;
            }

            n++;
            string strategyName = LoadPolicies.StrategyDisplayName(candidate.Strategy);
            status?.Invoke(n == 1
                ? $"Cargando (estrategia {strategyName})…"
                : $"Reintentando carga (estrategia {strategyName})…");

            try
            {
                if (!first && resetBetweenAttempts)
                    machine.Reset();
                first = false;

                RomMediaFormat openFormat = ResolveOpenFormat(candidate);
                MediaLoader.OpenPath(machine, candidate.Path, openFormat);
                ApplyPostOpenPreflight(machine, candidate, solutionHint);

                if (!runHealthCheck)
                {
                    return new LoadAttemptResult
                    {
                        Success = true,
                        Path = candidate.Path,
                        Strategy = candidate.Strategy,
                        Attempts = n,
                        StatusMessage = BuildSuccessStatus(machine, candidate.Path, strategyName, null),
                        AppliedHint = solutionHint
                    };
                }

                var health = LoadHealthCheck.Evaluate(machine, healthFrames);
                if (health.Verdict == LoadHealthVerdict.StillLoading)
                {
                    if (machine.Tape.UsedInstantLoad || machine.Tape.IsFastLoadActive)
                    {
                        return new LoadAttemptResult
                        {
                            Success = true,
                            Path = candidate.Path,
                            Strategy = candidate.Strategy,
                            Health = health,
                            Attempts = n,
                            StatusMessage = BuildSuccessStatus(machine, candidate.Path, strategyName, health),
                            AppliedHint = solutionHint
                        };
                    }

                    lastFail = new LoadAttemptResult
                    {
                        Success = false,
                        Path = candidate.Path,
                        Strategy = candidate.Strategy,
                        Health = health,
                        Attempts = n,
                        StatusMessage = $"Carga fallida ({strategyName}): sin flash/turbo activo",
                        AppliedHint = solutionHint
                    };
                    status?.Invoke(lastFail.StatusMessage);
                    continue;
                }

                if (health.Verdict == LoadHealthVerdict.Healthy)
                {
                    status?.Invoke($"Carga OK (estrategia {strategyName})");
                    return new LoadAttemptResult
                    {
                        Success = true,
                        Path = candidate.Path,
                        Strategy = candidate.Strategy,
                        Health = health,
                        Attempts = n,
                        StatusMessage = BuildSuccessStatus(machine, candidate.Path, strategyName, health),
                        AppliedHint = solutionHint
                    };
                }

                lastFail = new LoadAttemptResult
                {
                    Success = false,
                    Path = candidate.Path,
                    Strategy = candidate.Strategy,
                    Health = health,
                    Attempts = n,
                    StatusMessage = $"Carga fallida ({strategyName}): {health.Reason}",
                    AppliedHint = solutionHint
                };
                status?.Invoke(lastFail.StatusMessage);
            }
            catch (Exception ex)
            {
                lastFail = new LoadAttemptResult
                {
                    Success = false,
                    Path = candidate.Path,
                    Strategy = candidate.Strategy,
                    Attempts = n,
                    StatusMessage = $"Carga fallida ({strategyName}): {ex.Message}",
                    AppliedHint = solutionHint
                };
                status?.Invoke(lastFail.StatusMessage);
            }
        }

        return lastFail ?? new LoadAttemptResult
        {
            Success = false,
            Path = path,
            Strategy = LoadStrategyKind.Default,
            Attempts = n,
            StatusMessage = "No se pudo cargar el medio con ninguna estrategia",
            AppliedHint = solutionHint
        };
    }

    private static RomMediaFormat ResolveOpenFormat(LoadCandidate c) =>
        c.Strategy switch
        {
            LoadStrategyKind.ForceTap => RomMediaFormat.Tap,
            LoadStrategyKind.ForceTzx => RomMediaFormat.Tzx,
            LoadStrategyKind.PreferTzx => RomMediaFormat.Auto,
            _ => c.Format
        };

    private static void ApplyPostOpenPreflight(
        SpectrumMachine machine,
        LoadCandidate candidate,
        LoadSolutionHint hint)
    {
        _ = candidate;
        LoadPolicies.EnsureFastLoadForTurbo(machine.Tape);
        // FastLoadWarp solo si hay EAR turbo real (no por patrón TZX hermano al forzar TAP).
        if (hint.HasTag(LoadSolutionTags.FastLoadWarp)
            && (machine.Tape.IsFastLoadActive
                || machine.Tape.HasPendingNonTrapEarBlocks()
                || machine.Tape.TapeHasTurboBlocks()))
            LoadPolicies.EnsureFastLoadWarp(machine.Tape);

        if (machine.Tape.UsedInstantLoad || hint.HasInstantFlashIff)
            LoadPolicies.ApplyFlashLoadUsrEntry(machine);

        // Por defecto off (Reset); solo títulos con el tag (Arkanoid).
        LoadPolicies.ApplyFloatingBusVsyncLatch(
            machine,
            hint.HasTag(LoadSolutionTags.FloatingBusVsyncLatch));
    }

    private static string BuildSuccessStatus(
        SpectrumMachine machine,
        string path,
        string strategyName,
        LoadHealthResult? health)
    {
        string name = System.IO.Path.GetFileName(path);
        string? desc = machine.Tape.Tape?.Description;
        int blocks = machine.Tape.Tape?.Blocks.Count ?? 0;
        string baseMsg;
        if (blocks > 0)
        {
            string how = machine.Tape.UsedInstantLoad
                ? (machine.Tape.FastLoad ? "carga instantánea + rápida" : "carga instantánea")
                : (machine.Tape.FastLoad ? "cinta (carga rápida)" : "cinta");
            baseMsg = string.IsNullOrWhiteSpace(desc)
                ? $"{how}: {name} ({blocks} bloques)"
                : $"{how}: {name} — {desc} ({blocks} bloques)";
        }
        else if (machine.TrDos.LastLoadedName is { } trdFile)
            baseMsg = $"Disco TRD: {name} → {trdFile}";
        else
            baseMsg = $"Cargado: {name}";

        if (health != null && health.Verdict == LoadHealthVerdict.StillLoading)
            return $"{baseMsg} · estrategia {strategyName} (turbo en curso)";
        return $"{baseMsg} · estrategia {strategyName}";
    }
}
