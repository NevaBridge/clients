using System.Data;
using Microsoft.Data.SqlClient;
using NevaBridge.Chat.Settings;

namespace NevaBridge.Sample.KeyStore;

/// <summary>
/// Reads the NevaBridge settings from SQL Server through <c>dbo.GetNevaBridgeSettings</c>, as
/// created by <c>sql/create-nevabridge-settings.sql</c>. The connecting user needs the
/// NevaBridgeReader role and nothing else.
/// </summary>
public sealed class SqlServerSettingsProvider : INevaBridgeSettingsProvider
{
    private readonly string _connectionString;

    public SqlServerSettingsProvider(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async ValueTask<NevaBridgeSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        await using SqlConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using SqlCommand command = new("dbo.GetNevaBridgeSettings", connection)
        {
            CommandType = CommandType.StoredProcedure,
        };
        await using SqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new NevaBridgeSettingsException(
                "No NevaBridge settings are stored. Insert the row with Id = 1 into dbo.NevaBridgeSettings.");
        }

        int webChatUrlColumn = reader.GetOrdinal("WebChatUrl");
        string? webChatUrl = reader.IsDBNull(webChatUrlColumn) ? null : reader.GetString(webChatUrlColumn);

        return new NevaBridgeSettings(
            ParseBaseUrl(reader.GetString(reader.GetOrdinal("BaseUrl"))),
            reader.GetString(reader.GetOrdinal("ProductId")),
            reader.GetString(reader.GetOrdinal("ApiKey")),
            ParseWebChatUrl(webChatUrl));
    }

    private static Uri ParseBaseUrl(string value) =>
        ParseSecureAddress(value)
        ?? throw new NevaBridgeSettingsException(
            $"dbo.NevaBridgeSettings.BaseUrl must be an https address (plain http only on this machine). It is '{value}'.");

    private static Uri? ParseWebChatUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return ParseSecureAddress(value)
            ?? throw new NevaBridgeSettingsException(
                $"dbo.NevaBridgeSettings.WebChatUrl must be an https address (plain http only on this machine), or NULL. It is '{value}'.");
    }

    // The API address carries the key and the web chat page is trusted by the bridge, so neither may
    // travel unencrypted over a network. Plain http stays possible on this machine for local testing.
    private static Uri? ParseSecureAddress(string value) =>
        Uri.TryCreate(value.Trim(), UriKind.Absolute, out Uri? url)
        && (url.Scheme == Uri.UriSchemeHttps || (url.Scheme == Uri.UriSchemeHttp && url.IsLoopback))
            ? url
            : null;
}
