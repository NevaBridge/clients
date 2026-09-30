using System.Text.Json;
using System.Text.Json.Serialization;

namespace NevaBridge.Chat.Settings;

/// <summary>
/// The JSON form in which a secret store keeps all four settings together, so the web chat address
/// travels with the key: <c>{"apiBaseUrl", "productId", "apiKey", "webChatUrl"}</c>.
/// </summary>
public static class NevaBridgeSettingsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(NevaBridgeSettings settings) =>
        JsonSerializer.Serialize(
            new StoredSettings(
                settings.ApiBaseUrl.AbsoluteUri,
                settings.ProductId,
                settings.ApiKey,
                settings.WebChatUrl?.AbsoluteUri),
            Options);

    public static NevaBridgeSettings Deserialize(string json)
    {
        StoredSettings? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredSettings>(json, Options);
        }
        catch (JsonException error)
        {
            throw new NevaBridgeSettingsException("The stored NevaBridge settings are not valid JSON.", error);
        }

        if (stored is null
            || !Uri.TryCreate(stored.ApiBaseUrl, UriKind.Absolute, out Uri? apiBaseUrl)
            || stored.ProductId is null
            || stored.ApiKey is null)
        {
            throw new NevaBridgeSettingsException(
                "The stored NevaBridge settings are incomplete: apiBaseUrl, productId and apiKey are required.");
        }

        Uri? webChatUrl = null;
        if (!string.IsNullOrWhiteSpace(stored.WebChatUrl)
            && !Uri.TryCreate(stored.WebChatUrl, UriKind.Absolute, out webChatUrl))
        {
            throw new NevaBridgeSettingsException("The stored NevaBridge settings have an invalid webChatUrl.");
        }

        return new NevaBridgeSettings(apiBaseUrl, stored.ProductId, stored.ApiKey, webChatUrl);
    }

    private sealed record StoredSettings(string? ApiBaseUrl, string? ProductId, string? ApiKey, string? WebChatUrl);
}
