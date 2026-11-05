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

/// <summary>The first message of a Generic Intake conversation.</summary>
public sealed record StartIntakeConversationRequest(UserMessage UserMessage)
{
    /// <summary>The application's own context, stored with the conversation and shown in triage.</summary>
    public IReadOnlyDictionary<string, string>? ApplicationContext { get; init; }

    /// <summary>
    /// How the team can reach the reporter, when the application already knows it. The assistant
    /// then does not ask for it.
    /// </summary>
    public ReporterContact? ReporterContact { get; init; }
}

/// <summary>An email address, a phone number or both; at least one must be set.</summary>
public sealed record ReporterContact
{
    public string? Email { get; init; }

    public string? Phone { get; init; }
}

/// <summary>A started Generic Intake conversation and its first turn, which runs in the background.</summary>
public sealed record IntakeConversationStarted(string ConversationId, string TurnId);

/// <summary>A started Generic Intake turn, which runs in the background.</summary>
public sealed record IntakeTurnStarted(string TurnId);

/// <summary>One read of a turn's events. Send <see cref="Cursor"/> as <c>after</c> on the next read.</summary>
public sealed record IntakeTurnEvents(IReadOnlyList<IntakeTurnEvent> Events, string Cursor, bool Finished);

/// <summary>
/// One event of a turn: <c>status</c> (<see cref="Phase"/>, <see cref="Tool"/>), <c>text</c>
/// (<see cref="Delta"/>), and the terminal <c>done</c> (<see cref="Revision"/>) or <c>error</c>
/// (<see cref="Error"/>). Other types may appear later and are ignored.
/// </summary>
public sealed record IntakeTurnEvent(string Type)
{
    public string? Phase { get; init; }

    public string? Tool { get; init; }

    public string? Delta { get; init; }

    public int? Revision { get; init; }

    public IntakeTurnFailure? Error { get; init; }
}

/// <summary>Why a turn failed, with the HTTP status the failure would have had as a response.</summary>
public sealed record IntakeTurnFailure(string Error, string Message, int StatusCode)
{
    /// <summary>For ModelInvocationFailed: throttled, timed-out and so on.</summary>
    public string? Category { get; init; }
}

/// <summary>The next message of a Generic Intake conversation.</summary>
/// <param name="Content">The reporter's message.</param>
/// <param name="ExpectedRevision">The conversation's revision last read; a stale one is refused.</param>
public sealed record StartIntakeTurnRequest(string Content, int ExpectedRevision)
{
    /// <summary>Ends the conversation with this message.</summary>
    public bool? Finish { get; init; }
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

    /// <summary>Generic Intake only: send it as the next turn's expected revision.</summary>
    public int? Revision { get; init; }

    /// <summary>Generic Intake only, while a turn runs: the turn to follow.</summary>
    public string? ActiveTurnId { get; init; }

    /// <summary>A Generic Intake conversation is closed once it has ended.</summary>
    public bool IsClosed => Status == "closed";
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
