using System.Text.Json;
using NevaBridge.Chat;

namespace NevaBridge.Sample.Client;

/// <summary>Turns a NevaBridge error response into a <see cref="NevaBridgeApiException"/>.</summary>
internal static class ApiErrors
{
    public static NevaBridgeApiException FromResponse(int statusCode, string body)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Object)
            {
                string? message = ReadString(root, "message");
                string? errorName = ReadString(root, "error");
                if (errorName is not null)
                {
                    return new NevaBridgeApiException(statusCode, errorName, ReadString(root, "category"), message ?? errorName);
                }

                if (message is not null)
                {
                    // API Gateway's own body has no error name: the request never reached NevaBridge.
                    return new NevaBridgeApiException(
                        statusCode,
                        null,
                        null,
                        $"The NevaBridge gateway answered HTTP {statusCode}: {message}");
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON, for example a proxy's HTML error page. Fall through to the generic message.
        }

        return new NevaBridgeApiException(statusCode, null, null, $"NevaBridge answered HTTP {statusCode}.");
    }

    private static string? ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
