using ManagedDoom;
using ManagedDoom.Audio;
using UnityEngine;

namespace MCG_Doom.Audio
{
    internal sealed class DoomSoundChannel
    {
        private readonly GameObject _gameObject;

        public DoomSoundChannel(Transform parent, string name)
        {
            _gameObject = new GameObject(name);
            _gameObject.transform.SetParent(parent, false);

            Source = _gameObject.AddComponent<AudioSource>();
            Source.playOnAwake = false;
            Source.loop = false;
            Source.spatialBlend = 0f;
            Clear();
        }

        public AudioSource Source { get; }
        public Sfx Reserved { get; set; }
        public Sfx Playing { get; set; }
        public float Priority { get; set; }
        public Mobj Origin { get; set; }
        public SfxType Type { get; set; }
        public int RequestedVolume { get; set; }
        public Fixed LastX { get; set; }
        public Fixed LastY { get; set; }
        public bool IsPaused { get; set; }

        public void Clear()
        {
            Reserved = Sfx.NONE;
            Playing = Sfx.NONE;
            Priority = 0f;
            Origin = null;
            Type = 0;
            RequestedVolume = 0;
            LastX = Fixed.Zero;
            LastY = Fixed.Zero;
            IsPaused = false;
        }

        public void Stop()
        {
            Source.Stop();
            Source.clip = null;
            Clear();
        }

        public void Dispose()
        {
            Source.Stop();
            Source.clip = null;
            UnityEngine.Object.Destroy(_gameObject);
        }
    }
}
