namespace NevaBridge.Chat;

/// <summary>
/// The report types a conversation can start as. The type selects the report template, so it is
/// fixed once the conversation has started.
/// </summary>
public static class ReportCategories
{
    public const string BugReport = "bug_report";
    public const string FeatureRequest = "feature_request";
    public const string SupportRequest = "support_request";

    public static IReadOnlyList<string> All { get; } = [BugReport, FeatureRequest, SupportRequest];
}
