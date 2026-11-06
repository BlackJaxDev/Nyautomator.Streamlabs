using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Nyautomator.OAuth;

namespace Nyautomator.Streamlabs;

public static class StreamlabsOAuth
{
    public const string TokenKey = "streamlabs";
    public const string DefaultRedirectUri = "http://localhost:8082/streamlabs/callback";
    private static readonly Uri AuthorizeEndpoint = new("https://streamlabs.com/api/v2.0/authorize");
    private static readonly Uri TokenEndpoint = new("https://streamlabs.com/api/v2.0/token");
    private static readonly Uri UserEndpoint = new("https://streamlabs.com/api/v2.0/user");
    private static readonly TimeSpan AuthorizationTimeout = TimeSpan.FromMinutes(5);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        try
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Nyautomator/1.0 (+https://github.com/BlackJaxDev/Nyautomator)");
        }
        catch
        {
            // Ignore user-agent parse errors.
        }
        return client;
    }

    public static IntegrationToken? GetToken() => IntegrationTokenStore.Get(TokenKey);

    public static void ClearToken() => IntegrationTokenStore.Clear(TokenKey);

    public static async Task<StreamlabsOAuthResult> AuthorizeAsync(StreamlabsOAuthOptions options, CancellationToken cancellationToken = default)
    {
        if (options is null)
            throw new ArgumentNullException(nameof(options));

        var clientId = options.ClientId?.Trim();
        var clientSecret = options.ClientSecret?.Trim();
        var redirect = string.IsNullOrWhiteSpace(options.RedirectUri)
            ? DefaultRedirectUri
            : options.RedirectUri.Trim();

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            return StreamlabsOAuthResult.CreateFailure("Configure Streamlabs client ID and client secret before starting OAuth.");

        if (!Uri.TryCreate(redirect, UriKind.Absolute, out var redirectUri))
            return StreamlabsOAuthResult.CreateFailure("Streamlabs redirect URI must be an absolute URI.");

        var scopeList = NormalizeScopes(options.Scopes);
        var scopeParam = scopeList.Length > 0 ? string.Join(' ', scopeList) : string.Empty;
        var state = Guid.NewGuid().ToString("n");

        var authUrl = BuildAuthorizeUrl(clientId, redirectUri, scopeParam, state);
        LaunchBrowser(authUrl, options.AuthBrowserPath);

        var callback = await OAuthCallbackListener
            .WaitForCodeAsync(redirectUri, state, AuthorizationTimeout, cancellationToken)
            .ConfigureAwait(false);

        if (callback.TimedOut)
            return StreamlabsOAuthResult.CreateFailure("Streamlabs authorization timed out. Try again.");

        if (!string.IsNullOrEmpty(callback.Error))
        {
            var message = string.IsNullOrEmpty(callback.ErrorDescription)
                ? callback.Error
                : callback.ErrorDescription;
            return StreamlabsOAuthResult.CreateFailure($"Streamlabs authorization failed: {message}.");
        }

        if (string.IsNullOrEmpty(callback.Code))
            return StreamlabsOAuthResult.CreateFailure("Streamlabs did not return an authorization code.");

        var token = await ExchangeCodeAsync(clientId, clientSecret, redirectUri.ToString(), callback.Code, scopeParam, cancellationToken)
            .ConfigureAwait(false);
        if (token is null)
            return StreamlabsOAuthResult.CreateFailure("Unable to exchange the authorization code for Streamlabs tokens.");

        await PopulateUserMetadataAsync(token, cancellationToken).ConfigureAwait(false);
        IntegrationTokenStore.Set(TokenKey, token);

        return StreamlabsOAuthResult.CreateSuccess(token.Clone());
    }

    private static string BuildAuthorizeUrl(string clientId, Uri redirectUri, string scope, string state)
    {
        var query = new Dictionary<string, string?>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = redirectUri.ToString(),
            ["response_type"] = "code",
            ["state"] = state,
            ["scope"] = string.IsNullOrWhiteSpace(scope) ? null : scope
        };

        var queryString = QueryStringBuilder.Build(query);
        return new UriBuilder(AuthorizeEndpoint)
        {
            Query = queryString
        }.Uri.ToString();
    }

    private static async Task<IntegrationToken?> ExchangeCodeAsync(string clientId, string clientSecret, string redirectUri, string code, string requestedScope, CancellationToken cancellationToken)
    {
        var payload = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
            ["code"] = code
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(payload)
        };

        using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            Trace.WriteLine($"Streamlabs token exchange failed: {(int)response.StatusCode} {response.ReasonPhrase}");
            return null;
        }

        try
        {
            var content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var token = await JsonSerializer.DeserializeAsync<TokenResponse>(content, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (token?.AccessToken is null)
                return null;

            var expiresAt = token.ExpiresIn.HasValue
                ? DateTime.UtcNow.AddSeconds(Math.Max(30, token.ExpiresIn.Value))
                : (DateTime?)null;

            return new IntegrationToken
            {
                AccessToken = token.AccessToken,
                RefreshToken = token.RefreshToken,
                TokenType = string.IsNullOrWhiteSpace(token.TokenType) ? "Bearer" : token.TokenType,
                Scope = string.IsNullOrWhiteSpace(token.Scope) ? requestedScope : token.Scope,
                ExpiresAtUtc = expiresAt,
                Metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["redirectUri"] = redirectUri
                }
            };
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to parse Streamlabs token response: {ex.Message}");
            return null;
        }
    }

    private static async Task PopulateUserMetadataAsync(IntegrationToken token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token.AccessToken))
            return;

        using var request = new HttpRequestMessage(HttpMethod.Get, UserEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return;

            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("streamlabs", out var userElement))
                return;

            if (userElement.TryGetProperty("id", out var idProp) && idProp.ValueKind == JsonValueKind.Number)
            {
                token.AccountId = idProp.GetRawText();
            }

            if (userElement.TryGetProperty("username", out var usernameProp) && usernameProp.ValueKind == JsonValueKind.String)
            {
                token.AccountLogin = usernameProp.GetString();
            }

            if (userElement.TryGetProperty("display_name", out var displayNameProp) && displayNameProp.ValueKind == JsonValueKind.String)
            {
                token.AccountDisplayName = displayNameProp.GetString();
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to load Streamlabs user profile: {ex.Message}");
        }
    }

    private static string[] NormalizeScopes(IEnumerable<string>? scopes)
    {
        if (scopes is null)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var scope in scopes)
        {
            var trimmed = scope?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            if (!list.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                list.Add(trimmed);
        }
        return list.ToArray();
    }

    private static void LaunchBrowser(string url, string? browserPath)
    {
        if (string.IsNullOrWhiteSpace(url))
            return;

        try
        {
            if (string.Equals(browserPath?.Trim(), "__system__", StringComparison.Ordinal))
                browserPath = null;

            var psi = string.IsNullOrWhiteSpace(browserPath)
                ? new ProcessStartInfo { FileName = url, UseShellExecute = true }
                : new ProcessStartInfo { FileName = browserPath.Trim(), Arguments = $"\"{url}\"", UseShellExecute = false };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Failed to launch browser for Streamlabs OAuth: {ex.Message}");
        }
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] int? ExpiresIn,
        [property: JsonPropertyName("scope")] string? Scope
    );
}

/// <summary>
/// OAuth client settings supplied by the Streamlabs module configuration.
/// </summary>
public sealed record StreamlabsOAuthOptions(
    string? ClientId,
    string? ClientSecret,
    string? RedirectUri,
    IReadOnlyList<string>? Scopes,
    string? AuthBrowserPath = null);

public sealed class StreamlabsOAuthResult
{
    private StreamlabsOAuthResult(bool success, string? message, IntegrationToken? token)
    {
        Success = success;
        Message = message;
        Token = token;
    }

    public bool Success { get; }
    public string? Message { get; }
    public IntegrationToken? Token { get; }

    public static StreamlabsOAuthResult CreateSuccess(IntegrationToken token)
        => new StreamlabsOAuthResult(true, null, token);

    public static StreamlabsOAuthResult CreateFailure(string message)
        => new StreamlabsOAuthResult(false, string.IsNullOrWhiteSpace(message) ? "Streamlabs authorization failed." : message.Trim(), null);
}
