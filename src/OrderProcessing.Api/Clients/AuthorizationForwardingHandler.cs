using System.Net.Http.Headers;
using Microsoft.AspNetCore.Http;

namespace OrderProcessing.Api.Clients;

/// <summary>
/// Forwards the current inbound request's Authorization header onto Order's own internal
/// Inventory/Payment loopback calls. Now that every business endpoint requires a valid bearer token
/// (see Program.cs's fallback authorization policy), Inventory and Payments enforce that for these
/// server-to-server calls exactly like they would for any other caller — Order isn't a trusted
/// backdoor into them. There's no separate service-to-service credential in this design (see the
/// README's Authentication section): the caller who is already authorized to create the order is
/// the identity these downstream calls act under.
/// </summary>
public class AuthorizationForwardingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthorizationForwardingHandler(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var inboundAuthHeader = _httpContextAccessor.HttpContext?.Request.Headers.Authorization.ToString();

        if (!string.IsNullOrEmpty(inboundAuthHeader) && AuthenticationHeaderValue.TryParse(inboundAuthHeader, out var authHeader))
        {
            request.Headers.Authorization = authHeader;
        }

        return base.SendAsync(request, cancellationToken);
    }
}
