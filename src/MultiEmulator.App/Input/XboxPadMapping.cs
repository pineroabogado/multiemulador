using MultiEmulator.Spectrum.Input;

namespace MultiEmulator.App.Input;

/// <summary>
/// Mapeo mando → 0..N tokens Spectrum. Thread-safe para aplicar en caliente desde el loop.
/// </summary>
public sealed class XboxPadMapping
{
    private readonly object _sync = new();
    private Dictionary<XboxPadControl, string[]> _bindings;

    public XboxPadMapping() => _bindings = CreateDefaultBindings();

    public static IReadOnlyList<(XboxPadControl Control, string Label)> ControlLabels { get; } =
    [
        (XboxPadControl.DpadUp, "D-pad ↑"),
        (XboxPadControl.DpadDown, "D-pad ↓"),
        (XboxPadControl.DpadLeft, "D-pad ←"),
        (XboxPadControl.DpadRight, "D-pad →"),
        (XboxPadControl.StickUp, "Stick izq. ↑"),
        (XboxPadControl.StickDown, "Stick izq. ↓"),
        (XboxPadControl.StickLeft, "Stick izq. ←"),
        (XboxPadControl.StickRight, "Stick izq. →"),
        (XboxPadControl.A, "A"),
        (XboxPadControl.B, "B"),
        (XboxPadControl.X, "X"),
        (XboxPadControl.Y, "Y"),
        (XboxPadControl.LB, "LB"),
        (XboxPadControl.RB, "RB"),
        (XboxPadControl.LT, "LT"),
        (XboxPadControl.RT, "RT"),
        (XboxPadControl.Start, "Start"),
        (XboxPadControl.Back, "Back / Select"),
    ];

    public static Dictionary<XboxPadControl, string[]> CreateDefaultBindings()
    {
        string[] cursorsLeft = ["CAPS", "5", "O"];
        string[] cursorsRight = ["CAPS", "8", "P"];
        string[] cursorsUp = ["CAPS", "7"];
        string[] cursorsDown = ["CAPS", "6"];
        string[] fire = ["SPACE", "0"];
        string[] space = ["SPACE"];
        string[] enter = ["ENTER"];
        string[] brk = ["CAPS", "SPACE"];
        string[] none = [];

        return new Dictionary<XboxPadControl, string[]>
        {
            [XboxPadControl.DpadUp] = cursorsUp,
            [XboxPadControl.DpadDown] = cursorsDown,
            [XboxPadControl.DpadLeft] = cursorsLeft,
            [XboxPadControl.DpadRight] = cursorsRight,
            [XboxPadControl.StickUp] = cursorsUp.ToArray(),
            [XboxPadControl.StickDown] = cursorsDown.ToArray(),
            [XboxPadControl.StickLeft] = cursorsLeft.ToArray(),
            [XboxPadControl.StickRight] = cursorsRight.ToArray(),
            [XboxPadControl.A] = fire,
            [XboxPadControl.B] = space,
            [XboxPadControl.X] = none,
            [XboxPadControl.Y] = none,
            [XboxPadControl.LB] = none,
            [XboxPadControl.RB] = none,
            [XboxPadControl.LT] = fire.ToArray(),
            [XboxPadControl.RT] = fire.ToArray(),
            [XboxPadControl.Start] = enter,
            [XboxPadControl.Back] = brk,
        };
    }

    public void ResetToDefaults()
    {
        lock (_sync)
            _bindings = CreateDefaultBindings();
    }

    public string[] GetKeys(XboxPadControl control)
    {
        lock (_sync)
        {
            return _bindings.TryGetValue(control, out var keys)
                ? (string[])keys.Clone()
                : [];
        }
    }

    public void SetKeys(XboxPadControl control, IEnumerable<string> keys)
    {
        var cleaned = keys
            .Select(k => k.Trim().ToUpperInvariant())
            .Where(SpectrumKeyTokens.IsKnown)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        lock (_sync)
            _bindings[control] = cleaned;
    }

    public Dictionary<string, string[]> SnapshotForSave()
    {
        lock (_sync)
        {
            return _bindings.ToDictionary(
                kv => kv.Key.ToString(),
                kv => (string[])kv.Value.Clone(),
                StringComparer.OrdinalIgnoreCase);
        }
    }

    public void LoadFrom(Dictionary<string, string[]>? raw)
    {
        var next = CreateDefaultBindings();
        if (raw != null)
        {
            foreach (var (name, keys) in raw)
            {
                if (!Enum.TryParse<XboxPadControl>(name, ignoreCase: true, out var control))
                    continue;
                next[control] = keys
                    .Select(k => k.Trim().ToUpperInvariant())
                    .Where(SpectrumKeyTokens.IsKnown)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
        }
        lock (_sync)
            _bindings = next;
    }

    public void ApplyPressed(KeyboardMatrix kb, Func<XboxPadControl, bool> isPressed)
    {
        // Snapshot under lock, apply outside to keep matrix writes short.
        KeyValuePair<XboxPadControl, string[]>[] snapshot;
        lock (_sync)
            snapshot = _bindings.ToArray();

        foreach (var (control, keys) in snapshot)
        {
            if (keys.Length == 0 || !isPressed(control)) continue;
            SpectrumKeyTokens.ApplyAll(kb, keys);
        }
    }
}
