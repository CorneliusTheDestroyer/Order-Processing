using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OrderProcessing.Api.Configuration;
using OrderProcessing.Api.Services;

namespace OrderProcessing.Tests.Services;

public class TokenServiceTests
{
    private static JwtOptions CreateOptions() => new()
    {
        Issuer = "test-issuer",
        Audience = "test-audience",
        SigningKey = "unit-test-signing-key-at-least-32-bytes-long!!",
        ExpiryMinutes = 30,
        DemoClientId = "test-client",
        DemoClientSecret = "test-secret"
    };

    [Fact]
    public void IssueToken_ReturnsFailure_WhenClientIdDoesNotMatch()
    {
        var service = new TokenService(Options.Create(CreateOptions()), NullLogger<TokenService>.Instance);

        var result = service.IssueToken("wrong-client", "test-secret");

        Assert.False(result.Success);
        Assert.Null(result.Token);
    }

    [Fact]
    public void IssueToken_ReturnsFailure_WhenClientSecretDoesNotMatch()
    {
        var service = new TokenService(Options.Create(CreateOptions()), NullLogger<TokenService>.Instance);

        var result = service.IssueToken("test-client", "wrong-secret");

        Assert.False(result.Success);
        Assert.Null(result.Token);
    }

    [Fact]
    public void IssueToken_ReturnsSignedTokenWithExpectedClaims_WhenCredentialsMatch()
    {
        var options = CreateOptions();
        var service = new TokenService(Options.Create(options), NullLogger<TokenService>.Instance);

        var before = DateTime.UtcNow;
        var result = service.IssueToken(options.DemoClientId, options.DemoClientSecret);
        var after = DateTime.UtcNow;

        Assert.True(result.Success);
        Assert.NotNull(result.Token);
        Assert.False(string.IsNullOrWhiteSpace(result.Token!.AccessToken));
        Assert.Equal("Bearer", result.Token.TokenType);
        Assert.InRange(result.Token.ExpiresAt, before.AddMinutes(options.ExpiryMinutes), after.AddMinutes(options.ExpiryMinutes));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(result.Token.AccessToken);

        Assert.Equal(options.Issuer, jwt.Issuer);
        Assert.Contains(options.Audience, jwt.Audiences);
        Assert.Equal(options.DemoClientId, jwt.Subject);
    }
}
