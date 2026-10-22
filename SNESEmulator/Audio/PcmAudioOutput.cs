/*
   Copyright 2026 Nils Kopal <Nils.Kopal<at>kopaldev.de

   Licensed under the Apache License, Version 2.0 (the "License");
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

       http://www.apache.org/licenses/LICENSE-2.0

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/
using System;
using NAudio.Wave;

namespace SNESEmulator.Core
{
    /// <summary>
    /// Plays buffered mono PCM for the inherited network audio transport.
    /// </summary>
    public sealed class PcmAudioOutput : IDisposable
    {
        private readonly BufferedWaveProvider _bufferedProvider;
        private readonly WaveOutEvent _waveOut;

        /// <summary>
        /// Initializes a new PcmAudioOutput instance.
        /// </summary>
        public PcmAudioOutput(int sampleRate)
        {
            _bufferedProvider = new BufferedWaveProvider(new WaveFormat(sampleRate, 16, 1))
            {
                BufferDuration = TimeSpan.FromMilliseconds(250),
                DiscardOnBufferOverflow = true
            };

            _waveOut = new WaveOutEvent
            {
                DesiredLatency = 100,
                NumberOfBuffers = 2
            };

            _waveOut.Init(_bufferedProvider);
            _waveOut.Play();
        }

        /// <summary>
        /// Handles the write operation.
        /// </summary>
        public void Write(byte[] buffer, int count)
        {
            if (count <= 0)
            {
                return;
            }

            _bufferedProvider.AddSamples(buffer, 0, count);
        }

        /// <summary>
        /// Releases resources owned by the component.
        /// </summary>
        public void Dispose()
        {
            _waveOut.Stop();
            _waveOut.Dispose();
        }
    }
}
