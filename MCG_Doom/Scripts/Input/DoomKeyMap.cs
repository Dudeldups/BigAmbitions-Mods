using ManagedDoom;
using UnityEngine;

namespace MCG_Doom.Input
{
    internal static class DoomKeyMap
    {
        // Tab, Escape and Backspace are deliberately omitted because MCG owns them.
        // E is reserved for DOOM automap; M is left unused because Big Ambitions owns it.
        public static readonly DoomKeyBinding[] Bindings =
        {
            new DoomKeyBinding(KeyCode.Space, DoomKey.Space),
            new DoomKeyBinding(KeyCode.Return, DoomKey.Enter),
            new DoomKeyBinding(KeyCode.Delete, DoomKey.Delete),
            new DoomKeyBinding(KeyCode.Insert, DoomKey.Insert),
            new DoomKeyBinding(KeyCode.UpArrow, DoomKey.Up),
            new DoomKeyBinding(KeyCode.DownArrow, DoomKey.Down),
            new DoomKeyBinding(KeyCode.LeftArrow, DoomKey.Left),
            new DoomKeyBinding(KeyCode.RightArrow, DoomKey.Right),
            new DoomKeyBinding(KeyCode.LeftControl, DoomKey.LControl),
            new DoomKeyBinding(KeyCode.RightControl, DoomKey.RControl),
            new DoomKeyBinding(KeyCode.LeftShift, DoomKey.LShift),
            new DoomKeyBinding(KeyCode.RightShift, DoomKey.RShift),
            new DoomKeyBinding(KeyCode.Alpha0, DoomKey.Num0),
            new DoomKeyBinding(KeyCode.Alpha1, DoomKey.Num1),
            new DoomKeyBinding(KeyCode.Alpha2, DoomKey.Num2),
            new DoomKeyBinding(KeyCode.Alpha3, DoomKey.Num3),
            new DoomKeyBinding(KeyCode.Alpha4, DoomKey.Num4),
            new DoomKeyBinding(KeyCode.Alpha5, DoomKey.Num5),
            new DoomKeyBinding(KeyCode.Alpha6, DoomKey.Num6),
            new DoomKeyBinding(KeyCode.Alpha7, DoomKey.Num7),
            new DoomKeyBinding(KeyCode.Alpha8, DoomKey.Num8),
            new DoomKeyBinding(KeyCode.Alpha9, DoomKey.Num9),
            new DoomKeyBinding(KeyCode.A, DoomKey.A),
            new DoomKeyBinding(KeyCode.B, DoomKey.B),
            new DoomKeyBinding(KeyCode.C, DoomKey.C),
            new DoomKeyBinding(KeyCode.D, DoomKey.D),
            new DoomKeyBinding(KeyCode.F, DoomKey.F),
            new DoomKeyBinding(KeyCode.G, DoomKey.G),
            new DoomKeyBinding(KeyCode.H, DoomKey.H),
            new DoomKeyBinding(KeyCode.I, DoomKey.I),
            new DoomKeyBinding(KeyCode.J, DoomKey.J),
            new DoomKeyBinding(KeyCode.K, DoomKey.K),
            new DoomKeyBinding(KeyCode.L, DoomKey.L),
            new DoomKeyBinding(KeyCode.N, DoomKey.N),
            new DoomKeyBinding(KeyCode.O, DoomKey.O),
            new DoomKeyBinding(KeyCode.Q, DoomKey.Q),
            new DoomKeyBinding(KeyCode.R, DoomKey.R),
            new DoomKeyBinding(KeyCode.S, DoomKey.S),
            new DoomKeyBinding(KeyCode.T, DoomKey.T),
            new DoomKeyBinding(KeyCode.U, DoomKey.U),
            new DoomKeyBinding(KeyCode.V, DoomKey.V),
            new DoomKeyBinding(KeyCode.W, DoomKey.W),
            new DoomKeyBinding(KeyCode.X, DoomKey.X),
            new DoomKeyBinding(KeyCode.Y, DoomKey.Y),
            new DoomKeyBinding(KeyCode.Z, DoomKey.Z),
            new DoomKeyBinding(KeyCode.F1, DoomKey.F1),
            new DoomKeyBinding(KeyCode.F2, DoomKey.F2),
            new DoomKeyBinding(KeyCode.F3, DoomKey.F3),
            new DoomKeyBinding(KeyCode.F4, DoomKey.F4),
            new DoomKeyBinding(KeyCode.F5, DoomKey.F5),
            new DoomKeyBinding(KeyCode.F6, DoomKey.F6),
            new DoomKeyBinding(KeyCode.F7, DoomKey.F7),
            new DoomKeyBinding(KeyCode.F8, DoomKey.F8),
            new DoomKeyBinding(KeyCode.F9, DoomKey.F9),
            new DoomKeyBinding(KeyCode.F10, DoomKey.F10),
            new DoomKeyBinding(KeyCode.F11, DoomKey.F11),
            new DoomKeyBinding(KeyCode.F12, DoomKey.F12)
        };
    }
}
