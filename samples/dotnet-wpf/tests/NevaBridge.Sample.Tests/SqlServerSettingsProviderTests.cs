using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using NevaBridge.Chat.Settings;
using NevaBridge.Sample.KeyStore;
using Testcontainers.MsSql;

namespace NevaBridge.Sample.Tests;

/// <summary>Starts one SQL Server container for all settings-store tests.</summary>
public sealed class SqlServerFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public string AdminConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

public sealed class SqlServerSettingsProviderTests : IClassFixture<SqlServerFixture>, IAsyncLifetime
{
    private const string ReaderPassword = "Reader-Passw0rd!";

    private readonly SqlServerFixture _sql;
    private readonly string _database = "settings_" + Guid.NewGuid().ToString("N");
    private readonly string _readerLogin = "reader_" + Guid.NewGuid().ToString("N")[..12];

    public SqlServerSettingsProviderTests(SqlServerFixture sql)
    {
        _sql = sql;
    }

    public async Task InitializeAsync()
    {
        await ExecuteAsync(_sql.AdminConnectionString, $"CREATE DATABASE [{_database}]");
        string script = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "create-nevabridge-settings.sql"));
        foreach (string batch in Regex.Split(script, @"^\s*GO\s*$", RegexOptions.Multiline))
        {
            if (!string.IsNullOrWhiteSpace(batch))
            {
                await ExecuteAsync(DatabaseConnectionString(), batch);
            }
        }

        await ExecuteAsync(
            DatabaseConnectionString(),
            $"""
            CREATE LOGIN [{_readerLogin}] WITH PASSWORD = '{ReaderPassword}', CHECK_POLICY = OFF;
            CREATE USER [{_readerLogin}] FOR LOGIN [{_readerLogin}];
            ALTER ROLE NevaBridgeReader ADD MEMBER [{_readerLogin}];
            """);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task A_member_of_the_reader_role_loads_the_stored_settings()
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            """
            INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey)
            VALUES (1, 'https://tenant.api.sandbox.nevabridge.com', 'product-00000000-0000-4000-8000-000000000001', 'sk_test_key');
            """);
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettings settings = await store.GetSettingsAsync().AsTask();

        // Then
        Assert.Equal(new Uri("https://tenant.api.sandbox.nevabridge.com"), settings.ApiBaseUrl);
        Assert.Equal("product-00000000-0000-4000-8000-000000000001", settings.ProductId);
        Assert.Equal("sk_test_key", settings.ApiKey);
        Assert.Null(settings.WebChatUrl);
    }

    [Fact]
    public async Task A_web_chat_address_set_by_the_administrator_is_loaded_with_the_settings()
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            """
            INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey, WebChatUrl)
            VALUES (1, 'https://tenant.api.sandbox.nevabridge.com', 'p', 'k', 'https://chat.example.com/v1/index.html');
            """);
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettings settings = await store.GetSettingsAsync().AsTask();

        // Then
        Assert.Equal(new Uri("https://chat.example.com/v1/index.html"), settings.WebChatUrl);
    }

    [Theory]
    [InlineData("chat.example.com/index.html")]
    [InlineData("file:///C:/chat/index.html")]
    [InlineData("http://chat.example.com/index.html")]
    public async Task A_web_chat_address_that_is_not_an_absolute_web_address_is_refused(string webChatUrl)
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            $"""
            INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey, WebChatUrl)
            VALUES (1, 'https://tenant.api.sandbox.nevabridge.com', 'p', 'k', '{webChatUrl}');
            """);
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettingsException error =
            await Assert.ThrowsAsync<NevaBridgeSettingsException>(() => store.GetSettingsAsync().AsTask());

        // Then
        Assert.Contains("WebChatUrl", error.Message);
    }

    [Fact]
    public async Task A_web_chat_address_on_this_machine_may_use_plain_http_for_local_testing()
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            """
            INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey, WebChatUrl)
            VALUES (1, 'https://tenant.api.sandbox.nevabridge.com', 'p', 'k', 'http://localhost:8088/index.html');
            """);
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettings settings = await store.GetSettingsAsync().AsTask();

        // Then
        Assert.Equal(new Uri("http://localhost:8088/index.html"), settings.WebChatUrl);
    }

    [Fact]
    public async Task An_API_address_without_https_is_refused_before_the_key_is_used()
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            """
            INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey)
            VALUES (1, 'http://tenant.api.sandbox.nevabridge.com', 'p', 'k');
            """);
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettingsException error =
            await Assert.ThrowsAsync<NevaBridgeSettingsException>(() => store.GetSettingsAsync().AsTask());

        // Then
        Assert.Contains("BaseUrl", error.Message);
    }

    [Fact]
    public async Task Loading_before_the_settings_row_exists_explains_what_is_missing()
    {
        // Given
        SqlServerSettingsProvider store = new(ReaderConnectionString());

        // When
        NevaBridgeSettingsException error =
            await Assert.ThrowsAsync<NevaBridgeSettingsException>(() => store.GetSettingsAsync().AsTask());

        // Then
        Assert.Contains("dbo.NevaBridgeSettings", error.Message);
    }

    [Fact]
    public async Task A_member_of_the_reader_role_cannot_select_the_key_directly()
    {
        // Given
        await ExecuteAsync(
            DatabaseConnectionString(),
            "INSERT INTO dbo.NevaBridgeSettings (Id, BaseUrl, ProductId, ApiKey) VALUES (1, 'https://x.test', 'p', 'k');");

        // When
        SqlException error = await Assert.ThrowsAsync<SqlException>(
            () => ExecuteAsync(ReaderConnectionString(), "SELECT ApiKey FROM dbo.NevaBridgeSettings"));

        // Then
        Assert.Contains("SELECT permission was denied", error.Message);
    }

    private string DatabaseConnectionString() =>
        new SqlConnectionStringBuilder(_sql.AdminConnectionString) { InitialCatalog = _database }.ConnectionString;

    private string ReaderConnectionString() =>
        new SqlConnectionStringBuilder(_sql.AdminConnectionString)
        {
            InitialCatalog = _database,
            UserID = _readerLogin,
            Password = ReaderPassword,
        }.ConnectionString;

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using SqlConnection connection = new(connectionString);
        await connection.OpenAsync();
        await using SqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
