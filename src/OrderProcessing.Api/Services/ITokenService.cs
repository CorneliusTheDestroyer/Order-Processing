using OrderProcessing.Api.Dtos;

namespace OrderProcessing.Api.Services;

public interface ITokenService
{
    /// <summary>Validates the given credentials against the configured demo client and, if they
    /// match, issues a signed JWT. Deliberately returns its own small result type rather than the
    /// shared OperationResult/OperationOutcome the business services use — "bad credentials"
    /// doesn't conceptually belong next to Inventory/Order/Payment's business outcomes, and keeping
    /// it separate contains auth's blast radius to its own code.</summary>
    TokenIssueResult IssueToken(string clientId, string clientSecret);
}

/// <summary>Outcome of a token issuance attempt.</summary>
public class TokenIssueResult
{
    public bool Success { get; }

    public TokenResponse? Token { get; }

    public string? Error { get; }

    private TokenIssueResult(bool success, TokenResponse? token, string? error)
    {
        Success = success;
        Token = token;
        Error = error;
    }

    public static TokenIssueResult Succeeded(TokenResponse token) => new(true, token, null);

    public static TokenIssueResult Failed(string error) => new(false, null, error);
}
