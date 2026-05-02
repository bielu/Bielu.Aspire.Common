using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.ApplicationModel.Docker; // Required for DockerfileBuildAnnotation
using Aspire.Hosting.Lifecycle;
using Aspire.Hosting.Publishing; // Required for ManifestPublishingContext

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
    [Experimental("ASPIREPIPELINES003")]
    public static IResourceBuilder<DockerfileImageResource> AddDockerfileImage(
        this IDistributedApplicationBuilder builder,
        string name,
        string? contextPath = null,
        string? dockerfilePath = null,
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
                      .WithManifestPublishingCallback(context => WriteResourceToManifest(context, resource));
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

        // Use the ReferenceExpression directly for late-bound environment variable
        builder.WithEnvironment(ImageEnvKey(imageResource.Resource), imageResource.Resource.GetFullImageName());

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

        // Use the ReferenceExpression directly for late-bound environment variable
        builder.WithEnvironment(ImageEnvKey(imageResource.Resource), imageResource.Resource.GetFullImageName());

        // Leverage Aspire's built-in WaitFor so the dashboard and orchestrator
        // understand and visualise the dependency edge.
        builder.WaitFor(imageResource);

        return builder;
    }

    // -------------------------------------------------------------------------
    // 4. AddContainer with DockerfileImageResource
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a container resource to the application, using a <see cref="DockerfileImageResource"/>
    /// as its image source. This configures the container to be built from the specified Dockerfile.
    /// </summary>
    /// <param name="builder">The <see cref="IDistributedApplicationBuilder"/>.</param>
    /// <param name="name">The name of the resource.</param>
    /// <param name="dockerfileImageResourceBuilder">The <see cref="IResourceBuilder{DockerfileImageResource}"/>
    /// that defines the Dockerfile build process.</param>
    /// <returns>The <see cref="IResourceBuilder{ContainerResource}"/> for chaining.</returns>
    [Experimental("ASPIREPIPELINES003")]
    public static IResourceBuilder<ContainerResource> AddContainer(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        IResourceBuilder<DockerfileImageResource> dockerfileImageResourceBuilder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(dockerfileImageResourceBuilder);

        var dockerfileResource = dockerfileImageResourceBuilder.Resource;

        // Create a new ContainerResource with a placeholder image name.
        // The image will be properly configured by WithDockerfile and WithImage.
        var containerBuilder = builder.AddContainer(name, "placeholder");

        // Configure the container to be built from the DockerfileImageResource's details
        containerBuilder.WithDockerfile(
            dockerfileResource.ContextPath,
            dockerfileResource.DockerfilePath,
            dockerfileResource.Target);

        // Add build arguments
        if (dockerfileResource.BuildArgs is { Count: > 0 })
        {
            foreach (var arg in dockerfileResource.BuildArgs)
            {
                // Use the public WithBuildArg method
                containerBuilder.WithBuildArg(arg.Key, arg.Value);
            }
        }

        // Set the final image name using WithImage.
        // This will add/update the ContainerImageAnnotation with the correct image reference.
        containerBuilder.WithImage(dockerfileResource.GetFullImageName().ValueExpression);

        // Add a dependency from the new ContainerResource to the DockerfileImageResource
        // This ensures the DockerfileImageResource is built before the ContainerResource starts.
        containerBuilder.WithReference(dockerfileImageResourceBuilder);

        return containerBuilder;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static void WriteResourceToManifest(ManifestPublishingContext context, DockerfileImageResource resource)
    {
        // Using "image" as requested for custom exporting later
        context.Writer.WriteString("type", "image");

        // The 'image' property should be the manifest expression for the full image name.
        // This allows Aspire's publishing tools to resolve the image name, including any registry, at publish time.
        context.Writer.WriteString("image", resource.GetFullImageName().ValueExpression);

        // The 'build' section provides instructions on how to build this image.
        context.Writer.WriteStartObject("build");
        {
            context.Writer.WriteString("context", resource.ContextPath);
            context.Writer.WriteString("dockerfile", resource.DockerfilePath);

            if (!string.IsNullOrEmpty(resource.Target))
            {
                context.Writer.WriteString("target", resource.Target);
            }

            if (resource.BuildArgs is { Count: > 0 })
            {
                context.Writer.WriteStartObject("args");
                {
                    foreach (var arg in resource.BuildArgs)
                    {
                        context.Writer.WriteString(arg.Key, arg.Value);
                    }
                }
                context.Writer.WriteEndObject(); // End "args" object
            }
        }
        context.Writer.WriteEndObject(); // End "build" object
    }

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
