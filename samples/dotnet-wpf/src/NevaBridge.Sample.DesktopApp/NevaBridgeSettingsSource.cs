using NevaBridge.Chat.Settings;
using NevaBridge.Sample.KeyStore;
using NevaBridge.Wpf;

namespace NevaBridge.Sample.DesktopApp;

/// <summary>
/// Step 1 in your application: provide the NevaBridge settings (API key, product id, API base URL,
/// web chat URL). Pick the one source that matches how your application keeps configuration.
/// </summary>
/// <remarks>
/// This example lets you choose with NEVABRIDGE_SETTINGS_SOURCE:
/// <list type="bullet">
/// <item><c>environment</c> (default): NEVABRIDGE_API_KEY and NEVABRIDGE_PRODUCT_ID, to try the chat.</item>
/// <item><c>credential-manager</c>: Windows Credential Manager, saved once with File, NevaBridge settings.</item>
/// <item><c>sql</c>: a SQL Server table, see sql/create-nevabridge-settings.sql.</item>
/// </list>
/// </remarks>
internal static class NevaBridgeSettingsSource
{
    public const string SourceVariable = "NEVABRIDGE_SETTINGS_SOURCE";

    public static string Name =>
        Environment.GetEnvironmentVariable(SourceVariable)?.Trim().ToLowerInvariant() is { Length: > 0 } name
            ? name
            : "environment";

    public static INevaBridgeSettingsProvider Create() => Name switch
    {
        "environment" => new EnvironmentSettingsProvider(),
        "credential-manager" => new WindowsCredentialSettingsStore(),
        "sql" => new SqlServerSettingsProvider(SqlConnectionString()),
        string other => throw new NevaBridgeSettingsException(
            $"{SourceVariable} is '{other}'. Use environment, credential-manager or sql."),
    };

    // Your application already has a connection string; use it instead.
    private static string SqlConnectionString() =>
        Environment.GetEnvironmentVariable("NEVABRIDGE_SAMPLE_SQL")
        ?? "Server=localhost;Database=NevaBridgeSample;Integrated Security=true;TrustServerCertificate=true";
}
