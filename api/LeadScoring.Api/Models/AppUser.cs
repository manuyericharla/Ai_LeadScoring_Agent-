namespace LeadScoring.Api.Models;

public class AppUser
{
    public Guid Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public Guid TenantId { get; set; }
    public Tenant Tenant { get; set; } = null!;
    public DateTime CreatedAtUtc { get; set; }
}
