using System;
using ManagedDoom;
using ManagedDoom.Audio;
using UnityEngine;

namespace MCG_Doom.Audio
{
    internal sealed class DoomSfxLibrary : IDisposable
    {
        private readonly AudioClip[] _clips;
        private readonly float[] _amplitudes;

        public DoomSfxLibrary(GameContent content)
        {
            if (content == null) throw new ArgumentNullException(nameof(content));

            _clips = new AudioClip[DoomInfo.SfxNames.Length];
            _amplitudes = new float[DoomInfo.SfxNames.Length];

            for (var i = 0; i < DoomInfo.SfxNames.Length; i++)
            {
                var name = "DS" + DoomInfo.SfxNames[i].ToString().ToUpperInvariant();
                if (content.Wad.GetLumpNumber(name) == -1)
                {
                    continue;
                }

                int sampleRate;
                byte[] samples;
                if (!TryReadSamples(content.Wad, name, out sampleRate, out samples))
                {
                    continue;
                }

                var pcm = ConvertToPcm(samples);
                var clip = AudioClip.Create("MCG_Doom " + name, pcm.Length, 1, sampleRate, false);
                clip.SetData(pcm, 0);

                _clips[i] = clip;
                _amplitudes[i] = GetAmplitude(samples, sampleRate);
            }
        }

        public AudioClip GetClip(Sfx sfx)
        {
            var index = (int)sfx;
            return index >= 0 && index < _clips.Length ? _clips[index] : null;
        }

        public float GetAmplitude(Sfx sfx)
        {
            var index = (int)sfx;
            return index >= 0 && index < _amplitudes.Length ? _amplitudes[index] : 0f;
        }

        public void Dispose()
        {
            for (var i = 0; i < _clips.Length; i++)
            {
                if (_clips[i] != null)
                {
                    UnityEngine.Object.Destroy(_clips[i]);
                    _clips[i] = null;
                }
            }
        }

        private static bool TryReadSamples(Wad wad, string name, out int sampleRate, out byte[] samples)
        {
            var data = wad.ReadLump(name);
            if (data == null || data.Length < 8)
            {
                sampleRate = 0;
                samples = null;
                return false;
            }

            sampleRate = BitConverter.ToUInt16(data, 2);
            var sampleCount = BitConverter.ToInt32(data, 4);
            var offset = 8;

            if (ContainsDmxPadding(data, sampleCount))
            {
                offset += 16;
                sampleCount -= 32;
            }

            if (sampleRate <= 0 || sampleCount <= 0 || offset < 0 || offset >= data.Length)
            {
                samples = null;
                return false;
            }

            sampleCount = Math.Min(sampleCount, data.Length - offset);
            if (sampleCount <= 0)
            {
                samples = null;
                return false;
            }

            samples = new byte[sampleCount];
            Buffer.BlockCopy(data, offset, samples, 0, sampleCount);
            return true;
        }

        private static bool ContainsDmxPadding(byte[] data, int sampleCount)
        {
            if (sampleCount < 32 || data.Length < 8 + sampleCount)
            {
                return false;
            }

            var first = data[8];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + i] != first)
                {
                    return false;
                }
            }

            var last = data[8 + sampleCount - 1];
            for (var i = 1; i < 16; i++)
            {
                if (data[8 + sampleCount - i - 1] != last)
                {
                    return false;
                }
            }

            return true;
        }

        private static float[] ConvertToPcm(byte[] samples)
        {
            var pcm = new float[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                pcm[i] = (samples[i] - 128) / 128f;
            }
            return pcm;
        }

        private static float GetAmplitude(byte[] samples, int sampleRate)
        {
            var max = 0;
            var count = Math.Min(sampleRate / 5, samples.Length);
            for (var i = 0; i < count; i++)
            {
                var amplitude = samples[i] - 128;
                if (amplitude < 0)
                {
                    amplitude = -amplitude;
                }
                if (amplitude > max)
                {
                    max = amplitude;
                }
            }
            return max / 128f;
        }
    }
}
