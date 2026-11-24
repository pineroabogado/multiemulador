using System.Runtime.InteropServices;
using MultiEmulator.Spectrum.Input;

namespace MultiEmulator.App.Services;

/// <summary>
/// Lee el estado físico del teclado (Win32) y lo proyecta a la matriz Spectrum.
/// Evita teclas “pegadas” cuando Avalonia pierde el KeyUp.
/// </summary>
internal static class WindowsKeyboardPoller
{
    private const int VkBack = 0x08;
    private const int VkReturn = 0x0D;
    private const int VkShift = 0x10;
    private const int VkSpace = 0x20;
    private const int VkLeft = 0x25;
    private const int VkUp = 0x26;
    private const int VkRight = 0x27;
    private const int VkDown = 0x28;
    private const int VkDelete = 0x2E;
    private const int Vk0 = 0x30;
    private const int Vk1 = 0x31;
    private const int Vk9 = 0x39;
    private const int VkA = 0x41;
    private const int VkZ = 0x5A;
    private const int VkLShift = 0xA0;
    private const int VkRShift = 0xA1;
    private const int VkNumpad0 = 0x60;
    private const int VkNumpad9 = 0x69;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    public static void Apply(KeyboardMatrix kb)
    {
        kb.ReleaseAll();

        // Caps = Shift izquierdo; Symbol = Shift derecho (no ambos a la vez por VK_SHIFT genérico).
        bool caps = Down(VkLShift);
        bool sym = Down(VkRShift);
        if (!caps && !sym && Down(VkShift))
            caps = true;

        if (caps) kb.SetKey(0, 0, true);
        if (sym) kb.SetKey(7, 1, true);

        // Letras A–Z
        for (int i = 0; i <= 25; i++)
        {
            if (!Down(VkA + i)) continue;
            char c = (char)('A' + i);
            var map = KeyboardMatrix.MapPcKey(c.ToString());
            if (map != null) kb.SetKey(map.Value.row, map.Value.bit, true);
        }

        // Dígitos fila superior
        for (int d = 0; d <= 9; d++)
        {
            if (!Down(Vk0 + d) && !(d <= 9 && Down(VkNumpad0 + d))) continue;
            var map = KeyboardMatrix.MapPcKey("D" + d);
            if (map != null) kb.SetKey(map.Value.row, map.Value.bit, true);
        }

        if (Down(VkReturn)) kb.SetKey(6, 0, true);
        if (Down(VkSpace)) kb.SetKey(7, 0, true);

        // Cursores = Caps Shift + 5/6/7/8
        if (Down(VkLeft)) { kb.SetKey(0, 0, true); kb.SetKey(3, 4, true); }
        if (Down(VkDown)) { kb.SetKey(0, 0, true); kb.SetKey(4, 4, true); }
        if (Down(VkUp)) { kb.SetKey(0, 0, true); kb.SetKey(4, 3, true); }
        if (Down(VkRight)) { kb.SetKey(0, 0, true); kb.SetKey(4, 2, true); }

        // Backspace / Delete = Caps Shift + 0
        if (Down(VkBack) || Down(VkDelete))
        {
            kb.SetKey(0, 0, true);
            kb.SetKey(4, 0, true);
        }
    }
}
