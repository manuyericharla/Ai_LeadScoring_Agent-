using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using LeadScoring.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace LeadScoring.Api.Services;

public class JwtAuthTokenService(IConfiguration configuration)
{
    private readonly byte[] _key = Encoding.UTF8.GetBytes(
        configuration["Auth:JwtSigningKey"]
        ?? configuration["Tracking:SigningKey"]
        ?? throw new InvalidOperationException("Auth:JwtSigningKey or Tracking:SigningKey is required."));

    public string CreateToken(AppUser user, Tenant tenant)
    {
        var expiryHours = configuration.GetValue("Auth:TokenExpiryHours", 24);
        var issuer = configuration["Auth:JwtIssuer"] ?? "LeadScoring";
        var audience = configuration["Auth:JwtAudience"] ?? "LeadScoring.App";

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D", CultureInfo.InvariantCulture)),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim("given_name", user.FirstName),
            new Claim("family_name", user.LastName),
            new Claim("company", tenant.CompanyName),
            new Claim("tenant_id", tenant.Id.ToString("D", CultureInfo.InvariantCulture)),
            new Claim("tenant_db", tenant.DatabaseName),
            new Claim("plan", tenant.SelectedPlan)
        };

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(expiryHours),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(_key),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
