using System;
using ManagedDoom;
using ManagedDoom.UserInput;
using UnityEngine;

namespace MCG_Doom.Input
{
    internal sealed class UnityDoomInput : IUserInput
    {
        private readonly Config _config;
        private readonly DoomTicCommandBuilder _commandBuilder;

        public UnityDoomInput(Config config)
        {
            _config = config;
            _commandBuilder = new DoomTicCommandBuilder(config);
        }

        public void PumpEvents(Doom doom)
        {
            foreach (var binding in DoomKeyMap.Bindings)
            {
                PostTransition(doom, binding.UnityKey, binding.DoomKey);
            }

            // MCG owns Tab/Escape, and Big Ambitions owns M for the city map.
            // Expose DOOM's automap/menu on E and P instead.
            PostTransition(doom, KeyCode.E, DoomKey.Tab);
            PostTransition(doom, KeyCode.P, DoomKey.Escape);
        }

        public void BuildTicCmd(TicCmd cmd)
        {
            _commandBuilder.Build(cmd);
        }

        public void Reset()
        {
            _commandBuilder.Reset();
        }

        public void GrabMouse()
        {
        }

        public void ReleaseMouse()
        {
        }

        public int MaxMouseSensitivity => 9;

        public int MouseSensitivity
        {
            get => _config.mouse_sensitivity;
            set => _config.mouse_sensitivity = Math.Max(0, Math.Min(value, MaxMouseSensitivity));
        }

        private static void PostTransition(Doom doom, KeyCode unityKey, DoomKey doomKey)
        {
            if (UnityEngine.Input.GetKeyDown(unityKey))
            {
                doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyDown, doomKey));
            }

            if (UnityEngine.Input.GetKeyUp(unityKey))
            {
                doom.PostEvent(new DoomEvent(ManagedDoom.EventType.KeyUp, doomKey));
            }
        }
    }
}

