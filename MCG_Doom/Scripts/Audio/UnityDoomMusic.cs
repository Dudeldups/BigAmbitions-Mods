using System;
using ManagedDoom;
using ManagedDoom.Audio;
using MeltySynth;
using UnityEngine;

namespace MCG_Doom.Audio
{
    internal sealed class UnityDoomMusic : IMusic, IDisposable
    {
        private const int ChannelCount = 2;
        private const int StreamLengthSeconds = 2;

        private readonly object _sync = new object();
        private readonly Config _config;
        private readonly Wad _wad;
        private readonly Synthesizer _synthesizer;
        private readonly GameObject _audioObject;
        private readonly AudioSource _audioSource;
        private readonly AudioClip _audioClip;

        private DoomMusDecoder _current;
        private DoomMusDecoder _pending;
        private float[] _left = new float[2048];
        private float[] _right = new float[2048];
        private Bgm _currentBgm = Bgm.NONE;
        private bool _disposed;

        public UnityDoomMusic(Config config, GameContent content, string soundFontPath, Transform parent)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (string.IsNullOrEmpty(soundFontPath)) throw new ArgumentNullException(nameof(soundFontPath));

            _wad = content.Wad;
            _config.audio_musicvolume = Clamp(_config.audio_musicvolume, 0, MaxVolume);

            var settings = new SynthesizerSettings(DoomMusDecoder.SampleRate)
            {
                BlockSize = DoomMusDecoder.BlockLength,
                EnableReverbAndChorus = false
            };
            _synthesizer = new Synthesizer(soundFontPath, settings);

            _audioObject = new GameObject("MCG_Doom Music");
            _audioObject.transform.SetParent(parent, false);
            _audioSource = _audioObject.AddComponent<AudioSource>();
            _audioSource.playOnAwake = false;
            _audioSource.loop = true;
            _audioSource.spatialBlend = 0f;
            _audioSource.volume = 1f;

            _audioClip = AudioClip.Create(
                "MCG_Doom Music Stream",
                DoomMusDecoder.SampleRate * StreamLengthSeconds,
                ChannelCount,
                DoomMusDecoder.SampleRate,
                true,
                FillAudioBuffer);

            _audioSource.clip = _audioClip;
            _audioSource.Play();
        }

        public void StartMusic(Bgm bgm, bool loop)
        {
            if (_disposed || bgm == _currentBgm)
            {
                return;
            }

            if (bgm == Bgm.NONE)
            {
                lock (_sync)
                {
                    _pending = null;
                    _currentBgm = Bgm.NONE;
                }
                return;
            }

            var lump = "D_" + DoomInfo.BgmNames[(int)bgm].ToString().ToUpperInvariant();
            var data = _wad.ReadLump(lump);
            var decoder = new DoomMusDecoder(data, loop);

            lock (_sync)
            {
                _pending = decoder;
                _currentBgm = bgm;
            }
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get => _config.audio_musicvolume;
            set => _config.audio_musicvolume = Clamp(value, 0, MaxVolume);
        }

        private void FillAudioBuffer(float[] samples)
        {
            if (samples == null)
            {
                return;
            }

            lock (_sync)
            {
                if (_disposed)
                {
                    Array.Clear(samples, 0, samples.Length);
                    return;
                }

                if (!ReferenceEquals(_pending, _current))
                {
                    _synthesizer.Reset();
                    _current = _pending;
                }

                if (_current == null)
                {
                    Array.Clear(samples, 0, samples.Length);
                    return;
                }

                var frames = samples.Length / ChannelCount;
                EnsureBuffers(frames);
                Array.Clear(_left, 0, frames);
                Array.Clear(_right, 0, frames);
                _current.Render(_synthesizer, _left, _right, frames);

                var gain = 2f * Volume / MaxVolume;
                var position = 0;
                for (var i = 0; i < frames; i++)
                {
                    samples[position++] = _left[i] * gain;
                    samples[position++] = _right[i] * gain;
                }
            }
        }

        private void EnsureBuffers(int frames)
        {
            if (_left.Length >= frames)
            {
                return;
            }

            _left = new float[frames];
            _right = new float[frames];
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _pending = null;
                _current = null;
            }

            if (_audioSource != null)
            {
                _audioSource.Stop();
                _audioSource.clip = null;
            }

            if (_audioClip != null)
            {
                UnityEngine.Object.Destroy(_audioClip);
            }

            if (_audioObject != null)
            {
                UnityEngine.Object.Destroy(_audioObject);
            }
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }
    }
}
