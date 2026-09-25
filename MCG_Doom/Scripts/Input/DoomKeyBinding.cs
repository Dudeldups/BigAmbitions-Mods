using ManagedDoom;
using UnityEngine;

namespace MCG_Doom.Input
{
    internal readonly struct DoomKeyBinding
    {
        public DoomKeyBinding(KeyCode unityKey, DoomKey doomKey)
        {
            UnityKey = unityKey;
            DoomKey = doomKey;
        }

        public KeyCode UnityKey { get; }
        public DoomKey DoomKey { get; }
    }
}
