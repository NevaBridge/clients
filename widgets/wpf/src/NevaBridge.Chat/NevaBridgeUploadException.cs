namespace NevaBridge.Chat;

/// <summary>
/// The upload of an attachment to its presigned address failed, so the attachment was not
/// confirmed. <see cref="StatusCode"/> is the upload's HTTP status, or null when no answer arrived.
/// </summary>
public sealed class NevaBridgeUploadException : Exception
{
    public NevaBridgeUploadException(int? statusCode, Exception? innerException = null)
        : base(
            statusCode is null
                ? "The file could not be uploaded."
                : $"The file could not be uploaded (HTTP {statusCode}).",
            innerException)
    {
        StatusCode = statusCode;
    }

    public int? StatusCode { get; }
}
