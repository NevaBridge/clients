using System.Security.Principal;
using NevaBridge.Chat;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Step 2 in your application: tell NevaBridge who is reporting. Map your own signed-in user here.
/// </summary>
internal static class CurrentReporter
{
    /// <remarks>
    /// The id becomes the report's reporterId, so keep it stable and ASCII.
    /// </remarks>
    public static NevaBridgeReporter Get() =>
        new(Id: WindowsIdentity.GetCurrent().Name, DisplayName: Environment.UserName, Roles: KnowledgeRoles);

    /// <summary>
    /// Which knowledge-base documents the assistant may use to answer this reporter. Send every
    /// role that applies, because roles do not imply one another: "anonymous" for documents marked
    /// public, "customer" for documents for your customers, "tenant" for all documents, including
    /// internal ones. An application only your own staff use sends "tenant". Without roles the
    /// assistant uses only public documents. This example stands in for an application your
    /// customers use.
    /// </summary>
    private static readonly string[] KnowledgeRoles = ["anonymous", "customer"];

    /// <summary>
    /// How your team can reach the reporter, if your application knows it. The Generic Intake
    /// assistant then never asks for it, and support requests carry it. This example reads it from
    /// NEVABRIDGE_REPORTER_EMAIL and NEVABRIDGE_REPORTER_PHONE; use your user's profile instead.
    /// </summary>
    public static ReporterContact? Contact()
    {
        string? email = Value("NEVABRIDGE_REPORTER_EMAIL");
        string? phone = Value("NEVABRIDGE_REPORTER_PHONE");
        return email is null && phone is null ? null : new ReporterContact { Email = email, Phone = phone };
    }

    private static string? Value(string variable) =>
        Environment.GetEnvironmentVariable(variable)?.Trim() is { Length: > 0 } value ? value : null;
}
