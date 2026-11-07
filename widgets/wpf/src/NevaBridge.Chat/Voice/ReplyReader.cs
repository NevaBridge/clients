namespace NevaBridge.Chat;

/// <summary>
/// Reads one reply aloud sentence by sentence, synthesizing the next sentence while the current
/// one plays, so speech flows without waiting for the whole reply.
/// </summary>
/// <remarks>
/// Runs on the thread that drives it, like the chat; nothing here locks. It keeps these
/// invariants:
/// <list type="number">
/// <item>At most one synthesis runs ahead of the sentence being read, and it starts only once the
/// current sentence's audio has arrived, so a failed or refused synthesis is never followed by
/// another paid one.</item>
/// <item>Every synthesis is observed when it completes, not when its sentence's turn comes: a
/// failure, a refusal in particular, ends reading at once and reaches <c>onError</c> even while
/// another sentence plays.</item>
/// <item>The reader finishes exactly once: <c>onIdle</c> after the last sentence played or on
/// <see cref="Stop"/>, or <c>onError</c> on the first failure. Nothing is reported afterwards,
/// and the cancellation a stop or a failure causes stays quiet, as does everything once the
/// window closed.</item>
/// </list>
/// </remarks>
internal sealed class ReplyReader
{
    private readonly INevaBridgeVoice _voice;
    private readonly VoiceLanguage _language;
    private readonly ISpeechPlayer _player;
    private readonly Action<string> _onSentence;
    private readonly Action _onIdle;
    private readonly Action<Exception> _onError;
    private readonly CancellationTokenSource _stopping;

    /// <summary>Sentences not yet playing; the head is the one whose audio comes next.</summary>
    private readonly Queue<string> _pending = new();

    /// <summary>The synthesis of the head of <see cref="_pending"/>, started while another sentence plays.</summary>
    private Task<byte[]>? _next;

    /// <summary>The read loop runs: it waits for a sentence's audio or plays it.</summary>
    private bool _reading;

    /// <summary>A sentence's audio arrived and plays; the next sentence may be synthesized.</summary>
    private bool _playing;

    private bool _ended;
    private bool _finished;

    /// <param name="onSentence">A sentence starts playing.</param>
    /// <param name="onIdle">Every sentence was played, or reading was stopped.</param>
    /// <param name="onError">A sentence could not be synthesized or played; nothing more is read.</param>
    public ReplyReader(
        INevaBridgeVoice voice,
        VoiceLanguage language,
        ISpeechPlayer player,
        Action<string> onSentence,
        Action onIdle,
        Action<Exception> onError,
        CancellationToken closing)
    {
        _voice = voice;
        _language = language;
        _player = player;
        _onSentence = onSentence;
        _onIdle = onIdle;
        _onError = onError;
        _stopping = CancellationTokenSource.CreateLinkedTokenSource(closing);
    }

    public void Enqueue(string sentence)
    {
        if (_finished || _ended)
        {
            return;
        }

        _pending.Enqueue(sentence);
        if (_reading)
        {
            SynthesizeNext();
        }
        else
        {
            _ = ReadAsync();
        }
    }

    /// <summary>No more sentences will come; idle follows once the last one has played.</summary>
    public void End()
    {
        _ended = true;
        if (!_reading && _pending.Count == 0)
        {
            Finish(error: null);
        }
    }

    /// <summary>Stops the sentence playing and drops the queued ones and any synthesis in flight.</summary>
    public void Stop() => Finish(error: null);

    private async Task ReadAsync()
    {
        _reading = true;
        while (!_finished && _pending.TryPeek(out string? sentence))
        {
            Task<byte[]> audio = _next ?? Synthesize(sentence);
            _next = null;
            byte[] mp3;
            try
            {
                mp3 = await audio;
            }
            catch (Exception)
            {
                // The synthesis's own observer has finished the reader, or a stop cancelled it.
                return;
            }

            if (_finished)
            {
                return;
            }

            _pending.Dequeue();
            _playing = true;
            SynthesizeNext();
            _onSentence(sentence);
            try
            {
                await _player.PlayAsync(mp3, _stopping.Token);
            }
            catch (Exception error)
            {
                Finish(error);
                return;
            }

            _playing = false;
        }

        _reading = false;
        if (_ended)
        {
            Finish(error: null);
        }
    }

    /// <summary>While a sentence plays, starts synthesizing the next one if it has arrived.</summary>
    private void SynthesizeNext()
    {
        if (_playing && _next is null && _pending.TryPeek(out string? next))
        {
            _next = Synthesize(next);
        }
    }

    /// <summary>Requests one sentence's audio; a failure finishes the reader as soon as it arrives.</summary>
    private Task<byte[]> Synthesize(string sentence)
    {
        Task<byte[]> audio = SynthesizeAsync(sentence);
        _ = ObserveAsync(audio);
        return audio;
    }

    private async Task<byte[]> SynthesizeAsync(string sentence)
    {
        SpeechSynthesis speech = await _voice.SynthesizeSpeechAsync(sentence, _language, _stopping.Token);
        return Convert.FromBase64String(speech.AudioBase64);
    }

    private async Task ObserveAsync(Task<byte[]> audio)
    {
        try
        {
            await audio;
        }
        catch (Exception error)
        {
            Finish(error);
        }
    }

    /// <summary>Finishes once: idle on the end or a stop, onError on the first failure.</summary>
    private void Finish(Exception? error)
    {
        if (_finished)
        {
            return;
        }

        // Read before cancelling: only the window closing cancels while the reader still runs.
        bool closing = _stopping.IsCancellationRequested;

        // Finished before cancelling: the cancelled playback and syntheses resume at once.
        _finished = true;
        _pending.Clear();
        _stopping.Cancel();
        if (error is null)
        {
            _onIdle();
        }
        else if (!closing)
        {
            _onError(error);
        }
    }
}
