using System.Text;
using System.Text.Json;

namespace NevaBridge.Chat.Tests.Support;

/// <summary>
/// NevaBridge's voice operations, scripted per call: each answers with the next queued result, an
/// exception, or a task the test completes later. Without a script a session and speech succeed.
/// </summary>
internal sealed class FakeVoice : INevaBridgeVoice
{
    public const string PresignedUrl = "wss://transcribestreaming.example:8443/stream?X-Amz-Signature=secret-signature";

    private readonly Queue<object> _sessions = new();
    private readonly Queue<object> _speech = new();

    public List<VoiceLanguage> SessionRequests { get; } = [];

    public List<(string Text, VoiceLanguage Language)> SpeechRequests { get; } = [];

    public static TranscriptionSession Session(int maxDurationSeconds = 60, int sampleRate = 16000) =>
        new(new Uri(PresignedUrl), DateTimeOffset.UnixEpoch, "en-US", sampleRate, maxDurationSeconds);

    public static SpeechSynthesis Speech(string text) =>
        new(Convert.ToBase64String(Encoding.UTF8.GetBytes(text)), "audio/mpeg", "Danielle");

    public FakeVoice AnswerSession(object answer)
    {
        _sessions.Enqueue(answer);
        return this;
    }

    public FakeVoice AnswerSpeech(object answer)
    {
        _speech.Enqueue(answer);
        return this;
    }

    public Task<TranscriptionSession> CreateTranscriptionSessionAsync(VoiceLanguage language, CancellationToken cancellationToken = default)
    {
        SessionRequests.Add(language);
        return Next(_sessions, Session());
    }

    public Task<SpeechSynthesis> SynthesizeSpeechAsync(string text, VoiceLanguage language, CancellationToken cancellationToken = default)
    {
        SpeechRequests.Add((text, language));
        return Next(_speech, Speech(text));
    }

    private static Task<T> Next<T>(Queue<object> answers, T otherwise) =>
        answers.Count == 0
            ? Task.FromResult(otherwise)
            : answers.Dequeue() switch
            {
                Exception error => Task.FromException<T>(error),
                Task<T> pending => pending,
                T result => Task.FromResult(result),
                object other => throw new InvalidOperationException($"Scripted {other.GetType().Name}, not {typeof(T).Name}."),
            };
}

/// <summary>A microphone the test speaks into; it records whether it is open.</summary>
internal sealed class FakeMicrophone : IMicrophone
{
    private Action<ReadOnlyMemory<byte>>? _onAudio;

    public Exception? Refusal { get; set; }

    public bool IsOpen { get; private set; }

    public int Opened { get; private set; }

    public Task<IDisposable> OpenAsync(int sampleRate, Action<ReadOnlyMemory<byte>> onAudio, CancellationToken cancellationToken)
    {
        if (Refusal is not null)
        {
            return Task.FromException<IDisposable>(Refusal);
        }

        _onAudio = onAudio;
        IsOpen = true;
        Opened++;
        return Task.FromResult<IDisposable>(new Handle(this));
    }

    /// <summary>Delivers audio, as the sound card does, while the microphone is open.</summary>
    public void Speak(params byte[] pcm)
    {
        if (IsOpen)
        {
            _onAudio?.Invoke(pcm);
        }
    }

    private sealed class Handle(FakeMicrophone microphone) : IDisposable
    {
        public void Dispose()
        {
            microphone.IsOpen = false;
            microphone._onAudio = null;
        }
    }
}

/// <summary>
/// A Transcribe WebSocket the test plays the far side of: it records what was sent and delivers
/// the messages and the close the test chooses.
/// </summary>
internal sealed class FakeTranscribeSocket : ITranscribeSocket
{
    private readonly Queue<TaskCompletionSource<byte[]?>> _waiting = new();
    private readonly Queue<byte[]?> _incoming = new();

    public Exception? ConnectFailure { get; set; }

    public Uri? ConnectedTo { get; private set; }

    public List<EventStreamMessage> Sent { get; } = [];

    public bool Closed { get; private set; }

    public bool Disposed { get; private set; }

    public string? CloseReason { get; private set; }

    /// <summary>The audio sent, one entry per AudioEvent; an empty entry ended the stream.</summary>
    public IEnumerable<byte[]> Audio => Sent.Where(m => m.Headers[":event-type"] == "AudioEvent").Select(m => m.Payload);

    public bool Ended => Audio.Any(a => a.Length == 0);

    public Task ConnectAsync(Uri url, CancellationToken cancellationToken)
    {
        ConnectedTo = url;
        return ConnectFailure is null ? Task.CompletedTask : Task.FromException(ConnectFailure);
    }

    public Task SendAsync(ReadOnlyMemory<byte> message, CancellationToken cancellationToken)
    {
        Sent.Add(EventStreamCodec.Decode(message.Span));
        return Task.CompletedTask;
    }

    public Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
    {
        if (_incoming.Count > 0)
        {
            return Task.FromResult(_incoming.Dequeue());
        }

        TaskCompletionSource<byte[]?> next = new();
        _waiting.Enqueue(next);
        return next.Task;
    }

    /// <summary>Like Transcribe, answers the closing frame by ending the stream.</summary>
    public Task CloseAsync(CancellationToken cancellationToken)
    {
        Closed = true;
        Deliver(null);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Disposed = true;
        while (_waiting.TryDequeue(out TaskCompletionSource<byte[]?>? waiting))
        {
            waiting.TrySetException(new ObjectDisposedException(nameof(FakeTranscribeSocket)));
        }
    }

