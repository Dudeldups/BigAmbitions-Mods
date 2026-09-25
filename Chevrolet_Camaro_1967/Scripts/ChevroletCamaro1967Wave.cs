#nullable enable
using System;
using System.IO;
using System.Text;
using UnityEngine;

internal static class ChevroletCamaro1967Wave
{
    private const int SampleRate = 44100;

    internal static AudioClip Load(string path)
    {
        if (!File.Exists(path))
            return CreateProcedural(Path.GetFileNameWithoutExtension(path));

        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (stream.Length < 44 || stream.Length > 4 * 1024 * 1024 ||
            new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Invalid engine WAV size or header.");
        var end = 8L + reader.ReadUInt32();
        if (end > stream.Length || new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Invalid engine WAV container.");
        int format = 0, channels = 0, rate = 0, bits = 0, alignment = 0;
        byte[]? bytes = null;
        while (stream.Position + 8 <= end)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (next > end) throw new InvalidDataException("Truncated engine WAV chunk.");
            if (id == "fmt ")
            {
                if (size < 16) throw new InvalidDataException("Truncated engine WAV format.");
                format = reader.ReadUInt16(); channels = reader.ReadUInt16();
                rate = reader.ReadInt32(); reader.ReadUInt32();
                alignment = reader.ReadUInt16(); bits = reader.ReadUInt16();
            }
            else if (id == "data") bytes = reader.ReadBytes(checked((int)size));
            stream.Position = next + (size & 1);
        }
        if (format != 1 || channels != 1 || bits != 16 || alignment != 2 ||
            rate != SampleRate || bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 ||
            bytes.Length / 2 > rate * 5)
            throw new InvalidDataException("Expected mono 44.1 kHz 16-bit PCM, at most five seconds.");
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[2*i] | bytes[2*i+1] << 8) / 32768f;
        return CreateClip(Path.GetFileNameWithoutExtension(path), samples);
    }

    private static AudioClip CreateProcedural(string name)
    {
        var horn = name.StartsWith("Horn", StringComparison.OrdinalIgnoreCase);
        var seconds = horn ? 2.0f : 4.0f;
        var count = Mathf.RoundToInt(SampleRate * seconds);
        var samples = new float[count];
        var load = name.EndsWith("Load", StringComparison.OrdinalIgnoreCase);
        var fundamental = name.IndexOf("High", StringComparison.OrdinalIgnoreCase) >= 0
            ? (horn ? 410f : 420f)
            : name.IndexOf("Mid", StringComparison.OrdinalIgnoreCase) >= 0
                ? 220f
                : horn ? 340f : 70f;

        for (var index = 0; index < count; index++)
        {
            var t = index / (float)SampleRate;
            float sample;
            if (horn)
            {
                sample = 0.55f * Mathf.Sin(2f * Mathf.PI * fundamental * t) +
                         0.20f * Mathf.Sin(2f * Mathf.PI * fundamental * 2f * t + 0.15f) +
                         0.08f * Mathf.Sin(2f * Mathf.PI * fundamental * 3f * t + 0.42f);
            }
            else
            {
                var pulse = 0.86f + 0.14f * Mathf.Sin(2f * Mathf.PI * 8f * t);
                sample = pulse * (
                    0.54f * Mathf.Sin(2f * Mathf.PI * fundamental * t) +
                    0.24f * Mathf.Sin(2f * Mathf.PI * fundamental * 2f * t + 0.31f) +
                    0.13f * Mathf.Sin(2f * Mathf.PI * fundamental * 3f * t + 0.77f) +
                    0.07f * Mathf.Sin(2f * Mathf.PI * fundamental * 4f * t + 1.13f));
                if (load)
                    sample += 0.08f * Mathf.Sin(2f * Mathf.PI * fundamental * 0.5f * t + 0.22f);
            }
            samples[index] = Mathf.Clamp(sample * (horn ? 0.65f : 0.55f), -0.92f, 0.92f);
        }
        return CreateClip(name + "_ProceduralFallback", samples);
    }

    private static AudioClip CreateClip(string name, float[] samples)
    {
        var clip = AudioClip.Create(name, samples.Length, 1, SampleRate, false);
        try
        {
            if (!clip.SetData(samples, 0))
                throw new InvalidDataException("Cannot populate engine audio clip.");
            return clip;
        }
        catch
        {
            UnityEngine.Object.Destroy(clip);
            throw;
        }
    }
}
