namespace LeadScoring.Api.Contracts;

public record SignupRequest(
    string FirstName,
    string LastName,
    string Email,
    string Password,
    string ConfirmPassword,
    string Company,
    string Plan);

public record LoginRequest(string Email, string Password);

public record AuthUserDto(
    Guid Id,
    string Email,
    string FirstName,
    string LastName,
    string CompanyName,
    string SelectedPlan);

public record AuthResponse(string Token, AuthUserDto User);

public record AuthPlansResponse(IReadOnlyList<string> Plans);
