#nullable enable
using System;
using System.IO;
using System.Text;
using UnityEngine;

internal static class BugattiChironWave
{
    // Config is installed verbatim, keeping the original synthesized audio
    // assets self-contained in this mod.
    internal static AudioClip Load(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.ASCII);
        if (stream.Length < 44 || stream.Length > 4 * 1024 * 1024 ||
            new string(reader.ReadChars(4)) != "RIFF")
            throw new InvalidDataException("Invalid horn WAV size or header.");
        var end = 8L + reader.ReadUInt32();
        if (end > stream.Length || new string(reader.ReadChars(4)) != "WAVE")
            throw new InvalidDataException("Invalid horn WAV container.");

        int format = 0, channels = 0, rate = 0, bits = 0, alignment = 0;
        byte[]? bytes = null;
        while (stream.Position + 8 <= end)
        {
            var id = new string(reader.ReadChars(4));
            var size = reader.ReadUInt32();
            var next = stream.Position + size;
            if (next > end)
                throw new InvalidDataException("Truncated horn WAV chunk.");
            if (id == "fmt ")
            {
                if (size < 16)
                    throw new InvalidDataException("Truncated horn WAV format.");
                format = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                rate = reader.ReadInt32();
                reader.ReadUInt32();
                alignment = reader.ReadUInt16();
                bits = reader.ReadUInt16();
            }
            else if (id == "data")
            {
                bytes = reader.ReadBytes(checked((int)size));
            }
            stream.Position = next + (size & 1);
        }

        if (format != 1 || channels != 1 || bits != 16 || alignment != 2 ||
            rate != 44100 || bytes == null || bytes.Length == 0 || bytes.Length % 2 != 0 ||
            bytes.Length / 2 > rate * 5)
            throw new InvalidDataException("Expected mono 44.1 kHz 16-bit PCM, at most five seconds.");

        var samples = new float[bytes.Length / 2];
        for (var index = 0; index < samples.Length; index++)
            samples[index] =
                (short)(bytes[index * 2] | bytes[index * 2 + 1] << 8) / 32768f;
        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), samples.Length, 1, rate, false);
        try
        {
            if (!clip.SetData(samples, 0))
                throw new InvalidDataException("Cannot populate horn audio clip.");
            return clip;
        }
        catch
        {
            UnityEngine.Object.Destroy(clip);
            throw;
        }
    }
}
