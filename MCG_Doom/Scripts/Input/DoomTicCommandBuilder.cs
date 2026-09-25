using System;
using ManagedDoom;
using UnityEngine;

namespace MCG_Doom.Input
{
    internal sealed class DoomTicCommandBuilder
    {
        private static readonly KeyCode[] WeaponKeys =
        {
            KeyCode.Alpha1,
            KeyCode.Alpha2,
            KeyCode.Alpha3,
            KeyCode.Alpha4,
            KeyCode.Alpha5,
            KeyCode.Alpha6,
            KeyCode.Alpha7
        };

        private readonly Config _config;
        private int _turnHeld;

        public DoomTicCommandBuilder(Config config)
        {
            _config = config;
        }

        public void Build(TicCmd cmd)
        {
            cmd.Clear();

            var run = IsDown(KeyCode.LeftShift, KeyCode.RightShift) ? 1 : 0;
            if (_config.game_alwaysrun)
            {
                run = 1 - run;
            }

            ApplyTurning(cmd, run);
            ApplyMovement(cmd, run);
            ApplyMouseTurn(cmd);
            ApplyButtons(cmd);
            ApplyWeaponChange(cmd);
        }

        public void Reset()
        {
            _turnHeld = 0;
        }

        private void ApplyTurning(TicCmd cmd, int speed)
        {
            var left = UnityEngine.Input.GetKey(KeyCode.LeftArrow);
            var right = UnityEngine.Input.GetKey(KeyCode.RightArrow);

            _turnHeld = left || right ? _turnHeld + 1 : 0;
            var turnSpeed = _turnHeld < PlayerBehavior.SlowTurnTics ? 2 : speed;

            if (right)
            {
                cmd.AngleTurn -= (short)PlayerBehavior.AngleTurn[turnSpeed];
            }
            if (left)
            {
                cmd.AngleTurn += (short)PlayerBehavior.AngleTurn[turnSpeed];
            }
        }

        private void ApplyMovement(TicCmd cmd, int speed)
        {
            var forward = 0;
            var side = 0;

            if (IsDown(KeyCode.W, KeyCode.UpArrow))
            {
                forward += PlayerBehavior.ForwardMove[speed];
            }
            if (IsDown(KeyCode.S, KeyCode.DownArrow))
            {
                forward -= PlayerBehavior.ForwardMove[speed];
            }
            if (UnityEngine.Input.GetKey(KeyCode.A))
            {
                side -= PlayerBehavior.SideMove[speed];
            }
            if (UnityEngine.Input.GetKey(KeyCode.D))
            {
                side += PlayerBehavior.SideMove[speed];
            }

            forward = Math.Max(-PlayerBehavior.MaxMove, Math.Min(forward, PlayerBehavior.MaxMove));
            side = Math.Max(-PlayerBehavior.MaxMove, Math.Min(side, PlayerBehavior.MaxMove));
            cmd.ForwardMove += (sbyte)forward;
            cmd.SideMove += (sbyte)side;
        }

        private void ApplyMouseTurn(TicCmd cmd)
        {
            var mouseDelta = UnityEngine.Input.GetAxisRaw("Mouse X");
            if (Math.Abs(mouseDelta) < 0.001f)
            {
                return;
            }

            var scaled = (int)Math.Round(mouseDelta * Math.Max(1, _config.mouse_sensitivity) * 5.0f);
            cmd.AngleTurn -= (short)(scaled * 8);
        }

        private static void ApplyButtons(TicCmd cmd)
        {
            var fire = IsDown(KeyCode.LeftControl, KeyCode.RightControl) || UnityEngine.Input.GetMouseButton(0);
            var use = UnityEngine.Input.GetKey(KeyCode.Space) || UnityEngine.Input.GetMouseButton(1);

            if (fire)
            {
                cmd.Buttons |= TicCmdButtons.Attack;
            }
            if (use)
            {
                cmd.Buttons |= TicCmdButtons.Use;
            }
        }

        private static void ApplyWeaponChange(TicCmd cmd)
        {
            for (var i = 0; i < WeaponKeys.Length; i++)
            {
                if (!UnityEngine.Input.GetKey(WeaponKeys[i]))
                {
                    continue;
                }

                cmd.Buttons |= TicCmdButtons.Change;
                cmd.Buttons |= (byte)(i << TicCmdButtons.WeaponShift);
                return;
            }
        }

        private static bool IsDown(KeyCode first, KeyCode second)
        {
            return UnityEngine.Input.GetKey(first) || UnityEngine.Input.GetKey(second);
        }
    }
}
