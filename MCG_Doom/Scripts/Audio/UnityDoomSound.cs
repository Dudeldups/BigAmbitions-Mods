using System;
using ManagedDoom;
using ManagedDoom.Audio;
using UnityEngine;

namespace MCG_Doom.Audio
{
    internal sealed class UnityDoomSound : ISound, IDisposable
    {
        private const int ChannelCount = 8;
        private const float ClipDistance = 1200f;
        private const float CloseDistance = 160f;
        private const float Attenuator = ClipDistance - CloseDistance;

        private static readonly float FastDecay = (float)Math.Pow(0.5, 1.0 / (35.0 / 5.0));
        private static readonly float SlowDecay = (float)Math.Pow(0.5, 1.0 / 35.0);

        private readonly Config _config;
        private readonly DoomSfxLibrary _library;
        private readonly DoomSoundChannel[] _channels;
        private readonly DoomSoundChannel _uiChannel;
        private readonly DoomRandom _random;

        private Mobj _listener;
        private Sfx _uiReserved;
        private float _masterVolume;
        private DateTime _lastUpdate;
        private bool _disposed;

        public UnityDoomSound(Config config, GameContent content, Transform parent)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            if (content == null) throw new ArgumentNullException(nameof(content));
            if (parent == null) throw new ArgumentNullException(nameof(parent));

            _config.audio_soundvolume = Clamp(_config.audio_soundvolume, 0, MaxVolume);
            _masterVolume = _config.audio_soundvolume / (float)MaxVolume;
            _library = new DoomSfxLibrary(content);
            _random = _config.audio_randompitch ? new DoomRandom() : null;

