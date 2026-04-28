using System.Collections.Immutable;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;

namespace Bielu.Aspire.Resources.Containers;

/// <summary>
/// Extension methods for working with <see cref="DockerfileImageResource"/> in Aspire.
/// </summary>
public static class DockerfileImageExtensions
{
    // -------------------------------------------------------------------------
    // 1. AddDockerfileImage — registers the image build resource
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a resource that builds a Docker image from a Dockerfile without
    /// starting a container. The resulting image can be referenced by other
    /// resources via <see cref="WithImage{T}"/> and
    /// <see cref="WaitForDockerfileImage{T}"/>.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <param name="name">Logical name for the image resource.</param>
    /// <param name="dockerfilePath">
    ///   Path to the Dockerfile. Relative paths are resolved from
    ///   <see cref="IDistributedApplicationBuilder.AppHostDirectory"/>.
    /// </param>
    /// <param name="contextPath">
    ///   Path to the build context. Relative paths are resolved from
    ///   <see cref="IDistributedApplicationBuilder.AppHostDirectory"/>.
    ///   When <see langword="null"/>, defaults to the directory containing
    ///   the Dockerfile.
    /// </param>
    /// <param name="imageName">
    ///   Override the output image name (<c>repository:tag</c>).
    ///   Defaults to <c>{name}:latest</c>.
    /// </param>
    /// <param name="target">Optional multi-stage build target.</param>
    /// <param name="buildArgs">Optional build arguments (<c>--build-arg</c>).</param>
    /// <returns>A builder for further configuration of the image resource.</returns>
    public static IResourceBuilder<DockerfileImageResource> AddDockerfileImage(
        this IDistributedApplicationBuilder builder,
        string name,
        string dockerfilePath,
        string? contextPath = null,
        string? imageName = null,
        string? target = null,
        IReadOnlyDictionary<string, string>? buildArgs = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(dockerfilePath);

        var resolvedDockerfile = ResolvePath(builder.AppHostDirectory, dockerfilePath);
        var resolvedContext    = ResolvePath(builder.AppHostDirectory, contextPath
                                     ?? Path.GetDirectoryName(resolvedDockerfile)
                                     ?? builder.AppHostDirectory);

        var resource = new DockerfileImageResource(name, resolvedDockerfile, resolvedContext)
        {
            ImageName = imageName ?? $"{name.ToLowerInvariant()}:latest",
            Target    = target,
            BuildArgs = buildArgs,
        };

        // Register the lifecycle hook exactly once per application builder.
        builder.Services.TryAddEventingSubscriber<DockerfileImageLifecycleHook>();

        return builder.AddResource(resource)
                      .WithInitialState(new CustomResourceSnapshot
                      {
                          ResourceType = "DockerfileImage",
                          State        = new ResourceStateSnapshot(KnownResourceStates.Starting, KnownResourceStateStyles.Info),
                          Properties   = BuildSnapshotProperties(resource),
                      })
                      .ExcludeFromManifest();
    }

    // -------------------------------------------------------------------------
    // 2. WithImage — reference the built image name without blocking start
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures the resource to reference the image produced by a
    /// <see cref="DockerfileImageResource"/>. The image name is injected as
    /// the environment variable <c>CONTAINERS__IMAGES__{NAME}</c>.
    /// The resource starts without waiting for the image build to complete.
    /// </summary>
    /// <typeparam name="T">The consumer resource type.</typeparam>
    /// <param name="builder">The consumer resource builder.</param>
    /// <param name="imageResource">The image resource to reference.</param>
    /// <returns>The consumer builder for chaining.</returns>
    public static IResourceBuilder<T> WithImage<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<DockerfileImageResource> imageResource)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(imageResource);

        builder.Resource.Annotations.Add(new DockerfileImageAnnotation(imageResource.Resource)
        {
            WaitForBuild = false,
        });

        builder.WithEnvironment(ImageEnvKey(imageResource.Resource), imageResource.Resource.ImageName);

        return builder;
    }

    // -------------------------------------------------------------------------
    // 3. WaitForDockerfileImage — block start until the image build completes
    // -------------------------------------------------------------------------

    /// <summary>
    /// Causes the resource to wait until the <see cref="DockerfileImageResource"/>
    /// image build has completed successfully before starting.
    /// Also injects the image name as <c>CONTAINERS__IMAGES__{NAME}</c>.
    /// </summary>
    /// <typeparam name="T">The consumer resource type.</typeparam>
    /// <param name="builder">The consumer resource builder.</param>
    /// <param name="imageResource">The image resource to wait for.</param>
    /// <returns>The consumer builder for chaining.</returns>
    public static IResourceBuilder<T> WaitForDockerfileImage<T>(
        this IResourceBuilder<T> builder,
        IResourceBuilder<DockerfileImageResource> imageResource)
        where T : IResourceWithEnvironment, IResourceWithWaitSupport
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(imageResource);

        builder.Resource.Annotations.Add(new DockerfileImageAnnotation(imageResource.Resource)
        {
            WaitForBuild = true,
        });

        builder.WithEnvironment(ImageEnvKey(imageResource.Resource), imageResource.Resource.ImageName);

        // Leverage Aspire's built-in WaitFor so the dashboard and orchestrator
        // understand and visualise the dependency edge.
        builder.WaitFor(imageResource);

        return builder;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static string ImageEnvKey(DockerfileImageResource resource) =>
        $"CONTAINERS__IMAGES__{resource.Name.ToUpperInvariant().Replace('-', '_')}";

    private static string ResolvePath(string appHostDir, string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(path, appHostDir);

    private static ImmutableArray<ResourcePropertySnapshot> BuildSnapshotProperties(
        DockerfileImageResource resource)
    {
        var builder = ImmutableArray.CreateBuilder<ResourcePropertySnapshot>();

        builder.Add(new ResourcePropertySnapshot("dockerfile", resource.DockerfilePath));
        builder.Add(new ResourcePropertySnapshot("context",    resource.ContextPath));
        builder.Add(new ResourcePropertySnapshot("image",      resource.ImageName));

        if (resource.Target is not null)
            builder.Add(new ResourcePropertySnapshot("target", resource.Target));

        if (resource.BuildArgs is { Count: > 0 })
        {
            foreach (var (k, v) in resource.BuildArgs)
                builder.Add(new ResourcePropertySnapshot($"build-arg:{k}", v));
        }

        return builder.ToImmutable();
    }
}
