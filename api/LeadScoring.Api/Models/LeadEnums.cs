namespace LeadScoring.Api.Models;

public enum EventType
{
    EmailClick,
    WebsiteActivity,
    BookDemo,
    BlogPost,
    PricingPage,
    Signup
}

public enum EventSource
{
    Unknown = 0,
    Email = 1,
    Website = 2,
    LinkedIn = 3,
    Direct = 4,
    Organic = 5
}

public enum LeadStage
{
    Cold,
    Warm,
    Mql,
    Hot
}

public enum BatchType
{
    Daily,
    Weekly,
    Monthly
}

public enum BatchStatus
{
    Running,
    Completed,
    Failed
}

public enum BatchLeadStatus
{
    Pending,
    Success,
    Failed
}
