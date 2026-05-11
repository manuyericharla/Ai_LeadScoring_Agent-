namespace LeadScoring.Api.Contracts;

public record SendEmailRequest(string Subject, string HtmlBody, string RedirectUrl);
public record SendStageTemplateTestEmailRequest(string Email, string Stage);
public record SendStageTemplateTestEmailResponse(string Email, string Stage, bool Sent, string Message);
