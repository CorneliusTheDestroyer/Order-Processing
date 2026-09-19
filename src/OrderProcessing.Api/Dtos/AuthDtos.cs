using System.ComponentModel.DataAnnotations;

namespace OrderProcessing.Api.Dtos;

/// <summary>Request body for POST /api/auth/token.</summary>
public class TokenRequest
{
    [Required(AllowEmptyStrings = false)]
    public string ClientId { get; set; } = string.Empty;

    [Required(AllowEmptyStrings = false)]
    public string ClientSecret { get; set; } = string.Empty;
}

/// <summary>Response body for a successful token request.</summary>
public class TokenResponse
{
    public string AccessToken { get; set; } = string.Empty;

    public string TokenType { get; set; } = "Bearer";

    public DateTime ExpiresAt { get; set; }
}
