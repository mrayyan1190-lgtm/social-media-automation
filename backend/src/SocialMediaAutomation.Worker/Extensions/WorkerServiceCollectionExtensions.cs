using SocialMediaAutomation.Worker.Options;
using SocialMediaAutomation.Worker.Workers;
using SocialMediaAutomation.Worker.Workers.Ai;
using SocialMediaAutomation.Worker.Workers.Publishing;

namespace SocialMediaAutomation.Worker.Extensions;

public static class WorkerServiceCollectionExtensions
{
    /// <summary>Registers the background workers selected by "Workers:Mode" (Ai, Publishing or Combined).</summary>
    public static WorkerMode AddWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(WorkersOptions.SectionName);
        var modeSetting = section["Mode"];
        if (!Enum.TryParse<WorkerMode>(modeSetting, ignoreCase: true, out var mode) || !Enum.IsDefined(mode))
            throw new InvalidOperationException($"Workers:Mode must be one of {string.Join(", ", Enum.GetNames<WorkerMode>())}; got '{modeSetting}'.");

        services.Configure<WorkersOptions>(section);
        services.AddSingleton<WorkerHeartbeatWriter>();

        if (mode is WorkerMode.Ai or WorkerMode.Combined)
            services.AddHostedService<AiGenerationWorker>();
        if (mode is WorkerMode.Publishing or WorkerMode.Combined)
            services.AddHostedService<PublicationWorker>();

        return mode;
    }
}
