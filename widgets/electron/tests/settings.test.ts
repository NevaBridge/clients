import {describe, expect, test} from "bun:test";
import {
  createSettings,
  deserializeSettings,
  environmentSettingsProvider,
  NevaBridgeSettingsError,
  SANDBOX_API_BASE_URL,
  serializeSettings,
} from "../src/settings.js";

const PRODUCT_ID = "product-00000000-0000-4000-8000-000000000001";
const API_KEY = "sk_test_not_a_real_key";

describe("settings", () => {
  test("never print or serialize the API key by accident", () => {
    // Given
    const settings = createSettings({
      apiBaseUrl: SANDBOX_API_BASE_URL,
      productId: PRODUCT_ID,
      apiKey: API_KEY,
    });

    // When
    const printed = JSON.stringify(settings) + String(Object.keys(settings));

    // Then
    expect(printed).not.toContain(API_KEY);
    expect(settings.apiKey).toBe(API_KEY);
  });

  test.each([
    ["http://tenant.api.nevabridge.com", undefined, "API base URL"],
    [
      "https://tenant.api.nevabridge.com",
      "http://chat.example.com/index.html",
      "web chat URL",
    ],
    ["ftp://tenant.api.nevabridge.com", undefined, "API base URL"],
  ])(
    "refuse %s / %s because it travels unencrypted",
    (apiBaseUrl, webChatUrl, named) => {
      // When
      const create = (): unknown =>
        createSettings({
          apiBaseUrl,
          productId: PRODUCT_ID,
          apiKey: API_KEY,
          webChatUrl,
        });

      // Then
      expect(create).toThrow(NevaBridgeSettingsError);
      expect(create).toThrow(named);
    },
  );

  test.each(["http://127.0.0.1:5000", "http://localhost:5000"])(
    "allow plain http on this machine for local testing: %s",
    (address) => {
      // When
      const settings = createSettings({
        apiBaseUrl: address,
        productId: PRODUCT_ID,
        apiKey: API_KEY,
        webChatUrl: `${address}/index.html`,
      });

      // Then
      expect(settings.webChatUrl).toBe(`${address}/index.html`);
    },
  );

  test.each([
    ["", API_KEY, "product id"],
    [PRODUCT_ID, " ", "API key"],
  ])("require a product and a key (%s)", (productId, apiKey, named) => {
    // When
    const create = (): unknown =>
      createSettings({apiBaseUrl: SANDBOX_API_BASE_URL, productId, apiKey});

    // Then
    expect(create).toThrow(named);
  });

  test("two environment variables are enough to try the sandbox", async () => {
    // Given
    const provider = environmentSettingsProvider({
      NEVABRIDGE_API_KEY: API_KEY,
      NEVABRIDGE_PRODUCT_ID: PRODUCT_ID,
    });

    // When
    const settings = await provider.getSettings();

    // Then
    expect(settings.apiBaseUrl).toBe(SANDBOX_API_BASE_URL);
    expect(settings.productId).toBe(PRODUCT_ID);
    expect(settings.apiKey).toBe(API_KEY);
    expect(settings.webChatUrl).toBeUndefined();
  });

  test("optional environment variables set the API and web chat addresses", async () => {
    // Given
    const provider = environmentSettingsProvider({
      NEVABRIDGE_API_KEY: API_KEY,
      NEVABRIDGE_PRODUCT_ID: PRODUCT_ID,
      NEVABRIDGE_API_BASE_URL: "https://tenant.api.nevabridge.com",
      NEVABRIDGE_WEBCHAT_URL: "https://chat.nevabridge.com/v1/index.html",
    });

    // When
    const settings = await provider.getSettings();

    // Then
    expect(settings.apiBaseUrl).toBe("https://tenant.api.nevabridge.com");
    expect(settings.webChatUrl).toBe(
      "https://chat.nevabridge.com/v1/index.html",
    );
  });

  test.each(["NEVABRIDGE_API_KEY", "NEVABRIDGE_PRODUCT_ID"])(
    "name the missing environment variable %s",
    async (missing) => {
      // Given
      const variables: Record<string, string> = {
        NEVABRIDGE_API_KEY: API_KEY,
        NEVABRIDGE_PRODUCT_ID: PRODUCT_ID,
      };
      delete variables[missing];

      // When
      const result = environmentSettingsProvider(variables).getSettings();

      // Then
      await expect(result).rejects.toThrow(missing);
    },
  );

  test("round-trip through the JSON form a secret store keeps", () => {
    // Given
    const settings = createSettings({
      apiBaseUrl: SANDBOX_API_BASE_URL,
      productId: PRODUCT_ID,
      apiKey: API_KEY,
      webChatUrl: "https://chat.nevabridge.com/latest/index.html",
    });

    // When
    const restored = deserializeSettings(serializeSettings(settings));

    // Then
    expect(restored.apiBaseUrl).toBe(settings.apiBaseUrl);
    expect(restored.productId).toBe(settings.productId);
    expect(restored.apiKey).toBe(settings.apiKey);
    expect(restored.webChatUrl).toBe(settings.webChatUrl);
  });

  test.each([
    "not json",
    '{"apiBaseUrl":"https://tenant.api.sandbox.nevabridge.com","productId":"p"}',
  ])("explain unreadable stored settings: %s", (json) => {
    // Then
    expect(() => deserializeSettings(json)).toThrow(
      "stored NevaBridge settings",
    );
  });
});
