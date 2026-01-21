namespace DevHabit.Api.Settings;

public class GitHubAutomationOptions
{
    public const string SectionName = "GitHubAutomation";

    public required int ScanIntervalMinutes { get; init; }
}
