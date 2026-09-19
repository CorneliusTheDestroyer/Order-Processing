namespace OrderProcessing.Api.Configuration;

/// <summary>
/// Bound from the "Jwt" section of appsettings.json via the Options pattern — mirrors
/// <see cref="PaymentOptions"/>. TokenService (issuing tokens) and Program.cs's JwtBearer
/// validation setup both read from this same bound instance, so issuer/audience/signing key can
/// never drift out of sync between the two sides.
/// </summary>
public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;

    public string Audience { get; set; } = string.Empty;

    /// <summary>HMAC-SHA256 requires a key of at least 256 bits (32 bytes) — Program.cs fails fast
    /// at startup if this is ever shortened below that, rather than throwing an opaque exception on
    /// the first token request.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public int ExpiryMinutes { get; set; } = 60;

    /// <summary>There's no real user/account system in this domain — CustomerId on an order is a
    /// free-text string, not an account. A single demo client-credential pair, config-driven like
    /// everything else here, stands in for one so the JWT mechanism has something real to
    /// authenticate against without inventing an identity system unrelated to what's being
    /// assessed. See the README's Authentication section and Trade-offs.</summary>
    public string DemoClientId { get; set; } = string.Empty;

    public string DemoClientSecret { get; set; } = string.Empty;
}
