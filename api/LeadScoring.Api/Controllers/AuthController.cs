using System.Globalization;
using System.Security.Claims;
using LeadScoring.Api.Contracts;
using LeadScoring.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LeadScoring.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpGet("plans")]
    [AllowAnonymous]
    public ActionResult<AuthPlansResponse> GetPlans()
    {
        return Ok(new AuthPlansResponse(authService.GetPlans()));
    }

    [HttpPost("signup")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Signup([FromBody] SignupRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await authService.SignupAsync(request, cancellationToken);
            return Ok(response);
        }
        catch (AuthValidationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        if (response is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        return Ok(response);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthUserDto>> Me(CancellationToken cancellationToken)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (!Guid.TryParse(sub, out var userId))
        {
            return Unauthorized();
        }

        var user = await authService.GetCurrentUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Unauthorized();
        }

        return Ok(user);
    }
}
