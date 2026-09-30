namespace NevaBridge.Chat;

// Wire shapes of the Tenant Integration API, limited to the fields this sample uses or shows.
// Enumerations stay strings so a value NevaBridge adds later reads as that value instead of
// failing the whole response.

public sealed record UserMessage(string Content)
{
    public string? WriterModelKey { get; init; }

    /// <summary>A BCP 47 tag such as "de" that fixes the reply language for this turn.</summary>
    public string? ResponseLanguage { get; init; }
}

public sealed record ReporterRole(string Name, string Description);

public sealed record StartConversationRequest(UserMessage UserMessage)
{
    public string? TemplateId { get; init; }

    /// <summary>bug_report, feature_request or support_request. Omitted means support_request.</summary>
    public string? Category { get; init; }

    /// <summary>The application's own context, stored with the report and shown in triage.</summary>
    public IReadOnlyDictionary<string, string>? ApplicationContext { get; init; }

    public ReporterRole? ReporterRole { get; init; }
}

public sealed record AppendMessageRequest(string Content)
{
    public string? WriterModelKey { get; init; }

    public string? ResponseLanguage { get; init; }
}

public sealed record Message(
    string Id,
    string ConversationId,
    string Role,
    string Content,
    DateTimeOffset CreatedAt);

public sealed record ReportDelivery(
    string ConnectorType,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string? IssueUrl { get; init; }

    public string? ExternalId { get; init; }

    public string? Error { get; init; }
}

public sealed record Report(
    string Id,
    string ConversationId,
    string TemplateId,
    string TemplateVersion,
    string Status,
    IReadOnlyDictionary<string, string> Structured,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public string? Category { get; init; }

    public string? DisplayTitle { get; init; }

    public string? StatusReason { get; init; }

    public string? SubmissionOrigin { get; init; }

    public IReadOnlyList<ReportDelivery>? Deliveries { get; init; }

    /// <summary>True while the reporter is still working on the report.</summary>
    public bool IsInProgress => Status == "reporting_in_progress";
}

public sealed record ConnectorDeliveryResult(string Status)
{
    public string? Ticket { get; init; }

    public string? Error { get; init; }
}

public sealed record ConversationTurn(
    Message UserMessage,
    Message AssistantMessage,
    IReadOnlyList<Report> Reports)
{
    public string? DisplayTitle { get; init; }

    /// <summary>Present when the assistant judged the report complete and delivered it in this turn.</summary>
    public IReadOnlyDictionary<string, ConnectorDeliveryResult>? Connectors { get; init; }
}

public sealed record ConversationDetail(
    string Id,
    string ProductId,
    string ReporterId,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<Message> Messages,
    IReadOnlyList<Report> Reports)
{
    public string? DisplayTitle { get; init; }

    public string? ReporterDisplayName { get; init; }
}

public sealed record SubmitReportResult(Report Report)
{
    public IReadOnlyDictionary<string, ConnectorDeliveryResult>? Connectors { get; init; }
}

/// <summary>One confirmed file attached to a conversation.</summary>
public sealed record Attachment(
    string Id,
    string FileName,
    string Kind,
    long SizeBytes,
    string UploadedBy,
    DateTimeOffset UploadedAt);
