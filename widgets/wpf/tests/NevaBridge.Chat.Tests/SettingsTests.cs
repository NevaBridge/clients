using NevaBridge.Chat.Settings;

namespace NevaBridge.Chat.Tests;

public sealed class SettingsTests
{
    private const string ProductId = "product-00000000-0000-4000-8000-000000000001";
    private const string ApiKey = "sk_test_not_a_real_key";

    private static readonly Uri Sandbox = new("https://tenant.api.sandbox.nevabridge.com");

    [Fact]
    public void Settings_never_print_the_API_key()
    {
        // Given
        NevaBridgeSettings settings = new(Sandbox, ProductId, ApiKey, null);

        // When
        string printed = settings.ToString();

        // Then
        Assert.DoesNotContain(ApiKey, printed);
        Assert.Contains(ProductId, printed);
    }

    [Theory]
    [InlineData("http://tenant.api.nevabridge.com", null, "API base URL")]
    [InlineData("https://tenant.api.nevabridge.com", "http://chat.example.com/index.html", "web chat URL")]
    public void Settings_refuse_addresses_that_travel_unencrypted(string apiBaseUrl, string? webChatUrl, string named)
    {
        // When
        NevaBridgeSettingsException error = Assert.Throws<NevaBridgeSettingsException>(() => new NevaBridgeSettings(
            new Uri(apiBaseUrl),
            ProductId,
            ApiKey,
            webChatUrl is null ? null : new Uri(webChatUrl)));

        // Then
        Assert.Contains(named, error.Message);
    }

    [Theory]
    [InlineData("ftp://tenant.api.nevabridge.com")]
    [InlineData("http://192.168.1.20:8080")]
    public void Settings_refuse_API_addresses_other_than_https(string apiBaseUrl)
    {
        // When
        NevaBridgeSettingsException error = Assert.Throws<NevaBridgeSettingsException>(
            () => new NevaBridgeSettings(new Uri(apiBaseUrl), ProductId, ApiKey, null));

        // Then
        Assert.Contains("https", error.Message);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000")]
    [InlineData("http://localhost:5000")]
    public void Settings_allow_plain_http_on_this_machine_for_local_testing(string apiBaseUrl)
    {
        // When
        NevaBridgeSettings settings = new(new Uri(apiBaseUrl), ProductId, ApiKey, new Uri(apiBaseUrl + "/index.html"));

        // Then
        Assert.Equal(new Uri(apiBaseUrl), settings.ApiBaseUrl);
    }

    [Theory]
    [InlineData("", ApiKey, "product id")]
    [InlineData(ProductId, " ", "API key")]
    public void Settings_require_a_product_and_a_key(string productId, string apiKey, string named)
    {
        // When
        NevaBridgeSettingsException error = Assert.Throws<NevaBridgeSettingsException>(
            () => new NevaBridgeSettings(Sandbox, productId, apiKey, null));

        // Then
        Assert.Contains(named, error.Message);
    }

    [Fact]
    public async Task Two_environment_variables_are_enough_to_try_the_sandbox()
    {
        // Given
        EnvironmentSettingsProvider provider = new(Variables(new()
        {
            ["NEVABRIDGE_API_KEY"] = ApiKey,
            ["NEVABRIDGE_PRODUCT_ID"] = ProductId,
        }));

        // When
        NevaBridgeSettings settings = await provider.GetSettingsAsync();

        // Then
        Assert.Equal(Sandbox, settings.ApiBaseUrl);
        Assert.Equal(ProductId, settings.ProductId);
        Assert.Equal(ApiKey, settings.ApiKey);
        Assert.Null(settings.WebChatUrl);
    }

    [Fact]
    public async Task Optional_environment_variables_set_the_API_and_web_chat_addresses()
    {
        // Given
        EnvironmentSettingsProvider provider = new(Variables(new()
        {
            ["NEVABRIDGE_API_KEY"] = ApiKey,
            ["NEVABRIDGE_PRODUCT_ID"] = ProductId,
            ["NEVABRIDGE_API_BASE_URL"] = "https://tenant.api.nevabridge.com",
            ["NEVABRIDGE_WEBCHAT_URL"] = "https://chat.nevabridge.com/v1/index.html",
        }));

        // When
        NevaBridgeSettings settings = await provider.GetSettingsAsync();

        // Then
        Assert.Equal(new Uri("https://tenant.api.nevabridge.com"), settings.ApiBaseUrl);
        Assert.Equal(new Uri("https://chat.nevabridge.com/v1/index.html"), settings.WebChatUrl);
    }

    [Theory]
    [InlineData("NEVABRIDGE_API_KEY")]
    [InlineData("NEVABRIDGE_PRODUCT_ID")]
    public async Task A_missing_environment_variable_is_named_in_the_error(string missing)
    {
        // Given
        Dictionary<string, string> values = new()
        {
            ["NEVABRIDGE_API_KEY"] = ApiKey,
            ["NEVABRIDGE_PRODUCT_ID"] = ProductId,
        };
        values.Remove(missing);
        EnvironmentSettingsProvider provider = new(Variables(values));

        // When
        NevaBridgeSettingsException error =
            await Assert.ThrowsAsync<NevaBridgeSettingsException>(() => provider.GetSettingsAsync().AsTask());

        // Then
        Assert.Contains(missing, error.Message);
    }

    [Fact]
    public async Task A_malformed_address_in_an_environment_variable_is_named_in_the_error()
    {
        // Given
        EnvironmentSettingsProvider provider = new(Variables(new()
        {
            ["NEVABRIDGE_API_KEY"] = ApiKey,
            ["NEVABRIDGE_PRODUCT_ID"] = ProductId,
            ["NEVABRIDGE_WEBCHAT_URL"] = "chat.example.com",
        }));

        // When
        NevaBridgeSettingsException error =
            await Assert.ThrowsAsync<NevaBridgeSettingsException>(() => provider.GetSettingsAsync().AsTask());

        // Then
        Assert.Contains("NEVABRIDGE_WEBCHAT_URL", error.Message);
    }

    [Fact]
    public void Stored_settings_round_trip_through_their_JSON_form()
    {
        // Given
        NevaBridgeSettings settings = new(Sandbox, ProductId, ApiKey, new Uri("https://chat.nevabridge.com/latest/index.html"));

        // When
        NevaBridgeSettings restored = NevaBridgeSettingsJson.Deserialize(NevaBridgeSettingsJson.Serialize(settings));

        // Then
        Assert.Equal(settings, restored);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"apiBaseUrl":"https://tenant.api.sandbox.nevabridge.com","productId":"p"}""")]
    public void Unreadable_stored_settings_explain_themselves(string json)
    {
        // When
        NevaBridgeSettingsException error =
            Assert.Throws<NevaBridgeSettingsException>(() => NevaBridgeSettingsJson.Deserialize(json));

        // Then
        Assert.Contains("stored NevaBridge settings", error.Message);
    }

    private static Func<string, string?> Variables(Dictionary<string, string> values) =>
        name => values.TryGetValue(name, out string? value) ? value : null;
}
