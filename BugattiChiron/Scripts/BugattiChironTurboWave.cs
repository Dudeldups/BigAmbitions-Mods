#nullable enable
using System;
using UnityEngine;

internal static class BugattiChironTurboWave
{
    private const int SampleRate = 44100;
    private const float Duration = 2f;

    internal static AudioClip Create()
    {
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        uint random = 0x57313654u;
        var filteredNoise = 0f;
        for (var index = 0; index < sampleCount; index++)
        {
            random = random * 1664525u + 1013904223u;
            var noise = ((random >> 8) / 8388607.5f) - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.025f);
            var time = index / (float)SampleRate;
            var compressorTone =
                0.25f * Mathf.Sin(2f * Mathf.PI * 660f * time) +
                0.12f * Mathf.Sin(2f * Mathf.PI * 990f * time + 0.7f) +
                0.08f * Mathf.Sin(2f * Mathf.PI * 1320f * time + 1.4f);
            samples[index] = (compressorTone * 0.10f + filteredNoise * 0.30f) * 0.32f;
        }

        var clip = AudioClip.Create(
            "Bugatti Chiron quad-turbo airflow",
            sampleCount,
            1,
            SampleRate,
            false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural turbo layer.");
        }
        return clip;
    }
}
