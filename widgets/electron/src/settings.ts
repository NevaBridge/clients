import {isSecureAddress} from "./secure-address.js";

export const SANDBOX_API_BASE_URL = "https://tenant.api.sandbox.nevabridge.com";
export const PRODUCTION_API_BASE_URL = "https://tenant.api.nevabridge.com";

/**
 * Everything the chat needs to reach NevaBridge. `webChatUrl` is where the web chat page is loaded
 * from; absent means the page shipped with the application.
 */
export interface NevaBridgeSettings {
  readonly apiBaseUrl: string;
  readonly productId: string;
  /** Not enumerable, so logging or serializing the settings object does not reveal it. */
  readonly apiKey: string;
  readonly webChatUrl?: string;
}

export interface NevaBridgeSettingsInput {
  readonly apiBaseUrl: string;
  readonly productId: string;
  readonly apiKey: string;
  readonly webChatUrl?: string | null;
}

/**
 * Supplies the NevaBridge settings to the chat. Implement it to read the settings wherever your
 * application keeps its configuration; the environment provider and the safeStorage store are
 * ready-made implementations.
 */
export interface NevaBridgeSettingsProvider {
  getSettings(): Promise<NevaBridgeSettings>;
}

/** The NevaBridge settings are missing or unusable. The message says which one and why. */
export class NevaBridgeSettingsError extends Error {
  public constructor(message: string, options?: {cause?: unknown}) {
    super(message, options);
    this.name = "NevaBridgeSettingsError";
  }
}

/** Validates the settings: https addresses (plain http only on this machine), a product and a key. */
export function createSettings(
  input: NevaBridgeSettingsInput,
): NevaBridgeSettings {
  // The API address carries the key and the web chat page is trusted by the bridge, so neither
  // may travel unencrypted over a network.
  if (!isSecureAddress(input.apiBaseUrl)) {
    throw new NevaBridgeSettingsError(
      `The API base URL must use https (plain http only on this machine), not '${input.apiBaseUrl}'.`,
    );
  }
  const webChatUrl = input.webChatUrl?.trim()
    ? input.webChatUrl.trim()
    : undefined;
  if (webChatUrl !== undefined && !isSecureAddress(webChatUrl)) {
    throw new NevaBridgeSettingsError(
      `The web chat URL must use https (plain http only on this machine), not '${webChatUrl}'.`,
    );
  }
  if (!input.productId.trim()) {
    throw new NevaBridgeSettingsError("The NevaBridge product id is missing.");
  }
  if (!input.apiKey.trim()) {
    throw new NevaBridgeSettingsError("The NevaBridge API key is missing.");
  }

  const settings = {
    apiBaseUrl: input.apiBaseUrl.trim(),
    productId: input.productId.trim(),
    ...(webChatUrl === undefined ? {} : {webChatUrl}),
  };
  Object.defineProperty(settings, "apiKey", {
    value: input.apiKey.trim(),
    enumerable: false,
  });
  return Object.freeze(settings) as NevaBridgeSettings;
}

/**
 * Reads the settings from environment variables: the quickest way to try the chat with a
 * temporary sandbox key. NEVABRIDGE_API_KEY and NEVABRIDGE_PRODUCT_ID are required;
 * NEVABRIDGE_API_BASE_URL defaults to the sandbox and NEVABRIDGE_WEBCHAT_URL to the bundled page.
 */
export function environmentSettingsProvider(
  variables: Readonly<Record<string, string | undefined>> = process.env,
): NevaBridgeSettingsProvider {
  return {
    async getSettings(): Promise<NevaBridgeSettings> {
      const required = (name: string): string => {
        const value = variables[name];
        if (!value?.trim()) {
          throw new NevaBridgeSettingsError(
            `The environment variable ${name} is not set.`,
          );
        }
        return value;
      };
      return createSettings({
        apiKey: required("NEVABRIDGE_API_KEY"),
        productId: required("NEVABRIDGE_PRODUCT_ID"),
        apiBaseUrl:
          variables["NEVABRIDGE_API_BASE_URL"]?.trim() || SANDBOX_API_BASE_URL,
        webChatUrl: variables["NEVABRIDGE_WEBCHAT_URL"],
      });
    },
  };
}

/**
 * The JSON form in which a secret store keeps all four settings together, so the web chat address
 * travels with the key. The same form as the .NET packages use.
 */
export function serializeSettings(settings: NevaBridgeSettings): string {
  return JSON.stringify({
    apiBaseUrl: settings.apiBaseUrl,
    productId: settings.productId,
    apiKey: settings.apiKey,
    webChatUrl: settings.webChatUrl,
  });
}

export function deserializeSettings(json: string): NevaBridgeSettings {
  let stored: unknown;
  try {
    stored = JSON.parse(json);
  } catch (error: unknown) {
    throw new NevaBridgeSettingsError(
      "The stored NevaBridge settings are not valid JSON.",
      {cause: error},
    );
  }
  const value = (stored ?? {}) as Record<string, unknown>;
  const text = (name: string): string | undefined =>
    typeof value[name] === "string" ? (value[name] as string) : undefined;
  const apiBaseUrl = text("apiBaseUrl");
  const productId = text("productId");
  const apiKey = text("apiKey");
  if (
    apiBaseUrl === undefined ||
    productId === undefined ||
    apiKey === undefined
  ) {
    throw new NevaBridgeSettingsError(
      "The stored NevaBridge settings are incomplete: apiBaseUrl, productId and apiKey are required.",
    );
  }
  return createSettings({
    apiBaseUrl,
    productId,
    apiKey,
    webChatUrl: text("webChatUrl"),
  });
}
