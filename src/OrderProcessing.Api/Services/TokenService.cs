using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OrderProcessing.Api.Configuration;
using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Services;

/// <summary>
/// Validates the single demo client-credential pair (see JwtOptions and the README's Authentication
/// section — there's no real user/account system in this domain) and issues a signed JWT for it.
/// Stateless beyond its configured options, so it's registered as a Singleton — the same lifetime
/// as IPaymentGatewaySimulator.
/// </summary>
public class TokenService : ITokenService
{
    private readonly JwtOptions _options;
    private readonly ILogger<TokenService> _logger;

    public TokenService(IOptions<JwtOptions> options, ILogger<TokenService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public TokenIssueResult IssueToken(string clientId, string clientSecret)
    {
        // This is a single demo credential pair, not a real secret store, so a plain equality check
        // is appropriate — no need for the timing-attack hardening a real password/API-key check
        // would warrant.
        if (clientId != _options.DemoClientId || clientSecret != _options.DemoClientSecret)
        {
            _logger.LogWarning("Token request rejected for client id '{ClientId}': credentials did not match.", clientId);
            return TokenIssueResult.Failed("Invalid client credentials.");
        }

        var now = DateTime.UtcNow;
        var expiresAt = now.AddMinutes(_options.ExpiryMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, clientId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, ((DateTimeOffset)now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expiresAt,
            signingCredentials: credentials);

        var accessToken = new JwtSecurityTokenHandler().WriteToken(token);

        _logger.LogInformation("Issued token for client '{ClientId}', expiring at {ExpiresAt:o}.", clientId, expiresAt);

        return TokenIssueResult.Succeeded(new TokenResponse
        {
            AccessToken = accessToken,
            TokenType = "Bearer",
            ExpiresAt = expiresAt
        });
    }
}
