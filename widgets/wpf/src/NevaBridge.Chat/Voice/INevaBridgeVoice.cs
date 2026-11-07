using System.Globalization;
using System.Text;

namespace NevaBridge.Chat;

/// <summary>
/// The NevaBridge voice operations the Generic Intake chat needs for dictation and the hands-free
/// conversation. It is separate from <see cref="INevaBridgeIntakeConversations"/>, so
/// implementations of that interface keep compiling; the chat offers voice only when the host
/// passes one.
/// </summary>
public interface INevaBridgeVoice
{
    /// <summary>
    /// Starts a paid speech recognition session and returns its presigned Amazon Transcribe
    /// address. Needs the <c>voiceDictation</c> or the <c>voiceConversation</c> feature (403
    /// otherwise); 402 when the credits do not cover one session.
    /// </summary>
    Task<TranscriptionSession> CreateTranscriptionSessionAsync(
        VoiceLanguage language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads up to 3,000 characters of plain text aloud and returns the MP3 audio. Needs the
    /// <c>voiceConversation</c> feature (403 otherwise); 402 when the credits do not cover the
    /// text, 424 when Amazon Polly failed.
    /// </summary>
    Task<SpeechSynthesis> SynthesizeSpeechAsync(
        string text,
        VoiceLanguage language,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A speech recognition session. <see cref="Url"/> is a credential for the session: it never
/// appears in <see cref="object.ToString"/>, so logging the session does not leak it.
/// </summary>
/// <param name="Url">The presigned <c>wss://</c> address of the Transcribe stream.</param>
/// <param name="ExpiresAt">Open the stream before this instant.</param>
/// <param name="LanguageCode">The language recognized, such as <c>en-US</c>.</param>
/// <param name="SampleRate">The PCM sample rate in Hz the stream expects.</param>
/// <param name="MaxDurationSeconds">The listening time this session was charged for.</param>
public sealed record TranscriptionSession(
    Uri Url,
    DateTimeOffset ExpiresAt,
    string LanguageCode,
    int SampleRate,
    int MaxDurationSeconds)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Url = (hidden), ExpiresAt = ").Append(ExpiresAt.ToString("O", CultureInfo.InvariantCulture))
            .Append(", LanguageCode = ").Append(LanguageCode)
            .Append(", SampleRate = ").Append(SampleRate)
            .Append(", MaxDurationSeconds = ").Append(MaxDurationSeconds);
        return true;
    }
}

/// <summary>Synthesized speech as NevaBridge returns it.</summary>
/// <param name="AudioBase64">The audio, base64-encoded.</param>
/// <param name="ContentType">The audio's media type, <c>audio/mpeg</c>.</param>
/// <param name="VoiceId">The Amazon Polly voice that read the text.</param>
public sealed record SpeechSynthesis(string AudioBase64, string ContentType, string VoiceId);

/// <summary>A language dictation and the hands-free conversation listen and speak in.</summary>
public sealed record VoiceLanguage
{
    public static readonly VoiceLanguage English = new("en-US", "English");

    public static readonly VoiceLanguage German = new("de-DE", "Deutsch");

    private VoiceLanguage(string code, string name)
    {
        Code = code;
        Name = name;
    }

    /// <summary>Every language NevaBridge recognizes and speaks.</summary>
    public static IReadOnlyList<VoiceLanguage> All { get; } = [English, German];

    /// <summary>The language code NevaBridge takes, such as <c>de-DE</c>.</summary>
    public string Code { get; }

    /// <summary>The language's name in that language, for a menu.</summary>
    public string Name { get; }

    /// <summary>German for a German culture, English for any other.</summary>
    public static VoiceLanguage For(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName == "de" ? German : English;

    public override string ToString() => Name;
}
