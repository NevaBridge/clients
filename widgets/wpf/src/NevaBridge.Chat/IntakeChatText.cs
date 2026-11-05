namespace NevaBridge.Chat;

/// <summary>
/// The English text of the Generic Intake chat, in one place so an application can translate it.
/// The assistant itself answers in the reporter's language.
/// </summary>
public static class IntakeChatText
{
    public const string Title = "Support assistant";

    public const string Lead =
        "Describe the problem or your idea. The assistant asks for whatever the team needs to know.";

    /// <summary>The message Finish sends; it ends the conversation and files what was noted.</summary>
    public const string FinishMessage = "I'm done. Please pass this on to the team.";

    public const string FinishBlocked = "Send or clear your message first. Finishing now would leave it out.";

    public const string Working = "Working on a reply";

    public const string Unreadable = "The reply could not be loaded. Check again to see whether it arrived.";

    public const string TurnConflict =
        "Your message was not sent because the chat changed in the meantime. It is kept here, so you can try again.";

    public const string Ended = "This chat is finished.";

    public const string NothingFiled = "Nothing was passed on to the team.";

    public const string AttachmentsOff = "Attachments are turned off for your organization.";

    public const string FilesUnknown = "The attached files could not be loaded.";

    public const string RefreshFiles = "Load files again";

    public const string Send = "Send";

    public const string SendTooltip = "Ctrl+Enter sends the message";

    public const string Finish = "Finish";

    public const string FinishTooltip = "End the chat and pass everything on to the team";

    public const string CheckAgain = "Check again";

    public const string AttachFile = "Attach file";

    public const string AttachedFiles = "Attached files";

    public const string RemoveFile = "Remove this file";

    /// <summary>The accessible name of a file's remove button; <c>{0}</c> is the file name.</summary>
    public const string RemoveNamedFile = "Remove {0}";

    /// <summary>What the assistant is doing, from a turn's last <c>status</c> event.</summary>
    public static string Progress(string phase, string? tool) => (phase, tool) switch
    {
        ("checking", _) => "Reading your message",
        ("planning", _) => "Working out the next step",
        ("writing", _) => "Writing a reply",
        ("tool", "searchKnowledge") => "Searching the knowledge base",
        ("tool", "inspectTemplate") => "Checking the report template",
        ("tool", "readTranscript") => "Re-reading the chat",
        ("tool", "readAttachment") => "Reading your attachment",
        // Phases and tools grow without notice; an unknown one is still work in progress.
        _ => Working,
    };

    public static string Category(string? category) => category switch
    {
        "bug_report" => "Bug report",
        "feature_request" => "Feature request",
        "support_request" => "Support request",
        "feedback" => "Feedback",
        "intake_record" => "Note for the team",
        null => "Report",
        string other => other,
    };

    public static string Status(string status) => status switch
    {
        "submitted" => "Passed on to the team",
        "closed" => "Closed",
        "assigned" => "Assigned",
        "engineering_in_progress" => "Being worked on",
        "pending_release" => "Fixed, awaiting release",
        string other => other,
    };

    public static string? StatusReason(string? reason) => reason switch
    {
        null => null,
        "answered" => "Answered in the chat",
        "follow_up_declined" => "Follow-up declined",
        "no_contact_details" => "No contact details",
        string other => other,
    };
}
