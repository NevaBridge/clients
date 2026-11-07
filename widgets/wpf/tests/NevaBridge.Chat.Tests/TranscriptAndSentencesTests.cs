namespace NevaBridge.Chat.Tests;

public sealed class TranscriptAndSentencesTests
{
    [Fact]
    public void The_revised_partial_shows_after_what_is_final_and_is_replaced_when_it_finishes()
    {
        // Given
        Transcript transcript = Transcript.Empty
            .Apply([new TranscriptResult("a", "The export", IsPartial: true)])
            .Apply([new TranscriptResult("a", "The export fails", IsPartial: true)]);
        Assert.Equal("The export fails", transcript.Text);
        Assert.Equal(string.Empty, transcript.FinalText);

        // When
        transcript = transcript.Apply(
        [
            new TranscriptResult("a", "The export fails.", IsPartial: false),
            new TranscriptResult("b", "Since", IsPartial: true),
        ]);

        // Then
        Assert.Equal("The export fails. Since", transcript.Text);
        Assert.Equal("The export fails.", transcript.FinalText);
        Assert.Equal("Since", transcript.InterimText);
    }

    [Fact]
    public void A_newer_partial_stays_when_an_older_result_finishes()
    {
        // When
        Transcript transcript = Transcript.Empty.Apply(
        [
            new TranscriptResult("b", "since Monday", IsPartial: true),
            new TranscriptResult("a", "It broke", IsPartial: false),
        ]);

        // Then
        Assert.Equal("It broke since Monday", transcript.Text);
    }

    [Fact]
    public void Results_of_overlapping_sessions_keep_the_order_of_the_sessions()
    {
        // Given the previous session still revises its phrase when the next one starts
        Transcript transcript = Transcript.Empty
            .Apply([new TranscriptResult("a", "The export", IsPartial: true) { Session = 0 }])
            .Apply([new TranscriptResult("b", "since", IsPartial: true) { Session = 1 }]);
        Assert.Equal("The export since", transcript.Text);

        // When the next session finishes first
        transcript = transcript
            .Apply([new TranscriptResult("b", "since Monday.", IsPartial: false) { Session = 1 }])
            .Apply([new TranscriptResult("a", "The export fails", IsPartial: false) { Session = 0 }]);

        // Then
        Assert.Equal("The export fails since Monday.", transcript.Text);
        Assert.Equal("The export fails since Monday.", transcript.FinalText);
        Assert.False(transcript.IsUnsettled);
    }

    [Fact]
    public void Each_sentence_is_handed_out_once_the_live_reply_completes_it()
    {
        // Given
        SentenceSplitter splitter = new();

        // When, Then
        Assert.Empty(splitter.Push("Danke für die Info"));
        Assert.Equal(["Danke für die Info."], splitter.Push(". Seit wann tritt"));
        Assert.Equal(["Seit wann tritt das auf?"], splitter.Push(" das auf? Und"));
        Assert.Equal(["Und"], splitter.Flush());
        Assert.Empty(splitter.Flush());
    }

    [Fact]
    public void Markdown_markup_is_not_read_aloud()
    {
        // When
        IReadOnlyList<string> sentences = SentenceSplitter.Split("**Thanks!** See [the guide](https://x.test).\n- Step one");

        // Then
        Assert.Equal(["Thanks!", "See the guide.", "Step one"], sentences);
    }

    [Fact]
    public void A_sentence_longer_than_one_speech_request_is_split()
    {
        // When
        IReadOnlyList<string> chunks = SentenceSplitter.Split(new string('a', SentenceSplitter.MaximumSpeechLength + 10));

        // Then
        Assert.Equal([SentenceSplitter.MaximumSpeechLength, 10], chunks.Select(chunk => chunk.Length));
    }
}
