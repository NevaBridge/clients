namespace NevaBridge.Chat;

/// <summary>
/// Which addresses may carry the API key or a page that the bridge trusts: https anywhere, plain
/// http only on this machine, where no network carries the traffic.
/// </summary>
public static class SecureAddress
{
    public static bool IsAllowed(Uri address) =>
        address.IsAbsoluteUri
        && (address.Scheme == Uri.UriSchemeHttps || (address.Scheme == Uri.UriSchemeHttp && address.IsLoopback));
}
