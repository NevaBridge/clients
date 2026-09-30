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
}