            _channels = new DoomSoundChannel[ChannelCount];
            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i] = new DoomSoundChannel(parent, "MCG_Doom SFX Channel " + (i + 1));
            }

            _uiChannel = new DoomSoundChannel(parent, "MCG_Doom UI SFX Channel");
            _uiReserved = Sfx.NONE;
            _lastUpdate = DateTime.MinValue;
        }

        public void SetListener(Mobj listener)
        {
            _listener = listener;
        }

        public void Update()
        {
            if (_disposed)
            {
                return;
            }

            var now = DateTime.Now;
            if ((now - _lastUpdate).TotalSeconds < 0.01)
            {
                return;
            }

            for (var i = 0; i < _channels.Length; i++)
            {
                UpdateChannel(_channels[i]);
            }

            if (_uiReserved != Sfx.NONE)
            {
                PlayUiSound(_uiReserved);
                _uiReserved = Sfx.NONE;
            }

            _lastUpdate = now;
        }

        public void StartSound(Sfx sfx)
        {
            if (_disposed || _library.GetClip(sfx) == null)
            {
                return;
            }

            _uiReserved = sfx;
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type)
        {
            StartSound(mobj, sfx, type, 100);
        }

        public void StartSound(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (_disposed || mobj == null || _library.GetClip(sfx) == null)
            {
                return;
            }

            var priority = CalculatePriority(mobj, sfx, type, volume);

            for (var i = 0; i < _channels.Length; i++)
            {
                var channel = _channels[i];
                if (channel.Origin == mobj && channel.Type == type)
                {
                    Reserve(channel, mobj, sfx, type, volume, priority);
                    return;
                }
            }

            for (var i = 0; i < _channels.Length; i++)
            {
                var channel = _channels[i];
                if (channel.Reserved == Sfx.NONE && channel.Playing == Sfx.NONE)
                {
                    Reserve(channel, mobj, sfx, type, volume, priority);
                    return;
                }
            }

            var minPriority = float.MaxValue;
            DoomSoundChannel replacement = null;
            for (var i = 0; i < _channels.Length; i++)
            {
                if (_channels[i].Priority < minPriority)
                {
                    minPriority = _channels[i].Priority;
                    replacement = _channels[i];
                }
            }

            if (replacement != null && priority >= minPriority)
            {
                Reserve(replacement, mobj, sfx, type, volume, priority);
            }
        }

        public void StopSound(Mobj mobj)
        {
            if (mobj == null)
            {
                return;
            }

            for (var i = 0; i < _channels.Length; i++)
            {
                var channel = _channels[i];
                if (channel.Origin == mobj)
                {
                    channel.LastX = mobj.X;
                    channel.LastY = mobj.Y;
                    channel.Origin = null;
                    channel.RequestedVolume /= 5;
                }
            }
        }

        public void Reset()
        {
            if (_random != null)
            {
                _random.Clear();
            }

            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i].Stop();
            }

            _uiChannel.Stop();
            _uiReserved = Sfx.NONE;
            _listener = null;
        }

        public void Pause()
        {
            for (var i = 0; i < _channels.Length; i++)
            {
                var source = _channels[i].Source;
                if (source.isPlaying && source.clip != null && source.clip.length - source.time > 0.2f)
                {
                    source.Pause();
                    _channels[i].IsPaused = true;
                }
            }
        }

        public void Resume()
        {
            for (var i = 0; i < _channels.Length; i++)
            {
                var channel = _channels[i];
                if (channel.Playing != Sfx.NONE && channel.IsPaused && channel.Source.clip != null)
                {
                    channel.Source.UnPause();
                    channel.IsPaused = false;
                }
            }
        }

        public int MaxVolume => 15;

        public int Volume
        {
            get => _config.audio_soundvolume;
            set
            {
                _config.audio_soundvolume = Clamp(value, 0, MaxVolume);
                _masterVolume = _config.audio_soundvolume / (float)MaxVolume;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Reset();

            for (var i = 0; i < _channels.Length; i++)
            {
                _channels[i].Dispose();
            }

            _uiChannel.Dispose();
            _library.Dispose();
        }

        private void UpdateChannel(DoomSoundChannel channel)
        {
            if (channel.Playing != Sfx.NONE)
            {
                if (channel.IsPaused)
                {
                    return;
                }

                if (channel.Source.isPlaying)
                {
                    channel.Priority *= channel.Type == SfxType.Diffuse ? SlowDecay : FastDecay;
                    ApplyParameters(channel);
                }
                else
                {
                    channel.Playing = Sfx.NONE;
                    if (channel.Reserved == Sfx.NONE)
                    {
                        channel.Origin = null;
                    }
                }
            }

            if (channel.Reserved == Sfx.NONE)
            {
                return;
            }

            if (channel.Playing != Sfx.NONE)
            {
                channel.Source.Stop();
            }

            var clip = _library.GetClip(channel.Reserved);
            channel.Source.clip = clip;
            ApplyParameters(channel);
            channel.Source.pitch = GetPitch(channel.Type, channel.Reserved);
            channel.Source.Play();
            channel.IsPaused = false;
            channel.Playing = channel.Reserved;
            channel.Reserved = Sfx.NONE;
        }

        private void PlayUiSound(Sfx sfx)
        {
            var clip = _library.GetClip(sfx);
            if (clip == null)
            {
                return;
            }

            _uiChannel.Source.Stop();
            _uiChannel.Source.clip = clip;
            _uiChannel.Source.volume = _masterVolume;
            _uiChannel.Source.panStereo = 0f;
            _uiChannel.Source.pitch = 1f;
            _uiChannel.Source.Play();
        }

        private void ApplyParameters(DoomSoundChannel channel)
        {
            if (channel.Type == SfxType.Diffuse || _listener == null)
            {
                channel.Source.panStereo = 0f;
                channel.Source.volume = Clamp01(0.01f * _masterVolume * channel.RequestedVolume);
                return;
            }

            Fixed sourceX;
            Fixed sourceY;
            if (channel.Origin == null)
            {
                sourceX = channel.LastX;
                sourceY = channel.LastY;
            }
            else
            {
                sourceX = channel.Origin.X;
                sourceY = channel.Origin.Y;
            }

            var x = (sourceX - _listener.X).ToFloat();
            var y = (sourceY - _listener.Y).ToFloat();
            if (Math.Abs(x) < 16f && Math.Abs(y) < 16f)
            {
                channel.Source.panStereo = 0f;
                channel.Source.volume = Clamp01(0.01f * _masterVolume * channel.RequestedVolume);
                return;
            }

            var dist = (float)Math.Sqrt(x * x + y * y);
            var angle = (float)Math.Atan2(y, x) - (float)_listener.Angle.ToRadian();
            channel.Source.panStereo = ClampFloat(-(float)Math.Sin(angle), -1f, 1f);
            channel.Source.volume = Clamp01(0.01f * _masterVolume * GetDistanceDecay(dist) * channel.RequestedVolume);
        }

        private float CalculatePriority(Mobj mobj, Sfx sfx, SfxType type, int volume)
        {
            if (type == SfxType.Diffuse || _listener == null)
            {
                return volume;
            }

            var x = (mobj.X - _listener.X).ToFloat();
            var y = (mobj.Y - _listener.Y).ToFloat();
            var dist = (float)Math.Sqrt(x * x + y * y);
            return _library.GetAmplitude(sfx) * GetDistanceDecay(dist) * volume;
        }

        private float GetDistanceDecay(float distance)
        {
            if (distance < CloseDistance)
            {
                return 1f;
            }

            return Math.Max((ClipDistance - distance) / Attenuator, 0f);
        }

        private float GetPitch(SfxType type, Sfx sfx)
        {
            if (_random == null || sfx == Sfx.ITEMUP || sfx == Sfx.TINK || sfx == Sfx.RADIO)
            {
                return 1f;
            }

            if (type == SfxType.Voice)
            {
                return 1f + 0.075f * (_random.Next() - 128) / 128f;
            }

            return 1f + 0.025f * (_random.Next() - 128) / 128f;
        }

        private static void Reserve(DoomSoundChannel channel, Mobj mobj, Sfx sfx, SfxType type, int volume, float priority)
        {
            channel.Reserved = sfx;
            channel.Priority = priority;
            channel.Origin = mobj;
            channel.Type = type;
            channel.RequestedVolume = volume;
        }

        private static int Clamp(int value, int min, int max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static float ClampFloat(float value, float min, float max)
        {
            return value < min ? min : value > max ? max : value;
        }

        private static float Clamp01(float value)
        {
            return ClampFloat(value, 0f, 1f);
        }
    }
}
