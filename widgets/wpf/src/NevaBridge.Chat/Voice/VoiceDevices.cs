namespace NevaBridge.Chat;

/// <summary>
/// The microphone voice listens through. <c>NevaBridge.Wpf</c> provides one on Windows audio; tests
/// and tools can play prepared audio instead.
/// </summary>
public interface IMicrophone
{
    /// <summary>
    /// Opens the default microphone and delivers its audio as 16-bit little-endian mono PCM at
    /// <paramref name="sampleRate"/>, about 100 ms per call of <paramref name="onAudio"/>, until the
    /// returned handle is disposed. <paramref name="onAudio"/> may be called on any thread.
    /// </summary>
    /// <exception cref="MicrophoneUnavailableException">There is no microphone, or access to it is denied.</exception>
    Task<IDisposable> OpenAsync(int sampleRate, Action<ReadOnlyMemory<byte>> onAudio, CancellationToken cancellationToken);
}

/// <summary>Plays the speech that reads a reply aloud.</summary>
public interface ISpeechPlayer
{
    /// <summary>
    /// Plays MP3 audio and completes when it has played to its end. Cancelling stops playback at
    /// once and completes the task as cancelled.
    /// </summary>
    Task PlayAsync(byte[] mp3, CancellationToken cancellationToken);
}

/// <summary>There is no microphone, or the operating system denied access to it.</summary>
public sealed class MicrophoneUnavailableException : Exception
{
    public MicrophoneUnavailableException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Speech recognition stopped: the Transcribe stream failed, closed, or reported an error. The
/// message never contains the session's presigned address.
/// </summary>
public sealed class TranscriptionFailedException : Exception
{
    public TranscriptionFailedException(string message)
        : base(message)
    {
    }
}
