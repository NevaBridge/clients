using System.Text.Json;

namespace NevaBridge.Sample.Client;

/// <summary>
/// Which NevaBridge features this example offers, read from <see cref="FileName"/> next to the
/// application. A feature appears only when this file switches it on and the tenant's NevaBridge
/// feature gate allows it. The file holds no secrets; the API key comes from the settings source.
/// </summary>
/// <param name="GenericIntakeChat">The native Generic Intake chat (also needs <c>genericIntake</c>).</param>
/// <param name="ClassicChat">The native classic chat, where the reporter picks the report type.</param>
/// <param name="WebChat">The NevaBridge web chat in WebView2.</param>
/// <param name="Attachments">File attachments in the Generic Intake chat and the web chat (also needs <c>chatAttachments</c>).</param>
/// <param name="Dictation">Dictation in the Generic Intake chat (also needs <c>voiceDictation</c>).</param>
/// <param name="HandsFreeVoice">The hands-free conversation in the Generic Intake chat (also needs <c>voiceConversation</c>).</param>
public sealed record SampleFeatures(
    bool GenericIntakeChat,
    bool ClassicChat,
    bool WebChat,
    bool Attachments,
    bool Dictation,
    bool HandsFreeVoice)
{
    public const string FileName = "nevabridge-features.json";

    private static readonly string[] Keys =
        ["genericIntakeChat", "classicChat", "webChat", "attachments", "dictation", "handsFreeVoice"];

    /// <summary>Reads the file; every key is required, so a typo never silently switches a feature off.</summary>
    /// <exception cref="SampleFeaturesException">The file is missing or not valid.</exception>
    public static SampleFeatures Read(string path)
    {
        string json;
        try
        {
            json = File.ReadAllText(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new SampleFeaturesException($"{path} could not be read: {error.Message}", error);
        }

        return Parse(json, path);
    }

    /// <param name="json">The file's content.</param>
    /// <param name="source">Where it came from, named in errors.</param>
    public static SampleFeatures Parse(string json, string source)
    {
        Dictionary<string, bool> values = new(StringComparer.Ordinal);
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new SampleFeaturesException($"{source} must hold a JSON object with the keys {string.Join(", ", Keys)}.");
            }

            foreach (JsonProperty property in document.RootElement.EnumerateObject())
            {
                if (!Keys.Contains(property.Name, StringComparer.Ordinal))
                {
                    throw new SampleFeaturesException(
                        $"{source} has the unknown key '{property.Name}'. Known keys: {string.Join(", ", Keys)}.");
                }

                if (property.Value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    throw new SampleFeaturesException($"{source}: '{property.Name}' must be true or false.");
                }

                values[property.Name] = property.Value.GetBoolean();
            }
        }
        catch (JsonException error)
        {
            throw new SampleFeaturesException($"{source} is not valid JSON: {error.Message}", error);
        }

        string[] missing = Keys.Where(key => !values.ContainsKey(key)).ToArray();
        if (missing.Length > 0)
        {
            throw new SampleFeaturesException($"{source} is missing {string.Join(", ", missing)}; set each to true or false.");
        }

        return new SampleFeatures(
            values["genericIntakeChat"],
            values["classicChat"],
            values["webChat"],
            values["attachments"],
            values["dictation"],
            values["handsFreeVoice"]);
    }
}

/// <summary>The features file is missing or not valid.</summary>
public sealed class SampleFeaturesException(string message, Exception? innerException = null)
    : Exception(message, innerException);
