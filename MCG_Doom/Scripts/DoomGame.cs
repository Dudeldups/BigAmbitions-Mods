using System;
using Capisoft.Lib.BaComputerGames;
using MCG_Doom.Core;
using MCG_Doom.Rendering;
using UnityEngine;

namespace MCG_Doom
{
    public sealed class DoomGame : ComputerGameBehaviour
    {
        private DoomRuntime _runtime;
        private DoomScreen _screen;
        private bool _exitRequested;

        public override Camera Camera => _screen?.Camera;

        protected override void OnInitialize()
        {
            try
            {
                _screen = DoomScreen.Create(transform);
                var wadPath = DoomPaths.FindBundledSharewareWad();
                var soundFontPath = DoomPaths.FindBundledSoundFont();
                _runtime = new DoomRuntime(wadPath, soundFontPath, _screen.FrameBuffer, transform);
            }
            catch (Exception exception)
            {
                Debug.LogError($"[MCG_Doom] Failed to initialize DOOM: {exception}");
                _screen?.ShowError("DOOM failed to start.\nCheck the Big Ambitions log for details.");
            }
        }

        protected override void OnTick(ComputerGameFrame frame)
        {
            _screen?.Tick(frame.DeltaSeconds);

            if (_runtime == null || _exitRequested)
            {
                return;
            }

            if (_runtime.Tick(frame.DeltaSeconds))
            {
                _exitRequested = true;
                Context?.RequestExit();
            }
        }

        public override void SetScreenResolution(int width, int height)
        {
            if (_screen?.Camera == null)
            {
                return;
            }

            var safeWidth = Math.Max(1, width);
            var safeHeight = Math.Max(1, height);
            var aspect = safeWidth / (float)safeHeight;

            // The display quad is 16:10. Keep the whole image visible when
            // the host render target is narrower, and letterbox otherwise.
            _screen.Camera.orthographicSize = Math.Max(5f, 8f / aspect);
        }

        protected override void OnShutdown()
        {
            _runtime?.Dispose();
            _runtime = null;
            _exitRequested = false;

            _screen?.Dispose();
            _screen = null;
        }
    }
}
