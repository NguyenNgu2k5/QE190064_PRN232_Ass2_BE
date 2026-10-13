using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTrack.Service.Dtos;
using TaskTrack.Service.Services;

namespace TaskTrack.API.Controllers;

[ApiController, Route("api/auth")]
public class AuthController(AccountService service, TokenIssuer tokens) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken ct) => StatusCode(201, await service.RegisterAsync(request, ct));

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var account = await service.LoginAsync(request, ct);
        var session = account is null ? null : await tokens.IssueAsync(account, ct);
        return session is null ? Unauthorized(new { message = "Invalid email or password." }) : Ok(session);
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(RefreshRequest request, CancellationToken ct)
    {
        var session = await tokens.RefreshAsync(request.RefreshToken, ct);
        return session is null ? Unauthorized(new { message = "Session expired. Sign in again." }) : Ok(session);
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(RefreshRequest request, CancellationToken ct)
    {
        await tokens.LogoutAsync(request.RefreshToken, ct);
        return NoContent();
    }

    [Authorize, HttpPut("profile")]
    public async Task<IActionResult> Profile(ProfileRequest request, CancellationToken ct) => Ok(await service.UpdateProfileAsync(int.Parse(User.FindFirstValue("AccountID")!), request, ct));

    [Authorize, HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct) => Ok(await service.GetAsync(int.Parse(User.FindFirstValue("AccountID")!), ct));
}
