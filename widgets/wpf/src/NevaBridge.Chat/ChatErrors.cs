using System.Net.Http;
using System.Text.Json;

namespace NevaBridge.Chat;

/// <summary>What a reporter is told when a chat request fails.</summary>
internal static class ChatErrors
{
    public static string Describe(Exception error) => error switch
    {
        NevaBridgeApiException { StatusCode: 402 } =>
            "NevaBridge has no credits left for this organization. Please contact your administrator.",
        NevaBridgeApiException { StatusCode: 401 or 403 } api =>
            $"NevaBridge refused the request ({api.Message}). Check the stored API key and product.",
        NevaBridgeApiException { IsRetryable: true } =>
            "NevaBridge is busy or took too long to answer. Please try again in a moment.",
        NevaBridgeApiException api => $"NevaBridge could not process the request: {api.Message}",
        NevaBridgeUploadException => "The file could not be uploaded. Please try again.",
        TaskCanceledException => "The request to NevaBridge timed out. Please try again.",
        JsonException => "NevaBridge sent an answer this version of the chat cannot read.",
        HttpRequestException => "NevaBridge could not be reached. Check the network connection and try again.",
        _ => "The application could not complete the chat request.",
    };
}