    /// <summary>Transcribe reports results; each is (id, text, partial).</summary>
    public void Hear(params (string Id, string Text, bool Partial)[] results) =>
        Deliver(Frame(
            ("event", "TranscriptEvent"),
            new
            {
                Transcript = new
                {
                    Results = results.Select(r => new
                    {
                        ResultId = r.Id,
                        IsPartial = r.Partial,
                        Alternatives = new[] { new { Transcript = r.Text } },
                    }),
                },
            }));

    public void Fail(string type, string message) =>
        Deliver(Frame(("exception", type), new { Message = message }));

    public void CloseFromTranscribe(string reason)
    {
        CloseReason = reason;
        Deliver(null);
    }

    private void Deliver(byte[]? message)
    {
        if (_waiting.TryDequeue(out TaskCompletionSource<byte[]?>? waiting))
        {
            waiting.SetResult(message);
        }
        else
        {
            _incoming.Enqueue(message);
        }
    }

    /// <summary>Encodes a server message with the event-stream codec's own framing.</summary>
    private static byte[] Frame((string MessageType, string Type) kind, object body)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(body);
        string typeHeader = kind.MessageType == "exception" ? ":exception-type" : ":event-type";
        return EventStreamTestFrames.Encode(
            [(":message-type", kind.MessageType), (typeHeader, kind.Type), (":content-type", "application/json")],
            payload);
    }
}

/// <summary>Builds event-stream frames with arbitrary string headers, checked by the codec's decoder.</summary>
internal static class EventStreamTestFrames
{
    public static byte[] Encode((string Name, string Value)[] headers, byte[] payload)
    {
        using MemoryStream headerBytes = new();
        foreach ((string name, string value) in headers)
        {
            byte[] n = Encoding.UTF8.GetBytes(name);
            byte[] v = Encoding.UTF8.GetBytes(value);
            headerBytes.WriteByte((byte)n.Length);
            headerBytes.Write(n);
            headerBytes.WriteByte(7);
            headerBytes.WriteByte((byte)(v.Length >> 8));
            headerBytes.WriteByte((byte)v.Length);
            headerBytes.Write(v);
        }

        byte[] h = headerBytes.ToArray();
        int total = 12 + h.Length + payload.Length + 4;
        byte[] frame = new byte[total];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame, total);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(frame.AsSpan(4), h.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), Crc(frame.AsSpan(0, 8)));
        h.CopyTo(frame, 12);
        payload.CopyTo(frame, 12 + h.Length);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(total - 4), Crc(frame.AsSpan(0, total - 4)));
        return frame;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }

        return ~crc;
    }
}

/// <summary>
/// Delays the test ends by hand, in the order they were started, so timing rules (the pause
/// before sending, session rotation, draining) run without waiting.
/// </summary>
internal sealed class ManualDelays
{
    private readonly List<(TimeSpan Duration, TaskCompletionSource Done)> _pending = [];

    public IReadOnlyList<TimeSpan> Pending => _pending.Where(p => !p.Done.Task.IsCompleted).Select(p => p.Duration).ToList();

    public Task Delay(TimeSpan duration, CancellationToken cancellationToken)
    {
        TaskCompletionSource done = new();
        cancellationToken.Register(() => done.TrySetCanceled(cancellationToken));
        _pending.Add((duration, done));
        return done.Task;
    }

    /// <summary>Ends the oldest running delay of this length.</summary>
    public void Elapse(TimeSpan duration)
    {
        (TimeSpan _, TaskCompletionSource done) = _pending.First(p => p.Duration == duration && !p.Done.Task.IsCompleted);
        done.TrySetResult();
    }

    public bool IsRunning(TimeSpan duration) => Pending.Contains(duration);
}

/// <summary>A speaker the test controls: each playback runs until the test finishes or fails it.</summary>
internal sealed class FakeSpeechPlayer : ISpeechPlayer
{
    private readonly List<(string Text, TaskCompletionSource Done)> _playing = [];

    /// <summary>Every sentence started, decoded from the fake speech audio.</summary>
    public List<string> Started { get; } = [];

    public List<string> Stopped { get; } = [];

    public Func<FakeMicrophone?>? MicrophoneToCheck { get; set; }

    /// <summary>Whether the microphone was open while any sentence played.</summary>
    public bool HeardTheMicrophoneOpen { get; private set; }

    public string? Playing => _playing.LastOrDefault(p => !p.Done.Task.IsCompleted).Text;

    public Task PlayAsync(byte[] mp3, CancellationToken cancellationToken)
    {
        string text = Encoding.UTF8.GetString(mp3);
        Started.Add(text);
        if (MicrophoneToCheck?.Invoke()?.IsOpen == true)
        {
            HeardTheMicrophoneOpen = true;
        }

        TaskCompletionSource done = new();
        cancellationToken.Register(() =>
        {
            if (done.TrySetCanceled(cancellationToken))
            {
                Stopped.Add(text);
            }
        });
        _playing.Add((text, done));
        return done.Task;
    }

    /// <summary>The sentence playing now plays to its end.</summary>
    public void Finish() => _playing.First(p => !p.Done.Task.IsCompleted).Done.SetResult();

    public void FailPlayback() => _playing.First(p => !p.Done.Task.IsCompleted).Done.SetException(new InvalidOperationException("The sound card failed."));
}
