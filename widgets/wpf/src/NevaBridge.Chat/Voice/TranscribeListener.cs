using System.Threading.Channels;

namespace NevaBridge.Chat;

/// <summary>What a <see cref="TranscribeListener"/> reports while it listens.</summary>
/// <param name="OnResults">Recognized text as it arrives, partial and final.</param>
/// <param name="OnError">Called once, when listening stops because of a failure.</param>
public sealed record TranscribeCallbacks(Action<IReadOnlyList<TranscriptResult>> OnResults, Action<Exception> OnError)
{
    /// <summary>Called once the microphone is open, before any session is requested.</summary>
    public Action? OnMicrophoneOpen { get; init; }

    /// <summary>Every rotated-out stream has ended, so no earlier words can still arrive.</summary>
    public Action? OnDrained { get; init; }
}

/// <summary>Replaceable parts of a <see cref="TranscribeListener"/>.</summary>
public sealed record TranscribeListenerOptions
{
    /// <summary>Creates the connection for each session.</summary>
    public Func<ITranscribeSocket> CreateSocket { get; init; } = () => new WebSocketTranscribeSocket();

    /// <summary>Waits before rotating and while a stream drains. Replaceable so tests need not wait.</summary>
    public Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = Task.Delay;
}

/// <summary>
/// Streams the microphone to Amazon Transcribe through the presigned sessions NevaBridge hands out
/// and reports the recognized text. Each session is paid for a fixed time; shortly before it runs
/// out a new session takes over, so listening continues without a gap.
/// </summary>
/// <remarks>
/// Start it on the thread that should receive the callbacks, such as the UI thread: everything
/// but the microphone's own audio callback continues there.
/// </remarks>
public sealed class TranscribeListener
{
    /// <summary>The rate every transcription session is presigned for.</summary>
    public const int SampleRate = 16000;

    /// <summary>A new session is opened this long before the current one's paid time ends.</summary>
    internal static readonly TimeSpan RotationLead = TimeSpan.FromSeconds(3);

    /// <summary>How long a finished stream may take to deliver its last results.</summary>
    internal static readonly TimeSpan DrainTime = TimeSpan.FromSeconds(5);

    private readonly INevaBridgeVoice _voice;
    private readonly VoiceLanguage _language;
    private readonly IMicrophone _microphone;
    private readonly TranscribeListenerOptions _options;
    private readonly CancellationTokenSource _stopping = new();

