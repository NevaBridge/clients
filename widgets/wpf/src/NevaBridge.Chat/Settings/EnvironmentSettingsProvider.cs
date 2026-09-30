namespace NevaBridge.Chat.Settings;

/// <summary>
/// Reads the settings from environment variables: the quickest way to try the chat with a
/// temporary sandbox key.
/// </summary>
/// <remarks>
/// <c>NEVABRIDGE_API_KEY</c> and <c>NEVABRIDGE_PRODUCT_ID</c> are required.
/// <c>NEVABRIDGE_API_BASE_URL</c> defaults to the sandbox; <c>NEVABRIDGE_WEBCHAT_URL</c> defaults to
/// the page shipped with the application.
/// </remarks>
public sealed class EnvironmentSettingsProvider : INevaBridgeSettingsProvider
{
    public const string ApiKeyVariable = "NEVABRIDGE_API_KEY";
    public const string ProductIdVariable = "NEVABRIDGE_PRODUCT_ID";
    public const string ApiBaseUrlVariable = "NEVABRIDGE_API_BASE_URL";
    public const string WebChatUrlVariable = "NEVABRIDGE_WEBCHAT_URL";

    private readonly Func<string, string?> _getVariable;

    public EnvironmentSettingsProvider()
        : this(Environment.GetEnvironmentVariable)
    {
    }

    public EnvironmentSettingsProvider(Func<string, string?> getVariable)
    {
        _getVariable = getVariable;
    }

    public ValueTask<NevaBridgeSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        string apiKey = Required(ApiKeyVariable);
        string productId = Required(ProductIdVariable);
        Uri apiBaseUrl = OptionalAddress(ApiBaseUrlVariable) ?? NevaBridgeSettings.SandboxApiBaseUrl;
        Uri? webChatUrl = OptionalAddress(WebChatUrlVariable);
        return ValueTask.FromResult(new NevaBridgeSettings(apiBaseUrl, productId, apiKey, webChatUrl));
    }

    private string Required(string name) =>
        _getVariable(name) is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new NevaBridgeSettingsException($"The environment variable {name} is not set.");

    private Uri? OptionalAddress(string name)
    {
        if (_getVariable(name) is not { } value || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? address)
            ? address
            : throw new NevaBridgeSettingsException($"The environment variable {name} is not an absolute address: '{value}'.");
    }
}
