namespace LeadScoring.Api.Models;

public class Lead
{
    public Guid Id { get; set; }
    public string? VisitorId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public EventSource? FirstSource { get; set; }
    public EventSource? LastSource { get; set; }
    public int? ProductId { get; set; }
    public bool WelcomeEmailSent { get; set; }
    public int Score { get; set; }
    public LeadStage Stage { get; set; } = LeadStage.Cold;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastActivityUtc { get; set; }
    public DateTime? LastScoredAtUtc { get; set; }
    public bool UserExists { get; set; }
    public bool SignupCompleted { get; set; }
    public bool LoginDataExists { get; set; }
    public bool ProfileCompletion { get; set; }
    public bool IsPlanSelected { get; set; }
    public string? SelectedPlan { get; set; }
    public DateTime? PlanRenewalDate { get; set; }

    public ICollection<BatchLead> BatchLeads { get; set; } = new List<BatchLead>();
}
