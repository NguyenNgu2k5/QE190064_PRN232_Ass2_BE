using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using TaskTrack.Service.Dtos;
using TaskTrack.Repo.Models;
using TaskTrack.Repo.Repositories;
using TaskTrack.Service.Services;

namespace TaskTrack.API;

public class TokenIssuer(IConfiguration configuration, AccountRepository repo)
{
    public const string Issuer = "TaskTrack.API";
    public const string Audience = "TaskTrack.Web";
    private const string RefreshAudience = Audience + ".Refresh";
    public static string RoleName(short role) => role == 1 ? "Admin" : "Staff";
    public static SymmetricSecurityKey Key(IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"];
        if (string.IsNullOrWhiteSpace(secret) || Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Configure JWT_SECRET with at least 32 bytes outside source control.");
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    }

    // shortcut: one refresh session per account; use a session table when multiple device sessions are required.
    public async Task<SessionResponse?> IssueAsync(AccountResponse response, CancellationToken ct)
    {
        var account = await repo.FindAsync(response.AccountId, ct);
        if (account is null) return null;
        var family = Guid.NewGuid();
        var expires = DateTime.UtcNow.AddDays(7);
        var refresh = RefreshToken(account.AccountId, account.TokenVersion, family, expires);
        return await repo.StartSessionAsync(account, Hash(refresh), expires, family, ct) == 1 ? Session(account, refresh) : null;
    }

    public async Task<SessionResponse?> RefreshAsync(string token, CancellationToken ct)
    {
        var identity = ValidateRefresh(token);
        if (identity is null) return null;
        var (id, version, family) = identity.Value;
        var account = await repo.ReadAsync(id, ct);
        if (account is null || account.TokenVersion != version || account.RefreshTokenFamilyId != family || account.RefreshTokenExpiresAt <= DateTime.UtcNow || account.RefreshTokenExpiresAt is null) return null;
        var next = RefreshToken(id, version, family, account.RefreshTokenExpiresAt.Value);
        if (await repo.RotateAsync(id, version, family, Hash(token), Hash(next), ct) == 1) return Session(account, next);
        // Reuse of a signed token from the current family revokes that family, including the rotated token.
        await repo.RevokeAsync(id, version, family, ct);
        return null;
    }

    public async Task LogoutAsync(string token, CancellationToken ct)
    {
        var identity = ValidateRefresh(token);
        if (identity is { } value) await repo.RevokeAsync(value.Id, value.Version, value.Family, ct);
    }

    private SessionResponse Session(SystemAccount account, string refresh)
    {
        var expires = DateTime.UtcNow.AddMinutes(15);
        var claims = new[] { new Claim("AccountID", account.AccountId.ToString()), new Claim("TokenVersion", account.TokenVersion.ToString()), new Claim("Email", account.Email), new Claim("Role", RoleName(account.Role)), new Claim("FullName", account.FullName) };
        return new(Write(Audience, claims, expires), expires, refresh, AccountService.Map(account));
    }

    private string RefreshToken(int id, int version, Guid family, DateTime expires) => Write(RefreshAudience,
        [new Claim("AccountID", id.ToString()), new Claim("TokenVersion", version.ToString()), new Claim("FamilyID", family.ToString())], expires);

    private string Write(string audience, IEnumerable<Claim> claims, DateTime expires)
    {
        var token = new JwtSecurityToken(Issuer, audience, claims.Append(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())),
            expires: expires, signingCredentials: new SigningCredentials(Key(configuration), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private (int Id, int Version, Guid Family)? ValidateRefresh(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            var principal = handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true, IssuerSigningKey = Key(configuration), ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = true, ValidIssuer = Issuer, ValidateAudience = true, ValidAudience = RefreshAudience,
                ValidateLifetime = true, RequireExpirationTime = true, ClockSkew = TimeSpan.Zero
            }, out _);
            return int.TryParse(principal.FindFirstValue("AccountID"), out var id) && int.TryParse(principal.FindFirstValue("TokenVersion"), out var version) &&
                Guid.TryParse(principal.FindFirstValue("FamilyID"), out var family) ? (id, version, family) : null;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException) { return null; }
    }

    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public record SessionResponse(string Token, DateTime ExpiresAt, string RefreshToken, AccountResponse Account);
