using LeadScoring.Api.Contracts;
using LeadScoring.Api.Data;
using LeadScoring.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LeadScoring.Api.Services;

public class AuthService(
    MasterDbContext masterDb,
    ITenantDatabaseProvisioner provisioner,
    JwtAuthTokenService jwtAuthTokenService,
    IConfiguration configuration) : IAuthService
{
    private readonly PasswordHasher<AppUser> _passwordHasher = new();

    public IReadOnlyList<string> GetPlans()
    {
        return configuration.GetSection("Auth:Plans").Get<string[]>()
            ?? ["Starter", "Professional", "Enterprise"];
    }

    public async Task<AuthResponse> SignupAsync(SignupRequest request, CancellationToken cancellationToken = default)
    {
        var firstName = request.FirstName?.Trim() ?? "";
        var lastName = request.LastName?.Trim() ?? "";
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var company = request.Company?.Trim() ?? "";
        var plan = request.Plan?.Trim() ?? "";
        var password = request.Password ?? "";
        var confirmPassword = request.ConfirmPassword ?? "";

        if (firstName.Length < 1 || lastName.Length < 1)
        {
            throw new AuthValidationException("First name and last name are required.");
        }

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            throw new AuthValidationException("A valid email address is required.");
        }

        if (company.Length < 2)
        {
            throw new AuthValidationException("Company name must be at least 2 characters.");
        }

        if (password.Length < 8)
        {
            throw new AuthValidationException("Password must be at least 8 characters.");
        }

        if (!string.Equals(password, confirmPassword, StringComparison.Ordinal))
        {
            throw new AuthValidationException("Password and confirm password do not match.");
        }

        var plans = GetPlans();
        if (!plans.Contains(plan, StringComparer.OrdinalIgnoreCase))
        {
            throw new AuthValidationException("Please select a valid plan.");
        }

        if (await masterDb.Users.AnyAsync(u => u.Email == email, cancellationToken))
        {
            throw new AuthValidationException("An account with this email already exists.");
        }

        var normalizedCompany = company;
        if (await masterDb.Tenants.AnyAsync(
                t => EF.Functions.ILike(t.CompanyName, normalizedCompany),
                cancellationToken))
        {
            throw new AuthValidationException("A company with this name is already registered.");
        }

        var schemaName = TenantConnectionStringBuilder.ToSchemaName(company);
        if (await masterDb.Tenants.AnyAsync(t => t.DatabaseName == schemaName, cancellationToken))
        {
            throw new AuthValidationException("This company name cannot be used. Try a slightly different name.");
        }

        await provisioner.ProvisionAsync(schemaName, cancellationToken);

        var tenant = new Tenant
        {
            Id = Guid.NewGuid(),
            CompanyName = company,
            DatabaseName = schemaName,
            SelectedPlan = plan,
            CreatedAtUtc = DateTime.UtcNow
        };

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            TenantId = tenant.Id,
            CreatedAtUtc = DateTime.UtcNow
        };
        user.PasswordHash = _passwordHasher.HashPassword(user, password);

        masterDb.Tenants.Add(tenant);
        masterDb.Users.Add(user);
        await masterDb.SaveChangesAsync(cancellationToken);

        var token = jwtAuthTokenService.CreateToken(user, tenant);
        return new AuthResponse(token, ToDto(user, tenant));
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email?.Trim().ToLowerInvariant() ?? "";
        var password = request.Password ?? "";

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return null;
        }

        var user = await masterDb.Users
            .Include(u => u.Tenant)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

        if (user is null)
        {
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        var token = jwtAuthTokenService.CreateToken(user, user.Tenant);
        return new AuthResponse(token, ToDto(user, user.Tenant));
    }

    public async Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await masterDb.Users
            .Include(u => u.Tenant)
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        return user is null ? null : ToDto(user, user.Tenant);
    }

    private static AuthUserDto ToDto(AppUser user, Tenant tenant) =>
        new(user.Id, user.Email, user.FirstName, user.LastName, tenant.CompanyName, tenant.SelectedPlan);
}

public class AuthValidationException(string message) : Exception(message);
