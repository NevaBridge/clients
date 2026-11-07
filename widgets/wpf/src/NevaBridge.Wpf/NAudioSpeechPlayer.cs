using System.IO;
using NAudio.Wave;
using NevaBridge.Chat;

namespace NevaBridge.Wpf;

/// <summary>Plays the MP3 speech NevaBridge synthesizes on the default Windows audio output.</summary>
public sealed class NAudioSpeechPlayer : ISpeechPlayer
{
    public async Task PlayAsync(byte[] mp3, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using Mp3FileReader reader = new(new MemoryStream(mp3, writable: false));
        using WaveOutEvent output = new();
        TaskCompletionSource played = new(TaskCreationOptions.RunContinuationsAsynchronously);
        output.PlaybackStopped += (_, e) =>
        {
            if (e.Exception is { } error)
            {
                played.TrySetException(error);
            }
            else
            {
                played.TrySetResult();
            }
        };
        output.Init(reader);
        using (cancellationToken.Register(() =>
        {
            played.TrySetCanceled(cancellationToken);
            output.Stop();
        }))
        {
            output.Play();
            await played.Task;
        }
    }
}
