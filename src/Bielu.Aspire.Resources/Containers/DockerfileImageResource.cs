using Aspire.Hosting.ApplicationModel;

namespace Bielu.Aspire.Resources.Containers;

/// <summary>
/// Represents a Docker image built from a Dockerfile. Unlike a container resource,
/// this resource only builds the image — it does not run a container.
/// </summary>
/// <param name="name">Logical resource name.</param>
/// <param name="dockerfilePath">Absolute path to the Dockerfile.</param>
/// <param name="contextPath">Absolute path to the build context directory.</param>
public sealed class DockerfileImageResource(string name, string dockerfilePath, string contextPath)
    : Resource(name)
{
    /// <summary>Absolute path to the Dockerfile.</summary>
    public string DockerfilePath { get; } = dockerfilePath;

    /// <summary>Absolute path to the build context directory.</summary>
    public string ContextPath { get; } = contextPath;

    /// <summary>Optional build target for multi-stage builds.</summary>
    public string? Target { get; init; }

    /// <summary>Optional build arguments passed via <c>--build-arg</c>.</summary>
    public IReadOnlyDictionary<string, string>? BuildArgs { get; init; }

    /// <summary>
    /// The base image name (<c>repository:tag</c>) that will be produced.
    /// Defaults to the resource name (lowercased) with tag <c>latest</c>.
    /// </summary>
    public string ImageName { get; init; } = $"{name.ToLowerInvariant()}:latest";

    /// <summary>
    /// Returns a <see cref="ReferenceExpression"/> that resolves to the full image name,
    /// including any registry specified via <see cref="ContainerRegistryReferenceAnnotation"/>.
    /// </summary>
    public ReferenceExpression GetFullImageName()
    {
        var registry = Annotations.OfType<ContainerRegistryReferenceAnnotation>().LastOrDefault()?.Registry;
        if (registry == null)
        {
            return ReferenceExpression.Create($"{ImageName}");
        }

        if (registry.Repository != null)
        {
            return ReferenceExpression.Create($"{registry.Endpoint}/{registry.Repository}/{ImageName}");
        }

        return ReferenceExpression.Create($"{registry.Endpoint}/{ImageName}");
    }
}

/// <summary>
/// Annotation placed on a consumer resource to express a dependency on a
/// <see cref="DockerfileImageResource"/> — either to reference the built image name
/// or to block start until the build completes.
/// </summary>
/// <param name="imageResource">The image resource being referenced.</param>
public sealed class DockerfileImageAnnotation(DockerfileImageResource imageResource) : IResourceAnnotation
{
    /// <summary>The image resource this annotation references.</summary>
    public DockerfileImageResource ImageResource { get; } = imageResource;

    /// <summary>
    /// When <see langword="true"/> the consumer will not start until the image build
    /// has completed successfully.
    /// </summary>
    public bool WaitForBuild { get; init; }
}
