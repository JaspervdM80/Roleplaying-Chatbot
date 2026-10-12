namespace RoleplayStudio.AI.Providers;

public static class ProviderHttp
{
    // Not IHttpClientFactory: the service defaults put a 10-second resilience timeout on every client, far shorter than a generation.
    private static readonly SocketsHttpHandler Handler = new() { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };

    public static HttpClient Client(Uri? baseAddress = null) => new(Handler, disposeHandler: false) { BaseAddress = baseAddress };

    public static HttpClient Client(TimeSpan timeout, Uri? baseAddress = null)
    {
        var client = Client(baseAddress);
        client.Timeout = timeout;
        return client;
    }
}
