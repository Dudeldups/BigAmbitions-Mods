using System;
using ManagedDoom;
using MCG_Doom.Audio;
using MCG_Doom.Input;
using MCG_Doom.Rendering;
using UnityEngine;

namespace MCG_Doom.Core
{
    internal sealed class DoomRuntime : IDisposable
    {
        private const double TicDuration = 1.0 / 35.0;
        private const double MaxFrameDelta = 0.25;

        private readonly Doom _doom;
        private readonly UnityDoomInput _input;
        private readonly UnityDoomVideo _video;
        private readonly UnityDoomSound _sound;
        private readonly UnityDoomMusic _music;
        private double _accumulator;
        private bool _completed;

        public DoomRuntime(string wadPath, string soundFontPath, DoomFrameBuffer frameBuffer, Transform audioParent)
        {
            // Unity adapters provide both DOOM sound effects and music, so keep
            // the engine audio paths enabled.
            var args = new CommandLineArgs(new[]
            {
                "-iwad", wadPath
            });

            var config = new Config
            {
                video_highresolution = false,
                video_fpsscale = 1,
                mouse_disableyaxis = true
            };

            var content = new GameContent(args);
            _input = new UnityDoomInput(config);
            _video = new UnityDoomVideo(config, content, frameBuffer);
            _sound = new UnityDoomSound(config, content, audioParent);
            _music = new UnityDoomMusic(config, content, soundFontPath, audioParent);
            _doom = new Doom(args, config, content, _video, _sound, _music, _input);

            _video.Render(_doom, Fixed.One);
        }

        public bool Tick(double deltaSeconds)
        {
            if (_completed)
            {
                return true;
            }

            _input.PumpEvents(_doom);
            _accumulator += Math.Max(0.0, Math.Min(deltaSeconds, MaxFrameDelta));

            while (_accumulator >= TicDuration)
            {
                var result = _doom.Update();
                _accumulator -= TicDuration;

                if (result == UpdateResult.Completed)
                {
                    _completed = true;
                    return true;
                }
            }

            _video.Render(_doom, Fixed.One);
            return false;
        }

        public void Dispose()
        {
            _input.Reset();
            _sound.Dispose();
            _music.Dispose();
            _video.Dispose();
        }
    }
}
