using System.Security.Principal;
using NevaBridge.Chat;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Step 2 in your application: tell NevaBridge who is reporting. Map your own signed-in user here.
/// </summary>
internal static class CurrentReporter
{
    /// <remarks>
    /// The id becomes the report's reporterId, so keep it stable and ASCII. Roles (anonymous,
    /// customer, tenant) select which knowledge-base documents the assistant may use; this example
    /// sends none.
    /// </remarks>
    public static NevaBridgeReporter Get() =>
        new(Id: WindowsIdentity.GetCurrent().Name, DisplayName: Environment.UserName);

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
