using Nyautomator;
using Nyautomator.Module.Abstractions;
using Nyautomator.Runtime.Abstractions;
using Nyautomator.Streamlabs;
using static Nyautomator.Modules.Streamlabs.ModuleHttp;

namespace Nyautomator.Modules.Streamlabs;

/// <summary>
/// Serves <c>/modules/streamlabs/api/*</c>: token status, OAuth authorization, and token removal.
/// </summary>
public sealed class StreamlabsModuleApiHandler : IModuleApiHandler
{
    public const string ModuleIdValue = "streamlabs";

    private readonly IModuleOptionsProvider? _options;
    private readonly Func<StreamlabsOAuthOptions, CancellationToken, Task<StreamlabsOAuthResult>> _authorize;
    private readonly Func<IntegrationToken?> _getToken;
    private readonly Action _clearToken;

    public StreamlabsModuleApiHandler(IModuleOptionsProvider? options)
        : this(options, StreamlabsOAuth.AuthorizeAsync, StreamlabsOAuth.GetToken, StreamlabsOAuth.ClearToken)
    {
    }

    internal StreamlabsModuleApiHandler(
        IModuleOptionsProvider? options,
        Func<StreamlabsOAuthOptions, CancellationToken, Task<StreamlabsOAuthResult>> authorize,
        Func<IntegrationToken?> getToken,
        Action clearToken)
    {
        _options = options;
        _authorize = authorize;
        _getToken = getToken;
        _clearToken = clearToken;
    }

    public string ModuleId => ModuleIdValue;

    public StreamlabsModuleOptions GetEffectiveOptions()
        => _options?.Get(ModuleIdValue, StreamlabsModuleOptions.CreateDefault()) ?? StreamlabsModuleOptions.CreateDefault();

    public async Task<ModuleApiResponse> HandleAsync(ModuleApiRequest request, CancellationToken cancellationToken)
    {
        var path = NormalizePath(request.Path);
        try
        {
            return path switch
            {
                "status" when IsMethod(request, "GET") => Ok(BuildStatus()),
                "authorize" or "connect" when IsMethod(request, "POST") => await AuthorizeAsync(cancellationToken).ConfigureAwait(false),
                "token" when IsMethod(request, "DELETE") => Disconnect(),
                "disconnect" when IsMethod(request, "POST") => Disconnect(),
                _ => NotFound("Streamlabs", path)
            };
        }
        catch (OperationCanceledException)
        {
            return Error("Streamlabs request cancelled or timed out.", 504);
        }
        catch (Exception ex)
        {
            return Error($"Streamlabs request failed: {ex.Message}", 500);
        }
    }

    internal object BuildStatus()
    {
        var token = _getToken();
        var options = GetEffectiveOptions();
        var connected = !string.IsNullOrWhiteSpace(token?.AccessToken);

        return new
        {
            success = true,
            connected,
            configured = options.HasClientCredentials,
            displayName = token?.AccountDisplayName,
            accountLogin = token?.AccountLogin,
            account = new
            {
                id = token?.AccountId,
                login = token?.AccountLogin,
                displayName = token?.AccountDisplayName
            },
            tokenType = token?.TokenType,
            scopes = ParseScopes(token?.Scope),
            scope = token?.Scope,
            expiresAtUtc = token?.ExpiresAtUtc,
            updatedAtUtc = token?.UpdatedAtUtc,
            hasRefreshToken = !string.IsNullOrWhiteSpace(token?.RefreshToken),
            message = connected
                ? null
                : options.HasClientCredentials
                    ? "Streamlabs credentials saved; account not linked."
                    : "Add a Streamlabs client ID and secret in Settings."
        };
    }

    private async Task<ModuleApiResponse> AuthorizeAsync(CancellationToken cancellationToken)
    {
        var options = GetEffectiveOptions();
        if (!options.HasClientCredentials)
            return Ok(new { success = false, message = "Configure Streamlabs client ID and client secret before starting OAuth." });

        var result = await _authorize(options.ToOAuthOptions(), cancellationToken).ConfigureAwait(false);
        return Ok(new
        {
            success = result.Success,
            message = result.Message,
            token = ToTokenPayload(result.Token)
        });
    }

    private ModuleApiResponse Disconnect()
    {
        _clearToken();
        return Ok(new { success = true, message = "Streamlabs tokens cleared." });
    }

    internal static string[] ParseScopes(string? scope)
        => string.IsNullOrWhiteSpace(scope)
            ? []
            : scope
                .Split([' ', ',', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(static value => value.Trim())
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    private static object? ToTokenPayload(IntegrationToken? token)
        => token is null
            ? null
            : new
            {
                token.TokenType,
                token.Scope,
                scopes = ParseScopes(token.Scope),
                token.AccountId,
                token.AccountLogin,
                token.AccountDisplayName,
                token.ExpiresAtUtc,
                token.UpdatedAtUtc
            };
}
