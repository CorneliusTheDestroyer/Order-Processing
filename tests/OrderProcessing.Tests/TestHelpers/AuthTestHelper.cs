using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Tests.TestHelpers;

/// <summary>
/// Fetches a real access token from the running instance's POST /api/auth/token endpoint and
/// attaches it to the given client. Used by every OrdersEndToEndTests case so the integration tests
/// exercise the real auth flow rather than bypassing it — consistent with this project's existing
/// preference for real end-to-end behavior over test-only shortcuts (see the loopback-HTTP client
/// re-pointing in OrdersEndToEndTests.CreateFactory).
/// </summary>
internal static class AuthTestHelper
{
    // Must match appsettings.json's Jwt:DemoClientId/DemoClientSecret — WebApplicationFactory loads
    // the real appsettings.json, so these are the real demo credentials, not test-only stand-ins.
    private const string DemoClientId = "demo-client";
    private const string DemoClientSecret = "demo-secret";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/token", new
        {
            clientId = DemoClientId,
            clientSecret = DemoClientSecret
        }, JsonOptions);

        response.EnsureSuccessStatusCode();

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(JsonOptions);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token!.AccessToken);
    }
}
