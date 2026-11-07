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

    public const string Dictate = "Dictate";

    public const string StopDictating = "Stop dictating";

    public const string DictateTooltip = "Type what you say into the message box";

    public const string Talk = "Talk";

    public const string TalkTooltip = "Talk with the assistant hands-free: it sends after a short pause and reads the reply aloud";

    public const string VoiceLanguageLabel = "Speech language";

    public const string SendNow = "Send now";

    public const string StopReading = "Stop reading";

    public const string EndVoiceMode = "End voice mode";

    public const string EndVoiceModeTooltip = "Escape also ends voice mode; words not yet sent go into the message box";

    public const string OpeningMicrophone = "Opening the microphone";

    public const string Listening = "Listening";

    public const string WaitingForReply = "Waiting for the reply";

    public const string ReadingAloud = "Reading the reply aloud";

    public const string MicrophoneUnavailable =
        "The microphone could not be opened. Check that one is connected and that Windows allows this application to use it.";

    public const string VoiceOff = "Voice input is turned off for your organization.";

    public const string RecognitionStoppedDictate =
        "Speech recognition stopped. What was heard is in the message box. Choose Dictate to start again.";

    public const string RecognitionStoppedTalk =
        "Speech recognition stopped. Anything heard is in the message box. Choose Talk to start again.";

    public const string SpeechFailed = "The reply could not be read aloud. It is in the chat, and voice mode listens again.";

    public const string SpokenNotSent = "Your spoken message was not sent, so voice mode ended. Its words are in the message box.";

    public const string VoiceModeClosed = "This chat is finished, so voice mode ended.";

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
