namespace NevaBridge.Chat;

/// <summary>
/// What has been heard so far. A session reports its finished results in order and revises one
/// partial result after them. While a rotated-out session drains, its results may arrive after
/// the next session's, so the text is ordered by session first, then by arrival, finished
/// results ahead of the partial one.
/// </summary>
internal sealed record Transcript(IReadOnlyList<TranscriptResult> Results)
{
    public static readonly Transcript Empty = new([]);

    /// <summary>Everything heard so far, as one line of text.</summary>
    public string Text => Join(Ordered);

    /// <summary>Only the finished part, which Transcribe will no longer change.</summary>
    public string FinalText => Join(Ordered.Where(result => !result.IsPartial));

    /// <summary>The part Transcribe is still revising, shown apart until it settles.</summary>
    public string InterimText => Join(Ordered.Where(result => result.IsPartial));

    /// <summary>Some words are still being revised, in any session.</summary>
    public bool IsUnsettled => Results.Any(result => result.IsPartial);

    // OrderBy is stable, so arrival order holds within a session.
    private IEnumerable<TranscriptResult> Ordered => Results.OrderBy(result => result.Session).ThenBy(result => result.IsPartial);

    /// <summary>Folds new results in; a result replaces the partial one with its id.</summary>
    public Transcript Apply(IEnumerable<TranscriptResult> results)
    {
        List<TranscriptResult> next = [.. Results];
        foreach (TranscriptResult result in results)
        {
            next.RemoveAll(held => held.IsPartial && held.Session == result.Session && held.ResultId == result.ResultId);
            next.Add(result);
        }

        return new Transcript(next);
    }

    private static string Join(IEnumerable<TranscriptResult> results) =>
        string.Join(" ", results.Select(result => result.Text).Where(text => text.Length > 0));
}
