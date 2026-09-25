using System;

namespace MCG_Doom.Audio
{
    internal sealed class DoomMusReader
    {
        private readonly byte[] _data;
        private readonly int _scoreStart;
        private readonly int _scoreEnd;
        private readonly int[] _lastVolume = new int[16];
        private int _position;

        public DoomMusReader(byte[] data)
        {
            if (data == null || data.Length < 16 ||
                data[0] != (byte)'M' || data[1] != (byte)'U' ||
                data[2] != (byte)'S' || data[3] != 0x1A)
            {
                throw new ArgumentException("Invalid DOOM MUS data.", nameof(data));
            }

            _data = data;
            var scoreLength = BitConverter.ToUInt16(data, 4);
            _scoreStart = BitConverter.ToUInt16(data, 6);
            _scoreEnd = Math.Min(data.Length, _scoreStart + scoreLength);

            if (_scoreStart < 16 || _scoreStart >= _scoreEnd)
            {
                throw new ArgumentException("Invalid DOOM MUS score range.", nameof(data));
            }

            Reset();
        }

        public void Reset()
        {
            Array.Clear(_lastVolume, 0, _lastVolume.Length);
            _position = _scoreStart;
        }

        public int ReadEventGroup(DoomMusEvent[] events, out int eventCount)
        {
            eventCount = 0;

            while (true)
            {
                var result = ReadSingleEvent(events, ref eventCount);
                if (result == ReadResult.EndOfFile)
                {
                    return -1;
                }

                if (result == ReadResult.EndOfGroup)
                {
                    break;
                }
            }

            var time = 0;
            while (true)
            {
                EnsureAvailable(1);
                var value = _data[_position++];
                time = time * 128 + (value & 127);
                if ((value & 128) == 0)
                {
                    return time;
                }
            }
        }

        private ReadResult ReadSingleEvent(DoomMusEvent[] events, ref int eventCount)
        {
            EnsureAvailable(1);
            var descriptor = _data[_position++];
            var channel = descriptor & 0x0F;
            channel = channel == 15 ? 9 : channel >= 9 ? channel + 1 : channel;

            var eventType = (descriptor & 0x70) >> 4;
            var last = (descriptor & 0x80) != 0;

            if (eventType == 6)
            {
                return ReadResult.EndOfFile;
            }

            if (eventCount >= events.Length)
            {
                throw new InvalidOperationException("A DOOM MUS event group exceeded the supported size.");
            }

            var item = new DoomMusEvent
            {
                Type = eventType,
                Channel = channel
            };

            switch (eventType)
            {
                case 0: // Release note.
                    item.Data1 = ReadByte();
                    break;

                case 1: // Play note.
                    var note = ReadByte();
                    item.Data1 = note & 127;
                    if ((note & 128) != 0)
                    {
                        _lastVolume[channel] = ReadByte();
                    }
                    item.Data2 = _lastVolume[channel];
                    break;

                case 2: // Pitch wheel.
                    var pitch = ReadByte();
                    var pitch14 = (pitch << 7) / 2;
                    item.Data1 = pitch14 & 127;
                    item.Data2 = pitch14 >> 7;
                    break;

                case 3: // System event.
                    item.Data1 = ReadByte();
                    break;

                case 4: // Control change.
                    item.Data1 = ReadByte();
                    item.Data2 = ReadByte();
                    break;

                default:
                    throw new InvalidOperationException("Unknown DOOM MUS event type: " + eventType);
            }

            events[eventCount++] = item;
            return last ? ReadResult.EndOfGroup : ReadResult.Ongoing;
        }

        private int ReadByte()
        {
            EnsureAvailable(1);
            return _data[_position++];
        }

        private void EnsureAvailable(int count)
        {
            if (_position < _scoreStart || _position + count > _scoreEnd)
            {
                throw new InvalidOperationException("Unexpected end of DOOM MUS data.");
            }
        }

        private enum ReadResult
        {
            Ongoing,
            EndOfGroup,
            EndOfFile
        }
    }
}
