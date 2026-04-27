using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;

namespace Bielu.Aspire.Resources.Containers;

/// <summary>
/// Subscribes to <see cref="BeforeStartEvent"/> and executes <c>docker build</c>
/// for every <see cref="DockerfileImageResource"/> registered in the app model.
/// All output is routed through <see cref="ResourceLoggerService"/> so it
/// streams to the Aspire dashboard per-resource.
/// </summary>
internal sealed partial class DockerfileImageLifecycleHook(
    ResourceNotificationService notifications,
    ResourceLoggerService       loggers)
    : IDistributedApplicationEventingSubscriber
{
    public Task SubscribeAsync(
        IDistributedApplicationEventing eventing,
        DistributedApplicationExecutionContext executionContext,
        CancellationToken cancellationToken)
    {
        eventing.Subscribe<BeforeStartEvent>(async (@event, ct) =>
        {
            var imageResources = @event.Model.Resources
                .OfType<DockerfileImageResource>()
                .ToList();

            if (imageResources.Count == 0)
            {
                return;
            }

            // All image builds are independent — run them concurrently.
            await Task.WhenAll(imageResources.Select(r =>
                BuildImageAsync(r, ct))).ConfigureAwait(false);
        });

        return Task.CompletedTask;
    }

    // -------------------------------------------------------------------------

    private async Task BuildImageAsync(
        DockerfileImageResource resource,
        CancellationToken cancellationToken)
    {
        var log = loggers.GetLogger(resource);

        await notifications.PublishUpdateAsync(resource, s => s with
        {
            State = new ResourceStateSnapshot("Building", KnownResourceStateStyles.Info),
        }).ConfigureAwait(false);

        Log.BuildStarting(log, resource.ImageName);

        try
        {
            var args = BuildDockerArgs(resource);
            var (exitCode, output, errors) = await RunDockerAsync(args, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(output))
            {
                foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    Log.BuildOutputLine(log, line);
                }
            }

            if (exitCode != 0)
            {
                var errorMessage = string.IsNullOrWhiteSpace(errors)
                    ? $"docker build exited with code {exitCode}"
                    : errors;

                Log.BuildFailed(log, resource.Name, exitCode, errorMessage);

                await notifications.PublishUpdateAsync(resource, s => s with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                }).ConfigureAwait(false);

                throw new InvalidOperationException(
                    $"docker build for '{resource.Name}' failed with exit code {exitCode}. " +
                    $"See resource logs for details.");
            }

            Log.BuildSucceeded(log, resource.ImageName);

            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Exited, KnownResourceStateStyles.Warn),
            }).ConfigureAwait(false);
            throw;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            Log.BuildUnexpectedError(log, ex, resource.Name);

            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            }).ConfigureAwait(false);
            throw;
        }
    }

    // -------------------------------------------------------------------------

    private static string BuildDockerArgs(DockerfileImageResource resource)
    {
        var parts = new List<string>
        {
            "build",
            "--file", Quote(resource.DockerfilePath),
            "--tag",  resource.ImageName,
        };

        if (resource.Target is not null)
        {
            parts.Add("--target");
            parts.Add(resource.Target);
        }

        if (resource.BuildArgs is not null)
        {
            foreach (var (k, v) in resource.BuildArgs)
            {
                parts.Add("--build-arg");
                parts.Add($"{k}={v}");
            }
        }

        // Context path is always last.
        parts.Add(Quote(resource.ContextPath));

        return string.Join(' ', parts);
    }

    private static async Task<(int ExitCode, string Output, string Errors)> RunDockerAsync(
        string args,
        CancellationToken cancellationToken)
    {
        using var process = new System.Diagnostics.Process();
        process.StartInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "docker",
            Arguments              = args,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        process.Start();

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask  = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

        return (process.ExitCode, await outputTask.ConfigureAwait(false), await errorTask.ConfigureAwait(false));
    }

    private static string Quote(string path) =>
        path.Contains(' ') ? $"\"{path}\"" : path;

    // -------------------------------------------------------------------------
    // CA1848 / CA1873 — source-generated logger delegates
    // -------------------------------------------------------------------------

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Starting docker build for image '{ImageName}'.")]
        internal static partial void BuildStarting(ILogger logger, string imageName);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Line}")]
        internal static partial void BuildOutputLine(ILogger logger, string line);

        [LoggerMessage(Level = LogLevel.Error, Message = "Image '{Name}' build failed (exit {ExitCode}): {Error}")]
        internal static partial void BuildFailed(ILogger logger, string name, int exitCode, string error);

        [LoggerMessage(Level = LogLevel.Information, Message = "Image '{ImageName}' built successfully.")]
        internal static partial void BuildSucceeded(ILogger logger, string imageName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error building Dockerfile image '{Name}'.")]
        internal static partial void BuildUnexpectedError(ILogger logger, Exception ex, string name);
    }
}