    // Synchronous continuations keep the audio pump on the starting thread's context.
    private readonly Channel<byte[]> _audio = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = true });

    private TranscribeCallbacks? _callbacks;
    private CancellationTokenRegistration _startRegistration;
    private IDisposable? _capture;
    private ITranscribeSocket? _socket;

    /// <summary>Rotated-out streams whose results may still arrive, until each has ended.</summary>
    private readonly HashSet<ITranscribeSocket> _draining = [];
    private int _sessions;
    private bool _started;
    private bool _stopped;

    public TranscribeListener(
        INevaBridgeVoice voice,
        VoiceLanguage language,
        IMicrophone microphone,
        TranscribeListenerOptions? options = null)
    {
        _voice = voice;
        _language = language;
        _microphone = microphone;
        _options = options ?? new TranscribeListenerOptions();
    }

    /// <summary>A rotated-out stream may still deliver results for audio sent before the rotation.</summary>
    public bool IsDraining => _draining.Count > 0;

    /// <summary>
    /// Opens the microphone, then the first session, and completes once audio is streaming.
    /// Cancelling <paramref name="cancellationToken"/> stops listening at any point, also later.
    /// </summary>
    /// <exception cref="MicrophoneUnavailableException">The microphone could not be opened; no session was requested.</exception>
    /// <exception cref="NevaBridgeApiException">NevaBridge refused the session, for example with 402 or 403.</exception>
    /// <exception cref="TranscriptionFailedException">The stream could not be opened.</exception>
    public async Task StartAsync(TranscribeCallbacks callbacks, CancellationToken cancellationToken)
    {
        if (_started)
        {
            throw new InvalidOperationException("A listener listens once; create a new one to listen again.");
        }

        _started = true;
        _callbacks = callbacks;
        _startRegistration = cancellationToken.Register(Stop);

        // The microphone first: a refusal needs no paid session.
        IDisposable capture = await _microphone.OpenAsync(SampleRate, OnAudio, _stopping.Token);
        if (_stopped)
        {
            capture.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }

        _capture = capture;
        callbacks.OnMicrophoneOpen?.Invoke();

        ITranscribeSocket first;
        int maxDurationSeconds;
        try
        {
            (first, maxDurationSeconds) = await OpenSessionAsync();
        }
        catch (Exception) when (!_stopped)
        {
            Shut();
            throw;
        }
        catch (Exception)
        {
            throw new OperationCanceledException(cancellationToken);
        }

        if (_stopped)
        {
            first.Dispose();
            throw new OperationCanceledException(cancellationToken);
        }

        Use(first);
        _ = PumpAsync();
        _ = RotateAsync(maxDurationSeconds);
    }

    /// <summary>
    /// Stops listening: the microphone closes at once, the stream is told the audio has ended, and
    /// nothing more is reported.
    /// </summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        Shut();
        if (_socket is { } socket)
        {
            _ = FinishAsync(socket);
        }
    }

    private void OnAudio(ReadOnlyMemory<byte> pcm) => _audio.Writer.TryWrite(pcm.ToArray());

    private async Task<(ITranscribeSocket Socket, int MaxDurationSeconds)> OpenSessionAsync()
    {
        TranscriptionSession session = await _voice.CreateTranscriptionSessionAsync(_language, _stopping.Token);
        if (session.SampleRate != SampleRate)
        {
            throw new TranscriptionFailedException($"The transcription session expects {session.SampleRate} Hz audio.");
        }

        ITranscribeSocket socket = _options.CreateSocket();
        try
        {
            await socket.ConnectAsync(session.Url, _stopping.Token);
        }
        catch (Exception error)
        {
            socket.Dispose();
            if (error is OperationCanceledException && _stopping.IsCancellationRequested)
            {
                throw;
            }

            // Not wrapped: the connection's own message may name the presigned address.
            throw new TranscriptionFailedException("Could not connect to Amazon Transcribe.");
        }

        return (socket, session.MaxDurationSeconds);
    }

    /// <summary>
    /// Makes <paramref name="socket"/> the stream audio goes to and starts reading it. Its results
    /// carry the session's ordinal, because a replaced stream may still report while it drains.
    /// </summary>
    private void Use(ITranscribeSocket socket)
    {
        _socket = socket;
        _ = ReceiveAsync(socket, _sessions++);
    }

    private async Task ReceiveAsync(ITranscribeSocket socket, int session)
    {
        try
        {
            await ReceiveUntilEndAsync(socket, session);
        }
        finally
        {
            if (_draining.Remove(socket) && _draining.Count == 0 && !_stopped)
            {
                _callbacks!.OnDrained?.Invoke();
            }
        }
    }

    private async Task ReceiveUntilEndAsync(ITranscribeSocket socket, int session)
    {
        try
        {
            while (true)
            {
                byte[]? message = await socket.ReceiveAsync(CancellationToken.None);
                if (message is null)
                {
                    if (socket == _socket)
                    {
                        Fail(new TranscriptionFailedException(socket.CloseReason ?? "Amazon Transcribe closed the stream."));
                    }

                    return;
                }

                // After a stop the caller has moved on; a replaced stream's last results still count.
                if (_stopped)
                {
                    continue;
                }

                switch (EventStreamCodec.DecodeTranscribeMessage(message))
                {
                    case TranscribeFailure failure:
                        Fail(new TranscriptionFailedException($"{failure.Type}: {failure.Message}"));
                        return;
                    case TranscribeResults { Results.Count: > 0 } results:
                        _callbacks!.OnResults(results.Results.Select(result => result with { Session = session }).ToList());
                        break;
                }
            }
        }
        catch (InvalidDataException)
        {
            Fail(new TranscriptionFailedException("Amazon Transcribe sent a message that could not be read."));
        }
        catch (Exception) when (socket == _socket)
        {
            Fail(new TranscriptionFailedException("The connection to Amazon Transcribe failed."));
        }
        catch (Exception)
        {
            // A replaced or finished stream ending is expected.
        }
    }

    private async Task PumpAsync()
    {
        while (await _audio.Reader.WaitToReadAsync())
        {
            while (_audio.Reader.TryRead(out byte[]? pcm))
            {
                if (_stopped || _socket is not { } socket)
                {
                    continue;
                }

                try
                {
                    // Not cancellable: cancelling a WebSocket send aborts the whole connection.
                    await socket.SendAsync(EventStreamCodec.EncodeAudioEvent(pcm), CancellationToken.None);
                }
                catch (Exception) when (socket == _socket)
                {
                    Fail(new TranscriptionFailedException("The connection to Amazon Transcribe failed."));
                    return;
                }
                catch (Exception)
                {
                    // Sent to a stream that was replaced meanwhile.
                }
            }
        }
    }

    private async Task RotateAsync(int maxDurationSeconds)
    {
        while (!_stopped)
        {
            TimeSpan wait = TimeSpan.FromSeconds(maxDurationSeconds) - RotationLead;
            try
            {
                await _options.Delay(wait > TimeSpan.Zero ? wait : TimeSpan.Zero, _stopping.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            ITranscribeSocket next;
            try
            {
                (next, maxDurationSeconds) = await OpenSessionAsync();
            }
            catch (Exception) when (_stopped)
            {
                return;
            }
            catch (Exception error)
            {
                Fail(error);
                return;
            }

            if (_stopped)
            {
                next.Dispose();
                return;
            }

            ITranscribeSocket? previous = _socket;
            Use(next);
            if (previous is not null)
            {
                _draining.Add(previous);
                _ = FinishAsync(previous);
            }
        }
    }

    /// <summary>
    /// Tells Transcribe the audio has ended with an empty audio event, then closes the stream once
    /// it has had time to deliver its last results.
    /// </summary>
    private async Task FinishAsync(ITranscribeSocket socket)
    {
        try
        {
            await socket.SendAsync(EventStreamCodec.EncodeAudioEvent([]), CancellationToken.None);
            await _options.Delay(DrainTime, CancellationToken.None);
            await socket.CloseAsync(CancellationToken.None);
        }
        catch (Exception)
        {
            // The stream is going away either way.
        }
        finally
        {
            if (socket != _socket || _stopped)
            {
                socket.Dispose();
            }
        }
    }

    private void Fail(Exception error)
    {
        if (_stopped)
        {
            return;
        }

        _stopped = true;
        Shut();
        _socket?.Dispose();
        _callbacks!.OnError(error);
    }

    /// <summary>Closes the microphone and ends the pump and the rotation.</summary>
    private void Shut()
    {
        _stopping.Cancel();
        _audio.Writer.TryComplete();
        _capture?.Dispose();
        _capture = null;
        _startRegistration.Dispose();
    }
}
