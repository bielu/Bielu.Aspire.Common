using System.Diagnostics.CodeAnalysis;

namespace Bielu.Aspire.Common.Example.Worker;

/// <summary>
/// Example background worker that demonstrates a containerized .NET application
/// managed by the microservices orchestrator.
/// </summary>
[SuppressMessage("Performance", "CA1848:Use the LoggerMessage delegates")]
[SuppressMessage("Performance", "CA1873:Avoid potentially expensive logging")]
public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    private readonly string _workerId = Environment.GetEnvironmentVariable("WORKER_ID") ?? "default";
    private readonly string _environment = Environment.GetEnvironmentVariable("ENVIRONMENT") ?? "dev";

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker {WorkerId} starting in {Environment} environment", _workerId, _environment);
        return base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var counter = 0;
        while (!stoppingToken.IsCancellationRequested)
        {
            counter++;
            logger.LogInformation(
                "[{WorkerId}] Worker iteration {Counter} at {Time} - Environment: {Environment}",
                _workerId,
                counter,
                DateTimeOffset.Now,
                _environment);

            await Task.Delay(5000, stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Worker {WorkerId} stopping gracefully", _workerId);
        return base.StopAsync(cancellationToken);
    }
}
