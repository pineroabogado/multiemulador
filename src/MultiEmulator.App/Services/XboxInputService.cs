using System.Runtime.InteropServices;
using MultiEmulator.App.Input;
using MultiEmulator.Spectrum.Input;

namespace MultiEmulator.App.Services;

/// <summary>
/// Poll Xbox pad → Kempston/Sinclair JoystickState and Spectrum keyboard via XboxPadMapping.
/// Level-triggered each frame; disconnect clears stuck state.
/// </summary>
public sealed class XboxInputService
{
    public bool IsConnected { get; private set; }

    private readonly bool[] _pressed = new bool[Enum.GetValues<XboxPadControl>().Length];

    public XboxPadMapping Mapping { get; } = new();

    public void Poll(JoystickState joystick)
    {
        if (XInputGetState(0, out var state) != 0)
        {
            IsConnected = false;
            Array.Clear(_pressed);
            joystick.Reset();
            return;
        }

        IsConnected = true;
        var g = state.Gamepad;
        const short dead = 8000;

        Set(XboxPadControl.DpadUp, (g.wButtons & 0x0001) != 0);
        Set(XboxPadControl.DpadDown, (g.wButtons & 0x0002) != 0);
        Set(XboxPadControl.DpadLeft, (g.wButtons & 0x0004) != 0);
        Set(XboxPadControl.DpadRight, (g.wButtons & 0x0008) != 0);

        Set(XboxPadControl.StickUp, g.sThumbLY > dead);
        Set(XboxPadControl.StickDown, g.sThumbLY < -dead);
        Set(XboxPadControl.StickLeft, g.sThumbLX < -dead);
        Set(XboxPadControl.StickRight, g.sThumbLX > dead);

        Set(XboxPadControl.Start, (g.wButtons & 0x0010) != 0);
        Set(XboxPadControl.Back, (g.wButtons & 0x0020) != 0);
        Set(XboxPadControl.LB, (g.wButtons & 0x0100) != 0);
        Set(XboxPadControl.RB, (g.wButtons & 0x0200) != 0);
        Set(XboxPadControl.A, (g.wButtons & 0x1000) != 0);
        Set(XboxPadControl.B, (g.wButtons & 0x2000) != 0);
        Set(XboxPadControl.X, (g.wButtons & 0x4000) != 0);
        Set(XboxPadControl.Y, (g.wButtons & 0x8000) != 0);
        Set(XboxPadControl.LT, g.bLeftTrigger > 40);
        Set(XboxPadControl.RT, g.bRightTrigger > 40);

        // Kempston: D-pad OR stick; Fire = A OR triggers (independiente del mapa teclado)
        joystick.Left = Pressed(XboxPadControl.DpadLeft) || Pressed(XboxPadControl.StickLeft);
        joystick.Right = Pressed(XboxPadControl.DpadRight) || Pressed(XboxPadControl.StickRight);
        joystick.Up = Pressed(XboxPadControl.DpadUp) || Pressed(XboxPadControl.StickUp);
        joystick.Down = Pressed(XboxPadControl.DpadDown) || Pressed(XboxPadControl.StickDown);
        joystick.Fire = Pressed(XboxPadControl.A)
                        || Pressed(XboxPadControl.LT)
                        || Pressed(XboxPadControl.RT);
    }

    /// <summary>
    /// Inyecta teclas Spectrum según el mapa actual. Tras WindowsKeyboardPoller (ReleaseAll + PC).
    /// </summary>
    public void ApplyKeyboard(KeyboardMatrix kb)
    {
        if (!IsConnected) return;
        Mapping.ApplyPressed(kb, Pressed);
    }

    private void Set(XboxPadControl c, bool on) => _pressed[(int)c] = on;

    private bool Pressed(XboxPadControl c) => _pressed[(int)c];

    [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
    private static extern int XInputGetState(int dwUserIndex, out XInputState pState);

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputState
    {
        public uint dwPacketNumber;
        public XInputGamepad Gamepad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputGamepad
    {
        public ushort wButtons;
        public byte bLeftTrigger;
        public byte bRightTrigger;
        public short sThumbLX;
        public short sThumbLY;
        public short sThumbRX;
        public short sThumbRY;
    }
}
