namespace NevaBridge.Chat.Settings;

/// <summary>
/// Supplies the NevaBridge settings to the chat. Implement it to read the settings wherever your
/// application keeps its configuration; <see cref="EnvironmentSettingsProvider"/> and the stores in
/// the platform packages are ready-made implementations.
/// </summary>
public interface INevaBridgeSettingsProvider
{
    ValueTask<NevaBridgeSettings> GetSettingsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Everything the chat needs to reach NevaBridge. <see cref="WebChatUrl"/> is where the web chat
/// page is loaded from; null means the page shipped with the application.
/// </summary>
public sealed record NevaBridgeSettings
{
    public static readonly Uri SandboxApiBaseUrl = new("https://tenant.api.sandbox.nevabridge.com");
    public static readonly Uri ProductionApiBaseUrl = new("https://tenant.api.nevabridge.com");

    public NevaBridgeSettings(Uri apiBaseUrl, string productId, string apiKey, Uri? webChatUrl)
    {
        // The API address carries the key and the web chat page is trusted by the bridge, so
        // neither may travel unencrypted over a network.
        if (!SecureAddress.IsAllowed(apiBaseUrl))
        {
            throw new NevaBridgeSettingsException(
                $"The API base URL must use https (plain http only on this machine), not '{apiBaseUrl}'.");
        }

        if (webChatUrl is not null && !SecureAddress.IsAllowed(webChatUrl))
        {
            throw new NevaBridgeSettingsException(
                $"The web chat URL must use https (plain http only on this machine), not '{webChatUrl}'.");
        }

        if (string.IsNullOrWhiteSpace(productId))
        {
            throw new NevaBridgeSettingsException("The NevaBridge product id is missing.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new NevaBridgeSettingsException("The NevaBridge API key is missing.");
        }

        ApiBaseUrl = apiBaseUrl;
        ProductId = productId.Trim();
        ApiKey = apiKey.Trim();
        WebChatUrl = webChatUrl;
    }

    public Uri ApiBaseUrl { get; }

    public string ProductId { get; }

    public string ApiKey { get; }

    public Uri? WebChatUrl { get; }

    // Records print every property by default; keep the key out of logs and debugger output.
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append("ApiBaseUrl = ").Append(ApiBaseUrl)
            .Append(", ProductId = ").Append(ProductId)
            .Append(", ApiKey = ***")
            .Append(", WebChatUrl = ").Append(WebChatUrl);
        return true;
    }
}

/// <summary>The NevaBridge settings are missing or unusable. The message says which one and why.</summary>
public sealed class NevaBridgeSettingsException : Exception
{
    public NevaBridgeSettingsException(string message)
        : base(message)
    {
    }

    public NevaBridgeSettingsException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
