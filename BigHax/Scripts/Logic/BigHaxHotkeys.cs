#nullable enable
namespace BigHax
{
    internal static class BigHaxHotkeys
    {
        public static readonly string[] ChoiceKeys =
        {
            "F4",
            "F5",
            "F6",
            "F7",
            "F8",
            "F9",
            "F10",
            "Home",
            "Insert",
            "Delete"
        };

        public static readonly UnityEngine.KeyCode[] Values =
        {
            UnityEngine.KeyCode.F4,
            UnityEngine.KeyCode.F5,
            UnityEngine.KeyCode.F6,
            UnityEngine.KeyCode.F7,
            UnityEngine.KeyCode.F8,
            UnityEngine.KeyCode.F9,
            UnityEngine.KeyCode.F10,
            UnityEngine.KeyCode.Home,
            UnityEngine.KeyCode.Insert,
            UnityEngine.KeyCode.Delete
        };

        public static UnityEngine.KeyCode GetKeyCode(int index)
        {
            if (index < 0 || index >= Values.Length)
                return Values[BigHaxSettings.DefaultUiHotkeyIndex];

            return Values[index];
        }

        public static int ClampIndex(int index)
        {
            if (index < 0 || index >= Values.Length)
                return BigHaxSettings.DefaultUiHotkeyIndex;

            return index;
        }
    }
}
