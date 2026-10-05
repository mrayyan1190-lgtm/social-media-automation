namespace SocialMediaAutomation.Worker.Options;

public enum WorkerMode
{
    Ai,
    Publishing,
    Combined,
}

/// <summary>Binds the "Workers" configuration section.</summary>
public sealed class WorkersOptions
{
    public const string SectionName = "Workers";

    public JobLoopOptions Ai { get; init; } = new();

    public JobLoopOptions Publishing { get; init; } = new();
}

public sealed class JobLoopOptions
{
    public int PollIntervalSeconds { get; init; } = 5;
}
