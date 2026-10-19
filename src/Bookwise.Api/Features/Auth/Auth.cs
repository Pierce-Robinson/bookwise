using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text;
using Bookwise.Api.Data;
using Bookwise.Api.Domain;
using Bookwise.Api.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bookwise.Api.Features.Auth;

public record LoginRequest([Required] string Email, [Required] string Password);

public record TokenResponse(string AccessToken, string TokenType, int ExpiresIn);

[ApiController]
[Route("api/auth")]
[AllowAnonymous]
[Tags("Authentication")]
public class AuthController(
    BookwiseDbContext db,
    IPasswordHasher<AdminUser> hasher,
    IOptions<JwtOptions> jwtOptions) : ControllerBase
{
    /// <summary>Log in as an admin to get a JWT for the /api/admin endpoints.</summary>
    [HttpPost("login")]
    [ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var admin = await db.AdminUsers.FirstOrDefaultAsync(a => a.Email == email, ct);
        if (admin is null || hasher.VerifyHashedPassword(admin, admin.PasswordHash, request.Password)
                == PasswordVerificationResult.Failed)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authentication failed",
                detail: "Invalid email or password.");
        }

        var jwt = jwtOptions.Value;
        // Token lifetimes are checked against the real clock by the JWT handler, so they're
        // issued from the real clock too (not the injectable TimeProvider used for bookings).
        var now = DateTime.UtcNow;
        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, admin.Email), new Claim("role", "admin")]),
            NotBefore = now,
            Expires = now.AddMinutes(jwt.ExpiryMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256),
        });

        return Ok(new TokenResponse(token, "Bearer", jwt.ExpiryMinutes * 60));
    }
}
