namespace MultiEmulator.Spectrum.Input;

public sealed class JoystickState
{
    public bool Up, Down, Left, Right, Fire;
    public JoystickType Type { get; set; } = JoystickType.Kempston;

    public void Reset() => Up = Down = Left = Right = Fire = false;

    public byte ReadKempston()
    {
        byte v = 0;
        if (Right) v |= 0x01;
        if (Left) v |= 0x02;
        if (Down) v |= 0x04;
        if (Up) v |= 0x08;
        if (Fire) v |= 0x10;
        return v;
    }

    /// <summary>Inject Sinclair joystick into keyboard matrix rows.</summary>
    public void ApplySinclair(KeyboardMatrix kb)
    {
        if (Type == JoystickType.Sinclair1)
        {
            // 6,7,8,9,0
            kb.SetKey(4, 4, Down);  // 6
            kb.SetKey(4, 3, Up);    // 7
            kb.SetKey(4, 2, Right); // 8
            kb.SetKey(4, 1, Left);  // 9
            kb.SetKey(4, 0, Fire);  // 0
        }
        else if (Type == JoystickType.Sinclair2)
        {
            // 1,2,3,4,5
            kb.SetKey(3, 0, Left);
            kb.SetKey(3, 1, Right);
            kb.SetKey(3, 2, Down);
            kb.SetKey(3, 3, Up);
            kb.SetKey(3, 4, Fire);
        }
    }
}
