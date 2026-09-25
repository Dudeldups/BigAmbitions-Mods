#nullable enable
using System;
using UnityEngine;

internal static class BigfootMonsterTruckEngineWave
{
    private const int SampleRate = 44100;
    private const float Duration = 2f;

    internal static AudioClip CreateRumble() => CreateCombustionLoop(
        "Bigfoot V8 low rumble",
        78f,
        164f,
        0.12f);

    internal static AudioClip CreateRoar() => CreateCombustionLoop(
        "Bigfoot V8 supercharged roar",
        126f,
        348f,
        0.38f);

    internal static AudioClip CreateHorn()
    {
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        for (var index = 0; index < sampleCount; index++)
        {
            var time = index / (float)SampleRate;
            var flutter = 0.012f * Mathf.Sin(2f * Mathf.PI * 5f * time);
            var lowPhase = 2f * Mathf.PI * (138f * time + flutter);
            var highPhase = 2f * Mathf.PI * (174f * time + flutter * 1.08f);
            var lowPipe =
                Mathf.Sin(lowPhase) +
                0.48f * Mathf.Sin(2f * lowPhase + 0.12f) +
                0.35f * Mathf.Sin(3f * lowPhase + 0.31f) +
                0.22f * Mathf.Sin(4f * lowPhase + 0.48f) +
                0.12f * Mathf.Sin(5f * lowPhase + 0.67f) +
                0.06f * Mathf.Sin(6f * lowPhase + 0.83f);
            var highPipe =
                Mathf.Sin(highPhase + 0.18f) +
                0.46f * Mathf.Sin(2f * highPhase + 0.36f) +
                0.33f * Mathf.Sin(3f * highPhase + 0.54f) +
                0.21f * Mathf.Sin(4f * highPhase + 0.73f) +
                0.11f * Mathf.Sin(5f * highPhase + 0.91f);
            var brass = 0.56f * lowPipe + 0.58f * highPipe;
            var compressor = (float)Math.Tanh(brass * 1.85f);
            samples[index] = compressor * 0.70f;
        }

        var clip = AudioClip.Create(
            "Bigfoot continuous truck air horn",
            sampleCount,
            1,
            SampleRate,
            false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural truck horn.");
        }
        return clip;
    }

    internal static AudioClip CreateCrackle()
    {
        const int pulseCount = 40;
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        uint random = 0x4D595DF4u;
        var filteredNoise = 0f;
        for (var index = 0; index < sampleCount; index++)
        {
            random = random * 1664525u + 1013904223u;
            var noise = ((random >> 8) / 8388607.5f) - 1f;
            filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.34f);
            var pulsePosition = index * pulseCount / (float)sampleCount;
            var pulsePhase = pulsePosition - Mathf.Floor(pulsePosition);
            var envelope = Mathf.Exp(-pulsePhase * 24f);
            var metallicSnap = Mathf.Sin(2f * Mathf.PI * 620f * index / SampleRate);
            samples[index] = envelope *
                             (0.72f * filteredNoise + 0.28f * metallicSnap) * 0.58f;
        }

        var clip = AudioClip.Create(
            "Bigfoot V8 exhaust crackle",
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

    private static AudioClip CreateCombustionLoop(
        string name,
        float primaryResonance,
        float secondaryResonance,
        float gritMix)
    {
        const int pulseCount = 104;
        var sampleCount = (int)(SampleRate * Duration);
        var samples = new float[sampleCount];
        var pulseSpacing = sampleCount / (float)pulseCount;
        var tailSamples = (int)(SampleRate * 0.045f);
        uint random = 0xB16B00B5u;

        for (var pulse = 0; pulse < pulseCount; pulse++)
        {
            var jitter = (NextSigned(ref random) * 0.075f) * pulseSpacing;
            var start = Mathf.RoundToInt(pulse * pulseSpacing + jitter);
            var pulseGain = 0.72f + 0.28f * (NextSigned(ref random) * 0.5f + 0.5f);
            var filteredNoise = 0f;
            for (var offset = 0; offset < tailSamples; offset++)
            {
                var time = offset / (float)SampleRate;
                var attack = 1f - Mathf.Exp(-time * 1800f);
                var decay = Mathf.Exp(-time * 92f);
                var envelope = attack * decay;
                var noise = NextSigned(ref random);
                filteredNoise = Mathf.Lerp(filteredNoise, noise, 0.28f);
                var resonance = 0.72f * Mathf.Sin(2f * Mathf.PI * primaryResonance * time) +
                                0.28f * Mathf.Sin(2f * Mathf.PI * secondaryResonance * time);
                var grit = 0.65f * filteredNoise + 0.35f * noise;
                var value = pulseGain * envelope *
                            ((1f - gritMix) * resonance + gritMix * grit);
                var sampleIndex = (start + offset) % sampleCount;
                if (sampleIndex < 0)
                    sampleIndex += sampleCount;
                samples[sampleIndex] += value;
            }
        }

        var peak = 0f;
        for (var index = 0; index < sampleCount; index++)
            peak = Mathf.Max(peak, Mathf.Abs(samples[index]));

        var scale = peak > 0f ? 0.68f / peak : 1f;
        for (var index = 0; index < samples.Length; index++)
            samples[index] *= scale;

        var clip = AudioClip.Create(name, sampleCount, 1, SampleRate, false);
        if (!clip.SetData(samples, 0))
        {
            UnityEngine.Object.Destroy(clip);
            throw new InvalidOperationException("Could not initialize procedural engine audio.");
        }
        return clip;
    }

    private static float NextSigned(ref uint state)
    {
        state = state * 1664525u + 1013904223u;
        return ((state >> 8) / 8388607.5f) - 1f;
    }
}
