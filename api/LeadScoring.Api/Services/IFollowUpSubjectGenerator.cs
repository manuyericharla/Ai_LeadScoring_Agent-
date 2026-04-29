using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public interface IFollowUpSubjectGenerator
{
    Task<string> GenerateSubjectAsync(string fallbackSubject, Lead lead, int attemptNumber, CancellationToken cancellationToken);
}
