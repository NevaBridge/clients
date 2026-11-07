using NevaBridge.Chat.Tests.Support;

namespace NevaBridge.Chat.Tests;

public sealed class TranscribeListenerTests
{
    private static readonly TimeSpan FirstRotation = TimeSpan.FromSeconds(57);
    private static readonly TimeSpan Drain = TimeSpan.FromSeconds(5);

    private readonly FakeVoice _voice = new();
    private readonly FakeMicrophone _microphone = new();
    private readonly ManualDelays _delays = new();
    private readonly List<FakeTranscribeSocket> _sockets = [];
    private readonly List<TranscriptResult> _heard = [];
    private readonly List<Exception> _errors = [];
    private int _microphoneOpenings;

    private TranscribeListener Listener(VoiceLanguage? language = null) =>
        new(_voice, language ?? VoiceLanguage.English, _microphone, new TranscribeListenerOptions
        {
            CreateSocket = () =>
            {
                FakeTranscribeSocket socket = new();
                _sockets.Add(socket);
                return socket;
            },
            Delay = _delays.Delay,
        });

    private TranscribeCallbacks Callbacks => new(results => _heard.AddRange(results), error => _errors.Add(error))
    {
        OnMicrophoneOpen = () => _microphoneOpenings++,
    };

    [Fact]
    public async Task Listening_streams_the_microphone_as_audio_events_to_the_session_and_reports_what_Transcribe_heard()
    {
        // Given
        TranscribeListener listener = Listener(VoiceLanguage.German);

        // When
        await listener.StartAsync(Callbacks, CancellationToken.None);
        _microphone.Speak(1, 2);
        _microphone.Speak(3, 4);
        _sockets[0].Hear(("r1", "Der Export", true));

        // Then
        Assert.Equal([VoiceLanguage.German], _voice.SessionRequests);
        Assert.Equal(new Uri(FakeVoice.PresignedUrl), _sockets[0].ConnectedTo);
        Assert.Equal([[1, 2], [3, 4]], _sockets[0].Audio);
        Assert.Equal([new TranscriptResult("r1", "Der Export", IsPartial: true)], _heard);
        Assert.Equal(1, _microphoneOpenings);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task Shortly_before_the_paid_time_runs_out_the_audio_moves_to_a_new_session()
    {
        // Given
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);
        _microphone.Speak(1);

        // When
        _delays.Elapse(FirstRotation);
        _microphone.Speak(2);

        // Then
        Assert.Equal(2, _voice.SessionRequests.Count);
        Assert.Equal([[1], []], _sockets[0].Audio);
        Assert.Equal([[2]], _sockets[1].Audio);
        Assert.True(_delays.IsRunning(FirstRotation), "The next rotation is scheduled.");
        Assert.True(_delays.IsRunning(Drain), "The previous stream may still deliver its last results.");
        Assert.False(_sockets[0].Closed);

        // When both streams report, the previous one while it drains
        _sockets[1].Hear(("r2", "new", false));
        _sockets[0].Hear(("r1", "late but final", false));
        _delays.Elapse(Drain);

        // Then each result names its session, so their order can be kept
        Assert.Equal(
            [new TranscriptResult("r2", "new", IsPartial: false) { Session = 1 }, new TranscriptResult("r1", "late but final", IsPartial: false) { Session = 0 }],
            _heard);
        Assert.True(_sockets[0].Closed);
        Assert.Empty(_errors);
    }

    [Fact]
    public async Task A_rotated_out_stream_drains_until_it_ends()
    {
        // Given
        int drained = 0;
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks with { OnDrained = () => drained++ }, CancellationToken.None);
        Assert.False(listener.IsDraining);

        // When the session rotates
        _delays.Elapse(FirstRotation);

