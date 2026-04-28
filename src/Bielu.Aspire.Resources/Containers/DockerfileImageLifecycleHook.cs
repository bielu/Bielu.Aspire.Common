using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Eventing;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics; // Required for System.Diagnostics.Process

namespace Bielu.Aspire.Resources.Containers;

/// <summary>
/// Subscribes to <see cref="BeforeStartEvent"/> and executes <c>docker build</c>
/// for every <see cref="DockerfileImageResource"/> registered in the app model.
/// All output is routed through <see cref="ResourceLoggerService"/> so it
/// streams to the Aspire dashboard per-resource.
/// </summary>
[Experimental("ASPIREPIPELINES003")]
internal sealed partial class DockerfileImageLifecycleHook(
    ResourceNotificationService notifications,
    ResourceLoggerService       loggers)
    : IDistributedApplicationEventingSubscriber
{
    [Experimental("ASPIREPIPELINES003")]
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

    [Experimental("ASPIREPIPELINES003")]
    private async Task BuildImageAsync(
        DockerfileImageResource resource,
        CancellationToken cancellationToken)
    {
        var log = loggers.GetLogger(resource);

        await notifications.PublishUpdateAsync(resource, s => s with
        {
            State = new ResourceStateSnapshot("Building", KnownResourceStateStyles.Info),
        }).ConfigureAwait(false);

        var fullImageName = await resource.GetFullImageName().GetValueAsync(cancellationToken).ConfigureAwait(false) ?? resource.ImageName;
        Log.BuildStarting(log, fullImageName);

        try
        {
            var args = await BuildDockerArgsAsync(resource, cancellationToken).ConfigureAwait(false);
            var exitCode = await RunProcessAsync("docker", args,
                line => Log.BuildOutputLine(log, line),
                line => Log.BuildErrorLine(log, line),
                cancellationToken).ConfigureAwait(false);

            if (exitCode != 0)
            {
                // The error messages would have already been logged by BuildErrorLine
                // So here we just log the failure summary.
                Log.BuildFailed(log, resource.Name, exitCode, "Docker build failed. See logs above for details.");

                await notifications.PublishUpdateAsync(resource, s => s with
                {
                    State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
                }).ConfigureAwait(false);
             return;
            }

            Log.BuildSucceeded(log, fullImageName);

            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.Running, KnownResourceStateStyles.Success),
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Warn),
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.BuildUnexpectedError(log, ex, resource.Name);

            await notifications.PublishUpdateAsync(resource, s => s with
            {
                State = new ResourceStateSnapshot(KnownResourceStates.FailedToStart, KnownResourceStateStyles.Error),
            }).ConfigureAwait(false);
        }
    }

    // -------------------------------------------------------------------------

    private static async Task<string> BuildDockerArgsAsync(DockerfileImageResource resource, CancellationToken cancellationToken)
    {
        var fullImageName = await resource.GetFullImageName().GetValueAsync(cancellationToken).ConfigureAwait(false) ?? resource.ImageName;

        var parts = new List<string>
        {
            "build",
            "--file", Quote(resource.DockerfilePath),
            "--tag",  fullImageName,
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

    private static async Task<int> RunProcessAsync(
        string fileName,
        string arguments,
        Action<string> outputHandler,
        Action<string> errorHandler,
        CancellationToken cancellationToken)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName               = fileName,
            Arguments              = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
        };

        process.Start();

        // Create tasks to read the output and error streams asynchronously
        var outputReadingTask = ReadStreamAsync(process.StandardOutput, outputHandler, cancellationToken);
        var errorReadingTask = ReadStreamAsync(process.StandardError, errorHandler, cancellationToken);

        // Wait for the process to exit and for both stream reading tasks to complete
        await Task.WhenAll(process.WaitForExitAsync(cancellationToken), outputReadingTask, errorReadingTask).ConfigureAwait(false);

        return process.ExitCode;
    }

    private static async Task ReadStreamAsync(System.IO.StreamReader reader, Action<string> handler, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null) // End of stream
            {
                break;
            }
            handler(line);
        }
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

        [LoggerMessage(Level = LogLevel.Error, Message = "{Line}")]
        internal static partial void BuildErrorLine(ILogger logger, string line);

        [LoggerMessage(Level = LogLevel.Error, Message = "Image '{Name}' build failed (exit {ExitCode}): {Error}")]
        internal static partial void BuildFailed(ILogger logger, string name, int exitCode, string error);

        [LoggerMessage(Level = LogLevel.Information, Message = "Image '{ImageName}' built successfully.")]
        internal static partial void BuildSucceeded(ILogger logger, string imageName);

        [LoggerMessage(Level = LogLevel.Error, Message = "Unexpected error building Dockerfile image '{Name}'.")]
        internal static partial void BuildUnexpectedError(ILogger logger, Exception ex, string name);
    }
}
