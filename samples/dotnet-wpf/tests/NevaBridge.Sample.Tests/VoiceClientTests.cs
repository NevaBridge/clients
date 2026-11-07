using System.Text.Json.Nodes;
using NevaBridge.Chat;
using NevaBridge.Chat.Settings;
using NevaBridge.Sample.Client;
using NevaBridge.Sample.Tests.Support;

namespace NevaBridge.Sample.Tests;

public sealed class VoiceClientTests : IAsyncLifetime
{
    private const string ApiKey = "sk_test_not_a_real_key";
    private const string ProductId = "product-00000000-0000-4000-8000-000000000001";
    private const string PresignedUrl = "wss://transcribestreaming.eu-central-1.amazonaws.com:8443/stream-transcription-websocket?X-Amz-Signature=secret";

    private FakeNevaBridgeApi _api = null!;
    private HttpClient _http = null!;
    private INevaBridgeVoice _client = null!;

    public async Task InitializeAsync()
    {
        _api = await FakeNevaBridgeApi.StartAsync();
        _http = new HttpClient();
        _client = new NevaBridgeClient(_http, new NevaBridgeSettings(_api.BaseUrl, ProductId, ApiKey, null));
    }

    public async Task DisposeAsync()
    {
        _http.Dispose();
        await _api.DisposeAsync();
    }

    [Fact]
    public async Task CreateTranscriptionSession_sends_the_language_and_reads_the_session()
    {
        // Given
        _api.Respond(200, $$"""
            {"url": "{{PresignedUrl}}", "expiresAt": "2026-11-07T10:05:00Z", "languageCode": "de-DE",
             "sampleRate": 16000, "maxDurationSeconds": 60}
            """);

        // When
        TranscriptionSession session = await _client.CreateTranscriptionSessionAsync(VoiceLanguage.German);

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/v1/voice/transcription-sessions", sent.PathAndQuery);
        Assert.Equal($"Bearer {ApiKey}", sent.Headers["Authorization"]);
        AssertSameJson("""{"languageCode":"de-DE"}""", sent.Body);
        Assert.Equal(new Uri(PresignedUrl), session.Url);
        Assert.Equal(new DateTimeOffset(2026, 11, 7, 10, 5, 0, TimeSpan.Zero), session.ExpiresAt);
        Assert.Equal(("de-DE", 16000, 60), (session.LanguageCode, session.SampleRate, session.MaxDurationSeconds));
    }

    [Fact]
    public async Task SynthesizeSpeech_sends_the_text_and_the_language_and_reads_the_audio()
    {
        // Given
        _api.Respond(200, """{"audioBase64": "SUQz", "contentType": "audio/mpeg", "voiceId": "Danielle"}""");

        // When
        SpeechSynthesis speech = await _client.SynthesizeSpeechAsync("When does it crash?", VoiceLanguage.English);

        // Then
        RecordedRequest sent = Assert.Single(_api.Requests);
        Assert.Equal("POST", sent.Method);
        Assert.Equal("/v1/voice/speech", sent.PathAndQuery);
        AssertSameJson("""{"text":"When does it crash?","languageCode":"en-US"}""", sent.Body);
        Assert.Equal(new SpeechSynthesis("SUQz", "audio/mpeg", "Danielle"), speech);
    }

    [Theory]
    [InlineData(402, "QuotaExceeded", "Not enough credits for a transcription session.")]
    [InlineData(403, "Forbidden", "Feature \\\"voiceDictation\\\" is not available for this tenant.")]
    public async Task CreateTranscriptionSession_refused_fails_with_the_status_and_the_message(int status, string error, string message)
    {
        // Given
        _api.Respond(status, $$"""{"error": "{{error}}", "message": "{{message}}"}""");

        // When
        NevaBridgeApiException failure = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.CreateTranscriptionSessionAsync(VoiceLanguage.English));

        // Then
        Assert.Equal(status, failure.StatusCode);
        Assert.Equal(error, failure.ErrorName);
    }

    [Fact]
    public async Task SynthesizeSpeech_failed_by_Polly_fails_with_status_424()
    {
        // Given
        _api.Respond(424, """{"error": "FailedDependency", "message": "Amazon Polly could not synthesize the text."}""");

        // When
        NevaBridgeApiException failure = await Assert.ThrowsAsync<NevaBridgeApiException>(
            () => _client.SynthesizeSpeechAsync("Hello.", VoiceLanguage.English));

        // Then
        Assert.Equal(424, failure.StatusCode);
    }

    [Fact]
    public void A_session_written_out_hides_its_presigned_address()
    {
        // Given
        TranscriptionSession session = new(new Uri(PresignedUrl), DateTimeOffset.UnixEpoch, "en-US", 16000, 60);

        // When
        string written = session.ToString();

        // Then
        Assert.DoesNotContain("X-Amz-Signature", written);
        Assert.DoesNotContain("transcribestreaming", written);
        Assert.Contains("MaxDurationSeconds = 60", written);
    }

    /// <summary>Equal JSON, whatever the serializer's escaping and spacing.</summary>
    private static void AssertSameJson(string expected, string actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), JsonNode.Parse(actual)), $"Expected {expected}, sent {actual}");
}
