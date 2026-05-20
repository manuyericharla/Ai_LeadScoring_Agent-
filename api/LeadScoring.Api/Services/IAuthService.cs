using LeadScoring.Api.Contracts;

namespace LeadScoring.Api.Services;

public interface IAuthService
{
    IReadOnlyList<string> GetPlans();
    Task<AuthResponse> SignupAsync(SignupRequest request, CancellationToken cancellationToken = default);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);
    Task<AuthUserDto?> GetCurrentUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
