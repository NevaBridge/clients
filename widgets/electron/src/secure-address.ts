const LOOPBACK_HOSTS = new Set(["localhost", "127.0.0.1", "[::1]"]);

/**
 * Whether an address may carry the API key or a page the bridge trusts: https anywhere, plain http
 * only on this machine, where no network carries the traffic.
 */
export function isSecureAddress(address: string): boolean {
  let url: URL;
  try {
    url = new URL(address);
  } catch {
    return false;
  }
  return (
    url.protocol === "https:" ||
    (url.protocol === "http:" && LOOPBACK_HOSTS.has(url.hostname))
  );
}

/** scheme://host:port, the unit browsers use for same-origin decisions. */
export function originOf(address: string): string {
  return new URL(address).origin;
}
