#nullable enable
using System;
using System.IO;
using System.Text;
using UnityEngine;

internal static class AudiRS6RWave
{
    // External-build installs Config verbatim; no vehicle bundle rebuild required.
    internal static AudioClip Load(string path)
    {
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
            rate != 44100 || bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 ||
            bytes.Length / 2 > rate * 5)
            throw new InvalidDataException("Expected mono 44.1 kHz 16-bit PCM, at most five seconds.");
        var samples = new float[bytes.Length / 2];
        for (var i = 0; i < samples.Length; i++)
            samples[i] = (short)(bytes[2*i] | bytes[2*i+1] << 8) / 32768f;
        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples.Length, 1, rate, false);
        try
        {
            if (!clip.SetData(samples, 0)) throw new InvalidDataException("Cannot populate engine audio clip.");
            return clip;
        }
        catch { UnityEngine.Object.Destroy(clip); throw; }
    }
}
