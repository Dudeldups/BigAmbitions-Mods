using System;
using UnityEngine;

namespace MCG_Doom.Rendering
{
    internal sealed class DoomFrameBuffer : IDisposable
    {
        private readonly byte[] _unityPixels;

        public DoomFrameBuffer(int width, int height)
        {
            Width = width;
            Height = height;
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "MCG_Doom_FrameBuffer",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            _unityPixels = new byte[width * height * 4];
            Clear();
        }

        public int Width { get; }
        public int Height { get; }
        public Texture2D Texture { get; }

        public void UploadManagedDoomRgba(byte[] source)
        {
            if (source == null || source.Length != _unityPixels.Length)
            {
                throw new ArgumentException("Unexpected Managed Doom frame-buffer size.", nameof(source));
            }

            for (var y = 0; y < Height; y++)
            {
                var unityY = Height - 1 - y;
                for (var x = 0; x < Width; x++)
                {
                    var sourceOffset = 4 * (Height * x + y);
                    var destinationOffset = 4 * (Width * unityY + x);

                    _unityPixels[destinationOffset] = source[sourceOffset];
                    _unityPixels[destinationOffset + 1] = source[sourceOffset + 1];
                    _unityPixels[destinationOffset + 2] = source[sourceOffset + 2];
                    _unityPixels[destinationOffset + 3] = source[sourceOffset + 3];
                }
            }

            Texture.LoadRawTextureData(_unityPixels);
            Texture.Apply(false, false);
        }

        public void Clear()
        {
            Array.Clear(_unityPixels, 0, _unityPixels.Length);
            Texture.LoadRawTextureData(_unityPixels);
            Texture.Apply(false, false);
        }

        public void Dispose()
        {
            if (Texture != null)
            {
                UnityEngine.Object.Destroy(Texture);
            }
        }
    }
}
