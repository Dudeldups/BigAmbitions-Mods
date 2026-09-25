using System;
using ManagedDoom;
using ManagedDoom.Video;

namespace MCG_Doom.Rendering
{
    internal sealed class UnityDoomVideo : IVideo, IDisposable
    {
        private readonly Renderer _renderer;
        private readonly byte[] _sourcePixels;
        private readonly DoomFrameBuffer _frameBuffer;

        public UnityDoomVideo(Config config, GameContent content, DoomFrameBuffer frameBuffer)
        {
            _renderer = new Renderer(config, content);
            _sourcePixels = new byte[4 * _renderer.Width * _renderer.Height];
            _frameBuffer = frameBuffer;

            if (_renderer.Width != frameBuffer.Width || _renderer.Height != frameBuffer.Height)
            {
                throw new InvalidOperationException(
                    $"DOOM renderer is {_renderer.Width}x{_renderer.Height}, " +
                    $"but Unity frame buffer is {frameBuffer.Width}x{frameBuffer.Height}.");
            }
        }

        public void Render(Doom doom, Fixed frameFrac)
        {
            _renderer.Render(doom, _sourcePixels, frameFrac);
            _frameBuffer.UploadManagedDoomRgba(_sourcePixels);
        }

        public void InitializeWipe() => _renderer.InitializeWipe();
        public bool HasFocus() => true;

        public int MaxWindowSize => _renderer.MaxWindowSize;
        public int WindowSize
        {
            get => _renderer.WindowSize;
            set => _renderer.WindowSize = value;
        }

        public bool DisplayMessage
        {
            get => _renderer.DisplayMessage;
            set => _renderer.DisplayMessage = value;
        }

        public int MaxGammaCorrectionLevel => _renderer.MaxGammaCorrectionLevel;
        public int GammaCorrectionLevel
        {
            get => _renderer.GammaCorrectionLevel;
            set => _renderer.GammaCorrectionLevel = value;
        }

        public int WipeBandCount => _renderer.WipeBandCount;
        public int WipeHeight => _renderer.WipeHeight;

        public void Dispose()
        {
        }
    }
}
