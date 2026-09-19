using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderProcessing.Api.Dtos;
using OrderProcessing.Api.Services;

namespace OrderProcessing.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ApiControllerBase
{
    private readonly ITokenService _tokenService;

    public AuthController(ITokenService tokenService)
    {
        _tokenService = tokenService;
    }

    /// <summary>POST /api/auth/token — exchanges the demo client credentials (see JwtOptions and
    /// the README) for a short-lived bearer token. The only anonymous endpoint in the API; every
    /// other endpoint requires the resulting token via the global fallback authorization policy in
    /// Program.cs.</summary>
    [HttpPost("token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult IssueToken([FromBody] TokenRequest request)
    {
        var result = _tokenService.IssueToken(request.ClientId, request.ClientSecret);

        if (!result.Success)
        {
            return ProblemResult(result.Error!, StatusCodes.Status401Unauthorized);
        }

        return Ok(result.Token);
    }
}
