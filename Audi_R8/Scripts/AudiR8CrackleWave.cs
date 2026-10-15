#nullable enable
using System;
using UnityEngine;

// Retained for serialized asset/GUID stability; AudiR8AudioController no
// longer creates or plays this noisy exhaust layer.
internal static class AudiR8CrackleWave
{
    private const int SampleRate = 44100;
    private const float Duration = 2f;

    internal static AudioClip Create()
    {
        const int pulseCount = 32;
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        uint random = 0x52455655u;
        var filteredNoise = 0f;
        for (var index = 0; index < sampleCount; index++)
        {
            random = random * 1664525u + 1013904223u;
            var noise = ((random >> 8) / 8388607.5f) - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, .16f);
            var pulsePosition = index * pulseCount / (float)sampleCount;
            var pulsePhase = pulsePosition - Mathf.Floor(pulsePosition);
            var envelope = Mathf.Exp(-pulsePhase * 15f);
            var texture = Mathf.Sin(2f * Mathf.PI * 410f * index / SampleRate);
            samples[index] = envelope * (.88f * filteredNoise + .12f * texture) * .34f;
        }

        var clip = AudioClip.Create(
            "Audi R8 naturally aspirated V10 exhaust crackle",
            sampleCount,
            1,
            SampleRate,
            false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural exhaust crackle.");
        }

        return clip;
    }
}
