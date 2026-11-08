namespace NevaBridge.Chat;

/// <summary>A request NevaBridge, or the gateway in front of it, answered with an error status.</summary>
public sealed class NevaBridgeApiException : Exception
{
    public NevaBridgeApiException(int statusCode, string? errorName, string? category, string message)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorName = errorName;
        Category = category;
    }

    public int StatusCode { get; }

    /// <summary>NevaBridge's error name, such as QuotaExceeded. Null when the gateway answered.</summary>
    public string? ErrorName { get; }

    /// <summary>For ModelInvocationFailed: throttled, timed-out, transient-unavailable and so on.</summary>
    public string? Category { get; }

    /// <summary>For a 409 IntakeTurnRunning: the Generic Intake turn that is still running.</summary>
    public string? TurnId { get; init; }

    /// <summary>
    /// For a 424 to a start with attachments: the conversation NevaBridge stored although it could not
    /// sign the uploads. Request each upload again for it.
    /// </summary>
    public string? ConversationId { get; init; }

    /// <summary>The gateway gave up before NevaBridge handled the request, typically after 29 seconds.</summary>
    public bool IsEdgeFailure => ErrorName is null;

    /// <summary>Whether sending the same request again later can succeed.</summary>
    public bool IsRetryable => StatusCode switch
    {
        408 or 424 or 429 or 502 or 504 => true,
        // A generic 409 means another caller owns the report for now. A model failure with 409 is
        // permanent (access denied or model retired).
        409 => ErrorName != "ModelInvocationFailed",
        _ => false,
    };
}
