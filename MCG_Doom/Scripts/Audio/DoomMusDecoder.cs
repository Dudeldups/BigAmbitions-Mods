using MeltySynth;

namespace MCG_Doom.Audio
{
    internal sealed class DoomMusDecoder
    {
        public const int SampleRate = 44100;
        public const int MusTickRate = 140;
        public const int BlockLength = SampleRate / MusTickRate;

        private readonly DoomMusReader _reader;
        private readonly DoomMusEvent[] _events = new DoomMusEvent[128];
        private readonly bool _loop;
        private int _delay;
        private int _blockWrote = BlockLength;
        private bool _ended;

        public DoomMusDecoder(byte[] data, bool loop)
        {
            _reader = new DoomMusReader(data);
            _loop = loop;
        }

        public void Render(Synthesizer synthesizer, float[] left, float[] right, int sampleCount)
        {
            var wrote = 0;
            while (wrote < sampleCount)
            {
                if (_blockWrote == BlockLength)
                {
                    ProcessMidiEvents(synthesizer);
                    _blockWrote = 0;
                }

                var rem = System.Math.Min(BlockLength - _blockWrote, sampleCount - wrote);
                synthesizer.Render(left, wrote, right, wrote, rem);
                _blockWrote += rem;
                wrote += rem;
            }
        }

        private void ProcessMidiEvents(Synthesizer synthesizer)
        {
            if (_ended)
            {
                return;
            }

            if (_delay > 0)
            {
                _delay--;
            }

            if (_delay != 0)
            {
                return;
            }

            int eventCount;
            _delay = _reader.ReadEventGroup(_events, out eventCount);
            SendEvents(synthesizer, eventCount);

            if (_delay != -1)
            {
                return;
            }

            synthesizer.NoteOffAll(false);
            if (_loop)
            {
                _reader.Reset();
                _delay = 0;
            }
            else
            {
                _ended = true;
            }
        }

        private void SendEvents(Synthesizer synthesizer, int count)
        {
            for (var i = 0; i < count; i++)
            {
                var item = _events[i];
                switch (item.Type)
                {
                    case 0:
                        synthesizer.NoteOff(item.Channel, item.Data1);
                        break;
                    case 1:
                        synthesizer.NoteOn(item.Channel, item.Data1, item.Data2);
                        break;
                    case 2:
                        synthesizer.ProcessMidiMessage(item.Channel, 0xE0, item.Data1, item.Data2);
                        break;
                    case 3:
                        SendSystemEvent(synthesizer, item);
                        break;
                    case 4:
                        SendControlChange(synthesizer, item);
                        break;
                }
            }
        }

        private static void SendSystemEvent(Synthesizer synthesizer, DoomMusEvent item)
        {
            if (item.Data1 == 11)
            {
                synthesizer.NoteOffAll(item.Channel, false);
            }
            else if (item.Data1 == 14)
            {
                synthesizer.ResetAllControllers(item.Channel);
            }
        }

        private static void SendControlChange(Synthesizer synthesizer, DoomMusEvent item)
        {
            switch (item.Data1)
            {
                case 0: synthesizer.ProcessMidiMessage(item.Channel, 0xC0, item.Data2, 0); break;
                case 1: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x00, item.Data2); break;
                case 2: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x01, item.Data2); break;
                case 3: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x07, item.Data2); break;
                case 4: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x0A, item.Data2); break;
                case 5: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x0B, item.Data2); break;
                case 6: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x5B, item.Data2); break;
                case 7: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x5D, item.Data2); break;
                case 8: synthesizer.ProcessMidiMessage(item.Channel, 0xB0, 0x40, item.Data2); break;
            }
        }
    }
}
