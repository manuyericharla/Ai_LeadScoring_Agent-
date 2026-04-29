using System.Net.Http.Json;
using System.Text.Json;
using LeadScoring.Api.Models;

namespace LeadScoring.Api.Services;

public class OpenAiFollowUpSubjectGenerator(
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<OpenAiFollowUpSubjectGenerator> logger) : IFollowUpSubjectGenerator
{
    public async Task<string> GenerateSubjectAsync(string fallbackSubject, Lead lead, int attemptNumber, CancellationToken cancellationToken)
    {
        var apiKey = configuration["OpenAI:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return fallbackSubject;
        }

        var model = configuration["OpenAI:Model"] ?? "gpt-4o-mini";
        var prompt = $"""
            Rewrite this cold follow-up email subject line.
            Keep max 8 words, no emojis, clear and professional.
            This is follow-up attempt #{attemptNumber}.
            Base subject: "{fallbackSubject}"
            Lead first name: "{lead.FirstName ?? "there"}"
            Return ONLY the subject text.
            """;

        var payload = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = "You write concise B2B email subjects." },
                new { role = "user", content = prompt }
            },
            temperature = 0.7
        };

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
            request.Headers.Authorization = new("Bearer", apiKey);
            request.Content = JsonContent.Create(payload);

            var client = httpClientFactory.CreateClient(nameof(OpenAiFollowUpSubjectGenerator));
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("OpenAI subject generation failed with status {StatusCode}. Using fallback subject.", response.StatusCode);
                return fallbackSubject;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var subject = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(subject))
            {
                return fallbackSubject;
            }

            return subject.Trim().Trim('"');
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "OpenAI subject generation exception. Using fallback subject.");
            return fallbackSubject;
        }
    }
}
