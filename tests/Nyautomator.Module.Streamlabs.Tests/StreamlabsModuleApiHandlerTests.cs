using Nyautomator;
using Nyautomator.Modules.Streamlabs;
using Nyautomator.Streamlabs;
using Xunit;
using static Nyautomator.Module.Streamlabs.Tests.TestSupport;

namespace Nyautomator.Module.Streamlabs.Tests;

public sealed class StreamlabsModuleApiHandlerTests
{
    [Fact]
    public async Task Status_ReportsLinkedAccount()
    {
        var token = new IntegrationToken { AccessToken = "token", AccountDisplayName = "Streamer", Scope = "donations.read alerts.create" };
        var handler = CreateHandler(new StreamlabsModuleOptions(), () => token);

        var response = await handler.HandleAsync(Request("streamlabs", "GET", "/status"), CancellationToken.None);

        var json = ToJson(response.Body);
        Assert.True(json.GetProperty("connected").GetBoolean());
        Assert.Equal("Streamer", json.GetProperty("displayName").GetString());
        Assert.Equal(2, json.GetProperty("scopes").GetArrayLength());
    }

    [Fact]
    public async Task Authorize_WithoutCredentials_DoesNotStartOAuth()
    {
        var started = false;
        var handler = new StreamlabsModuleApiHandler(
            new FakeOptionsProvider(new StreamlabsModuleOptions()),
            (_, _) => { started = true; return Task.FromResult(StreamlabsOAuthResult.CreateFailure("unexpected")); },
            () => null,
            () => { });

        var response = await handler.HandleAsync(Request("streamlabs", "POST", "/authorize"), CancellationToken.None);

        Assert.False(started);
        Assert.False(ToJson(response.Body).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Authorize_PassesModuleOptionsToOAuth()
    {
        StreamlabsOAuthOptions? received = null;
        var options = new StreamlabsModuleOptions { ClientId = "id", ClientSecret = "secret", Scopes = ["donations.read"] };
        var handler = new StreamlabsModuleApiHandler(
            new FakeOptionsProvider(options),
            (oauth, _) => { received = oauth; return Task.FromResult(StreamlabsOAuthResult.CreateSuccess(new IntegrationToken { AccessToken = "a" })); },
            () => null,
            () => { });

        var response = await handler.HandleAsync(Request("streamlabs", "POST", "/authorize"), CancellationToken.None);

        Assert.True(ToJson(response.Body).GetProperty("success").GetBoolean());
        Assert.NotNull(received);
        Assert.Equal("id", received!.ClientId);
        Assert.Equal(new[] { "donations.read" }, received.Scopes);
    }

    [Theory]
    [InlineData("DELETE", "/token")]
    [InlineData("POST", "/disconnect")]
    public async Task Disconnect_ClearsToken(string method, string path)
    {
        var cleared = false;
        var handler = new StreamlabsModuleApiHandler(new FakeOptionsProvider(null), (_, _) => throw new InvalidOperationException(), () => null, () => cleared = true);

        var response = await handler.HandleAsync(Request("streamlabs", method, path), CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.True(cleared);
    }

    private static StreamlabsModuleApiHandler CreateHandler(StreamlabsModuleOptions options, Func<IntegrationToken?> getToken)
        => new(new FakeOptionsProvider(options), (_, _) => throw new InvalidOperationException(), getToken, () => { });
}
