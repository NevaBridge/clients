using System.Text;
using System.Text.RegularExpressions;

namespace NevaBridge.Chat;

/// <summary>
/// Collects the live reply text and hands out each sentence once it is complete, so speech can
/// start before the whole reply has been written.
/// </summary>
internal sealed partial class SentenceSplitter
{
    /// <summary>Amazon Polly reads at most 3,000 characters per request.</summary>
    public const int MaximumSpeechLength = 3000;

    private readonly StringBuilder _buffer = new();

    /// <summary>Splits a whole reply into speakable sentences.</summary>
    public static IReadOnlyList<string> Split(string text)
    {
        SentenceSplitter splitter = new();
        return [.. splitter.Push(text), .. splitter.Flush()];
    }

    /// <summary>Adds text and returns the sentences it completed.</summary>
    public IReadOnlyList<string> Push(string delta)
    {
        _buffer.Append(delta);
        string buffered = _buffer.ToString();
        List<string> sentences = [];
        int start = 0;
        foreach (Match end in SentenceEnd().Matches(buffered))
        {
            int stop = end.Index + end.Length;
            sentences.AddRange(Chunk(buffered[start..stop]));
            start = stop;
        }

        _buffer.Remove(0, start);
        return sentences;
    }

    /// <summary>Returns whatever is left once the reply is complete.</summary>
    public IReadOnlyList<string> Flush()
    {
        IReadOnlyList<string> rest = Chunk(_buffer.ToString());
        _buffer.Clear();
        return rest;
    }

    private static IReadOnlyList<string> Chunk(string text)
    {
        string trimmed = StripMarkdown(text).Trim();
        List<string> chunks = [];
        for (int index = 0; index < trimmed.Length; index += MaximumSpeechLength)
        {
            chunks.Add(trimmed.Substring(index, Math.Min(MaximumSpeechLength, trimmed.Length - index)));
        }

        return chunks;
    }

    /// <summary>Replies are Markdown; reading the markup aloud would be noise.</summary>
    private static string StripMarkdown(string text)
    {
        string withoutLinks = MarkdownLink().Replace(text, "$1");
        string withoutMarks = MarkdownMarks().Replace(withoutLinks, string.Empty);
        return ListBullet().Replace(withoutMarks, string.Empty);
    }

    // A sentence ends at ., ! or ? (optionally followed by closing quotes, brackets or Markdown
    // emphasis) and then whitespace, or at a line break.
    [GeneratedRegex("""[.!?…]+["'”’)\]*_]*\s+|\n+""")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\[([^\]]*)\]\([^)]*\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"[*_`#>]+")]
    private static partial Regex MarkdownMarks();

    [GeneratedRegex(@"^\s*[-+]\s+", RegexOptions.Multiline)]
    private static partial Regex ListBullet();
}