        // Then the previous stream drains until it has ended
        Assert.True(listener.IsDraining);
        _delays.Elapse(Drain);
        Assert.False(listener.IsDraining);
        Assert.Equal(1, drained);
    }

    [Fact]
    public async Task The_previous_stream_closing_after_a_rotation_is_not_a_failure()
    {
        // Given
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);
        _delays.Elapse(FirstRotation);

        // When
        _sockets[0].CloseFromTranscribe("Stream ended.");

        // Then
        Assert.Empty(_errors);
        Assert.True(_microphone.IsOpen);
    }

    [Fact]
    public async Task Stopping_closes_the_microphone_ends_the_stream_and_reports_nothing_more()
    {
        // Given
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);

        // When
        listener.Stop();
        _sockets[0].Hear(("r1", "after the stop", false));
        _delays.Elapse(Drain);

        // Then
        Assert.False(_microphone.IsOpen);
        Assert.True(_sockets[0].Ended);
        Assert.True(_sockets[0].Closed);
        Assert.Empty(_heard);
        Assert.Empty(_errors);
        Assert.DoesNotContain(FirstRotation, _delays.Pending);
        Assert.Single(_voice.SessionRequests);
    }

    [Fact]
    public async Task Cancelling_the_start_token_later_stops_listening()
    {
        // Given
        using CancellationTokenSource listening = new();
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, listening.Token);

        // When
        listening.Cancel();

        // Then
        Assert.False(_microphone.IsOpen);
        Assert.True(_sockets[0].Ended);
    }

    [Fact]
    public async Task Transcribe_closing_the_stream_fails_listening_and_closes_the_microphone()
    {
        // Given
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);

        // When
        _sockets[0].CloseFromTranscribe("Your request timed out because no new audio was received for 15 seconds.");

        // Then
        TranscriptionFailedException error = Assert.IsType<TranscriptionFailedException>(Assert.Single(_errors));
        Assert.Contains("no new audio", error.Message);
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public async Task An_exception_from_Transcribe_fails_listening_with_its_type_and_message()
    {
        // Given
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);

        // When
        _sockets[0].Fail("BadRequestException", "Invalid audio.");

        // Then
        Assert.Equal("BadRequestException: Invalid audio.", Assert.Single(_errors).Message);
        Assert.False(_microphone.IsOpen);
        Assert.True(_sockets[0].Disposed);
    }

    [Fact]
    public async Task A_refused_microphone_requests_no_paid_session()
    {
        // Given
        _microphone.Refusal = new MicrophoneUnavailableException("Access to the microphone is denied.");
        TranscribeListener listener = Listener();

        // When
        await Assert.ThrowsAsync<MicrophoneUnavailableException>(() => listener.StartAsync(Callbacks, CancellationToken.None));

        // Then
        Assert.Empty(_voice.SessionRequests);
    }

    [Theory]
    [InlineData(402)]
    [InlineData(403)]
    public async Task A_refused_session_fails_the_start_with_the_refusal_and_closes_the_microphone(int status)
    {
        // Given
        _voice.AnswerSession(new NevaBridgeApiException(status, "Refused", null, "Refused."));
        TranscribeListener listener = Listener();

        // When
        NevaBridgeApiException error = await Assert.ThrowsAsync<NevaBridgeApiException>(() => listener.StartAsync(Callbacks, CancellationToken.None));

        // Then
        Assert.Equal(status, error.StatusCode);
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public async Task A_failed_connection_never_names_the_presigned_address()
    {
        // Given
        TranscribeListener listener = new(_voice, VoiceLanguage.English, _microphone, new TranscribeListenerOptions
        {
            CreateSocket = () => new FakeTranscribeSocket
            {
                ConnectFailure = new System.Net.WebSockets.WebSocketException($"Unable to connect to {FakeVoice.PresignedUrl}"),
            },
            Delay = _delays.Delay,
        });

        // When
        TranscriptionFailedException error = await Assert.ThrowsAsync<TranscriptionFailedException>(
            () => listener.StartAsync(Callbacks, CancellationToken.None));

        // Then
        Assert.DoesNotContain("X-Amz-Signature", error.ToString());
        Assert.DoesNotContain("transcribestreaming", error.ToString());
        Assert.Null(error.InnerException);
        Assert.False(_microphone.IsOpen);
    }

    [Fact]
    public async Task A_session_for_another_sample_rate_is_refused()
    {
        // Given
        _voice.AnswerSession(FakeVoice.Session(sampleRate: 8000));
        TranscribeListener listener = Listener();

        // When, Then
        await Assert.ThrowsAsync<TranscriptionFailedException>(() => listener.StartAsync(Callbacks, CancellationToken.None));
        Assert.Empty(_sockets);
    }

    [Fact]
    public async Task A_rotation_that_fails_fails_listening()
    {
        // Given
        _voice.AnswerSession(FakeVoice.Session()).AnswerSession(new NevaBridgeApiException(402, "QuotaExceeded", null, "No credits left."));
        TranscribeListener listener = Listener();
        await listener.StartAsync(Callbacks, CancellationToken.None);

        // When
        _delays.Elapse(FirstRotation);

        // Then
        Assert.Equal(402, Assert.IsType<NevaBridgeApiException>(Assert.Single(_errors)).StatusCode);
        Assert.False(_microphone.IsOpen);
    }
}
