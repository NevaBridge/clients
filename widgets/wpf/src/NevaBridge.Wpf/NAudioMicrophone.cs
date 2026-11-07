using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NevaBridge.Chat;

namespace NevaBridge.Wpf;

/// <summary>
/// The default Windows microphone through WASAPI, converted from whatever format the device uses
/// to 16-bit mono PCM at the rate the chat asks for.
/// </summary>
public sealed class NAudioMicrophone : IMicrophone
{
    private const int FrameMilliseconds = 100;

    public Task<IDisposable> OpenAsync(int sampleRate, Action<ReadOnlyMemory<byte>> onAudio, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        WasapiCapture capture;
        try
        {
            MMDevice device = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
            capture = new WasapiCapture(device, useEventSync: false, audioBufferMillisecondsLength: FrameMilliseconds);
        }
        catch (Exception error)
        {
            throw new MicrophoneUnavailableException("No microphone is connected.", error);
        }

        BufferedWaveProvider captured = new(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true,
            ReadFully = false,
        };
        ISampleProvider mono = new DownmixToMono(captured.ToSampleProvider());
        IWaveProvider pcm = new SampleToWaveProvider16(new WdlResamplingSampleProvider(mono, sampleRate));
        byte[] frame = new byte[sampleRate * 2 * FrameMilliseconds / 1000];
        capture.DataAvailable += (_, e) =>
        {
            captured.AddSamples(e.Buffer, 0, e.BytesRecorded);
            int read;
            while ((read = pcm.Read(frame, 0, frame.Length)) > 0)
            {
                onAudio(frame.AsMemory(0, read));
            }
        };

        try
        {
            capture.StartRecording();
        }
        catch (Exception error)
        {
            capture.Dispose();

            // Windows privacy settings deny access with E_ACCESSDENIED when the device is opened.
            throw new MicrophoneUnavailableException("Windows denied access to the microphone.", error);
        }

        return Task.FromResult<IDisposable>(new Recording(capture));
    }

    private sealed class Recording(WasapiCapture capture) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            capture.StopRecording();
            capture.Dispose();
        }
    }

    /// <summary>Averages every channel into one, whatever the device's channel count.</summary>
    private sealed class DownmixToMono(ISampleProvider source) : ISampleProvider
    {
        private readonly int _channels = source.WaveFormat.Channels;
        private float[] _buffer = [];

        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            if (_channels == 1)
            {
                return source.Read(buffer, offset, count);
            }

            int needed = count * _channels;
            if (_buffer.Length < needed)
            {
                _buffer = new float[needed];
            }

            int frames = source.Read(_buffer, 0, needed) / _channels;
            for (int frame = 0; frame < frames; frame++)
            {
                float sum = 0;
                for (int channel = 0; channel < _channels; channel++)
                {
                    sum += _buffer[(frame * _channels) + channel];
                }

                buffer[offset + frame] = sum / _channels;
            }

            return frames;
        }
    }
}
