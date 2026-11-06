using Nyautomator.Streamlabs;

namespace Nyautomator.Modules.Streamlabs;

public sealed class StreamlabsModuleOptions
{
    public string? ClientId { get; set; }

    /// <summary>
    /// Streamlabs application client secret. Stored through module secret storage.
    /// </summary>
    public string? ClientSecret { get; set; }

    public string? RedirectUri { get; set; }

    public List<string>? Scopes { get; set; }

    public string? AuthBrowserPath { get; set; }

    public static StreamlabsModuleOptions CreateDefault()
        => new()
        {
            RedirectUri = StreamlabsOAuth.DefaultRedirectUri,
            Scopes = []
        };

    public bool HasClientCredentials
        => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public StreamlabsOAuthOptions ToOAuthOptions()
        => new(ClientId, ClientSecret, RedirectUri, Scopes ?? [], AuthBrowserPath);
}
